// Активные-скилы-панель (2026-09-03): рантайм-состояние ОДНОГО сконфигурированного слота на
// панели скиллов. CombatManager.ActiveSkills — список таких состояний (сегодня всегда из 1
// элемента на класс, инфраструктура готова к N). Cooldown-поля (CooldownTimer/AutoMode) и
// Toggle-поле (IsToggleActive) сосуществуют в одном классе — какие из них значимы, определяет
// Data.skillType (см. ActiveSkillData/ActiveSkillType).
public class ActiveSkillRuntimeState
{
    public ActiveSkillData Data;
    public int HitCount;
    public float DamageMultiplierPerHit;
    public float CooldownTimer;
    public bool IsToggleActive;
    public bool AutoMode;
}

// Вход для CombatManager.ConfigureActiveSkills — то, что вызывающая сторона (RunFlowController)
// знает о скилле ДО начала боя (уровень-зависимый множитель урона/hitCount уже посчитаны снаружи,
// CombatManager сам левел-апы не считает).
public readonly struct ActiveSkillConfigEntry
{
    public readonly ActiveSkillData Data;
    public readonly int HitCount;
    public readonly float DamageMultiplierPerHit;
    public readonly bool AutoMode;

    public ActiveSkillConfigEntry(ActiveSkillData data, int hitCount, float damageMultiplierPerHit, bool autoMode)
    {
        Data = data;
        HitCount = hitCount;
        DamageMultiplierPerHit = damageMultiplierPerHit;
        AutoMode = autoMode;
    }
}
