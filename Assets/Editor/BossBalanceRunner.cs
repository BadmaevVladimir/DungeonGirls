using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// План 12: воспроизводимая матрица баланса боссов. Раннер намеренно использует настоящий
// CombatManager.Tick, а не упрощённую формулу DPS: фазы, телеграфы, щиты, миньоны и эффекты
// обязаны попадать в цифры тем же путём, которым они работают в игре.
public static class BossBalanceRunner
{
    const int SeedsPerProfile = 30;
    const float FixedStep = 0.02f;
    const float TimeLimitSeconds = 120f;
    const string OutputDirectory = "Docs/Balance";
    static readonly string[] MainCharacterIds = { "jennifer", "violet", "sasha" };
    static List<ItemData> itemCatalog;
    static Dictionary<SkillId, PassiveSkillData> skillCatalog;
    static List<MonsterData> bossCatalog;

    sealed class CharacterBuild
    {
        public string[] Items;
        public SkillId[] Skills;
        public int[] SkillLevels;
    }

    sealed class Profile
    {
        public string Id;
        public int ItemLevelOffset;
        public int ItemRank;
        public int BuildingLevel;
        public int UniquePassiveLevel;
        public int UniqueActiveLevel;
        public IReadOnlyDictionary<string, CharacterBuild> Builds;
    }

    sealed class RunMetrics
    {
        public readonly Dictionary<string, int> AbilityCounts = new Dictionary<string, int>();
        public float NormalDamage;
        public float HeavyDamage;
        public float DotDamage;
        public float RoomTickDamage;
        public float Duration;
        public bool Victory;
        public bool TimedOut;
        public float PlayerHpPercent;
    }

    [MenuItem("DungeonGirls/Balance/Run Boss Matrix")]
    public static void Run()
    {
        // AssetDatabase не должен сканироваться заново на каждый из тысяч прогонов, но повторный
        // запуск меню после правки ассетов обязан увидеть новые данные.
        itemCatalog = null;
        skillCatalog = null;
        bossCatalog = null;
        var bosses = LoadBosses();
        var charactersById = LoadAssets<CharacterData>()
            .Where(character => character != null && !string.IsNullOrWhiteSpace(character.characterId))
            .GroupBy(character => character.characterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var characters = new List<CharacterData>();
        foreach (string id in MainCharacterIds)
        {
            if (!charactersById.TryGetValue(id, out var matches) || matches.Count != 1)
                throw new InvalidDataException($"Expected exactly one main character asset with id '{id}'.");
            characters.Add(matches[0]);
        }
        if (bosses.Count == 0) throw new InvalidDataException("BossPool_Main содержит no bosses.");

        var csv = new StringBuilder("Boss,Character,Profile,Floor,Seed,Result,Virtual seconds,Player HP %,Abilities,Normal damage %,Heavy damage %,DoT damage %,RoomTick damage %\n");
        var summaryRows = new List<(string boss, string character, string profile, int floor, int total, int wins, float duration, float hp)>();
        foreach (var boss in bosses)
        {
            foreach (int floor in ResolveBossFloors(boss))
            {
                foreach (var character in characters)
                foreach (var profile in Profiles())
                {
                    int wins = 0;
                    float duration = 0f;
                    float hp = 0f;
                    for (int seed = 1; seed <= SeedsPerProfile; seed++)
                    {
                        var result = Simulate(boss, floor, character, profile, seed);
                        if (result.Victory) wins++;
                        duration += result.Duration;
                        hp += result.PlayerHpPercent;
                        float totalDamage = result.NormalDamage + result.HeavyDamage + result.DotDamage + result.RoomTickDamage;
                        csv.Append(Csv(boss.monsterName)).Append(',').Append(Csv(character.characterId)).Append(',')
                            .Append(profile.Id).Append(',').Append(floor).Append(',').Append(seed).Append(',')
                            .Append(result.Victory ? "Victory" : result.TimedOut ? "Timeout" : "Defeat").Append(',')
                            .Append(N(result.Duration)).Append(',').Append(N(result.PlayerHpPercent)).Append(',')
                            .Append(Csv(AbilityCounts(result.AbilityCounts))).Append(',')
                            .Append(N(Percent(result.NormalDamage, totalDamage))).Append(',')
                            .Append(N(Percent(result.HeavyDamage, totalDamage))).Append(',')
                            .Append(N(Percent(result.DotDamage, totalDamage))).Append(',')
                            .Append(N(Percent(result.RoomTickDamage, totalDamage))).Append('\n');
                    }
                    summaryRows.Add((boss.monsterName, character.characterId, profile.Id, floor, SeedsPerProfile, wins,
                        duration / SeedsPerProfile, hp / SeedsPerProfile));
                }
            }
        }

        var summary = new StringBuilder("Boss,Character,Profile,Floor,Runs,Wins,Win rate %,Average seconds,Average player HP %\n");
        foreach (var row in summaryRows)
            summary.Append(Csv(row.boss)).Append(',').Append(Csv(row.character)).Append(',').Append(row.profile)
                .Append(',').Append(row.floor).Append(',').Append(row.total).Append(',').Append(row.wins).Append(',')
                .Append(N(row.wins * 100f / row.total)).Append(',').Append(N(row.duration)).Append(',')
                .Append(N(row.hp)).Append('\n');

        Directory.CreateDirectory(OutputDirectory);
        string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string path = Path.Combine(OutputDirectory, $"BossBalance_{stamp}.csv");
        string summaryPath = Path.Combine(OutputDirectory, $"BossBalance_{stamp}_summary.csv");
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(true));
        AssetDatabase.Refresh();
        int floorCases = bosses.Sum(boss => ResolveBossFloors(boss).Count);
        Debug.Log($"[BossBalanceRunner] Exported {bosses.Count} bosses × {floorCases} eligible floors × {characters.Count} characters × 3 profiles × {SeedsPerProfile} seeds to {path}.");
    }

