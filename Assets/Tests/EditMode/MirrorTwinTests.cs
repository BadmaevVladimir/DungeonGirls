using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// План 9 (Docs/superpowers/plans/2026-09-11-boss-09-mirror-twin.md): Зеркальный Двойник —
// один ассет, три боя. Выбор ветки проверяется как чистая функция, а сборка рантайма — через
// фабрику, потому что именно там кит попадает в BossEncounterState.
public class MirrorTwinTests
{
    static BossKitData MakeKit(string name, float threshold = 100f)
    {
        var kit = ScriptableObject.CreateInstance<BossKitData>();
        kit.name = name;
        kit.phases.Add(new BossPhaseData { phaseName = name, hpThresholdPercent = threshold });
        return kit;
    }

    static MonsterData MakeTwin(BossKitData fallback, params (CharacterClass, BossKitData)[] variants)
    {
        var data = ScriptableObject.CreateInstance<MonsterData>();
        data.monsterName = "Зеркальный Двойник";
        data.isBoss = true;
        data.hp = 100f;
        data.damageMin = 10f;
        data.damageMax = 10f;
        data.attackSpeed = 1f;
        data.bossKit = fallback;
        foreach (var (cls, kit) in variants)
        {
            data.classVariantKits.Add(new BossClassVariantKit { playerClass = cls, kit = kit });
        }

        return data;
    }

    static CombatantRuntime MakePlayer(float hp, float damage) => new CombatantRuntime
    {
        DisplayName = "Тест-игрок",
        IsPlayer = true,
        MaxHP = hp,
        CurrentHP = hp,
        Weapons = { new WeaponAttackState { DamageMin = damage, DamageMax = damage, AttackSpeed = 1f } }
    };

    // ---- селектор ----

    [Test]
    public void Select_PicksTheBranchForThePlayersClass()
    {
        var warrior = MakeKit("Против Дженифер");
        var rogue = MakeKit("Против Вайолет");
        var twin = MakeTwin(MakeKit("По умолчанию"),
            (CharacterClass.Warrior, warrior), (CharacterClass.Rogue, rogue));

        Assert.AreSame(warrior, MirrorKitSelector.Select(twin, CharacterClass.Warrior));
        Assert.AreSame(rogue, MirrorKitSelector.Select(twin, CharacterClass.Rogue));
    }

    [Test]
    public void Select_FallsBackToDefaultKit_WhenTheClassHasNoBranch()
    {
        var fallback = MakeKit("По умолчанию");
        var twin = MakeTwin(fallback, (CharacterClass.Warrior, MakeKit("Против Дженифер")));

        // Мага в игре нет, но enum его содержит — ветка не обязана существовать.
        Assert.AreSame(fallback, MirrorKitSelector.Select(twin, CharacterClass.Mage));
        Assert.AreSame(fallback, MirrorKitSelector.Select(twin, CharacterClass.Barbarian));
    }

    [Test]
    public void Select_IgnoresAVariantWithNoKit_InsteadOfReturningNull()
    {
        var fallback = MakeKit("По умолчанию");
        var twin = MakeTwin(fallback);
        twin.classVariantKits.Add(new BossClassVariantKit { playerClass = CharacterClass.Rogue, kit = null });

        Assert.AreSame(fallback, MirrorKitSelector.Select(twin, CharacterClass.Rogue),
            "недозаполненная запись в ассете не должна оставлять босса вообще без кита");
    }

    [Test]
    public void Select_OnAnOrdinaryBossWithoutVariants_ReturnsItsOwnKit()
    {
        var kit = MakeKit("Обычный босс");
        var boss = MakeTwin(kit);

        Assert.AreSame(kit, MirrorKitSelector.Select(boss, CharacterClass.Barbarian));
        Assert.IsFalse(MirrorKitSelector.HasClassVariants(boss));
    }

    // ---- фабрика ----

    [Test]
    public void CreateBossCombatant_StartsTheEncounterOnTheSelectedBranch()
    {
        var barbarian = MakeKit("Против Саши");
        var twin = MakeTwin(MakeKit("По умолчанию"), (CharacterClass.Barbarian, barbarian));

        var runtime = CombatantFactory.CreateBossCombatant(twin, 1, CharacterClass.Barbarian);

        Assert.IsNotNull(runtime.BossEncounter);
        Assert.AreSame(barbarian, runtime.BossEncounter.Kit,
            "бой должен начаться на ветке класса, а не на ките по умолчанию");
    }

