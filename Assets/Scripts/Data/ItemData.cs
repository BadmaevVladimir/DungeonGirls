using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "DungeonGirls/Item")]
public class ItemData : ScriptableObject
{
    // Пустой ID означает legacy/base-предмет, который всегда остаётся в loot pool.
    public string prototypeId;
    public WeaponPrototypeEffectId prototypeEffect;
    // Generic data knobs used only by the selected prototypeEffect.
    public float prototypePrimaryValue;
    public float prototypeSecondaryValue;
    public int prototypeMaxStacks;
    public string itemName;
    public Sprite icon; // 10.6: пиксель-арт иконка предмета (64x64), общая для всех тиров архетипа.
    public EquipmentSlot slot;
    public WeaponSubtype weaponSubtype = WeaponSubtype.None;

    // 3.11: двуручное оружие (Варвар) занимает ОБА слота оружия/рук как единый предмет — при
    // экипировке заменяет оба текущих предмета в слотах рук одновременно (исключение из обычной
    // независимой логики слотов 3.4, см. CharacterManager.EquipItem). Only one variant exists today
    // (Двуручный топор, weaponSubtype = TwoHandedAxe) but the GDD explicitly leaves room for more.
    public bool isTwoHanded;
    public ItemTier tier;
    public int itemLevel = 1;
    // Отдельный, сохранённый ранг специальных Cursed-эффектов. Старые предметы оставляют 0 и
    // продолжают выводить ранг пассивки из itemLevel через StatScaling.ItemEffectRank.
    [Range(0, 5)] public int itemRank;
    public CharacterClass[] allowedClasses;

    public CursedEffectId cursedEffect;
    [TextArea] public string positiveEffectDescription;
    [TextArea] public string curseDescription;
    [TextArea] public string handUsageDescription;
    // Парное оружие — один ItemData/эквип, но два независимых источника атаки в бою.
    public bool isPairedWeapon;

    public float baseDamage;
    public DamageType damageType = DamageType.Physical;
    public float attackSpeed;

    public float physicalDefense;
    public float maxPhysicalDefenseBonus;

    // 3.11: у экипировки Плута/Варвара маг. щит и HP — это ОСНОВНЫЕ статы предмета, а не бонусы:
    // ГДД описывает Капюшон/Кожанку как "+3 физ.защ, +5 маг.щит" (два равноправных основных стата),
    // а Пояс — как чистый HP-предмет. Поэтому у них выделенные поля со ШКАЛОЙ ОСНОВНОГО СТАТА
    // (StatScaling, как physicalDefense), а не BonusStat с линейным baseValue × itemLevel.
    // Побочная причина: у одного предмета bonusStat ровно один, а этим предметам нужно 2-3
    // одновременных числа (напр. Пояс титана: HP + %урона + %Ярости).
    public float magicShieldBonus;
    public float hpBonus;

    // 3.11: Пояс титана (Эпик) — "+УровеньПредмета × 1%" к Ярости. Здесь шкала ЛИНЕЙНАЯ (как у
    // bonusStat), отдельное поле нужно только потому, что слот bonusStat уже занят %урона.
    public float rageBonusFlatPercent;

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
    public int EffectRank => cursedEffect != CursedEffectId.None
        ? Mathf.Clamp(itemRank <= 0 ? 1 : itemRank, 1, 5)
        : StatScaling.ItemEffectRank(itemLevel);

    // Старые ассеты уже хранят умноженную на тир базу. Cursed хранит базу архетипа и применяет
    // явно утверждённый ×2.2, а level-инкремент считает именно от базы до множителя.
    public float EffectiveDamage => tier == ItemTier.Cursed
        ? StatScaling.ApplyTierAndLevel(baseDamage, 2.2f, itemLevel)
        : StatScaling.ApplyLevelBonus(baseDamage, itemLevel);
    public float EffectiveDefense => StatScaling.ApplyLevelBonus(physicalDefense, itemLevel);
    public float EffectiveMaxDefenseBonus => StatScaling.ApplyLevelBonus(maxPhysicalDefenseBonus, itemLevel);
    public float MagicShieldEffective => StatScaling.ApplyLevelBonus(magicShieldBonus, itemLevel);
    public float HpBonusEffective => StatScaling.ApplyLevelBonus(hpBonus, itemLevel);
}