    static RunMetrics Simulate(MonsterData bossData, int floor, CharacterData character, Profile profile, int seed)
    {
        var progress = new RunCharacterProgress(character)
        {
            Level = Mathf.Clamp(floor, 1, RunCharacterProgress.MaxCharacterLevel)
        };
        ApplyProfileSkills(progress, character, profile);
        // Каждый профиль использует фиксированные совместимые ассеты. Это сохраняет повторяемость
        // замеров и не привязывает "бедный" профиль к стартовому снаряжению конкретного героя.
        var ownedEquipment = CreateProfileEquipment(character, profile, floor);
        var player = CombatantFactory.CreatePlayerCombatant(character, progress.Level, progress, ownedEquipment,
            profile.BuildingLevel, profile.BuildingLevel, profile.BuildingLevel);
        var boss = CombatantFactory.CreateBossCombatant(bossData, floor, character.characterClass, player);
        var enemies = new List<CombatantRuntime> { boss };
        enemies.AddRange(CombatantFactory.CreateBossCompanions(bossData, floor, boss));
        var history = LoadBosses().Where(candidate => candidate != bossData).ToList();

        var host = new GameObject("BossBalanceSimulation") { hideFlags = HideFlags.HideAndDontSave };
        var combat = host.AddComponent<CombatManager>();
        combat.SetHeadlessSimulationMode(true);
        combat.SetRandomSource(new DeterministicCombatRandom(seed));
        combat.SetDefeatedBossesThisRun(history);
        ConfigureAutoSkill(combat, character);

        var metrics = new RunMetrics();
        bool lastBossAttackIsHeavy = false;
        bool pendingBossAttackDamage = false;
        bool pendingRoomTick = false;
        combat.BossAbilityResolved += (actor, ability) =>
        {
            if (actor == null || ability == null) return;
            string abilityName = string.IsNullOrWhiteSpace(ability.displayName) ? ability.effectKind.ToString() : ability.displayName;
            metrics.AbilityCounts[abilityName] = metrics.AbilityCounts.TryGetValue(abilityName, out int count) ? count + 1 : 1;
            pendingRoomTick = ability.effectKind == BossAbilityEffectKind.RoomTick;
        };
        combat.AttackConnected += (attacker, target) =>
        {
            if (attacker != null && !attacker.IsPlayer && target == player)
                pendingBossAttackDamage = true;
        };
        combat.AttackPerformed += (attacker, regular) =>
        {
            // Значение само по себе не учитывается: только следующий AttackConnected подтверждает,
            // что удар не был уклонён и действительно способен породить HitResolved.
            if (attacker != null && !attacker.IsPlayer) lastBossAttackIsHeavy = !regular;
        };
        combat.HitResolved += (target, damage, _, _) =>
        {
            if (target != player) return;
            if (damage > 0f)
            {
                if (pendingRoomTick) metrics.RoomTickDamage += damage;
                else if (pendingBossAttackDamage && lastBossAttackIsHeavy) metrics.HeavyDamage += damage;
                else if (pendingBossAttackDamage) metrics.NormalDamage += damage;
                else metrics.DotDamage += damage;
            }
            pendingRoomTick = false;
            pendingBossAttackDamage = false;
        };

        try
        {
            combat.StartCombat(player, enemies);
            // В реальном UI игрок может нажать готовую активку сразу. Раннер делает то же, а затем
            // отдаёт кулдаун-навыки AutoMode; toggle Саши включается один раз на старте.
            if (combat.ActiveSkills.Count > 0) combat.TryActivateSkill(0);
            while (combat.IsCombatActive && metrics.Duration < TimeLimitSeconds)
            {
                combat.Tick(FixedStep);
                metrics.Duration += FixedStep;
                player.Target = SelectPlayerTarget(combat, boss);
            }
            metrics.Victory = player.IsAlive && combat.Enemies.All(enemy => enemy == null || !enemy.IsAlive);
            metrics.TimedOut = combat.IsCombatActive && metrics.Duration >= TimeLimitSeconds;
            metrics.PlayerHpPercent = player.MaxHP > 0f ? Mathf.Clamp01(player.CurrentHP / player.MaxHP) * 100f : 0f;
        }
        finally
        {
            if (combat.IsCombatActive) combat.AbortCombat();
            UnityEngine.Object.DestroyImmediate(host);
            if (ownedEquipment != null)
                foreach (var item in ownedEquipment) UnityEngine.Object.DestroyImmediate(item);
        }
        return metrics;
    }

