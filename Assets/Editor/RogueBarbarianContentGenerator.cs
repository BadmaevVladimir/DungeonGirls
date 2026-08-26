using UnityEditor;
using UnityEngine;

// One-shot batchmode content generator for GDD 3.11 (Плут/Варвар). Run via:
//   -executeMethod RogueBarbarianContentGenerator.Generate
//
// Почему генератор, а не рукописный YAML: во всех прошлых сессиях ассеты писались руками
// (.asset + .asset.meta с придуманным GUID), потому что CreateAssetMenu требует интерактивного
// редактора. Для 39 ассетов сразу это механическая работа с высоким шансом опечатки в GUID —
// AssetDatabase.CreateAsset делает то же самое надёжнее и в том же -batchmode -executeMethod,
// который уже используется для смоук-теста. Это осознанное разовое отступление от конвенции,
// а не новая конвенция.
//
// Идемпотентность: НЕ идемпотентен по замыслу — повторный запуск создаст дубликаты по путям
// вида "Name 1.asset". Это одноразовый инструмент, а не шаг сборки. Чтобы перегенерировать,
// сначала удалите ранее созданные .asset-файлы.
public static class RogueBarbarianContentGenerator
{
    const string BladesFolder = "Assets/ScriptableObjects/Items/Blades";
    const string HoodsFolder = "Assets/ScriptableObjects/Items/Hoods";
    const string LeathersFolder = "Assets/ScriptableObjects/Items/Leathers";
    const string AxesFolder = "Assets/ScriptableObjects/Items/TwoHandedAxes";
    const string BeltsFolder = "Assets/ScriptableObjects/Items/Belts";
    const string TrophiesFolder = "Assets/ScriptableObjects/Items/Trophies";
    const string RogueSkillsFolder = "Assets/ScriptableObjects/Skills/Rogue";
    const string BarbarianSkillsFolder = "Assets/ScriptableObjects/Skills/Barbarian";
    const string UniqueSkillsFolder = "Assets/ScriptableObjects/Skills/Unique";
    const string CharactersFolder = "Assets/ScriptableObjects/Characters";

    static readonly CharacterClass[] RogueOnly = { CharacterClass.Rogue };
    static readonly CharacterClass[] BarbarianOnly = { CharacterClass.Barbarian };

