public class CombatantRuntime
{
    public string DisplayName;
    public bool IsPlayer;

    public float MaxHP;
    public float CurrentHP;

    public float PhysicalDefenseMax;
    public float PhysicalDefenseCurrent;

    public float MagicShieldMax;
    public float MagicShieldCurrent;

    public float DamageMin;
    public float DamageMax;
    public DamageType DamageType;
    public float AttackSpeed;

    public float AttackTimer;
    public CombatantRuntime Target;

    public bool IsAlive => CurrentHP > 0f;

    public float AttackInterval => AttackSpeed > 0f ? 1f / AttackSpeed : float.PositiveInfinity;

    public void RestoreMagicShield()
    {
        MagicShieldCurrent = MagicShieldMax;
    }
}