    static void ConfigureAutoSkill(CombatManager combat, CharacterData character)
    {
        if (character?.uniqueActiveSkill == null) return;
        int hitCount = CombatManager.ResolveActiveSkillHitCount(character.characterClass);
        bool auto = character.uniqueActiveSkill.skillType == ActiveSkillType.Cooldown;
        combat.ConfigureActiveSkills(new[]
        {
            new ActiveSkillConfigEntry(character.uniqueActiveSkill, hitCount, 1.5f, auto,
                CombatManager.ResolveActiveSkillAttackLockSeconds(character.characterClass))
        });
    }

    // Для этих трёх встреч приоритет цели — часть механики, а не бонус к урону:
    // свечи делают Хранителя неуязвимым, органы замедляют RoomTick Сердца, а миньоны
    // Амальгамы и Прародительницы должны быть сняты до того, как их эффект исказит бой.
    // У остальных боссов раннер по-прежнему держит фокус на самом боссе.
    static CombatantRuntime SelectPlayerTarget(CombatManager combat, CombatantRuntime boss)
    {
        var aliveEnemies = combat.Enemies.Where(enemy => enemy != null && enemy.IsAlive);
        var anchor = aliveEnemies.FirstOrDefault(enemy => enemy.IsBossAnchor);
        if (anchor != null) return anchor;

        var heartOrgan = aliveEnemies.FirstOrDefault(enemy => enemy.IsBossMinion &&
            enemy.BossMinionDeathRoomTickSlowPercent > 0f);
        if (heartOrgan != null) return heartOrgan;

        bool clearMinionsFirst = boss != null && (boss.DisplayName == "Пожирающий Амальгам" ||
            boss.DisplayName == "Паучиха-Прародительница");
        if (clearMinionsFirst)
        {
            var minion = aliveEnemies.FirstOrDefault(enemy => enemy.IsBossMinion);
            if (minion != null) return minion;
        }

        if (boss != null && boss.IsAlive) return boss;
        return aliveEnemies.FirstOrDefault();
    }

