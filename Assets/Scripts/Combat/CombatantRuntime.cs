using System.Collections.Generic;
using UnityEngine;

// Временный дебафф, влияющий на скорость атаки (проклятие Колдуна, будущие эффекты и т.п.).
public class ActiveDebuff
{
    public string Id;
    public float RemainingTime;
    public float AttackSpeedMultiplier = 1f;

    // Финальный ревью-фикс #2: ActiveDebuffs изначально мыслился как список ТОЛЬКО дебаффов
    // (Проклятие замедления и т.п.), но "На волоске" (3.11, Плут) хранит в нём БАФФ скорости
    // атаки (AttackSpeedMultiplier > 1f) — тот же список используется просто как "временные
    // модификаторы скорости атаки с таймером". IsBuff=true исключает запись из HasActiveDebuff
    // ниже, чтобы "Несгибаемый" (бонус урона "пока есть активный дебафф") не срабатывал ложно
    // от собственного баффа персонажа.
    public bool IsBuff;
}

public class CombatantRuntime
{
    public string DisplayName;
    public bool IsPlayer;
    public bool IsBoss;

    // (доп.): пока true — TickCombatant пропускает этого участника целиком (таймер атаки не
    // копится, обычные атаки не резолвятся). Используется UI, чтобы обычная атака не могла начаться
    // и оборвать проигрывание анимации активного навыка (см. RunFlowController.Combat.cs,
    // OnActiveSkillActivated) — снижает ДПС на время анимации, это намеренно.
    public bool AttackLocked;

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

    // Боссы независимо от обычных атак готовят «Тяжёлую атаку» 5 секунд. Легаси-путь для боссов БЕЗ
    // BossKitData (см. BossEncounter ниже) — CombatManager.TickBossHeavyAttacks пропускает боссов,
    // у которых BossEncounter != null, чтобы не бить дважды.
    public float BossHeavyAttackTimer;
    public float BossHeavyAttackDamageMultiplier = 1.5f;

    // Boss framework (минимальный слайс): null для всех не-боссов и для боссов без bossKit (см.
    // MonsterData.bossKit/CombatantFactory). Владеет фазой/кулдаунами/ожидающим телеграфом этого
    // конкретного боя — см. BossEncounterState, CombatManager.TickBossEncounters.
    public BossEncounterState BossEncounter;

    // Boss framework (минимальный слайс) — отдельный shield pool способности (BossAbilityEffectKind.
    // ShieldPool), НЕ путать с MagicShieldCurrent/Max выше (тот — экипировка/маг. щит персонажа,
    // блокирует только Magical урон). Этот пул поглощает урон ЛЮБОГО типа ДО HP — см.
    // DamageCalculator.ApplyDamage. Генерик-поле на CombatantRuntime (не только для боссов), чтобы в
    // будущем его могли переиспользовать другие сущности/эффекты без новых полей.
    public float ShieldPoolMax;
    public float ShieldPoolCurrent;
    // float.PositiveInfinity = щит живёт, пока не поглотит весь урон (нет принудительного таймера).
    public float ShieldPoolExpireTimer = float.PositiveInfinity;

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
    // с этой пассивкой). Значение — ранг эффекта предмета (1–5). Вампиризм/Разрушение брони/Насквозь —
    // per-weapon поля на WeaponAttackState (см. выше), т.к. привязаны к конкретному оружию.
    public int ItemElusivenessLevel; // Эфирный доспех — складывается с "Уклонение" при уклонении от атаки
    public int ItemGoldenTouchLevel; // Корона Мидаса — бонус к валюте забега из сундука (8.2)
    public int ItemToughSoleLevel; // Бронированные сапоги — снижение урона от сработавших ловушек (5.5)
    public int ItemRepairLevel; // Молот кузнеца — бонус к восстановлению брони на привале (6.2)

