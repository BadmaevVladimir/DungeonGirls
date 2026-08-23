using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "DungeonGirls/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public EquipmentSlot slot;
    public WeaponSubtype weaponSubtype = WeaponSubtype.None;
    public ItemTier tier;
    public int itemLevel = 1;
    public CharacterClass[] allowedClasses;

    public float baseDamage;
    public DamageType damageType = DamageType.Physical;
    public float attackSpeed;

    public float physicalDefense;
    public float maxPhysicalDefenseBonus;

    public BonusStat bonusStat;
    public PassiveSkillData passiveSkill;

    // 3.10 [ОБНОВЛЕНО после плейтеста]: масштабирование ОСНОВНОГО стата предмета (урон/защита)
    // по уровню предмета. Заменяет чистые +10%/уровень — минимум +1 к стату гарантирован на
    // каждый уровень, даже если 10% округляются в 0 (проблема была видна на Деревянном щите +3).
    // Бонусные статы/пассивки (bonusStat, VampirismLevel и т.п.) эту формулу НЕ используют — у
    // них остаются свои проценты (см. CombatantFactory).
    // ФинальныйСтат = БазовыйСтат × МножительТира + МАКС(1, ОКРУГЛ(БазовыйСтат × 0.1)) × (Уровень−1).
    // Тир уже запечён в баланс-ассете (baseDamage/physicalDefense каждого тира авторизован с
    // учётом множителя тира, см. 3.10) — поэтому здесь достаточно взять сохранённое поле как есть.
    static float ScaleMainStat(float baseStat, int itemLevel)
    {
        // baseStat == 0 значит "у этого предмета нет такого стата" (напр. physicalDefense у
        // оружия) — масштабировать нечего, иначе формула ошибочно родила бы стат из ничего.
        if (baseStat <= 0f)
        {
            return baseStat;
        }

        float increment = Mathf.Max(1f, Mathf.Round(baseStat * 0.1f));
        return baseStat + increment * (itemLevel - 1);
    }

    public float EffectiveDamage => ScaleMainStat(baseDamage, itemLevel);
    public float EffectiveDefense => ScaleMainStat(physicalDefense, itemLevel);
    public float EffectiveMaxDefenseBonus => ScaleMainStat(maxPhysicalDefenseBonus, itemLevel);
}
