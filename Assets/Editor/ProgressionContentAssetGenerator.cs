using System.Linq;
using UnityEditor;
using UnityEngine;

// One-shot batchmode content generator: Codex left FoodRecipeCatalog/ForgeBlueprintCatalog as
// hardcoded C# lists even though FoodRecipeData/ForgeBlueprintData are already ScriptableObjects.
// Run via:
//   -executeMethod ProgressionContentAssetGenerator.Generate
//
// Same rationale as RogueBarbarianContentGenerator.cs: 21 assets by hand-authored YAML is a high
// GUID-typo risk; AssetDatabase.CreateAsset in -batchmode is the safe equivalent. After this runs,
// FoodRecipeCatalog.All / ForgeBlueprintCatalog.All load from these assets via Resources.LoadAll —
// the assets ARE the single source of truth, not a mirror of the old switch/list.
//
// NOT idempotent by design (see RogueBarbarianContentGenerator.cs) — a repeat run duplicates assets
// as "Name 1.asset". Delete the folders below before regenerating.
public static class ProgressionContentAssetGenerator
{
    const string RecipeFolder = "Assets/Resources/Progression/FoodRecipes";
    const string BlueprintFolder = "Assets/Resources/Progression/ForgeBlueprints";
    const string ForgePrototypeFolder = "Assets/ScriptableObjects/Items/ForgePrototypes";
    const string FoodIconFolder = "Assets/Art/Items/Food";