    [Test]
    public void Companions_ComeFromTheSelectedBranch_NotTheDefaultKit()
    {
        var minion = ScriptableObject.CreateInstance<MonsterData>();
        minion.monsterName = "Осколок";
        minion.hp = 10f;
        minion.damageMin = 1f;
        minion.damageMax = 1f;
        minion.attackSpeed = 1f;

        var branch = MakeKit("Против Вайолет");
        branch.companions.Add(new BossCompanionSpawn { monster = minion, count = 2 });

        var twin = MakeTwin(MakeKit("По умолчанию"), (CharacterClass.Rogue, branch));
        var runtime = CombatantFactory.CreateBossCombatant(twin, 1, CharacterClass.Rogue);
        var companions = CombatantFactory.CreateBossCompanions(twin, 1, runtime);

        Assert.AreEqual(2, companions.Count,
            "спутники обязаны читаться с уже выбранного кита, иначе ветка выбрана, а сцена собрана из дефолта");
    }

    // ---- пол статов ----

    [Test]
    public void MirrorFloor_LiftsATwinThatCameOutWeakerThanThePlayer()
    {
        var kit = MakeKit("Против Саши");
        kit.mirrorPlayerStatsPercent = 120f;
        var twin = MakeTwin(kit);
        twin.hp = 50f;          // заведомо слабее игрока
        twin.damageMin = 4f;
        twin.damageMax = 4f;

        var player = MakePlayer(hp: 200f, damage: 30f);
        var runtime = CombatantFactory.CreateBossCombatant(twin, 1, CharacterClass.Barbarian, player);

        Assert.AreEqual(240f, runtime.MaxHP, 0.5f, "120% от 200 HP игрока");
        Assert.AreEqual(runtime.MaxHP, runtime.CurrentHP, 0.5f, "подтянутый босс выходит на полном HP");
        Assert.AreEqual(36f, runtime.Weapons[0].DamageMax, 0.5f, "120% от 30 урона игрока");
    }

    [Test]
    public void MirrorFloor_LeavesAStrongTwinAlone()
    {
        var kit = MakeKit("Против Дженифер");
        kit.mirrorPlayerStatsPercent = 120f;
        var twin = MakeTwin(kit);
        twin.hp = 400f;
        twin.damageMin = 50f;
        twin.damageMax = 50f;

        var player = MakePlayer(hp: 100f, damage: 10f);
        var runtime = CombatantFactory.CreateBossCombatant(twin, 1, CharacterClass.Warrior, player);

        Assert.AreEqual(400f, runtime.MaxHP, 0.5f, "это пол, а не замена — сильного босса он не режет");
        Assert.AreEqual(50f, runtime.Weapons[0].DamageMax, 0.5f);
    }

    [Test]
    public void MirrorFloor_DoesNothingWhenTheKitDoesNotAskForIt()
    {
        var kit = MakeKit("Обычный босс");   // mirrorPlayerStatsPercent = 0
        var twin = MakeTwin(kit);
        twin.hp = 50f;

        var player = MakePlayer(hp: 1000f, damage: 100f);
        var runtime = CombatantFactory.CreateBossCombatant(twin, 1, CharacterClass.Warrior, player);

        Assert.AreEqual(50f, runtime.MaxHP, 0.5f, "механика обязана быть опциональной и выключенной по умолчанию");
    }

    [Test]
    public void MirrorFloor_WithoutAPlayer_IsSkippedInsteadOfZeroingTheBoss()
    {
        var kit = MakeKit("Против Саши");
        kit.mirrorPlayerStatsPercent = 120f;
        var twin = MakeTwin(kit);
        twin.hp = 180f;

        var runtime = CombatantFactory.CreateBossCombatant(twin, 1, CharacterClass.Barbarian, player: null);

        Assert.AreEqual(180f, runtime.MaxHP, 0.5f);
        Assert.Greater(runtime.Weapons[0].DamageMax, 0f);
    }

    // ---- честность контента ----

    // Если у босса заведены ветки по классам, они обязаны покрывать ВСЕ играбельные классы.
    // Пропущенная ветка не падает и не логируется — игрок просто получает набор по умолчанию,
    // то есть бой, который для его класса никто не проектировал.
    [Test]
    public void EveryBossWithClassVariants_CoversAllPlayableClasses()
    {
        var playable = new[] { CharacterClass.Warrior, CharacterClass.Rogue, CharacterClass.Barbarian };

        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:MonsterData"))
        {
            var boss = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (boss == null || !MirrorKitSelector.HasClassVariants(boss)) continue;

            Assert.IsNotNull(boss.bossKit,
                $"{boss.name}: есть ветки по классам, но нет кита по умолчанию — классу без ветки выйти не с чем.");

            foreach (var cls in playable)
            {
                var kit = MirrorKitSelector.Select(boss, cls);
                Assert.AreNotSame(boss.bossKit, kit,
                    $"{boss.name}: для класса {cls} ветки нет, сработает набор по умолчанию — " +
                    "бой, который для этого класса никто не проектировал.");
                Assert.Greater(kit.phases.Count, 0, $"{boss.name}, ветка {cls}: кит без фаз.");
            }
        }
    }
}
