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

    // 10.6: пиксель-арт спрайт персонажа/монстра (CharacterData.portrait / MonsterData.sprite),
    // для рендера в бою (CombatPanel). Может быть null (например, у Monster_Boss — арт ещё не готов).
    public Sprite Sprite;

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

    // 1, п.3: постоянный бонус к магическому урону от основного пассивного навыка наставника ("Магнум Опус").
    public float MentorMagicDamageBonusPercent;

    // Суммарный бонус к шансу крита от предметов (оружие/кольца/аксессуары), уже с учётом уровня предмета.
    public float CritChanceBonusFromItems;

    // 3.10 (ФИКС): остальные бонусные статы предметов (BonusStatType), раньше молча игнорировались
    // в CombatantFactory.AggregateEquipmentStats — только MagicShieldFlat/CritChancePercent реально
    // считались, семь остальных значений висели в ассетах предметов без эффекта. Суммарно по всему
    // снаряжению, с учётом уровня предмета (см. CombatantFactory).
    public float ItemAttackSpeedBonusPercent; // AttackSpeedPercent — множитель к GetEffectiveAttackSpeed
    public float ItemDamageBonusPercent; // DamagePercent — множитель к урону атаки (CombatManager.ResolveAttack)
    public float ItemEvasionBonusPercent; // EvasionPercent — складывается в общую формулу уклонения

    // 3.10: пассивки эпических предметов, не привязанные к конкретному оружию (0 = нет предмета
    // с этой пассивкой). Значение — уровень ПРЕДМЕТА. Вампиризм/Разрушение брони/Насквозь —
    // per-weapon поля на WeaponAttackState (см. выше), т.к. привязаны к конкретному оружию.
    public int ItemElusivenessLevel; // Эфирный доспех — складывается с "Уклонение" при уклонении от атаки
    public int ItemGoldenTouchLevel; // Корона Мидаса — бонус к валюте забега из сундука (8.2)
    public int ItemToughSoleLevel; // Бронированные сапоги — снижение урона от сработавших ловушек (5.5)
    public int ItemRepairLevel; // Молот кузнеца — бонус к восстановлению брони на привале (6.2)

    public List<ActiveDebuff> ActiveDebuffs = new List<ActiveDebuff>();

    // 2.4/2.8: пассивка монстра (MonsterSkillEffectMap-константа из monster.passiveSkill.skillName),
    // null = у монстра нет пассивки (например, Каменный страж).
    public string MonsterPassiveName;

    // "Порхание" (Летучая мышь): флэт-бонус к шансу уклонения ЭТОГО участника, складывается с
    // SkillEvasionLevel/ItemElusivenessLevel в существующей формуле уклонения CombatManager.
    public float MonsterEvasionPercent;

    // "Оглушающий крик" (Гарпия): временный дебафф шанса крита ЭТОГО участника (обычно — игрока).
    public float CritChanceDebuffPercent;
    public float CritChanceDebuffTimer;

    // "Яд" (Ядовитый паучок): стакается до 3, каждый стек 4 урона/сек, длительность 3 сек, обновляется
    // при повторном наложении (не суммирует длительность). Структурно похоже на HasBleed/BleedTimer,
    // но с явным счётчиком стаков вместо фиксированного урона.
    public int PoisonStacks;
    public float PoisonTimer;
    public float PoisonTickAccumulator;

    // "Тёмное исцеление" (Жрец тьмы) / "Двойной удар" (Рыцарь тьмы): периодические пассивки монстра,
    // не привязанные к таймеру атаки оружия — тикают отдельно в CombatManager.TickMonsterPeriodicPassives.
    public float MonsterPassiveCooldownTimer;

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

    // 3.11 (Плут) — "Скрытность": булево состояние + таймер, длительность всегда 3с (StealthStatus).
    // Персонаж начинает бой БЕЗ Скрытности (не устанавливается здесь, только объявлено — StartCombat
    // в CombatManager не трогает эти поля, они остаются false/0 по умолчанию у нового CombatantRuntime).
    public bool IsStealthed;
    public float StealthTimer;

    // 3.11 (Плут, классовые навыки) — уровни известны только игроку, копируются в
    // CombatantFactory.ApplyCharacterSkills так же, как остальные Skill*Level поля.
    public int SkillEyeForAnEyeLevel; // "В глаз"
    public int SkillPoisonedBladeLevel; // "Отравленный клинок"
    public int SkillByAThreadLevel; // "На волоске"
    public int SkillEliminationLevel; // "Устранение" — переопределяет крит-множитель, см. CritDamageMultiplierPercent
    public int SkillSlipAwayLevel; // "Ускользание"
    public int UniqueShadowLevel; // пассивка "Тень" (только Плут)

    // 3.11 (Плут) — "Отравленный клинок" накладывает СВОЙ яд на цель, отдельная сущность от
    // монстрового PoisonStacks/PoisonTimer (Ядовитый паучок, 2.4) — не суммируются, тикают независимо.
    public int RoguePoisonStacksOnTarget; // хранится на ЦЕЛИ (симметрично PoisonStacks у монстров)
    public float RoguePoisonTimer;
    public float RoguePoisonTickAccumulator;

    // 3.11 (Устранение) — переопределяет базовый крит-множитель 150% (см. CombatManager.ResolveAttack
    // `damage *= 1.5f`), null = нет навыка, используется база. Аналогичный паттерн для Barbarian ниже.
    public float? CritDamageMultiplierOverridePercent;

    // 3.11 (Дымовая граната) — счётчик гарантированных критов от активного навыка, отдельно от
    // IsStealthed (Скрытность может обновляться другими источниками во время того же окна).
    public int SmokeBombGuaranteedCritsRemaining;

    // 3.11 (Дымовая граната) — уровень уникальной активки Плута (1/2/3 = столько гарантированных
    // критов заряжается за активацию). Отсутствовало в исходном списке Task 1 — добавлено здесь
    // как явная зависимость CombatManager.TryActivateUniqueActiveSkill. Копируется в
    // CombatantFactory.ApplyCharacterSkills из progress.UniqueActiveLevel, тем же паттерном, что
    // и планируемый UniqueBerserkLevel у Варвара.
    public int UniqueSmokeBombLevel;

    // 3.11 (Варвар) — классовые навыки, копируются так же, как Rogue-поля выше.
    public int SkillStubbornnessLevel; // "Упёртость"
    public int SkillFrenzyLevel; // "Остервенелость" — общий индекс X (0.7/0.75/0.8/0.9/1.0) для 3 навыков
    public int SkillCombatRegenLevel; // "Боевая регенерация"
    public int SkillIntimidationLevel; // "Запугивание"
    public int SkillSuperstitionLevel; // "Суеверность"
    public int UniqueChampionOfTheTribeLevel; // пассивка "Чемпион племени"

    // 3.11 (Боевая регенерация) — счётчик полученных ударов по HP, сбрасывается при срабатывании.
    public int HitsTakenSinceLastRegen;

    // 3.11 (Берсерк) — ручной тумблер, не кулдаун-навык. Уровень 0 = навык не изучен = тумблер
    // недоступен (UI должен это учитывать так же, как ActiveSkillButton.SetEnabled для обычных
    // активок, см. RunFlowController).
    public int UniqueBerserkLevel;
    public bool IsBerserkActive;
    public float BerserkTickAccumulator;

    // 3.11 (Часть 2, "% сопротивления урону") — общая механика, применяется в DamageCalculator ДО
    // брони/щита. Суммируется, если несколько источников одного типа (сейчас только один источник на
    // тип у Варвара — Суеверность даёт магическое, Берсерк даёт физическое — но поле уже суммируемое
    // на будущее). Кламп на 100% — см. DamageCalculator.
    public float PhysicalResistancePercent;
    public float MagicalResistancePercent;

    // 3.11 (Чемпион племени, Варвар) — крит-шанс ВСЕГДА равен Rage×X%, полностью заменяя обычную
    // формулу; остальные источники крит-шанса конвертируются 1%→+2% крит-урона вместо суммирования в
    // шанс. true только если навык изучен (уровень > 0).
    public bool CritChanceReplacedByRage;

    public bool IsAlive => CurrentHP > 0f;

    public bool HasActiveDebuff => ActiveDebuffs.Count > 0 || IsFrozen || FreezeStacks > 0;

    // 3.11 (Варвар) — "Ярость" = % недостающего HP, ПЕРЕСЧИТЫВАЕТСЯ динамически (не хранимое поле).
    // Флэт-бонусы (Пояс титана) добавляются здесь же поверх формулы — могут увести Rage выше 100%.
    public float RageFlatBonusPercent;
    public float Rage => MaxHP > 0f
        ? Mathf.Max(0f, (1f - Mathf.Clamp01(CurrentHP / MaxHP)) * 100f + RageFlatBonusPercent)
        : 0f;

    // 3.11 (Варвар) — общая таблица X-по-уровню (0.7/0.75/0.8/0.9/1.0), используемая "Остервенелостью"
    // здесь и (отдельной копией) CombatManager для "Запугивания"/"Суеверности"/"Чемпиона племени" —
    // CombatantRuntime не MonoBehaviour и не должен тянуть зависимость на CombatManager ради одной таблицы.
    static float RageSkillMultiplierTable(int level) => level switch
    {
        1 => 0.7f, 2 => 0.75f, 3 => 0.8f, 4 => 0.9f, 5 => 1.0f, _ => 0f
    };

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
        multiplier *= 1f + ItemAttackSpeedBonusPercent / 100f; // 3.10 (ФИКС): AttackSpeedPercent от снаряжения

        // 3.11 (Варвар) — "Остервенелость": скорость атаки растёт с текущей Яростью.
        if (SkillFrenzyLevel > 0)
        {
            multiplier *= 1f + (Rage * RageSkillMultiplierTable(SkillFrenzyLevel) / 100f) / 100f;
        }

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