    // 3.11 (Task 6b, item-passive wiring) — armor-bound пассивки эпических предметов, character-level,
    // тем же паттерном, что и три поля выше.
    public int ItemRiposteLevel; // "Рипост" — доп. флэт-урон = ранг эффекта (не больше 5) на первой атаке после уклонения
    public int ItemEmbraceOfNightLevel; // "Объятия ночи" — доп. маг. урон в Скрытности = ранг эффекта × обычный урон атаки × 1%
    public int ItemJustAScratchLevel; // "Просто царапина" (Эпический трофей) — разовое лечение в начале боя

    // 3.11 (Рипост) — одноразовый флаг: взводится при успешном уклонении ЭТОГО участника (если у
    // него есть ItemRiposteLevel > 0), потребляется на его СЛЕДУЮЩЕЙ атаке (не немедленно) — см.
    // CombatManager.ResolveAttack (взвод — в блоке уклонения, потребление — attacker.RiposteArmed).
    public bool RiposteArmed;

    public List<ActiveDebuff> ActiveDebuffs = new List<ActiveDebuff>();

    // 2.4/2.8: пассивка монстра (из monster.passiveSkill.skillId), SkillId.None = у монстра нет
    // пассивки (например, Каменный страж).
    public SkillId MonsterPassiveSkillId;

    // "Порхание" (Летучая мышь): флэт-бонус к шансу уклонения ЭТОГО участника, складывается с
    // SkillEvasionLevel/ItemElusivenessLevel в существующей формуле уклонения CombatManager.
    public float MonsterEvasionPercent;

    // Дополнительный прямой износ брони от модификатора «Бронебойный».
    public float MonsterGuaranteedArmorDamage;

    // "Оглушающий крик" (Гарпия): временный дебафф шанса крита ЭТОГО участника (обычно — игрока).
    public float CritChanceDebuffPercent;
    public float CritChanceDebuffTimer;

    // Вторая часть «Коррозии» Коррозийного паука: яд стакается до 3, каждый стек 4 урона/сек,
    // длительность 3 сек, обновляется при повторном наложении (не суммирует длительность).
    // Структурно похоже на HasBleed/BleedTimer, но с явным счётчиком стаков вместо фиксированного урона.
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
    // Источник нужен для критических тиков ур. 5: их шанс всегда равен текущему шансу крита наложившего эффект персонажа.
    public bool HasBleed;
    public float BleedDamagePerSecond;
    public float BleedTimer;
    public float BleedTickAccumulator;
    public int BleedLevel;
    public CombatantRuntime BleedSource;

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
    // монстрового PoisonStacks/PoisonTimer (Коррозийный паук, 2.4) — не суммируются, тикают независимо.
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

    // 3.11 (Боевая регенерация) — счётчик полученных ударов, сбрасывается при срабатывании. Считает
    // ЛЮБОЙ разрешённый удар по цели, включая полностью заблокированный (не только прошедший по HP) —
    // см. CombatManager.ResolveAttack, где инкремент стоит безусловно после блока/урона.
    public int HitsTakenSinceLastRegen;
    public float CombatRegenCooldownRemaining;

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

    public bool HasActiveDebuff => ActiveDebuffs.Exists(d => !d.IsBuff) || IsFrozen || FreezeStacks > 0;

    // 3.11 (Варвар) — "Ярость" = 1% + % недостающего HP, ПЕРЕСЧИТЫВАЕТСЯ динамически
    // (не хранимое поле). Так 100% достижимы ещё при 1% HP. Флэт-бонусы (Пояс титана)
    // добавляются поверх формулы, но итог всегда остаётся в честном диапазоне 0–100%.
    public float RageFlatBonusPercent;
    public float Rage => MaxHP > 0f
        ? Mathf.Clamp(1f + (1f - Mathf.Clamp01(CurrentHP / MaxHP)) * 100f + RageFlatBonusPercent, 0f, 100f)
        : 0f;

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
            // ФИКС (код-ревью): один division /100 переводит Rage×X (число-проценты) в дробь для
            // множителя — этого достаточно. Двойное деление (было /100/100) давало ~1% от нужной
            // величины. При Rage=100, ур.5 (X=1.0): multiplier *= 1f + 1.0f = +100% скорости атаки.
            multiplier *= 1f + (Rage * RageRules.SkillMultiplier(SkillFrenzyLevel) / 100f);
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