    public static void Generate()
    {
        EnsureFolder(BladesFolder);
        EnsureFolder(HoodsFolder);
        EnsureFolder(LeathersFolder);
        EnsureFolder(AxesFolder);
        EnsureFolder(BeltsFolder);
        EnsureFolder(TrophiesFolder);
        EnsureFolder(RogueSkillsFolder);
        EnsureFolder(BarbarianSkillsFolder);
        EnsureFolder(UniqueSkillsFolder);

        // ---- Пассивки эпических предметов (создаются первыми — на них ссылаются ассеты предметов) ----
        var riposte = CreatePassive("Skill_Riposte", SkillEffectMap.Riposte,
            "Первая атака после успешного уклонения наносит доп. флэт-урон, равный уровню капюшона.",
            5, SkillCategory.ItemPassive, RogueSkillsFolder);
        var embraceOfNight = CreatePassive("Skill_EmbraceOfNight", SkillEffectMap.EmbraceOfNight,
            "Все атаки во время Скрытности наносят доп. магический урон = УровеньПредмета × ОбычныйУронАтаки × 1%.",
            5, SkillCategory.ItemPassive, RogueSkillsFolder);
        var execution = CreatePassive("Skill_Execution", SkillEffectMap.Execution,
            "Наносит физ. урон = 1% от недостающего HP противника за каждый уровень предмета.",
            5, SkillCategory.ItemPassive, RogueSkillsFolder);
        var giantSlayer = CreatePassive("Skill_GiantSlayer", SkillEffectMap.GiantSlayer,
            "Если макс. HP противника больше макс. HP Варвара — оружие наносит +5% урона за каждый уровень предмета против этой цели.",
            5, SkillCategory.ItemPassive, BarbarianSkillsFolder);
        var justAScratch = CreatePassive("Skill_JustAScratch", SkillEffectMap.JustAScratch,
            "В начале боя восстанавливает УровеньПредмета × 1% от максимума HP персонажа.",
            5, SkillCategory.ItemPassive, BarbarianSkillsFolder);

        // ---- Клинок (оружие Плута, 3 тира) ----
        // База: урон = 4 (как Копьё), скорость = 2.2/сек. Множитель тира (3.10: Обычный ×1.0,
        // Редкий ×1.5, Эпик ×2.2) уже ЗАПЕЧЁН в baseDamage, как и у всех остальных ассетов.
        var bladeCommon = CreateWeapon("Item_Blade_Common_Blade", "Клинок", ItemTier.Common, WeaponSubtype.Blade,
            baseDamage: 4f, attackSpeed: 2.2f, bonusStat: null, folder: BladesFolder, classes: RogueOnly);
        CreateWeapon("Item_Blade_Rare_JaggedBlade", "Зазубренный клинок", ItemTier.Rare, WeaponSubtype.Blade,
            baseDamage: 6f, attackSpeed: 2.2f, // 4 × 1.5
            bonusStat: Bonus(BonusStatType.ArmorIgnorePercent, 3f), folder: BladesFolder, classes: RogueOnly);
        CreateWeapon("Item_Blade_Epic_MomentoMori", "Моменто Мори", ItemTier.Epic, WeaponSubtype.Blade,
            baseDamage: 8.8f, attackSpeed: 2.2f, // 4 × 2.2
            // Эпик наследует bonusStat Редкого тира (общее правило 3.10), даже если абзац ГДД про
            // "Моменто Мори" его не переповторяет.
            bonusStat: Bonus(BonusStatType.ArmorIgnorePercent, 3f), folder: BladesFolder, classes: RogueOnly,
            passive: execution);

        // ---- Капюшон (замена слота "Шлем" у Плута, 3 тира) ----
        // 3.3: предмет НЕ в слоте "Броня" даёт только МАКСИМУМ физ. защиты, а не саму защиту.
        var hoodCommon = CreateHelmetLike("Item_Hood_Common_Hood", "Капюшон", ItemTier.Common,
            maxPhysicalDefenseBonus: 3f, magicShieldBonus: 5f, bonusStat: null, passive: null,
            folder: HoodsFolder, classes: RogueOnly);
        CreateHelmetLike("Item_Hood_Rare_DarkHood", "Тёмный капюшон", ItemTier.Rare,
            maxPhysicalDefenseBonus: 5f, magicShieldBonus: 8f,
            bonusStat: Bonus(BonusStatType.EvasionPercent, 1f), passive: null,
            folder: HoodsFolder, classes: RogueOnly);
        CreateHelmetLike("Item_Hood_Epic_DuelistHood", "Капюшон Дуэльянта", ItemTier.Epic,
            maxPhysicalDefenseBonus: 7f, magicShieldBonus: 11f,
            bonusStat: Bonus(BonusStatType.EvasionPercent, 1f), passive: riposte,
            folder: HoodsFolder, classes: RogueOnly);

        // ---- Кожанка (замена слота "Броня" у Плута, 3 тира) ----
        var leatherCommon = CreateArmorLike("Item_Leather_Common_Leather", "Кожанка", ItemTier.Common,
            physicalDefense: 7f, magicShieldBonus: 8f, hpBonus: 0f, rageBonusFlatPercent: 0f,
            bonusStat: null, passive: null, folder: LeathersFolder, classes: RogueOnly);
        CreateArmorLike("Item_Leather_Rare_ThickLeather", "Плотная кожанка", ItemTier.Rare,
            physicalDefense: 11f, magicShieldBonus: 12f, hpBonus: 0f, rageBonusFlatPercent: 0f,
            bonusStat: Bonus(BonusStatType.CritChancePercent, 1.5f), passive: null,
            folder: LeathersFolder, classes: RogueOnly);
        CreateArmorLike("Item_Leather_Epic_EmbraceOfNight", "Объятия ночи", ItemTier.Epic,
            physicalDefense: 15f, magicShieldBonus: 18f, hpBonus: 0f, rageBonusFlatPercent: 0f,
            bonusStat: Bonus(BonusStatType.CritChancePercent, 1.5f), passive: embraceOfNight,
            folder: LeathersFolder, classes: RogueOnly);

        // ---- Двуручный топор (оружие Варвара, 3 тира) ----
        var axeCommon = CreateWeapon("Item_TwoHandedAxe_Common_GreatAxe", "Двуручный топор", ItemTier.Common,
            WeaponSubtype.TwoHandedAxe, baseDamage: 20f, attackSpeed: 0.7f, bonusStat: null,
            folder: AxesFolder, classes: BarbarianOnly, isTwoHanded: true);
        CreateWeapon("Item_TwoHandedAxe_Rare_TemperedGreatAxe", "Закалённый двуручный топор", ItemTier.Rare,
            WeaponSubtype.TwoHandedAxe, baseDamage: 30f, attackSpeed: 0.7f, // 20 × 1.5
            bonusStat: Bonus(BonusStatType.CritChancePercent, 1.5f),
            folder: AxesFolder, classes: BarbarianOnly, isTwoHanded: true);
        CreateWeapon("Item_TwoHandedAxe_Epic_Headsplitter", "Головоруб", ItemTier.Epic,
            WeaponSubtype.TwoHandedAxe, baseDamage: 44f, attackSpeed: 0.7f, // 20 × 2.2
            bonusStat: Bonus(BonusStatType.CritChancePercent, 1.5f),
            folder: AxesFolder, classes: BarbarianOnly, isTwoHanded: true, passive: giantSlayer);

        // ---- Пояс (замена слота "Броня" у Варвара, 3 тира) ----
        // У Варвара НЕТ брони вообще, никогда: physicalDefense и maxPhysicalDefenseBonus = 0 на всех
        // тирах. Основной стат Пояса — HP.
        var beltCommon = CreateArmorLike("Item_Belt_Common_Belt", "Пояс", ItemTier.Common,
            physicalDefense: 0f, magicShieldBonus: 0f, hpBonus: 12f, rageBonusFlatPercent: 0f,
            bonusStat: null, passive: null, folder: BeltsFolder, classes: BarbarianOnly);
        CreateArmorLike("Item_Belt_Rare_ChampionBelt", "Пояс чемпиона", ItemTier.Rare,
            physicalDefense: 0f, magicShieldBonus: 0f, hpBonus: 12f, rageBonusFlatPercent: 0f,
            bonusStat: Bonus(BonusStatType.DamagePercent, 2f), passive: null,
            folder: BeltsFolder, classes: BarbarianOnly);
        CreateArmorLike("Item_Belt_Epic_TitanBelt", "Пояс титана", ItemTier.Epic,
            physicalDefense: 0f, magicShieldBonus: 0f, hpBonus: 12f, rageBonusFlatPercent: 1f,
            // Эпик наследует bonusStat Редкого тира (3.10) и ДОПОЛНИТЕЛЬНО получает бонус Ярости —
            // третье одновременное число на одном предмете, ради которого и заведено отдельное поле.
            bonusStat: Bonus(BonusStatType.DamagePercent, 2f), passive: null,
            folder: BeltsFolder, classes: BarbarianOnly);

        // ---- Трофей (замена слота "Шлем" у Варвара, 3 тира) ----
        // maxPhysicalDefenseBonus = 0 на всех тирах (у Варвара нет брони) — вместо неё флэт-урон.
        var trophyCommon = CreateTrophy("Item_Trophy_Common_Trophy", "Трофей", ItemTier.Common,
            flatDamagePerLevel: 3f, passive: null);
        CreateTrophy("Item_Trophy_Rare_RareTrophy", "Редкий трофей", ItemTier.Rare,
            flatDamagePerLevel: 4.5f, passive: null); // 3 × 1.5
        CreateTrophy("Item_Trophy_Epic_EpicTrophy", "Эпический трофей", ItemTier.Epic,
            flatDamagePerLevel: 6.6f, passive: justAScratch); // 3 × 2.2

        // ---- Классовые пулы навыков (PassiveSkillData, maxLevel = 5) ----
        CreatePassive("Skill_EyeForAnEye", SkillEffectMap.EyeForAnEye,
            "Шанс критической атаки: 1ур=+2%, 2ур=+5%, 3ур=+7.5%, 4ур=+10%, 5ур=+12.5%. Крит накладывает Скрытность на 3с.",
            5, SkillCategory.RogueClass, RogueSkillsFolder);
        CreatePassive("Skill_PoisonedBlade", SkillEffectMap.PoisonedBlade,
            "Пробивающие атаки накладывают стак Яда (3с, урон/сек=стаки, макс=уровень навыка). Удваивается в Скрытности.",
            5, SkillCategory.RogueClass, RogueSkillsFolder);
        CreatePassive("Skill_ByAThread", SkillEffectMap.ByAThread,
            "После уклонения: +скорость атаки на 3с — 1ур=+3%, 2ур=+6%, 3ур=+9%, 4ур=+12%, 5ур=+15%.",
            5, SkillCategory.RogueClass, RogueSkillsFolder);
        CreatePassive("Skill_Elimination", SkillEffectMap.Elimination,
            "Крит-множитель урона: 1ур=175%, 2ур=180%, 3ур=185%, 4ур=190%, 5ур=200% (заменяет базовые 150%).",
            5, SkillCategory.RogueClass, RogueSkillsFolder);
        CreatePassive("Skill_SlipAway", SkillEffectMap.SlipAway,
            "После уклонения даёт Скрытность на 3с. Шанс уклонения: 1ур=+1% ... 5ур=+5%.",
            5, SkillCategory.RogueClass, RogueSkillsFolder);

        CreatePassive("Skill_Stubbornness", SkillEffectMap.Stubbornness,
            "Если Ярость выше порога — игнорирует все дебафы: 1ур=90%, 2ур=80%, 3ур=70%, 4ур=60%, 5ур=50%.",
            5, SkillCategory.BarbarianClass, BarbarianSkillsFolder);
        CreatePassive("Skill_Frenzy", SkillEffectMap.Frenzy,
            "Скорость атаки += Ярость×X%: 1ур=0.7, 2ур=0.75, 3ур=0.8, 4ур=0.9, 5ур=1.0.",
            5, SkillCategory.BarbarianClass, BarbarianSkillsFolder);
        CreatePassive("Skill_CombatRegen", SkillEffectMap.CombatRegen,
            "Каждые N полученных ударов восстанавливает 10% HP: 1ур=5, 2ур=4, 3ур=3, 4ур=2, 5ур=1.",
            5, SkillCategory.BarbarianClass, BarbarianSkillsFolder);
        CreatePassive("Skill_Intimidation", SkillEffectMap.Intimidation,
            "При крите снижает скорость атаки цели на Ярость×X% на 3с (X как у Остервенелости).",
            5, SkillCategory.BarbarianClass, BarbarianSkillsFolder);
        CreatePassive("Skill_Superstition", SkillEffectMap.Superstition,
            "Сопротивление магическому урону = Ярость×X% (X как у Остервенелости).",
            5, SkillCategory.BarbarianClass, BarbarianSkillsFolder);

        // ---- Уникальные пассивка/активка на класс (maxLevel по ГДД: Тень=5, Граната=3, Чемпион=5, Берсерк=3) ----
        var shadow = CreatePassive("Skill_Shadow", SkillEffectMap.Shadow,
            "Пока активна Скрытность: +шанс уклонения — 1ур=+10%, 2ур=+15%, 3ур=+20%, 4ур=+25%, 5ур=+30%.",
            5, SkillCategory.Unique, UniqueSkillsFolder);
        var smokeBomb = CreateActive("Skill_SmokeBomb", SkillEffectMap.SmokeBomb, cooldownSeconds: 10f,
            "КД 10с. Даёт Скрытность на 3с. Первые N обычных атак — гарантированный крит: 1ур=1, 2ур=2, 3ур=3.",
            3, ActiveSkillTargetType.Self, UniqueSkillsFolder);
        var championOfTheTribe = CreatePassive("Skill_ChampionOfTheTribe", SkillEffectMap.ChampionOfTheTribe,
            "Шанс крита ВСЕГДА = Ярость×X% (заменяет остальные источники, которые конвертируются 1%→+2% крит-урона).",
            5, SkillCategory.Unique, UniqueSkillsFolder);
        var berserk = CreateActive("Skill_Berserk", SkillEffectMap.Berserk, cooldownSeconds: 0f, // тумблер, не обычная активка
            "Ручной тумблер. Активен: -1%HP/сек (мин. 1 HP), физ. сопротивление 1ур=10%, 2ур=20%, 3ур=30%.",
            3, ActiveSkillTargetType.Self, UniqueSkillsFolder);

        // ---- Персонажи ----
        // startingEquipment: Обычный тир своих же архетипов, по одному предмету на доступный слот —
        // ровно как у Дженифер (оружие + броня + ...), иначе персонаж выходит в бой без оружия.
        CreateCharacter("Character_Rogue", "Плут", CharacterClass.Rogue, baseHealth: 15, healthPerLevel: 15,
            uniquePassive: shadow, uniqueActive: smokeBomb,
            startingEquipment: new[] { bladeCommon, hoodCommon, leatherCommon });
        CreateCharacter("Character_Barbarian", "Варвар", CharacterClass.Barbarian, baseHealth: 30, healthPerLevel: 25,
            uniquePassive: championOfTheTribe, uniqueActive: berserk,
            startingEquipment: new[] { axeCommon, trophyCommon, beltCommon });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RogueBarbarianContentGenerator] Done: 18 items, 5 item-passives, 10 class skills, 4 unique skills, 2 characters.");