    static IEnumerable<Profile> Profiles()
    {
        yield return P("poor", 0, 1, 0, 1, 1,
            B(new[] { "Item_Sword_Common_IronSword", "Item_Shield_Common_WoodenShield", "Item_Helmet_Common_SimpleHelmet", "Item_Armor_Common_IronCuirass", "Item_Boots_Common_SturdyBoots" },
                null, null),
            B(new[] { "Item_Blade_Common_Blade", "Item_Hood_Common_Hood", "Item_Leather_Common_Leather", "Item_Boots_Common_SturdyBoots" },
                null, null),
            B(new[] { "Item_TwoHandedAxe_Common_GreatAxe", "Item_Trophy_Common_Trophy", "Item_Belt_Common_Belt", "Item_Boots_Common_SturdyBoots" },
                null, null));

        yield return P("mid", 0, 2, 2, 2, 2,
            B(new[] { "Item_Sword_Rare_SteelGladius", "Item_Shield_Common_WoodenShield", "Item_Helmet_Rare_SteelHelmet", "Item_Armor_Rare_SteelCuirass", "Item_Boots_Rare_SwiftBoots", "Item_Ring_Armor", "Item_Accessory_Resilience" },
                S(SkillId.Sturdy, SkillId.Ambidexterity, SkillId.CriticalHits), L(2, 2, 1)),
            B(new[] { "Item_Blade_Rare_JaggedBlade", "Item_Hood_Rare_DarkHood", "Item_Leather_Rare_ThickLeather", "Item_Boots_Rare_SwiftBoots", "Item_Ring_Agility", "Item_Accessory_Dexterity" },
                S(SkillId.Evasion, SkillId.CriticalHits, SkillId.PoisonedBlade), L(2, 2, 1)),
            B(new[] { "Item_Spear_Rare_SteelSpear", "Item_Trophy_Rare_RareTrophy", "Item_Belt_Rare_ChampionBelt", "Item_Boots_Rare_SwiftBoots", "Item_Ring_Health", "Item_Accessory_Vitality" },
                S(SkillId.Frenzy, SkillId.CombatRegen, SkillId.Stubbornness), L(2, 2, 1)));

        yield return P("rich", 2, 4, 4, 3, 3,
            B(new[] { "Item_Sword_Epic_BloodSword", "Item_Prototype_ResonanceScimitar", "Item_Helmet_Epic_MidasCrown", "Item_Armor_Epic_EtherealArmor", "Item_Boots_Epic_ArmoredBoots", "Item_Ring_Power", "Item_Ring_Speed", "Item_Accessory_Might" },
                S(SkillId.Sturdy, SkillId.Ambidexterity, SkillId.CriticalHits, SkillId.Evasion, SkillId.Bleed), L(3, 3, 2, 1, 1)),
            B(new[] { "Item_Blade_Epic_MomentoMori", "Item_Blade_Rare_JaggedBlade", "Item_Hood_Epic_DuelistHood", "Item_Leather_Epic_EmbraceOfNight", "Item_Boots_Epic_ArmoredBoots", "Item_Ring_Agility", "Item_Ring_Speed", "Item_Accessory_Dexterity" },
                S(SkillId.Evasion, SkillId.SlipAway, SkillId.ByAThread, SkillId.EyeForAnEye, SkillId.Elimination), L(3, 3, 2, 1, 1)),
            B(new[] { "Item_TwoHandedAxe_Epic_Headsplitter", "Item_Trophy_Epic_EpicTrophy", "Item_Belt_Epic_TitanBelt", "Item_Boots_Epic_ArmoredBoots", "Item_Ring_Health", "Item_Ring_Power", "Item_Accessory_Vitality" },
                S(SkillId.Frenzy, SkillId.CombatRegen, SkillId.Stubbornness, SkillId.CriticalHits, SkillId.Intimidation), L(3, 3, 2, 1, 1)));
    }

    static List<ItemData> CreateProfileEquipment(CharacterData character, Profile profile, int floor)
    {
        if (!profile.Builds.TryGetValue(character.characterId, out CharacterBuild build) || build.Items == null) return null;
        itemCatalog ??= LoadAssets<ItemData>().ToList();
        var byName = itemCatalog.ToDictionary(item => item.name, StringComparer.OrdinalIgnoreCase);
        var result = new List<ItemData>();
        foreach (string itemName in build.Items)
        {
            if (!byName.TryGetValue(itemName, out ItemData source))
                throw new InvalidDataException($"Profile '{profile.Id}' references missing item '{itemName}'.");
            if (source.allowedClasses != null && source.allowedClasses.Length > 0 &&
                Array.IndexOf(source.allowedClasses, character.characterClass) < 0)
                throw new InvalidDataException($"Item '{itemName}' is incompatible with '{character.characterId}'.");
            var clone = UnityEngine.Object.Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.itemLevel = Mathf.Clamp(floor + profile.ItemLevelOffset, 1, RunCharacterProgress.MaxCharacterLevel);
            clone.itemRank = Mathf.Clamp(profile.ItemRank, 1, 5);
            result.Add(clone);
        }
        return result;
    }

