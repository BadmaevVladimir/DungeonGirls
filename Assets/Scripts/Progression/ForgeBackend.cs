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

// UI-facing state for one blueprint card (Forge screen) — mirrors TavernRecipeState's role.
public enum ForgeBlueprintState
{
    AvailableToResearch,
    BlueprintLocked,
    NotEnoughMaterials,
    PrototypeCreated
}

public sealed class ForgeService
{
    // R03: сервис живёт всё время хаба, а SaveManager.ResetProgress ЗАМЕНЯЕТ объект SaveData
    // целиком. Захваченная в конструкторе ссылка после сброса указывала на отсоединённые данные:
    // расход шёл по старым запасам, а на диск писался уже новый объект. Держим провайдер, а не
    // ссылку, — любая замена SaveData подхватывается сама.
    readonly Func<SaveData> dataProvider;
    readonly Action persist;
    readonly CatalogUnlockPolicy access;

    SaveData data => dataProvider();

    public ForgeService(SaveData data, Action persist = null, CatalogUnlockPolicy access = null)
        : this(DataProviderFor(data), persist, access)
    {
    }

    public ForgeService(Func<SaveData> dataProvider, Action persist = null, CatalogUnlockPolicy access = null)
    {
        this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        this.persist = persist;
        this.access = access ?? new CatalogUnlockPolicy();
    }

    static Func<SaveData> DataProviderFor(SaveData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        return () => data;
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

    public ForgeBlueprintState GetBlueprintState(ForgeBlueprintData blueprint)
    {
        if (blueprint == null || string.IsNullOrWhiteSpace(blueprint.blueprintId)) return ForgeBlueprintState.BlueprintLocked;
        if (IsPrototypeResearched(blueprint.prototypeId)) return ForgeBlueprintState.PrototypeCreated;
        if (!access.IsUnlocked(blueprint.blueprintId, data.unlockedForgeBlueprints)) return ForgeBlueprintState.BlueprintLocked;
        if (!new ResourceInventory(data.resources).CanAfford(blueprint.materialCost)) return ForgeBlueprintState.NotEnoughMaterials;
        return ForgeBlueprintState.AvailableToResearch;
    }

    public int GetMaterialAmount(string resourceId) => new ResourceInventory(data.resources).GetAmount(resourceId);

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

// Данные живут в Assets/Resources/Progression/ForgeBlueprints/*.asset (сгенерированы
// ProgressionContentAssetGenerator.Generate, itemPrototype-ссылки указывают на существующие
// ассеты в ForgePrototypes/). Этот класс — только registry поверх них.
public static class ForgeBlueprintCatalog
{
    const string ResourcesPath = "Progression/ForgeBlueprints";
    static ForgeBlueprintData[] blueprints;
    public static IReadOnlyList<ForgeBlueprintData> All => blueprints ??= Resources.LoadAll<ForgeBlueprintData>(ResourcesPath);
}
