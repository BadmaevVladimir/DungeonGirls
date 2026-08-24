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
    // по уровню предмета через общую формулу StatScaling (см. 2.7 — та же формула у монстров).
    // Заменяет чистые +10%/уровень — минимум +1 к стату гарантирован на каждый уровень, даже
    // если 10% округляются в 0 (проблема была видна на Деревянном щите +3).
    // Бонусные статы/пассивки (bonusStat, VampirismLevel и т.п.) эту формулу НЕ используют — у
    // них остаются свои проценты (см. CombatantFactory).
    // Тир уже запечён в баланс-ассете (baseDamage/physicalDefense каждого тира авторизован с
    // учётом множителя тира, см. 3.10) — поэтому здесь достаточно взять сохранённое поле как есть.
    public float EffectiveDamage => StatScaling.ApplyLevelBonus(baseDamage, itemLevel);
    public float EffectiveDefense => StatScaling.ApplyLevelBonus(physicalDefense, itemLevel);
    public float EffectiveMaxDefenseBonus => StatScaling.ApplyLevelBonus(maxPhysicalDefenseBonus, itemLevel);
}