    static void ApplyProfileSkills(RunCharacterProgress progress, CharacterData character, Profile profile)
    {
        if (!profile.Builds.TryGetValue(character.characterId, out CharacterBuild build))
            throw new InvalidDataException($"Profile '{profile.Id}' has no build for '{character.characterId}'.");
        if (build.Skills != null && (build.SkillLevels == null || build.Skills.Length != build.SkillLevels.Length))
            throw new InvalidDataException($"Invalid skill levels for profile '{profile.Id}' and '{character.characterId}'.");

        int points = Mathf.Max(0, progress.Level - 1);
        skillCatalog ??= LoadAssets<PassiveSkillData>().Where(skill => skill != null && skill.skillId != SkillId.None)
            .GroupBy(skill => skill.skillId).ToDictionary(group => group.Key, group => group.First());
        if (build.Skills != null)
        {
            int maxRank = build.SkillLevels.Max();
            for (int rank = 1; rank <= maxRank && points > 0; rank++)
            {
                for (int i = 0; i < build.Skills.Length && points > 0; i++)
                {
                    SkillId skillId = build.Skills[i];
                    int desiredLevel = build.SkillLevels[i];
                    if (desiredLevel < rank) continue;
                    if (!skillCatalog.TryGetValue(skillId, out PassiveSkillData skillData))
                        throw new InvalidDataException($"Profile references missing skill '{skillId}'.");
                    progress.KnownSkillLevels[skillData] = rank;
                    points--;
                }
            }
        }
        progress.UniquePassiveLevel = character.uniquePassiveSkill != null
            ? SpendUniqueRank(profile.UniquePassiveLevel, character.uniquePassiveSkill.maxLevel, ref points) : 1;
        progress.UniqueActiveLevel = character.uniqueActiveSkill != null
            ? SpendUniqueRank(profile.UniqueActiveLevel, character.uniqueActiveSkill.maxLevel, ref points) : 1;
    }

    static int SpendUniqueRank(int desired, int maximum, ref int points)
    {
        int rank = 1;
        while (rank < Mathf.Clamp(desired, 1, maximum) && points > 0) { rank++; points--; }
        return rank;
    }

    static Profile P(string id, int itemLevelOffset, int itemRank, int buildingLevel, int uniquePassiveLevel,
        int uniqueActiveLevel, CharacterBuild jennifer, CharacterBuild violet, CharacterBuild sasha) => new Profile
    {
        Id = id, ItemLevelOffset = itemLevelOffset, ItemRank = itemRank, BuildingLevel = buildingLevel,
        UniquePassiveLevel = uniquePassiveLevel, UniqueActiveLevel = uniqueActiveLevel,
        Builds = new Dictionary<string, CharacterBuild>(StringComparer.OrdinalIgnoreCase)
        {
            ["jennifer"] = jennifer, ["violet"] = violet, ["sasha"] = sasha
        }
    };

    static CharacterBuild B(string[] items, SkillId[] skills, int[] levels) => new CharacterBuild { Items = items, Skills = skills, SkillLevels = levels };
    static SkillId[] S(params SkillId[] values) => values;
    static int[] L(params int[] values) => values;

    static List<MonsterData> LoadBosses()
    {
        if (bossCatalog != null) return bossCatalog;
        var pool = AssetDatabase.LoadAssetAtPath<BossPoolData>("Assets/ScriptableObjects/Bosses/BossPool_Main.asset");
        bossCatalog = pool == null ? new List<MonsterData>() : pool.entries.Where(entry => entry?.boss != null)
            .Select(entry => entry.boss).Distinct().ToList();
        return bossCatalog;
    }

    static List<int> ResolveBossFloors(MonsterData boss)
    {
        var pool = AssetDatabase.LoadAssetAtPath<BossPoolData>("Assets/ScriptableObjects/Bosses/BossPool_Main.asset");
        if (pool?.entries == null) return new List<int> { 1 };
        return pool.entries.Where(entry => entry?.boss == boss)
            .SelectMany(entry => Enumerable.Range(Mathf.Max(1, entry.minFloor), Mathf.Max(1, entry.maxFloor - entry.minFloor + 1)))
            .Distinct().OrderBy(floor => floor).ToList();
    }

    static IEnumerable<T> LoadAssets<T>() where T : UnityEngine.Object => AssetDatabase.FindAssets($"t:{typeof(T).Name}")
        .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid))).Where(asset => asset != null);
    static string AbilityCounts(Dictionary<string, int> counts) => string.Join("; ", counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
    static float Percent(float value, float total) => total > 0f ? value / total * 100f : 0f;
    static string N(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
    static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
