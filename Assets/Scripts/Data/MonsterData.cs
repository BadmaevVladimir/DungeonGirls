using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMonster", menuName = "DungeonGirls/Monster")]
public class MonsterData : ScriptableObject
{
    public string monsterName;
    public Sprite sprite; // 10.6: пиксель-арт спрайт монстра (64x64).
    public bool isBoss;

    public float hp;
    public float damageMin;
    public float damageMax;
    public DamageType damageType;
    public float attackSpeed;

    public float physicalDefense;
    public float magicDefense;
    public float universalShield;
    public bool frontlinePriority;

    [Tooltip("Сущность вообще не получает оружия и никогда не атакует (Свечи Свечника). Нужна "+
        "именно как цель: каждый лишний атакующий на сцене — лишний бросок против уклонения "+
        "Вайолет, а её 15 HP базы этого не прощают.")]
    public bool doesNotAttack;

    public PassiveSkillData passiveSkill;

    // Boss framework (минимальный слайс, см. Docs/Design/2026-09-01-floor-boss-system-design.md):
    // опциональный компаньон, только для isBoss=true. null = монстр (даже с isBoss=true) продолжает
    // работать через старую CombatManager.TickBossHeavyAttacks — не ломает существующих боссов без
    // авторского контента (см. CombatantFactory.CreateMonsterCombatant).
    public BossKitData bossKit;

    // План 9 (Зеркальный Двойник): наборы способностей по классу игрока. Пусто у всех остальных
    // боссов — это исключение, а не общий механизм. bossKit выше остаётся набором ПО УМОЛЧАНИЮ:
    // он же и срабатывает, если для класса игрока ветки нет (например, Маг — его в игре нет).
    public List<BossClassVariantKit> classVariantKits = new List<BossClassVariantKit>();

    // 2.8: род названия монстра — согласование прилагательного модификатора.
    public MonsterGender gender = MonsterGender.Masculine;

    // 2.4: минимальный этаж, с которого монстр может попасться в обычной боевой комнате (1/4/7/10
    // по черновому распределению ГДД). Монстр доступен на этом этаже И ВЫШЕ (тиры суммируются,
    // не заменяют друг друга — см. "Черновое распределение по этажам" в 2.4).
    public int minFloorTier = 1;
}
