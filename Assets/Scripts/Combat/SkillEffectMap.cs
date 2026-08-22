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
}
