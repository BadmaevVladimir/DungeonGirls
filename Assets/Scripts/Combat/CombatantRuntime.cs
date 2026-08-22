using System.Collections.Generic;
using UnityEngine;

// Временный дебафф, влияющий на скорость атаки (проклятие Колдуна, будущие эффекты и т.п.).
public class ActiveDebuff
{
    public string Id;
    public float RemainingTime;
    public float AttackSpeedMultiplier = 1f;
}

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

    // Одно оружие — у монстров и большинства снаряжения персонажа; два — при дуал-вилде
    // (3.9 "Амбидекстрия"), каждое со своим независимым таймером атаки.
    public List<WeaponAttackState> Weapons = new List<WeaponAttackState>();

    public CombatantRuntime Target;

    // 4.3: кулдаун уникального активного навыка (только у игрока в прототипе).
    public float ActiveSkillCooldownTimer;

    // Уровни навыков из 3.9, известных этому участнику боя (0 = не известен).
    // На практике заполняются только у игрока через CombatantFactory.ApplyCharacterSkills.
    public int SkillFreezeLevel;
    public int SkillLuckLevel;
    public int SkillEvasionLevel;
    public int SkillSturdyLevel;
    public int SkillCriticalHitsLevel;
    public int SkillIAmTheWallLevel;
    public int SkillAmbidexterityLevel;
    public int SkillThornsLevel;
    public int SkillUnyieldingLevel;
    public int SkillBleedLevel;

    // Суммарный бонус к шансу крита от предметов (оружие/кольца/аксессуары), уже с учётом уровня предмета.
    public float CritChanceBonusFromItems;

    public List<ActiveDebuff> ActiveDebuffs = new List<ActiveDebuff>();

    // Состояние "Заморозки" (общий навык, см. 3.9).
    public int FreezeStacks;
    public float FreezeStackTimer;
    public bool IsFrozen;
    public float FreezeTimer;
    public bool FreezeImmune;
    public float FreezeImmuneTimer;

    // Состояние "Кровотечения" (навык класса Воин, см. 3.9). Не стакается, только одна активная копия.
    public bool HasBleed;
    public float BleedDamagePerSecond;
    public float BleedTimer;
    public float BleedTickAccumulator;

    public bool IsAlive => CurrentHP > 0f;

    public bool HasActiveDebuff => ActiveDebuffs.Count > 0 || IsFrozen || FreezeStacks > 0;

    // Дебаффы скорости атаки (проклятие Колдуна и т.п.) и стаки заморозки действуют на персонажа
    // целиком, поэтому одинаково множат скорость атаки каждого из его оружий.
    public float GetEffectiveAttackSpeed(WeaponAttackState weapon)
    {
        float multiplier = 1f;
        foreach (var debuff in ActiveDebuffs)
        {
            multiplier *= debuff.AttackSpeedMultiplier;
        }

        multiplier *= Mathf.Max(0.01f, 1f - FreezeStacks * 0.05f);
        return Mathf.Max(0.01f, weapon.AttackSpeed * multiplier);
    }

    public float GetEffectiveAttackInterval(WeaponAttackState weapon)
    {
        return weapon.AttackSpeed > 0f ? 1f / GetEffectiveAttackSpeed(weapon) : float.PositiveInfinity;
    }

    public void RestoreMagicShield()
    {
        MagicShieldCurrent = MagicShieldMax;
    }
}