        // Конвенция -batchmode-скриптов проекта (см. PlayModeSmokeTest.Run): выходим сами, без -quit
        // в командной строке — иначе редактор остаётся висеть в фоне после executeMethod.
        EditorApplication.Exit(0);
    }

    static BonusStat Bonus(BonusStatType type, float baseValue) => new BonusStat { type = type, baseValue = baseValue };

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static PassiveSkillData CreatePassive(string assetName, string displayName, string description, int maxLevel,
        SkillCategory category, string folder)
    {
        var skill = ScriptableObject.CreateInstance<PassiveSkillData>();
        skill.skillName = displayName;
        skill.category = category;
        skill.effectDescription = description;
        skill.maxLevel = maxLevel;
        AssetDatabase.CreateAsset(skill, $"{folder}/{assetName}.asset");
        return skill;
    }

    static ActiveSkillData CreateActive(string assetName, string displayName, float cooldownSeconds, string description,
        int maxLevel, ActiveSkillTargetType targetType, string folder)
    {
        var skill = ScriptableObject.CreateInstance<ActiveSkillData>();
        skill.skillName = displayName;
        skill.effectDescription = description;
        skill.maxLevel = maxLevel;
        skill.cooldownSeconds = cooldownSeconds;
        skill.targetType = targetType;
        AssetDatabase.CreateAsset(skill, $"{folder}/{assetName}.asset");
        return skill;
    }

    static ItemData CreateWeapon(string assetName, string displayName, ItemTier tier, WeaponSubtype subtype,
        float baseDamage, float attackSpeed, BonusStat bonusStat, string folder, CharacterClass[] classes,
        PassiveSkillData passive = null, bool isTwoHanded = false)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemName = displayName;
        item.slot = EquipmentSlot.Weapon;
        item.weaponSubtype = subtype;
        item.isTwoHanded = isTwoHanded;
        item.tier = tier;
        item.itemLevel = 1;
        item.allowedClasses = classes;
        item.baseDamage = baseDamage;
        item.damageType = DamageType.Physical;
        item.attackSpeed = attackSpeed;
        item.bonusStat = bonusStat;
        item.passiveSkill = passive;
        AssetDatabase.CreateAsset(item, $"{folder}/{assetName}.asset");
        return item;
    }

    // Слот "Шлем": physicalDefense всегда 0 (3.3), число физ. защиты идёт в maxPhysicalDefenseBonus.
    static ItemData CreateHelmetLike(string assetName, string displayName, ItemTier tier,
        float maxPhysicalDefenseBonus, float magicShieldBonus, BonusStat bonusStat, PassiveSkillData passive,
        string folder, CharacterClass[] classes)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemName = displayName;
        item.slot = EquipmentSlot.Helmet;
        item.tier = tier;
        item.itemLevel = 1;
        item.allowedClasses = classes;
        item.maxPhysicalDefenseBonus = maxPhysicalDefenseBonus;
        item.magicShieldBonus = magicShieldBonus;
        item.bonusStat = bonusStat;
        item.passiveSkill = passive;
        AssetDatabase.CreateAsset(item, $"{folder}/{assetName}.asset");
        return item;
    }

    // Слот "Броня": только он несёт саму physicalDefense (3.3).
    static ItemData CreateArmorLike(string assetName, string displayName, ItemTier tier,
        float physicalDefense, float magicShieldBonus, float hpBonus, float rageBonusFlatPercent,
        BonusStat bonusStat, PassiveSkillData passive, string folder, CharacterClass[] classes)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemName = displayName;
        item.slot = EquipmentSlot.Armor;
        item.tier = tier;
        item.itemLevel = 1;
        item.allowedClasses = classes;
        item.physicalDefense = physicalDefense;
        item.magicShieldBonus = magicShieldBonus;
        item.hpBonus = hpBonus;
        item.rageBonusFlatPercent = rageBonusFlatPercent;
        item.bonusStat = bonusStat;
        item.passiveSkill = passive;
        AssetDatabase.CreateAsset(item, $"{folder}/{assetName}.asset");
        return item;
    }

    // Трофей — слот "Шлем" без единой единицы брони: вместо неё флэт-урон оружию
    // (BonusStatType.WeaponDamageFlat, линейно baseValue × УровеньПредмета).
    static ItemData CreateTrophy(string assetName, string displayName, ItemTier tier, float flatDamagePerLevel,
        PassiveSkillData passive)
    {
        return CreateHelmetLike(assetName, displayName, tier,
            maxPhysicalDefenseBonus: 0f, magicShieldBonus: 0f,
            bonusStat: Bonus(BonusStatType.WeaponDamageFlat, flatDamagePerLevel), passive: passive,
            folder: TrophiesFolder, classes: BarbarianOnly);
    }

    // portrait НЕ назначается: файл→класс маппинг для Sasha.png/Violet.png не поставлен (см. открытый
    // вопрос 2 плана), пустой портрет корректно откатывается на плейсхолдер (3.8).
    static CharacterData CreateCharacter(string assetName, string displayName, CharacterClass characterClass,
        int baseHealth, int healthPerLevel, PassiveSkillData uniquePassive, ActiveSkillData uniqueActive,
        ItemData[] startingEquipment)
    {
        var character = ScriptableObject.CreateInstance<CharacterData>();
        character.characterName = displayName;
        character.characterClass = characterClass;
        character.baseHealth = baseHealth;
        character.healthPerLevel = healthPerLevel;
        character.uniquePassiveSkill = uniquePassive;
        character.uniqueActiveSkill = uniqueActive;
        character.startingEquipment = startingEquipment;
        AssetDatabase.CreateAsset(character, $"{CharactersFolder}/{assetName}.asset");
        return character;
    }
}
