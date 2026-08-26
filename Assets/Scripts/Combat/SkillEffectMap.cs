// Связывает боевую логику с ассетами PassiveSkillData из Фазы 2 по их skillName.
// Имена должны совпадать с полем skillName созданных ассетов Assets/ScriptableObjects/Skills.
public static class SkillEffectMap
{
    public const string Freeze = "Заморозка";
    public const string Luck = "Удача";
    public const string Evasion = "Уклонение";
    public const string Sturdy = "Прочный";
    public const string CriticalHits = "Критические атаки";
    public const string IAmTheWall = "Я — стена";
    public const string Ambidexterity = "Амбидекстрия";
    public const string Thorns = "Шипы";
    public const string Unyielding = "Несгибаемый";
    public const string Bleed = "Кровотечение";

    // 3.10: пассивки эпических предметов (ItemData.passiveSkill.skillName). Каждая масштабируется
    // с уровнем ПРЕДМЕТА (item.itemLevel), а не с уровнем персонажа/навыка.
    public const string Vampirism = "Вампиризм"; // Кровавый меч
    public const string ArmorBreak = "Разрушение брони"; // Рубило
    public const string Piercing = "Насквозь"; // Стремительное копьё
    public const string Repair = "Ремонт"; // Молот кузнеца
    public const string Elusiveness = "Неуловимость"; // Эфирный доспех
    public const string GoldenTouch = "Золотое касание"; // Корона Мидаса
    public const string ToughSole = "Крепкая подошва"; // Бронированные сапоги

    // 3.11: Плут — классовые навыки (пул уровня персонажа, PassiveSkillData.maxLevel = 5).
    public const string EyeForAnEye = "В глаз";
    public const string PoisonedBlade = "Отравленный клинок";
    public const string ByAThread = "На волоске";
    public const string Elimination = "Устранение";
    public const string SlipAway = "Ускользание";

    // 3.11: Варвар — классовые навыки.
    public const string Stubbornness = "Упёртость";
    public const string Frenzy = "Остервенелость";
    public const string CombatRegen = "Боевая регенерация";
    public const string Intimidation = "Запугивание";
    public const string Superstition = "Суеверность";

    // 3.11: уникальные пассивки/активки новых классов (по одной паре на класс).
    public const string Shadow = "Тень"; // Плут, уникальная пассивка
    public const string SmokeBomb = "Дымовая граната"; // Плут, уникальная активка
    public const string ChampionOfTheTribe = "Чемпион племени"; // Варвар, уникальная пассивка
    public const string Berserk = "Берсерк"; // Варвар, уникальная активка (тумблер)

    // 3.11: пассивки эпических предметов новых классов (масштабируются с item.itemLevel, как 3.10).
    public const string Riposte = "Рипост"; // Капюшон Дуэльянта
    public const string EmbraceOfNight = "Объятия ночи"; // Кожанка "Объятия ночи"
    public const string Execution = "Казнь"; // Клинок "Моменто Мори"
    public const string GiantSlayer = "Убийца великанов"; // Двуручный топор "Головоруб"
    public const string JustAScratch = "Просто царапина"; // Эпический трофей
}
