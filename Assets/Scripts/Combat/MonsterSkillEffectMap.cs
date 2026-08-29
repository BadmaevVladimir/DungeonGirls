// Assets/Scripts/Combat/MonsterSkillEffectMap.cs
// Связывает боевую логику монстров с ассетами PassiveSkillData (2.4) по их skillName — аналог
// SkillEffectMap для навыков персонажа. "Проклятие замедления" (Колдун) уже существовало как
// ассет (Skill_SlowCurse) с самой первой сессии, но никогда не было подключено к CombatManager —
// эта карта исправляет и его тоже, заодно с 6 новыми монстрами 2.4.
public static class MonsterSkillEffectMap
{
    public const string SlowCurse = "Проклятие замедления"; // Колдун — уже существовавший ассет
    public const string Fluttering = "Порхание"; // Летучая мышь
    public const string ArmorPiercingBlade = "Бронебойный клинок"; // Гоблин-вор: игнор части брони
    public const string Corrosion = "Коррозия"; // Коррозийный паук
    public const string StunningScream = "Оглушающий крик"; // Гарпия
    public const string DarkHeal = "Тёмное исцеление"; // Жрец тьмы
    public const string DoubleStrike = "Двойной удар"; // Рыцарь тьмы
    // Каменный страж не имеет пассивки (2.4) — только базовые статы.
}
