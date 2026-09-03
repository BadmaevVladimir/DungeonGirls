using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ForgeBlueprint", menuName = "DungeonGirls/Forge Blueprint")]
public class ForgeBlueprintData : ScriptableObject
{
    public string blueprintId;
    public string prototypeId;
    public ItemData itemPrototype;
    public WeaponSubtype weaponCategory;
    public ItemTier rarity = ItemTier.Epic;
    public WeaponPrototypeEffectId effect;
    public float primaryEffectValue;
    public float secondaryEffectValue;
    public int maxStacks;
    public List<ResourceAmount> materialCost = new List<ResourceAmount>();
    public string displayName;
    [TextArea] public string description;
}

public sealed class ForgeService
{
    readonly SaveData data;
    readonly Action persist;
    readonly CatalogUnlockPolicy access;

    public ForgeService(SaveData data, Action persist = null, CatalogUnlockPolicy access = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.persist = persist;
        this.access = access ?? new CatalogUnlockPolicy();
    }

    public bool IsPrototypeResearched(string prototypeId) =>
        data.researchedItemPrototypes.Exists(id => string.Equals(id, prototypeId, StringComparison.Ordinal));

    public bool UnlockBlueprint(string blueprintId)
    {
        if (string.IsNullOrWhiteSpace(blueprintId) || data.unlockedForgeBlueprints.Contains(blueprintId)) return false;
        data.unlockedForgeBlueprints.Add(blueprintId);
        persist?.Invoke();
        return true;
    }

    public bool TryResearch(ForgeBlueprintData blueprint)
    {
        if (blueprint == null || string.IsNullOrWhiteSpace(blueprint.blueprintId) ||
            string.IsNullOrWhiteSpace(blueprint.prototypeId)) return false;
        if (!access.IsUnlocked(blueprint.blueprintId, data.unlockedForgeBlueprints)) return false;
        if (IsPrototypeResearched(blueprint.prototypeId)) return true;
        for (int i = 0; i < blueprint.materialCost.Count; i++)
            if (string.IsNullOrWhiteSpace(blueprint.materialCost[i].resourceId) || blueprint.materialCost[i].amount <= 0) return false;

        var inventory = new ResourceInventory(data.resources);
        if (!inventory.CanAfford(blueprint.materialCost)) return false;
        foreach (var cost in blueprint.materialCost)
            inventory.FindOrCreate(cost.resourceId).count -= cost.amount;
        data.researchedItemPrototypes.Add(blueprint.prototypeId);
        persist?.Invoke();
        return true;
    }
}

public static class ForgeBlueprintCatalog
{
    static ForgeBlueprintData[] blueprints;
    public static IReadOnlyList<ForgeBlueprintData> All => blueprints ??= CreateAll();

    static ForgeBlueprintData[] CreateAll() => new[]
    {
        Make("resonance_scimitar", "Скимитар Резонанса", WeaponSubtype.Sword,
            WeaponPrototypeEffectId.ResonanceScimitar, 5f, 5f, 4, 3, 2, 1, 1),
        Make("spell_eater", "Пожиратель чар", WeaponSubtype.Axe,
            WeaponPrototypeEffectId.SpellEater, 1f, 0f, 0, 3, 2, 2, 1),
        Make("lightning_spear", "Копьё молний", WeaponSubtype.Spear,
            WeaponPrototypeEffectId.LightningSpear, 50f, 0f, 3, 3, 3, 1, 1),
        Make("pendulum", "Маятник", WeaponSubtype.Hammer,
            WeaponPrototypeEffectId.Pendulum, 20f, 100f, 0, 4, 1, 2, 1),
        Make("day_and_night", "День и Ночь", WeaponSubtype.Blade,
            WeaponPrototypeEffectId.DayAndNight, 50f, 50f, 0, 4, 3, 2, 1),
        Make("last_argument_prototype", "Последний аргумент", WeaponSubtype.TwoHandedAxe,
            WeaponPrototypeEffectId.LastArgumentConversion, 1f, 0f, 0, 4, 1, 3, 1)
    };

    static ForgeBlueprintData Make(string id, string name, WeaponSubtype category,
        WeaponPrototypeEffectId effect, float primary, float secondary, int maxStacks,
        int steel, int crystal, int core, int shard)
    {
        var value = ScriptableObject.CreateInstance<ForgeBlueprintData>();
        value.blueprintId = "blueprint_" + id;
        value.prototypeId = "prototype_" + id;
        value.displayName = name;
        value.weaponCategory = category;
        value.rarity = ItemTier.Epic;
        value.effect = effect;
        value.primaryEffectValue = primary;
        value.secondaryEffectValue = secondary;
        value.maxStacks = maxStacks;
        value.materialCost.Add(new ResourceAmount(PersistentResourceIds.TemperedSteel, steel));
        value.materialCost.Add(new ResourceAmount(PersistentResourceIds.MagicCrystal, crystal));
        value.materialCost.Add(new ResourceAmount(PersistentResourceIds.MonsterCore, core));
        value.materialCost.Add(new ResourceAmount(PersistentResourceIds.AncientShard, shard));
        return value;
    }
}
