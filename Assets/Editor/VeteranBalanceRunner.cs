using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class VeteranBalanceRunner
{
    const string OutputPath = "Docs/Balance/VeteranAttestation_real_builds_v2.csv";
    const string SummaryPath = "Docs/Balance/VeteranAttestation_real_builds_v2_summary.csv";
    const int CharacterLevel = 15;

    sealed class BuildDefinition
    {
        public string Id, CharacterId, Archetype;
        public string[] Items;
        public SkillId[] Skills;
        public int[] SkillLevels;
        public int UniquePassiveLevel = 1;
        public int ItemLevel;
    }

    [MenuItem("DungeonGirls/Balance/Run Veteran Attestation Matrix")]
    public static void Run()
    {
        var config = Resources.Load<VeteranAttestationConfig>("VeteranAttestationConfig");
        string error = "Config asset was not found.";
        if (config == null || !config.TryValidate(out error))
            throw new InvalidDataException("Veteran attestation config is invalid: " + error);

        var characters = LoadAssets<CharacterData>().Where(x => !string.IsNullOrWhiteSpace(x.characterId))
            .ToDictionary(x => x.characterId, StringComparer.OrdinalIgnoreCase);
        var items = LoadAssets<ItemData>().ToDictionary(x => x.name, StringComparer.OrdinalIgnoreCase);
        var skills = LoadAssets<PassiveSkillData>().Where(x => x.skillId != SkillId.None)
            .GroupBy(x => x.skillId).ToDictionary(x => x.Key, x => x.First());
        var service = new VeteranAttestationService(new CombatSimulationEngine());
        var csv = new StringBuilder("Build ID,Character,Archetype,Trial,Tier,Seed,Result,Virtual time,HP left,Rank\n");
        var summary = new StringBuilder("Build ID,Character,Archetype,Item level,HP,Armor,Magic shield,Weapons,Skills,Rank,Qualifying trial,Simulations,Error\n");

        foreach (var definition in Definitions().SelectMany(ExpandItemLevelBands))
        {
            var snapshot = CreateSnapshot(definition, characters, items, skills, out CombatantRuntime runtime,
                out string itemLabel, out string skillLabel);
            var result = service.Evaluate(snapshot, config, AttestationRunMode.FullMatrix);
            foreach (var run in result.Runs)
                csv.Append(Csv(definition.Id)).Append(',').Append(Csv(definition.CharacterId)).Append(',')
                    .Append(Csv(definition.Archetype)).Append(',').Append(Csv(run.TrialId)).Append(',')
                    .Append(Csv(run.TierId)).Append(',').Append(run.Seed).Append(',').Append(run.Simulation.Outcome).Append(',')
                    .Append(run.Simulation.VirtualDuration.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(run.Simulation.PlayerRemainingHp.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(VeteranRankFormat.ToPersistentString(result.FinalRank)).Append('\n');

            summary.Append(Csv(definition.Id)).Append(',').Append(Csv(definition.CharacterId)).Append(',')
                .Append(Csv(definition.Archetype)).Append(',').Append(definition.ItemLevel).Append(',').Append(N(runtime.MaxHP)).Append(',')
                .Append(N(runtime.PhysicalDefenseMax)).Append(',').Append(N(runtime.MagicShieldMax)).Append(',')
                .Append(Csv(itemLabel)).Append(',').Append(Csv(skillLabel)).Append(',')
                .Append(VeteranRankFormat.ToPersistentString(result.FinalRank)).Append(',')
                .Append(Csv(result.QualifyingTrialId)).Append(',').Append(result.Runs.Count).Append(',')
                .Append(Csv(result.ErrorCode)).Append('\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(SummaryPath, summary.ToString(), new UTF8Encoding(true));
        AssetDatabase.Refresh();
        Debug.Log($"[VeteranBalanceRunner] Exported real builds to {OutputPath} and {SummaryPath}");
    }

    static VeteranBuildSnapshot CreateSnapshot(BuildDefinition definition,
        IReadOnlyDictionary<string, CharacterData> characters, IReadOnlyDictionary<string, ItemData> items,
        IReadOnlyDictionary<SkillId, PassiveSkillData> skills, out CombatantRuntime runtime,
        out string itemLabel, out string skillLabel)
    {
        if (!characters.TryGetValue(definition.CharacterId, out CharacterData character))
            throw new InvalidDataException($"Unknown character '{definition.CharacterId}' in '{definition.Id}'.");
        if (definition.Skills.Length != definition.SkillLevels.Length || definition.Skills.Length > RunCharacterProgress.MaxKnownSkillSlots)
            throw new InvalidDataException($"Invalid skill arrays in '{definition.Id}'.");
        int choices = definition.SkillLevels.Sum() + Mathf.Max(0, definition.UniquePassiveLevel - 1);
        if (choices > CharacterLevel - 1)
            throw new InvalidDataException($"Build '{definition.Id}' spends {choices} level-up choices; maximum is {CharacterLevel - 1}.");

        var progress = new RunCharacterProgress(character) {
            Level = CharacterLevel, UniquePassiveLevel = definition.UniquePassiveLevel,
            UniqueActiveLevel = character.uniqueActiveSkill != null ? character.uniqueActiveSkill.maxLevel : 1
        };
        for (int i = 0; i < definition.Skills.Length; i++)
        {
            if (!skills.TryGetValue(definition.Skills[i], out PassiveSkillData skill))
                throw new InvalidDataException($"Unknown skill '{definition.Skills[i]}' in '{definition.Id}'.");
            progress.KnownSkillLevels.Add(skill, Mathf.Clamp(definition.SkillLevels[i], 1, skill.maxLevel));
        }

        var equipment = new List<ItemData>();
        foreach (string itemName in definition.Items)
        {
            if (!items.TryGetValue(itemName, out ItemData source))
                throw new InvalidDataException($"Unknown item '{itemName}' in '{definition.Id}'.");
            if (source.allowedClasses != null && source.allowedClasses.Length > 0 &&
                Array.IndexOf(source.allowedClasses, character.characterClass) < 0)
                throw new InvalidDataException($"Item '{itemName}' is incompatible with '{definition.CharacterId}'.");
            var clone = UnityEngine.Object.Instantiate(source);
            clone.itemLevel = definition.ItemLevel;
            equipment.Add(clone);
        }

        runtime = CombatantFactory.CreatePlayerCombatant(character, CharacterLevel, progress, equipment, 0, 0, 0);
        itemLabel = string.Join(" + ", definition.Items.Select(x => x.Replace("Item_", string.Empty)));
        skillLabel = string.Join(" + ", definition.Skills.Select((id, i) => $"{id} {definition.SkillLevels[i]}"));
        var snapshot = VeteranBuildSnapshot.Capture(definition.CharacterId, runtime, character.uniqueActiveSkill, progress.UniqueActiveLevel);
        foreach (var item in equipment) UnityEngine.Object.DestroyImmediate(item);
        return snapshot;
    }

    static IEnumerable<T> LoadAssets<T>() where T : UnityEngine.Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) yield return asset;
        }
    }

    static string N(float value) => value.ToString("F1", CultureInfo.InvariantCulture);
    static string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    static BuildDefinition B(string id, string character, string archetype, string[] items,
        SkillId[] skills, int[] levels, int unique = 1) => new BuildDefinition {
            Id = id, CharacterId = character, Archetype = archetype, Items = items,
            Skills = skills, SkillLevels = levels, UniquePassiveLevel = unique };

    static IEnumerable<BuildDefinition> ExpandItemLevelBands(BuildDefinition source)
    {
        foreach (int itemLevel in new[] { 1, 4, 8, 12, 15 })
            yield return new BuildDefinition {
                Id = source.Id + "_il" + itemLevel, CharacterId = source.CharacterId,
                Archetype = source.Archetype, Items = source.Items, Skills = source.Skills,
                SkillLevels = source.SkillLevels, UniquePassiveLevel = source.UniquePassiveLevel,
                ItemLevel = itemLevel
            };
    }

    static IEnumerable<BuildDefinition> Definitions()
    {
        string[] jTank = { "Item_Sword_Epic_BloodSword", "Item_Shield_Common_WoodenShield", "Item_Helmet_Epic_MidasCrown", "Item_Armor_Epic_EtherealArmor", "Item_Boots_Epic_ArmoredBoots", "Item_Ring_Armor", "Item_Ring_Health", "Item_Accessory_Resilience" };
        string[] jDps = { "Item_Sword_Epic_BloodSword", "Item_Prototype_ResonanceScimitar", "Item_Helmet_Rare_SteelHelmet", "Item_Armor_Epic_EtherealArmor", "Item_Boots_Rare_SwiftBoots", "Item_Ring_Power", "Item_Ring_Speed", "Item_Accessory_Might" };
        yield return B("jennifer_wall", "jennifer", "shield-wall", jTank, S(SkillId.Sturdy, SkillId.IAmTheWall, SkillId.Thorns, SkillId.CriticalHits, SkillId.Bleed), L(5,4,2,2,1));
        yield return B("jennifer_dual_crit", "jennifer", "dual-crit", jDps, S(SkillId.Ambidexterity, SkillId.CriticalHits, SkillId.Bleed, SkillId.Freeze, SkillId.Evasion), L(5,4,3,1,1));
        yield return B("jennifer_bleed_lightning", "jennifer", "fast-bleed", W(jDps, "Item_Prototype_LightningSpear", "Item_Sword_Epic_BloodSword"), S(SkillId.Bleed, SkillId.CriticalHits, SkillId.Freeze, SkillId.Ambidexterity, SkillId.Unyielding), L(5,3,3,2,1));
        yield return B("jennifer_pendulum", "jennifer", "slow-burst", W(jTank, "Item_Prototype_Pendulum", "Item_Shield_Common_WoodenShield"), S(SkillId.CriticalHits, SkillId.IAmTheWall, SkillId.Sturdy, SkillId.Thorns, SkillId.Unyielding), L(5,4,2,2,1));
        yield return B("jennifer_spell_eater", "jennifer", "anti-shield", W(jDps, "Item_Prototype_SpellEater", "Item_Prototype_ResonanceScimitar"), S(SkillId.Ambidexterity, SkillId.CriticalHits, SkillId.Freeze, SkillId.Bleed, SkillId.Evasion), L(5,4,2,2,1));
        yield return B("jennifer_cursed_berserker", "jennifer", "cursed-speed", W(jDps, "Item_Cursed_BerserkerAxe", "Item_Sword_Epic_BloodSword"), S(SkillId.Ambidexterity, SkillId.CriticalHits, SkillId.Bleed, SkillId.Evasion, SkillId.Unyielding), L(5,4,2,2,1));
        yield return B("jennifer_cursed_last_argument", "jennifer", "cursed-hp", W(jTank, "Item_Cursed_LastArgument", "Item_Shield_Common_WoodenShield"), S(SkillId.Sturdy, SkillId.IAmTheWall, SkillId.CriticalHits, SkillId.Thorns, SkillId.Bleed), L(4,4,3,2,1));
        yield return B("jennifer_balanced", "jennifer", "balanced", W(jTank, "Item_Sword_Epic_BloodSword", "Item_Prototype_ResonanceScimitar"), S(SkillId.Sturdy, SkillId.Ambidexterity, SkillId.CriticalHits, SkillId.Evasion, SkillId.Bleed), L(3,3,3,2,2), 2);

        string[] vEvasion = { "Item_Blade_Epic_MomentoMori", "Item_Blade_Rare_JaggedBlade", "Item_Hood_Epic_DuelistHood", "Item_Leather_Epic_EmbraceOfNight", "Item_Boots_Epic_ArmoredBoots", "Item_Ring_Agility", "Item_Ring_Speed", "Item_Accessory_Dexterity" };
        string[] vCrit = { "Item_Blade_Epic_MomentoMori", "Item_Sword_Epic_BloodSword", "Item_Hood_Rare_DarkHood", "Item_Leather_Rare_ThickLeather", "Item_Boots_Rare_SwiftBoots", "Item_Ring_Fortune", "Item_Ring_Power", "Item_Accessory_Luck" };
        yield return B("violet_evasion", "violet", "evasion-riposte", vEvasion, S(SkillId.Evasion, SkillId.SlipAway, SkillId.ByAThread, SkillId.EyeForAnEye, SkillId.Elimination), L(5,5,2,1,1));
        yield return B("violet_poison", "violet", "poison-execution", vEvasion, S(SkillId.PoisonedBlade, SkillId.EyeForAnEye, SkillId.Elimination, SkillId.CriticalHits, SkillId.SlipAway), L(5,3,3,2,1));
        yield return B("violet_smoke_crit", "violet", "smoke-crit", vCrit, S(SkillId.EyeForAnEye, SkillId.Elimination, SkillId.CriticalHits, SkillId.PoisonedBlade, SkillId.Evasion), L(4,4,3,2,1));
        yield return B("violet_day_and_night", "violet", "paired-prototype", W(vCrit, "Item_Prototype_DayAndNight", "Item_Blade_Epic_MomentoMori"), S(SkillId.CriticalHits, SkillId.Elimination, SkillId.EyeForAnEye, SkillId.PoisonedBlade, SkillId.Evasion), L(4,4,3,2,1));
        yield return B("violet_paranoia", "violet", "cursed-evasion", W(vEvasion, "Item_Cursed_ParanoiaBlades"), S(SkillId.Evasion, SkillId.SlipAway, SkillId.ByAThread, SkillId.EyeForAnEye, SkillId.Elimination), L(5,4,2,2,1));
        yield return B("violet_betrayer", "violet", "cursed-stealth", W(vCrit, "Item_Cursed_BetrayerAndAccomplice"), S(SkillId.EyeForAnEye, SkillId.Elimination, SkillId.PoisonedBlade, SkillId.CriticalHits, SkillId.SlipAway), L(4,4,3,2,1));
        yield return B("violet_execution", "violet", "maximum-execution", vEvasion, S(SkillId.PoisonedBlade, SkillId.CriticalHits, SkillId.Elimination, SkillId.EyeForAnEye, SkillId.Evasion), L(5,3,3,2,1));
        yield return B("violet_balanced", "violet", "balanced", vCrit, S(SkillId.Evasion, SkillId.CriticalHits, SkillId.PoisonedBlade, SkillId.EyeForAnEye, SkillId.Elimination), L(3,3,3,2,2), 2);

        string[] sTwo = { "Item_TwoHandedAxe_Epic_Headsplitter", "Item_Trophy_Epic_EpicTrophy", "Item_Belt_Epic_TitanBelt", "Item_Boots_Epic_ArmoredBoots", "Item_Ring_Health", "Item_Ring_Power", "Item_Accessory_Vitality" };
        string[] sFast = { "Item_Spear_Epic_SwiftSpear", "Item_Prototype_LightningSpear", "Item_Trophy_Rare_RareTrophy", "Item_Belt_Epic_TitanBelt", "Item_Boots_Rare_SwiftBoots", "Item_Ring_Speed", "Item_Ring_Health", "Item_Accessory_Swiftness" };
        yield return B("sasha_headsplitter", "sasha", "giant-slayer", sTwo, S(SkillId.Frenzy, SkillId.Intimidation, SkillId.CriticalHits, SkillId.CombatRegen, SkillId.Stubbornness), L(5,3,3,2,1));
        yield return B("sasha_regen_tank", "sasha", "hp-regeneration", sTwo, S(SkillId.CombatRegen, SkillId.Frenzy, SkillId.Stubbornness, SkillId.Superstition, SkillId.Intimidation), L(5,4,2,2,1));
        yield return B("sasha_thorn_axe", "sasha", "self-bleed", W(sTwo, "Item_Cursed_ThornAxe"), S(SkillId.Frenzy, SkillId.CriticalHits, SkillId.CombatRegen, SkillId.Intimidation, SkillId.Stubbornness), L(5,4,2,2,1));
        yield return B("sasha_berserker_axes", "sasha", "cursed-speed", W(sFast, "Item_Cursed_BerserkerAxe", "Item_Sword_Epic_BloodSword"), S(SkillId.Frenzy, SkillId.CriticalHits, SkillId.Intimidation, SkillId.CombatRegen, SkillId.Stubbornness), L(5,4,2,2,1));
        yield return B("sasha_prototype_last_argument", "sasha", "speed-conversion", W(sTwo, "Item_Prototype_LastArgument"), S(SkillId.Frenzy, SkillId.CriticalHits, SkillId.Intimidation, SkillId.CombatRegen, SkillId.Superstition), L(5,4,2,2,1));
        yield return B("sasha_cursed_last_argument", "sasha", "hp-scaling", W(sFast, "Item_Cursed_LastArgument", "Item_Sword_Epic_BloodSword"), S(SkillId.Frenzy, SkillId.CriticalHits, SkillId.Intimidation, SkillId.CombatRegen, SkillId.Stubbornness), L(5,3,3,2,1));
        yield return B("sasha_fast_spears", "sasha", "fast-on-hit", sFast, S(SkillId.Frenzy, SkillId.Intimidation, SkillId.CriticalHits, SkillId.CombatRegen, SkillId.Stubbornness), L(5,3,3,2,1));
        yield return B("sasha_balanced", "sasha", "balanced", sTwo, S(SkillId.Frenzy, SkillId.CombatRegen, SkillId.Intimidation, SkillId.CriticalHits, SkillId.Stubbornness), L(3,3,3,2,2), 2);
    }

    static SkillId[] S(params SkillId[] values) => values;
    static int[] L(params int[] values) => values;
    static string[] W(string[] source, params string[] weapons) => source.Where(x =>
        !x.Contains("Sword_") && !x.Contains("Blade_") && !x.Contains("Spear_") &&
        !x.Contains("Axe_") && !x.Contains("Hammer_") && !x.Contains("Prototype_") &&
        !x.Contains("Cursed_") && !x.Contains("Shield_")).Concat(weapons).ToArray();
}