    // Патчит icon на уже сгенерированных FoodRecipeData-ассетах (PixelLab-иконки блюд добавлены
    // позже самой генерации рецептов) — не пересоздаёт ассеты, только дописывает Sprite-ссылку.
    // Run via: -executeMethod ProgressionContentAssetGenerator.AssignFoodIcons
    public static void AssignFoodIcons()
    {
        int assigned = 0, missing = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:FoodRecipeData", new[] { RecipeFolder }))
        {
            var recipe = AssetDatabase.LoadAssetAtPath<FoodRecipeData>(AssetDatabase.GUIDToAssetPath(guid));
            if (recipe == null) continue;
            string iconPath = $"{FoodIconFolder}/{RecipeIconFileName(recipe.recipeId)}.png";
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon == null)
            {
                missing++;
                Debug.LogWarning($"[ProgressionContentAssetGenerator] Нет иконки для {recipe.recipeId} по пути {iconPath}");
                continue;
            }
            recipe.icon = icon;
            EditorUtility.SetDirty(recipe);
            assigned++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ProgressionContentAssetGenerator] AssignFoodIcons: {assigned} назначено, {missing} не найдено.");
        if (missing > 0) throw new System.Exception($"[ProgressionContentAssetGenerator] {missing} рецептов остались без иконки.");
    }

    // recipeId (snake_case из FoodRecipeCatalog) -> PascalCase имя файла в Art/Items/Food/.
    static string RecipeIconFileName(string recipeId) => recipeId switch
    {
        "meat_stew" => "MeatStew",
        "mushroom_soup" => "MushroomSoup",
        "knight_porridge" => "KnightPorridge",
        "warden_roast" => "WardenRoast",
        "root_puree" => "RootPuree",
        "herbal_broth" => "HerbalBroth",
        "hunters_omelette" => "HuntersOmelette",
        "spicy_omelette" => "SpicyOmelette",
        "mushroom_pie" => "MushroomPie",
        "explorer_stew" => "ExplorerStew",
        "hearty_breakfast" => "HeartyBreakfast",
        "healing_casserole" => "HealingCasserole",
        "veterans_steak" => "VeteransSteak",
        "ethereal_soup" => "EtherealSoup",
        "royal_pie" => "RoyalPie",
        _ => recipeId
    };

    public static void Generate()
    {
        // Forces the AssetDatabase to fully resolve MonoScript-to-type mappings before the first
        // CreateAsset call below — without this, the first several CreateAsset calls for a type in
        // a freshly-started batchmode session can silently serialize with m_Script: {fileID: 0}
        // (broken script reference), logging "No script asset for X" instead of failing loudly.
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        EnsureFolder(RecipeFolder);
        EnsureFolder(BlueprintFolder);

        // Blueprints first: empirically the type used in the FIRST CreateAsset batch of a session
        // is the one at risk of the fileID:0 issue above: this order is not load-bearing on its own
        // (the Refresh call above is the real fix) but costs nothing to keep as a second safety net.
        GenerateBlueprints();
        GenerateRecipes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        VerifyNoBrokenScriptReferences();
        Debug.Log("[ProgressionContentAssetGenerator] Done: 15 food recipes + 6 forge blueprints.");
    }

    // Fails loudly (batchmode exit code != 0 territory via exception) instead of silently leaving
    // Resources.LoadAll<FoodRecipeData>/<ForgeBlueprintData> unable to find these assets at runtime.
    static void VerifyNoBrokenScriptReferences()
    {
        var recipes = AssetDatabase.FindAssets("t:FoodRecipeData", new[] { RecipeFolder });
        var blueprints = AssetDatabase.FindAssets("t:ForgeBlueprintData", new[] { BlueprintFolder });
        if (recipes.Length != 15 || blueprints.Length != 6)
        {
            throw new System.Exception($"[ProgressionContentAssetGenerator] Expected 15 recipes + 6 " +
                $"blueprints loadable by type, found {recipes.Length} + {blueprints.Length}. Broken " +
                "m_Script reference (see 'No script asset' warnings above) or generation failure.");
        }
    }

    static void GenerateRecipes()
    {
        Recipe("meat_stew", "Мясное рагу", 1, FoodEffectType.MaxHp, 8,
            "+8% к максимальному HP на 3 комнаты после привала.",
            (PersistentResourceIds.RawMeat, 2), (PersistentResourceIds.RootVegetables, 1));
        Recipe("mushroom_soup", "Грибной суп", 1, FoodEffectType.ReceivedHealing, 10,
            "+10% к получаемому лечению на 3 комнаты после привала.",
            (PersistentResourceIds.CaveMushrooms, 2), (PersistentResourceIds.HealingHerbs, 1));
        Recipe("knight_porridge", "Рыцарская каша", 1, FoodEffectType.BarrierAfterRest, 10,
            "После привала даёт барьер = 10% макс. HP. Барьер сохраняется между комнатами, поглощает урон по HP, " +
            "не восстанавливается между боями и исчезает, когда истощится или пройдёт 3 комнаты.",
            (PersistentResourceIds.Grain, 2), (PersistentResourceIds.Dairy, 1));

        Recipe("warden_roast", "Жаркое Стража", 2, FoodEffectType.PhysicalDamage, 6,
            "+6% физического урона на 3 комнаты после привала.",
            (PersistentResourceIds.RawMeat, 2), (PersistentResourceIds.CaveMushrooms, 1));
        Recipe("root_puree", "Корнеплодное пюре", 2, FoodEffectType.ArmorEffectiveness, 6,
            "+6% эффективности физической брони на 3 комнаты после привала.",
            (PersistentResourceIds.RootVegetables, 2), (PersistentResourceIds.Dairy, 1));
        Recipe("herbal_broth", "Травяной бульон", 2, FoodEffectType.HealAfterRoom, 3,
            "После каждой из следующих 3 завершённых комнат лечит на 3% макс. HP.",
            (PersistentResourceIds.HealingHerbs, 2), (PersistentResourceIds.Grain, 1));

        Recipe("hunters_omelette", "Омлет охотника", 3, FoodEffectType.CritChancePoints, 4,
            "+4 процентных пункта к шансу крита на 3 комнаты после привала.",
            (PersistentResourceIds.MonsterEggs, 2), (PersistentResourceIds.RawMeat, 1));
        Recipe("spicy_omelette", "Острый омлет", 3, FoodEffectType.AttackSpeed, 5,
            "+5% скорости атаки на 3 комнаты после привала.",
            (PersistentResourceIds.MonsterEggs, 2), (PersistentResourceIds.HealingHerbs, 1));
        Recipe("mushroom_pie", "Грибной пирог", 3, FoodEffectType.NegativeStatusDuration, 20,
            "-20% к длительности негативных статусов на 3 комнаты после привала.",
            (PersistentResourceIds.CaveMushrooms, 1), (PersistentResourceIds.Grain, 1), (PersistentResourceIds.Dairy, 1));

        Recipe("explorer_stew", "Похлёбка исследователя", 4, FoodEffectType.BonusIngredientAfterRoom, 0,
            "После каждой из следующих 3 комнат — 25% шанс получить +1 случайный ингредиент (не более 1 броска за комнату).",
            (PersistentResourceIds.RootVegetables, 1), (PersistentResourceIds.CaveMushrooms, 1), (PersistentResourceIds.Grain, 1),
            procChance: .25f);
        Recipe("hearty_breakfast", "Сытный завтрак", 4, FoodEffectType.AllDamageAndMaxHp, 4,
            "+4% ко всему урону и +5% к максимальному HP на 3 комнаты после привала.",
            (PersistentResourceIds.RawMeat, 1), (PersistentResourceIds.MonsterEggs, 1), (PersistentResourceIds.Grain, 1),
            secondary: 5);
        Recipe("healing_casserole", "Целебная запеканка", 4, FoodEffectType.LowHealthHeal, 8,
            "Первый раз в каждой комнате, когда HP падает ниже 30%, лечит на 8% макс. HP (не чаще раза за комнату, до 3 раз всего).",
            (PersistentResourceIds.HealingHerbs, 1), (PersistentResourceIds.Dairy, 1), (PersistentResourceIds.MonsterEggs, 1),
            threshold: 30);

        Recipe("veterans_steak", "Стейк ветерана", 5, FoodEffectType.BossDamage, 10,
            "+10% урона по боссам на 3 комнаты после привала.",
            (PersistentResourceIds.RawMeat, 2), (PersistentResourceIds.EtherealSpice, 1));
        Recipe("ethereal_soup", "Эфирный суп", 5, FoodEffectType.BlockFirstNegativeStatus, 0,
            "Игнорирует первый негативный статус, пока действует блюдо.",
            (PersistentResourceIds.CaveMushrooms, 1), (PersistentResourceIds.HealingHerbs, 1), (PersistentResourceIds.EtherealSpice, 1));
        Recipe("royal_pie", "Королевский пирог", 5, FoodEffectType.RoyalCombination, 5,
            "+5% ко всему урону, +5% эффективности брони и +5% к получаемому лечению на 3 комнаты после привала.",
            (PersistentResourceIds.Grain, 1), (PersistentResourceIds.Dairy, 1), (PersistentResourceIds.MonsterEggs, 1),
            (PersistentResourceIds.EtherealSpice, 1), secondary: 5, tertiary: 5);
    }

    static void Recipe(string id, string name, int level, FoodEffectType type, float primary, string description,
        (string id, int amount) a, (string id, int amount) b, (string id, int amount)? c = null,
        (string id, int amount)? d = null, float secondary = 0, float tertiary = 0,
        float procChance = 0, float threshold = 0)
    {
        var recipe = ScriptableObject.CreateInstance<FoodRecipeData>();
        recipe.recipeId = id;
        recipe.resultFoodId = "food_" + id;
        recipe.displayName = name;
        recipe.requiredTavernLevel = level;
        recipe.durationRooms = 3;
        recipe.description = description;
        recipe.ingredientCosts.Add(new ResourceAmount(a.id, a.amount));
        recipe.ingredientCosts.Add(new ResourceAmount(b.id, b.amount));
        if (c.HasValue) recipe.ingredientCosts.Add(new ResourceAmount(c.Value.id, c.Value.amount));
        if (d.HasValue) recipe.ingredientCosts.Add(new ResourceAmount(d.Value.id, d.Value.amount));
        recipe.effect = new FoodEffectConfig
        {
            effectType = type, primaryValue = primary, secondaryValue = secondary,
            tertiaryValue = tertiary, procChance = procChance, thresholdPercent = threshold
        };
        AssetDatabase.CreateAsset(recipe, $"{RecipeFolder}/FoodRecipe_{id}.asset");
    }

    static void GenerateBlueprints()
    {
        Blueprint("resonance_scimitar", "Скимитар Резонанса", WeaponSubtype.Sword,
            WeaponPrototypeEffectId.ResonanceScimitar, 5f, 5f, 4,
            "Каждый уникальный позитивный статус на владельце: +5% урона (макс. +20%). " +
            "Каждый уникальный негативный статус: +5% скорости атаки (макс. +20%).",
            3, 2, 1, 1);
        Blueprint("spell_eater", "Пожиратель чар", WeaponSubtype.Axe,
            WeaponPrototypeEffectId.SpellEater, 1f, 0f, 0,
            "Физическое оружие, способное наносить урон Магическому щиту. За каждую фактически уничтоженную " +
            "единицу щита (без учёта избыточного урона) — +1 плоского урона оружия до конца боя, сбрасывается после боя.",
            3, 2, 2, 1);
        Blueprint("lightning_spear", "Копьё молний", WeaponSubtype.Spear,
            WeaponPrototypeEffectId.LightningSpear, 50f, 0f, 3,
            "Каждая третья успешная обычная атака наносит дополнительный удар молнией (магический урон, " +
            "50% от базового физического урона атаки). Не триггерится рекурсивно, счётчик сбрасывается в новом бою.",
            3, 3, 1, 1);
        Blueprint("pendulum", "Маятник", WeaponSubtype.Hammer,
            WeaponPrototypeEffectId.Pendulum, 20f, 100f, 0,
            "Пока оружие не атакует, накапливает +20% базового урона за каждую полную секунду (макс. +100%). " +
            "Атака расходует и сбрасывает накопленный заряд.",
            4, 1, 2, 1);
        Blueprint("day_and_night", "День и Ночь", WeaponSubtype.Blade,
            WeaponPrototypeEffectId.DayAndNight, 50f, 50f, 0,
            "Парные клинки: одна атака наносит ~50% физического и ~50% магического урона одним результатом " +
            "попадания/крита, не удваивая суммарный DPS. Доступно только Вайолет/Плуту.",
            4, 3, 2, 1);
        Blueprint("last_argument_prototype", "Последний аргумент", WeaponSubtype.TwoHandedAxe,
            WeaponPrototypeEffectId.LastArgumentConversion, 1f, 0f, 0,
            "Позитивные бонусы скорости атаки не увеличивают реальную скорость атаки — вместо этого каждый " +
            "+1% позитивной скорости атаки даёт +1% урона оружия. Негативные модификаторы скорости остаются " +
            "обычным замедлением. Доступно только Саше/Варвару.",
            4, 1, 3, 1);
    }

    static void Blueprint(string id, string name, WeaponSubtype category, WeaponPrototypeEffectId effect,
        float primary, float secondary, int maxStacks, string description,
        int steel, int crystal, int core, int shard)
    {
        var blueprint = ScriptableObject.CreateInstance<ForgeBlueprintData>();
        blueprint.blueprintId = "blueprint_" + id;
        blueprint.prototypeId = "prototype_" + id;
        blueprint.displayName = name;
        blueprint.weaponCategory = category;
        blueprint.rarity = ItemTier.Epic;
        blueprint.effect = effect;
        blueprint.primaryEffectValue = primary;
        blueprint.secondaryEffectValue = secondary;
        blueprint.maxStacks = maxStacks;
        blueprint.description = description;
        blueprint.materialCost.Add(new ResourceAmount(PersistentResourceIds.TemperedSteel, steel));
        blueprint.materialCost.Add(new ResourceAmount(PersistentResourceIds.MagicCrystal, crystal));
        blueprint.materialCost.Add(new ResourceAmount(PersistentResourceIds.MonsterCore, core));
        blueprint.materialCost.Add(new ResourceAmount(PersistentResourceIds.AncientShard, shard));
        blueprint.itemPrototype = FindPrototypeAsset(blueprint.prototypeId);
        AssetDatabase.CreateAsset(blueprint, $"{BlueprintFolder}/ForgeBlueprint_{id}.asset");
    }

    // Прототипы уже существуют как ассеты (ForgePrototypes/) и зарегистрированы в ItemCatalog —
    // ищем по prototypeId, а не создаём заново.
    static ItemData FindPrototypeAsset(string prototypeId)
    {
        var guids = AssetDatabase.FindAssets("t:ItemData", new[] { ForgePrototypeFolder });
        return guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<ItemData>)
            .FirstOrDefault(item => item != null && item.prototypeId == prototypeId);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string leaf = path.Substring(path.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
