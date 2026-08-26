// Рантайм-состояние одного оружия в бою: свой урон, тип урона, скорость атаки и таймер.
// У монстра и персонажа с одним оружием их ровно одно; у персонажа с двумя оружиями в руках —
// два независимых экземпляра (3.9 "Амбидекстрия": у каждого оружия свой таймер атаки по своей
// собственной скорости атаки, бьют независимо друг от друга).
public class WeaponAttackState
{
    public float DamageMin;
    public float DamageMax;
    public DamageType DamageType;
    public float AttackSpeed;
    public float AttackTimer;

    // 3.10: пассивки эпических предметов, привязанные к конкретному оружию (0 = нет пассивки).
    // Значение — уровень ПРЕДМЕТА (не навыка), масштаб задаётся в CombatManager.
    public int VampirismLevel;
    public int ArmorBreakLevel;
    public int PiercingLevel;

    // 3.10 (ФИКС): "Пробивание" — бонусный стат (BonusStatType.ArmorPenetrationFlat) Топора/Молота
    // редкого+ тира, привязан к конкретному оружию (в отличие от прочих бонусных статов —
    // сумма по всему снаряжению, см. CombatantRuntime.ItemAttackSpeedBonusPercent и соседние).
    // "Против бронированных целей урон считается на +N больше" — добавляется к урону этого
    // оружия перед проверкой брони в DamageCalculator (см. CombatManager.ResolveAttack).
    public float ArmorPenetrationFlat;
}
