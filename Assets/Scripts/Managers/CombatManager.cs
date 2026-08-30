using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public CombatantRuntime Player { get; private set; }
    public List<CombatantRuntime> Enemies { get; private set; } = new List<CombatantRuntime>();
    public bool IsCombatActive { get; private set; }

    // Принудительное завершение используется только при явном выходе игрока из забега через паузу.
    public void AbortCombat()
    {
        IsCombatActive = false;
        if (Player != null) Player.IsBerserkActive = false;
    }

    // Позволяет UI подписаться на текстовый лог боя (7.2), не читая консоль Unity.
    public event System.Action<string> LogMessage;

    // "Карманник" (2.4): (жертва, украденный % текущей валюты забега).

    // 4.7: визуальный фидбэк боя. (цель, урон по HP [0, если полностью заблокировано], крит?,
    // заблокировано?) — единая точка для всплывающих цифр урона и тряски спрайта цели.
    public event System.Action<CombatantRuntime, float, bool, bool> HitResolved;

    // 4.7: (участник, активировавший навык, название навыка) — для баннера активации навыка.
    // Архитектурно не привязано к игроку — на будущее, если у монстров появятся активные навыки.
    public event System.Action<CombatantRuntime, string> ActiveSkillActivated;

    void Log(string message)
    {
        Debug.Log(message);
        LogMessage?.Invoke(message);
    }

    // 3.11 (Варвар) — общая таблица X-по-уровню (0.7/0.75/0.8/0.9/1.0), общая для "Остервенелости"
    // (собственная копия на CombatantRuntime — см. её комментарий), "Запугивания", "Суеверности" и
    // "Чемпиона племени".
    static float RageSkillMultiplier(int level) => level switch
    {
        1 => 0.7f, 2 => 0.75f, 3 => 0.8f, 4 => 0.9f, 5 => 1.0f, _ => 0f
    };

    // 3.11 (Варвар) — "Упёртость": СВОЯ, отдельная от RageSkillMultiplier, таблица порогов Ярости
    // (90/80/70/60/50%), выше которых персонаж полностью игнорирует НОВЫЕ дебаффы.
    static float StubbornnessThreshold(int level) => level switch
    {
        1 => 90f, 2 => 80f, 3 => 70f, 4 => 60f, 5 => 50f, _ => 101f
    };

    static bool IgnoresDebuffs(CombatantRuntime target) =>
        target.SkillStubbornnessLevel > 0 && target.Rage > StubbornnessThreshold(target.SkillStubbornnessLevel);

    // 4.3: уникальный активный навык персонажа (3.1 "3 быстрые атаки" — единственный активный
    // навык прототипа, общего пула активных навыков в 3.9 нет). Настраивается при входе в бой,
    // т.к. зависит от текущего уровня навыка игрока.
    int activeSkillHitCount;
    float activeSkillDamageMultiplierPerHit;
    float activeSkillCooldownSeconds;
    string activeSkillName;
    SkillId activeSkillId;
    bool activeSkillAutoMode = true;

    public bool IsActiveSkillConfigured { get; private set; }
    public float ActiveSkillCooldownRemaining => Player != null ? Mathf.Max(0f, Player.ActiveSkillCooldownTimer) : 0f;
    public bool IsActiveSkillReady => Player != null && Player.IsAlive && Player.ActiveSkillCooldownTimer <= 0f;

    // Финальный ревью-фикс #4: эта конфигурация/hit-loop машинерия (TryActivateUniqueActiveSkill)
    // была построена под Дженнифер ("3 быстрые атаки", см. RunFlowController). Берсерк (Варвар)
    // НИКОГДА не должен проходить через неё — он не кулдаун-навык, а ручной тумблер, включаемый
    // ТОЛЬКО через SetBerserkActive; TryActivateUniqueActiveSkill жёстко бейлит на нём (см. ниже).
    // Дымовая граната (Плут) должна проходить через неё, но с hitCount: 0 — она сама не бьёт,
    // только даёт Скрытность + гарантированные криты на СЛЕДУЮЩИЕ обычные атаки; ниже она тоже
    // жёстко возвращается до hit-loop, так что фактический hitCount с будущего character-select
    // UI для неё не важен, но 0 — по-прежнему корректное намерение при конфигурации.
    public void ConfigureUniqueActiveSkill(int hitCount, float damageMultiplierPerHit, float cooldownSeconds, bool autoMode, string skillName, SkillId skillId)
    {
        activeSkillHitCount = hitCount;
        activeSkillDamageMultiplierPerHit = damageMultiplierPerHit;
        activeSkillCooldownSeconds = cooldownSeconds;
        activeSkillName = skillName;
        activeSkillId = skillId;
        activeSkillAutoMode = autoMode;
        IsActiveSkillConfigured = true;
    }

    public void SetActiveSkillAutoMode(bool autoMode)
    {
        activeSkillAutoMode = autoMode;
    }

    // 3.11 (Варвар) — "Берсерк": ручной тумблер, а не кулдаун-навык, только у игрока (ГДД явно
    // говорит, что монстры этот навык никогда не получают). Хук для будущей UI-кнопки (out of
    // scope этого плана, см. RunFlowController) — мирроит форму SetActiveSkillAutoMode выше.
    public void SetBerserkActive(bool active)
    {
        if (Player == null)
        {
            return;
        }

        // ФИКС (код-ревью): деактивация всегда разрешена (безопасно и защитно), но нельзя ВКЛЮЧИТЬ
        // Берсерк персонажу, который его не изучил — без этой проверки он получал бы самоурон тика
        // при 0% сопротивления (UniqueBerserkLevel switch ниже даёт 0 для уровня 0).
        if (active && Player.UniqueBerserkLevel <= 0)
        {
            return;
        }

        Player.IsBerserkActive = active;
    }

    // 3.11 (Варвар) — "Суеверность"/"Берсерк": сопротивления зависят от ЖИВОЙ Ярости, поэтому
    // пересчитываются каждый Tick, а не запекаются один раз в CombatantFactory.ApplyCharacterSkills.
    void UpdateResistances(CombatantRuntime combatant)
    {
        // ФИКС (код-ревью): было двойное деление на 100 (Rage×X/100, затем ещё раз /100 ниже) —
        // MagicalResistancePercent хранится в ПРОЦЕНТНЫХ единицах (DamageCalculator сам делит на 100
        // при применении, см. ApplyDamage), точно как соседнее PhysicalResistancePercent от Берсерка
        // (10f/20f/30f без деления). Лишнее /100f давало ~1% от нужной величины сопротивления.
        combatant.MagicalResistancePercent = combatant.SkillSuperstitionLevel > 0
            ? combatant.Rage * RageSkillMultiplier(combatant.SkillSuperstitionLevel)
            : 0f;

        combatant.PhysicalResistancePercent = combatant.IsBerserkActive
            ? combatant.UniqueBerserkLevel switch { 1 => 20f, 2 => 30f, 3 => 40f, _ => 0f }
            : 0f;
    }

    // 4.3: ручной режим — доступна только по готовности кулдауна; автоматический — срабатывает
    // сама, без участия игрока (тикается в Tick()).
    public bool TryActivateUniqueActiveSkill()
    {
        if (!IsCombatActive || !IsActiveSkillConfigured || !IsActiveSkillReady || Player.Weapons.Count == 0)
        {
            return false;
        }

        // Финальный ревью-фикс #4: Берсерк — ручной тумблер (SetBerserkActive), а не hit-loop
        // активка. Его кулдаун конфигурируется как 0 (нет кулдауна по ГДД), из-за чего
        // IsActiveSkillReady был бы ПОСТОЯННО true — если бы этот метод не бейлил здесь, авто-режим
        // (Tick -> TryActivateUniqueActiveSkill) переигрывал бы полный комбо атак оружием КАЖДЫЙ
        // кадр. Недостижимо сегодня (у Варвара нет character-select UI за пределами этого плана),
        // но защищаемся заранее — см. комментарий на ConfigureUniqueActiveSkill выше.
        if (activeSkillId == SkillId.Berserk)
        {
            return false;
        }

        ActiveSkillActivated?.Invoke(Player, activeSkillName);

        // 3.11 "Дымовая граната" (уникальная активка Плута): при активации даёт Скрытность и
        // заряжает гарантированные криты на N последующих ОБЫЧНЫХ атак — распознаётся по имени
        // навыка, т.к. ConfigureUniqueActiveSkill уже получает его текстом (для баннера выше).
        if (activeSkillId == SkillId.SmokeBomb)
        {
            GrantOrRefreshStealth(Player);
            Player.SmokeBombGuaranteedCritsRemaining = Player.UniqueSmokeBombLevel;
            Log($"[Combat] «Дымовая граната»: {Player.DisplayName} получает Скрытность и {Player.UniqueSmokeBombLevel} гарантированных крита(ов).");
            Player.ActiveSkillCooldownTimer = activeSkillCooldownSeconds;

            // Финальный ревью-фикс #4: Дымовая граната НИКОГДА не бьёт сама — hit-loop ниже
            // (построенный под "3 быстрые атаки" Дженнифер) для неё пропускается безусловно, даже
            // если будущее character-select-подключение по ошибке передаст ненулевой hitCount.
            return true;
        }

        var weapon = Player.Weapons[0];
        for (int i = 0; i < activeSkillHitCount; i++)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                break;
            }

            // isRegularAttack: false — удары самой активки не должны расходовать/получать
            // гарантированные криты "Дымовой гранаты" (см. ResolveAttack).
            ResolveAttack(Player, weapon, activeSkillDamageMultiplierPerHit, isRegularAttack: false);
        }

        Player.ActiveSkillCooldownTimer = activeSkillCooldownSeconds;
        return true;
    }

    public void StartCombat(CombatantRuntime player, List<CombatantRuntime> enemies)
    {
        Player = player;
        Enemies = enemies ?? new List<CombatantRuntime>();
        IsCombatActive = true;

        ResetAttackTimers(Player);
        foreach (var enemy in Enemies)
        {
            ResetAttackTimers(enemy);
            if (enemy.IsBoss)
            {
                enemy.BossHeavyAttackTimer = 5f;
            }
        }

        // 4.3 (НОВОЕ): активный навык уходит в полный кулдаун сразу при старте боя, а не в 0 —
        // без этого навык (например "3 быстрые атаки") часто срабатывал мгновенно и сносил
        // противника до того, как игрок успевал его увидеть. Обычные атаки оружием (ResetAttackTimers
        // выше) это правило не затрагивает — они по-прежнему начинаются сразу по своей скорости атаки.
        Player.ActiveSkillCooldownTimer = activeSkillCooldownSeconds;
        Player.Target = GetDefaultTarget();

        // 3.11 (Task 6b, Эпический трофей): "Просто царапина" — разовое лечение РОВНО в начале боя,
        // только у игрока (у монстров предметов нет — ItemJustAScratchLevel всегда 0).
        if (Player.ItemJustAScratchLevel > 0)
        {
            Player.CurrentHP = Mathf.Min(Player.MaxHP, Player.CurrentHP + Player.MaxHP * ItemEffectBalance.JustAScratchHealPercent(Player.ItemJustAScratchLevel) / 100f);
        }

        Log($"[Combat] Бой начался: {Player.DisplayName} (HP {Player.CurrentHP:F1}) против {Enemies.Count} противников.");
    }

    public void EndCombat()
    {
        if (!IsCombatActive)
        {
            return;
        }

        IsCombatActive = false;

        // 3.3: магический щит восстанавливается до максимума после каждого боя; физ. защита — нет.
        Player.RestoreMagicShield();
        ResetTemporaryStatuses(Player);

        Log(Player.IsAlive
            ? $"[Combat] Бой окончен. {Player.DisplayName} побеждает."
            : $"[Combat] Бой окончен. {Player.DisplayName} погибает.");
    }

    // 4.5: всё временное боевое состояние заканчивается вместе с боем. Физическая броня
    // намеренно не входит в этот список — её износ сохраняется на весь забег.
    static void ResetTemporaryStatuses(CombatantRuntime combatant)
    {
        if (combatant == null) return;
        combatant.ActiveDebuffs.Clear();
        combatant.CritChanceDebuffPercent = 0f;
        combatant.CritChanceDebuffTimer = 0f;
        combatant.PoisonStacks = 0;
        combatant.PoisonTimer = 0f;
        combatant.PoisonTickAccumulator = 0f;
        combatant.RoguePoisonStacksOnTarget = 0;
        combatant.RoguePoisonTimer = 0f;
        combatant.RoguePoisonTickAccumulator = 0f;
        combatant.HasBleed = false;
        combatant.BleedDamagePerSecond = 0f;
        combatant.BleedTimer = 0f;
        combatant.BleedTickAccumulator = 0f;
        combatant.FreezeStacks = 0;
        combatant.FreezeStackTimer = 0f;
        combatant.IsFrozen = false;
        combatant.FreezeTimer = 0f;
        combatant.FreezeImmune = false;
        combatant.FreezeImmuneTimer = 0f;
        combatant.IsStealthed = false;
        combatant.StealthTimer = 0f;
        combatant.SmokeBombGuaranteedCritsRemaining = 0;
        combatant.RiposteArmed = false;
        combatant.HitsTakenSinceLastRegen = 0;
        combatant.CombatRegenCooldownRemaining = 0f;
        combatant.IsBerserkActive = false;
        combatant.BerserkTickAccumulator = 0f;
    }

    void Update()
    {
        if (IsCombatActive)
        {
            Tick(Time.deltaTime);
        }
    }

    // 10.2: у каждого участника боя свой таймер атаки, срабатывающий по достижении
    // интервала = 1 / СкоростьАтаки. Вынесен из Update() отдельным методом, чтобы бой
    // можно было тикать и без сцены/плеймода (например, из редакторских тестов).
    public void Tick(float deltaTime)
    {
        if (!IsCombatActive)
        {
            return;
        }

        // 3.11 (Варвар) — "Суеверность"/"Берсерк" дают сопротивление, зависящее от ЖИВОЙ Ярости
        // (пересчитывается каждый тик, а не один раз при создании боевого юнита — см. комментарий
        // на CombatantRuntime.Rage).
        UpdateResistances(Player);
        foreach (var enemy in Enemies)
        {
            UpdateResistances(enemy);
        }

        UpdateStatusEffects(Player, deltaTime);
        foreach (var enemy in Enemies)
        {
            UpdateStatusEffects(enemy, deltaTime);
        }

        // 3.11 (Берсерк, Варвар) — ручной тумблер, только у игрока (см. SetBerserkActive). Тикает как
        // кровотечение/яд — накопитель на 1 секунду, БЕЗ защиты от смерти (ГДД явно это оговаривает).
        if (Player.IsBerserkActive && Player.IsAlive)
        {
            Player.BerserkTickAccumulator += deltaTime;
            while (Player.BerserkTickAccumulator >= 1f && Player.IsAlive)
            {
                Player.BerserkTickAccumulator -= 1f;
                // [ПРЕДПОЛОЖЕНИЕ, см. Global Constraints] — урон от ТЕКУЩЕГО HP, не максимума; ГДД
                // сам помечает точную базу как неподтверждённую.
                float tickDamage = Mathf.Max(1f, Player.CurrentHP * 0.01f);
                Player.CurrentHP = Mathf.Max(0f, Player.CurrentHP - tickDamage);
                Log($"[Combat] «Берсерк» наносит {tickDamage:F1} урона {Player.DisplayName} (HP {Player.CurrentHP:F1}/{Player.MaxHP:F1}).");
            }
        }

        TickMonsterPeriodicPassives(deltaTime); // "Тёмное исцеление" / "Двойной удар" (2.4)
        TickBossHeavyAttacks(deltaTime);

        CheckCombatEnd();
        if (!IsCombatActive)
        {
            return;
        }

        TickCombatant(Player, deltaTime);

        foreach (var enemy in Enemies)
        {
            TickCombatant(enemy, deltaTime);
        }

        if (IsCombatActive && Player.IsAlive)
        {
            if (Player.ActiveSkillCooldownTimer > 0f)
            {
                Player.ActiveSkillCooldownTimer -= deltaTime;
            }

            if (IsActiveSkillConfigured && activeSkillAutoMode && IsActiveSkillReady)
            {
                TryActivateUniqueActiveSkill();
            }
        }

        CheckCombatEnd();
    }

    static void ResetAttackTimers(CombatantRuntime combatant)
    {
        foreach (var weapon in combatant.Weapons)
        {
            weapon.AttackTimer = 0f;
        }
    }

    // Боссы продолжают обычные атаки и параллельно готовят отдельную «Тяжёлую атаку».
    // Первый и каждый следующий удар происходят через 5 секунд; сила растёт 150% -> 175% ->
    // 200% на этажах 1-3 / 4-6 / 7-10.
    void TickBossHeavyAttacks(float deltaTime)
    {
        foreach (var enemy in Enemies)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                return;
            }

            if (!enemy.IsAlive || !enemy.IsBoss || enemy.Weapons.Count == 0)
            {
                continue;
            }

            enemy.BossHeavyAttackTimer -= deltaTime;
            if (enemy.BossHeavyAttackTimer > 0f)
            {
                continue;
            }

            enemy.BossHeavyAttackTimer += 5f;
            float multiplier = enemy.BossHeavyAttackDamageMultiplier;
            ActiveSkillActivated?.Invoke(enemy, "Тяжёлая атака");
            Log($"[Combat] {enemy.DisplayName} завершает подготовку «Тяжёлой атаки» ({multiplier * 100f:F0}% урона).");
            ResolveAttack(enemy, enemy.Weapons[0], multiplier, isRegularAttack: false);
        }
    }

    // 3.9 "Амбидекстрия": у каждого оружия персонажа свой независимый таймер атаки по своей
    // собственной скорости — обрабатываются в отдельных циклах, а не слитно одним таймером.
    void TickCombatant(CombatantRuntime attacker, float deltaTime)
    {
        // 3.9 "Заморозка": замороженный участник не может атаковать; таймеры атаки не копятся.
        if (!attacker.IsAlive || attacker.IsFrozen)
        {
            return;
        }

        foreach (var weapon in attacker.Weapons)
        {
            weapon.AttackTimer += deltaTime;

            while (IsCombatActive && attacker.IsAlive && !attacker.IsFrozen && weapon.AttackTimer >= attacker.GetEffectiveAttackInterval(weapon))
            {
                weapon.AttackTimer -= attacker.GetEffectiveAttackInterval(weapon);
                ResolveAttack(attacker, weapon);
            }
        }
    }

    // Дебаффы, влияющие на скорость атаки (Колдун и т.п.), стаки заморозки и кровотечение
    // тикают независимо от того, атакует ли участник в этом кадре.
    void UpdateStatusEffects(CombatantRuntime combatant, float deltaTime)
    {
        combatant.CombatRegenCooldownRemaining = Mathf.Max(0f, combatant.CombatRegenCooldownRemaining - deltaTime);
        for (int i = combatant.ActiveDebuffs.Count - 1; i >= 0; i--)
        {
            var debuff = combatant.ActiveDebuffs[i];
            debuff.RemainingTime -= deltaTime;
            if (debuff.RemainingTime <= 0f)
            {
                combatant.ActiveDebuffs.RemoveAt(i);
            }
        }

        if (combatant.FreezeStacks > 0 && !combatant.IsFrozen)
        {
            combatant.FreezeStackTimer -= deltaTime;
            if (combatant.FreezeStackTimer <= 0f)
            {
                combatant.FreezeStacks = 0;
            }
        }

        if (combatant.IsFrozen)
        {
            combatant.FreezeTimer -= deltaTime;
            if (combatant.FreezeTimer <= 0f)
            {
                combatant.IsFrozen = false;
                combatant.FreezeStacks = 0;
            }
        }

        if (combatant.FreezeImmune)
        {
            combatant.FreezeImmuneTimer -= deltaTime;
            if (combatant.FreezeImmuneTimer <= 0f)
            {
                combatant.FreezeImmune = false;
            }
        }

        // 3.11 (Плут) — "Скрытность": простой таймер, независимый от таймера атаки, тикает
        // каждый кадр пока активна (StartCombat не устанавливает её — см. CombatantRuntime).
        if (combatant.IsStealthed)
        {
            combatant.StealthTimer -= deltaTime;
            if (combatant.StealthTimer <= 0f)
            {
                combatant.IsStealthed = false;
            }
        }

        TickBleed(combatant, deltaTime);
        TickPoison(combatant, deltaTime);
        TickRoguePoison(combatant, deltaTime);

        if (combatant.CritChanceDebuffTimer > 0f)
        {
            combatant.CritChanceDebuffTimer -= deltaTime;
            if (combatant.CritChanceDebuffTimer <= 0f)
            {
                combatant.CritChanceDebuffPercent = 0f;
            }
        }
    }

    // 3.9 "Кровотечение": тикает раз в секунду, не зависит от таймера атаки.
    void TickBleed(CombatantRuntime target, float deltaTime)
    {
        if (!target.HasBleed)
        {
            return;
        }

        if (!float.IsPositiveInfinity(target.BleedTimer))
        {
            target.BleedTimer -= deltaTime;
        }

        target.BleedTickAccumulator += deltaTime;
        while (target.BleedTickAccumulator >= 1f && target.HasBleed && target.IsAlive)
        {
            target.BleedTickAccumulator -= 1f;
            target.CurrentHP -= target.BleedDamagePerSecond;
            HitResolved?.Invoke(target, target.BleedDamagePerSecond, false, false);
            Log($"[Combat] {target.DisplayName} получает {target.BleedDamagePerSecond:F1} урона от кровотечения (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

            if (!target.IsAlive)
            {
                Log($"[Combat] {target.DisplayName} погибает от кровотечения.");
            }
        }

        if (!float.IsPositiveInfinity(target.BleedTimer) && target.BleedTimer <= 0f)
        {
            target.HasBleed = false;
        }
    }

    // 4.2: игрок атакует выбранную вручную цель либо (по умолчанию) первого живого
    // противника в списке; противники всегда атакуют персонажа.
    // 3.9: сюда же подключены Уклонение, Критические атаки, Несгибаемый, Шипы, Заморозка, Кровотечение.
    // isRegularAttack: 3.11 "Дымовая граната" — true только для обычных, тикером атаки запущенных
    // ударов (TickCombatant); TryActivateUniqueActiveSkill передаёт false для СВОИХ ударов, чтобы
    // гарантированные криты гранаты не расходовались/не применялись к ним самим (по ГДД гарантия
    // распространяется только на "обычные атаки оружием").
    void ResolveAttack(CombatantRuntime attacker, WeaponAttackState weapon, float damageMultiplier = 1f, bool isRegularAttack = true)
    {
        CombatantRuntime target = attacker.IsPlayer ? GetPlayerTarget() : Player;

        if (target == null || !target.IsAlive)
        {
            return;
        }

        // "Уклонение" + пассивка предмета "Неуловимость" (3.10, Эфирный доспех) + бонусный стат
        // EvasionPercent (3.10 ФИКС, Кольцо ловкости/Амулет проворства — раньше игнорировался) +
        // "Ускользание" (3.11, Плут, собственный бонус шанса уклонения) + "Тень" (3.11, уникальная
        // пассивка Плута, только пока активна Скрытность) — складываются: шанс полностью
        // проигнорировать атаку (любого типа урона).
        float itemEvasionPercent = BalanceClamps.ClampItemEvasionPercent(
            ItemEffectBalance.ElusivenessEvasionPercent(target.ItemElusivenessLevel) + target.ItemEvasionBonusPercent);
        float evadeChancePercent = target.SkillEvasionLevel * 5f + itemEvasionPercent + target.MonsterEvasionPercent;

        float slipAwayBonus = target.SkillSlipAwayLevel switch { 1 => 1f, 2 => 2f, 3 => 3f, 4 => 4f, 5 => 5f, _ => 0f };
        evadeChancePercent += slipAwayBonus;

        if (target.IsStealthed && target.UniqueShadowLevel > 0)
        {
            evadeChancePercent += target.UniqueShadowLevel switch { 1 => 10f, 2 => 15f, 3 => 20f, 4 => 25f, 5 => 30f, _ => 0f };
        }

        evadeChancePercent = BalanceClamps.ClampEvasionChancePercent(evadeChancePercent);

        if (evadeChancePercent > 0f && Random.value * 100f < evadeChancePercent)
        {
            Log($"[Combat] {target.DisplayName} уклоняется от атаки {attacker.DisplayName}.");

            // "Ускользание" (даёт Скрытность на 3с) и "На волоске" (даёт временный бафф скорости
            // атаки на 3с) — оба срабатывают на СТОРОНЕ ЗАЩИЩАЮЩЕГОСЯ (target), т.к. это ОН уклонился.
            if (target.SkillSlipAwayLevel > 0)
            {
                GrantOrRefreshStealth(target);
            }

            if (target.SkillByAThreadLevel > 0)
            {
                float byAThreadBonus = target.SkillByAThreadLevel * 0.03f; // 3/6/9/12/15%
                var existing = target.ActiveDebuffs.Find(d => d.Id == "by_a_thread");
                if (existing != null)
                {
                    existing.RemainingTime = 3f;
                    existing.AttackSpeedMultiplier = 1f + byAThreadBonus;
                    existing.IsBuff = true;
                }
                else
                {
                    // Финальный ревью-фикс #2: IsBuff=true — это БАФФ скорости атаки, не дебафф,
                    // несмотря на то что хранится в ActiveDebuffs (см. ActiveDebuff.IsBuff).
                    target.ActiveDebuffs.Add(new ActiveDebuff { Id = "by_a_thread", RemainingTime = 3f, AttackSpeedMultiplier = 1f + byAThreadBonus, IsBuff = true });
                }
                Log($"[Combat] «На волоске» повышает скорость атаки {target.DisplayName} на {byAThreadBonus * 100f:F0}% (3 сек).");
            }

            // 3.11 (Task 6b, Капюшон Дуэльянта): "Рипост" — успешное уклонение ВЗВОДИТ флаг (не бьёт
            // немедленно), бонус применится на следующей СОБСТВЕННОЙ атаке target (см. ResolveAttack
            // выше, attacker.RiposteArmed) — ГДД: "первая атака ПОСЛЕ успешного уклонения".
            if (target.ItemRiposteLevel > 0)
            {
                target.RiposteArmed = true;
            }

            return;
        }

        float damage = Random.Range(weapon.DamageMin, weapon.DamageMax) * damageMultiplier;

        // 1, п.3: постоянный бонус к магическому урону от основного пассивного навыка наставника ("Магнум Опус").
        if (attacker.IsPlayer && weapon.DamageType == DamageType.Magical && attacker.MentorMagicDamageBonusPercent > 0f)
        {
            damage *= 1f + attacker.MentorMagicDamageBonusPercent / 100f;
        }

        // "Несгибаемый": пока на атакующем есть активный дебафф, его урон увеличен.
        if (attacker.SkillUnyieldingLevel > 0 && attacker.HasActiveDebuff)
        {
            damage *= 1f + attacker.SkillUnyieldingLevel * 0.05f; // 5/10/15/20/25%
        }

        // 3.10 (ФИКС): бонусный стат DamagePercent (Стальной шлем/Корона Мидаса) — раньше
        // игнорировался, не был подключён нигде.
        if (attacker.ItemDamageBonusPercent > 0f)
        {
            damage *= 1f + attacker.ItemDamageBonusPercent / 100f;
        }

        // 3.11 (Task 6b, Моменто Мори): "Казнь" — доп. физ. урон = 1% недостающего HP ЦЕЛИ за
        // уровень оружия. Только физический урон, только если оружие реально несёт эту пассивку.
        if (weapon.ExecutionLevel > 0 && weapon.DamageType == DamageType.Physical)
        {
            float missingHpPercent = target.MaxHP > 0f ? (1f - target.CurrentHP / target.MaxHP) : 0f;
            damage += target.MaxHP * missingHpPercent * (ItemEffectBalance.ExecutionMissingHealthPercent(weapon.ExecutionLevel) / 100f);
        }

        // 3.11 (Task 6b, Головоруб): "Убийца великанов" — +5% урона за уровень против цели с БОЛЬШИМ
        // максимальным HP (не текущим — сравнение по MaxHP, чтобы избитая цель не "переставала" считаться великаном).
        if (weapon.GiantSlayerLevel > 0 && target.MaxHP > attacker.MaxHP)
        {
            damage *= 1f + weapon.GiantSlayerLevel * 0.05f;
        }

        // 3.11 (Task 6b, Капюшон Дуэльянта): "Рипост" — взведён на предыдущем успешном уклонении
        // этого атакующего (см. блок уклонения ниже), срабатывает РОВНО на следующей атаке и сразу
        // сбрасывается — не копится, не бьёт немедленно в момент уклонения.
        if (attacker.RiposteArmed)
        {
            damage += damage * ItemEffectBalance.RiposteDamageMultiplier(attacker.ItemRiposteLevel);
            attacker.RiposteArmed = false;
        }

        // "Критические атаки" + бонус крита с предметов + "В глаз" (3.11, Плут — таблица не
        // регулярна: 2/5/7.5/10/12.5%), суммарно клампится на 75% (8.6).
        float eyeForAnEyeBonus = attacker.SkillEyeForAnEyeLevel switch
        {
            1 => 2f, 2 => 5f, 3 => 7.5f, 4 => 10f, 5 => 12.5f, _ => 0f
        };
        // 3.11 "Устранение" (Плут): переопределяет базовый крит-множитель 150%, если навык изучен.
        // Вычисляется ДО критChancePercent, т.к. "Чемпион племени" (Варвар) ниже может добавить к
        // нему конвертированные источники крит-шанса.
        float critMultiplier = attacker.CritDamageMultiplierOverridePercent ?? 150f;

        float critChancePercent;
        if (attacker.CritChanceReplacedByRage)
        {
            // 3.11 "Чемпион племени" (Варвар, уникальная пассивка): крит-шанс ВСЕГДА = Ярость×X%,
            // полностью заменяя обычную формулу. Остальные источники крит-шанса (навык "Критические
            // атаки" + бонус предметов — "В глаз"/крит-дебафф Гарпии сюда намеренно не входят, это
            // Rogue-специфика/дебафф шанса, а не источник шанса Варвара) конвертируются в крит-урон
            // по курсу 1%->+2% вместо суммирования в шанс.
            critChancePercent = Mathf.Clamp(attacker.Rage * RageSkillMultiplier(attacker.UniqueChampionOfTheTribeLevel), 0f, 100f);
            float convertedSources = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems;
            critMultiplier += convertedSources * 2f;
        }
        else
        {
            critChancePercent = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems - attacker.CritChanceDebuffPercent + eyeForAnEyeBonus; // 10/20/30/40/50% за уровень навыка - "Оглушающий крик" (2.4) + "В глаз"
            critChancePercent = Mathf.Max(0f, critChancePercent);
            critChancePercent = BalanceClamps.ClampCritChancePercent(critChancePercent);
        }

        bool isCrit = critChancePercent > 0f && Random.value * 100f < critChancePercent;

        // 3.11 "Дымовая граната" (уникальная активка Плута): пока есть заряды гарантированного
        // крита от этого навыка, ОБЫЧНАЯ атака (isRegularAttack) гарантированно критует и расходует
        // заряд — независимо от ролла выше.
        if (isRegularAttack && attacker.SmokeBombGuaranteedCritsRemaining > 0)
        {
            isCrit = true;
            attacker.SmokeBombGuaranteedCritsRemaining--;
        }

        if (isCrit)
        {
            damage *= critMultiplier / 100f;

            // "В глаз" (3.11, Плут): крит накладывает/обновляет Скрытность на 3с.
            if (attacker.SkillEyeForAnEyeLevel > 0)
            {
                GrantOrRefreshStealth(attacker);
            }

            // 3.11 "Запугивание" (Варвар): крит накладывает дебафф скорости атаки на цель,
            // пропорциональный Ярости атакующего. Подчиняется "Упёртости" цели, как любой дебафф.
            if (attacker.SkillIntimidationLevel > 0 && !IgnoresDebuffs(target))
            {
                float intimidationMultiplier = Mathf.Max(0.01f, 1f - (attacker.Rage * RageSkillMultiplier(attacker.SkillIntimidationLevel) / 100f));
                var existingIntimidation = target.ActiveDebuffs.Find(d => d.Id == "intimidation");
                if (existingIntimidation != null)
                {
                    existingIntimidation.RemainingTime = 3f;
                    existingIntimidation.AttackSpeedMultiplier = intimidationMultiplier;
                }
                else
                {
                    target.ActiveDebuffs.Add(new ActiveDebuff { Id = "intimidation", RemainingTime = 3f, AttackSpeedMultiplier = intimidationMultiplier });
                }
                Log($"[Combat] «Запугивание» снижает скорость атаки {target.DisplayName} на {(1f - intimidationMultiplier) * 100f:F0}% (3 сек).");
            }
        }

        // 3.10 (ФИКС): "Пробивание" (Топор/Молот редкого+ тира, BonusStatType.ArmorPenetrationFlat) —
        // раньше игнорировалось. "Против бронированных целей урон считается на +N больше" — флэт-
        // бонус к урону только для целей ПРОБИТИЯ брони, добавляется прямо перед проверкой брони.
        float armorPenetrationDamage = weapon.DamageType == DamageType.Physical ? weapon.ArmorPenetrationFlat : 0f;
        float armorBeforeAttack = target.PhysicalDefenseCurrent;
        var result = DamageCalculator.ApplyDamage(target, damage + armorPenetrationDamage, weapon.DamageType, weapon.ArmorIgnorePercent);
        float normalArmorLost = armorBeforeAttack - target.PhysicalDefenseCurrent;

        // «Бронебойный» снимает дополнительную гарантированную броню после любой неуклонённой
        // атаки, даже если обычный урон полностью заблокирован или был магическим.
        if (!attacker.IsPlayer && attacker.MonsterGuaranteedArmorDamage > 0f && target.PhysicalDefenseCurrent > 0f)
        {
            float armorBeforeModifier = target.PhysicalDefenseCurrent;
            target.PhysicalDefenseCurrent = Mathf.Max(0f, armorBeforeModifier - attacker.MonsterGuaranteedArmorDamage);
            float modifierArmorLost = armorBeforeModifier - target.PhysicalDefenseCurrent;
            if (modifierArmorLost > 0f)
            {
                Log($"[Combat] «Бронебойный» дополнительно снижает физ. защиту {target.DisplayName} на {modifierArmorLost:F1}.");
            }
        }

        if (result.WasBlocked)
        {
            string blockSuffix = normalArmorLost > 0f ? $", броня истёрлась (-{normalArmorLost:F0})" : string.Empty;
            Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}{(isCrit ? " (крит!)" : string.Empty)}: урон {damage:F1} полностью заблокирован{blockSuffix}.");
        }
        else
        {
            Log($"[Combat] {attacker.DisplayName} атакует {target.DisplayName}{(isCrit ? " (крит!)" : string.Empty)}: {result.DamageToHP:F1} урона по HP (осталось {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");
        }

        // 4.7: единая точка для всплывающих цифр урона и тряски спрайта цели — покрывает обычные
        // атаки оружием и каждый отдельный удар активного навыка (цикл в TryActivateUniqueActiveSkill
        // вызывает ResolveAttack по разу на удар, так что события уже приходят по одному, не суммарно).
        HitResolved?.Invoke(target, result.DamageToHP, isCrit, result.WasBlocked);

        // 3.11 (Task 6b, "Объятия ночи", Кожанка) — ОТДЕЛЬНЫЙ второй урон, только в Скрытности:
        // магический (проходит через щит, не через броню), поэтому не может быть просто добавлен в
        // физический `damage` — отдельный вызов DamageCalculator.ApplyDamage + отдельное всплывающее
        // число (HitResolved), но НЕ крит (isCrit жёстко false для этого удара).
        if (attacker.IsStealthed && attacker.ItemEmbraceOfNightLevel > 0)
        {
            float bonusMagicDamage = damage * ItemEffectBalance.EmbraceOfNightMagicDamagePercent(attacker.ItemEmbraceOfNightLevel) / 100f;
            var embraceResult = DamageCalculator.ApplyDamage(target, bonusMagicDamage, DamageType.Magical);
            HitResolved?.Invoke(target, embraceResult.DamageToHP, false, embraceResult.WasBlocked);
        }

        // "Вампиризм" (3.10, Кровавый меч): при крите восстанавливает атакующему часть урона крита здоровьем.
        if (isCrit && weapon.VampirismLevel > 0)
        {
            float healAmount = damage * ItemEffectBalance.VampirismHealPercentOfCritDamage(weapon.VampirismLevel) / 100f;
            attacker.CurrentHP = Mathf.Min(attacker.MaxHP, attacker.CurrentHP + healAmount);
            Log($"[Combat] «Вампиризм» восстанавливает {attacker.DisplayName} {healAmount:F1} HP.");
        }

        // «Разрушение брони» (Рубило): после физического попадания есть 25/50/75/100/100% шанс
        // снять ещё 1 ед. брони сверх обычной деградации DamageCalculator.
        if (!result.WasBlocked && weapon.DamageType == DamageType.Physical && weapon.ArmorBreakLevel > 0)
        {
            float extraWearChance = ItemEffectBalance.ArmorBreakExtraWearChancePercent(weapon.ArmorBreakLevel);
            if (Random.value * 100f < extraWearChance)
            {
                target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - 1f);
                Log($"[Combat] «Разрушение брони» снижает физ. защиту {target.DisplayName} ещё на 1.");
            }
        }

        // "Насквозь" (3.10, Стремительное копьё): часть урона дополнительно проходит по всем
        // остальным живым противникам в комнате, помимо выбранной цели.
        if (attacker.IsPlayer && weapon.PiercingLevel > 0)
        {
            float splashDamage = damage * ItemEffectBalance.PiercingSplashPercent(weapon.PiercingLevel) / 100f;
            if (splashDamage > 0f)
            {
                foreach (var other in Enemies)
                {
                    if (other == target || !other.IsAlive)
                    {
                        continue;
                    }

                    var splashResult = DamageCalculator.ApplyDamage(other, splashDamage, weapon.DamageType);
                    HitResolved?.Invoke(other, splashResult.DamageToHP, false, splashResult.WasBlocked);
                    Log($"[Combat] «Насквозь» задевает {other.DisplayName}: {splashResult.DamageToHP:F1} урона по HP.");
                }
            }
        }

        // "Шипы": если атака не пробила броню (полный блок) — отражается часть заблокированного
        // урона. Прогрессия 10/20/30/40/50%, жёсткий потолок — 50%.
        if (target.SkillThornsLevel > 0 && weapon.DamageType == DamageType.Physical)
        {
            float reflectPercent = BalanceClamps.ThornsReflectPercent(target.SkillThornsLevel) / 100f;
            float reflectedDamage = 0f;

            if (result.WasBlocked)
            {
                reflectedDamage = damage * reflectPercent;
            }
            else if (target.SkillThornsLevel >= 5)
            {
                reflectedDamage = damage * 0.5f;
            }

            if (reflectedDamage > 0f)
            {
                attacker.CurrentHP -= reflectedDamage;
                HitResolved?.Invoke(attacker, reflectedDamage, false, false);
                Log($"[Combat] Шипы {target.DisplayName} отражают {reflectedDamage:F1} урона по {attacker.DisplayName}.");
                if (!attacker.IsAlive)
                {
                    Log($"[Combat] {attacker.DisplayName} погибает от шипов.");
                }
            }
        }

        // "Заморозка": накладывается при любом уроне по HP; "разбивается" физическим уроном.
        if (attacker.SkillFreezeLevel > 0)
        {
            ApplyFreezeOnHit(attacker, weapon, target, result);
        }

        // "Кровотечение": только от физического урона, реально пробившего защиту (не от
        // минимального прохождения при полном блоке, см. 3.3).
        if (attacker.SkillBleedLevel > 0 && weapon.DamageType == DamageType.Physical && !result.WasBlocked)
        {
            ApplyBleed(target, attacker.SkillBleedLevel);
        }

        // "Отравленный клинок" (3.11, Плут): собственный яд Плута, отдельный от ядовитого укуса
        // монстров (PoisonStacks/PoisonTimer) — та же логическая точка, что и Кровотечение выше.
        if (attacker.SkillPoisonedBladeLevel > 0 && weapon.DamageType == DamageType.Physical && !result.WasBlocked)
        {
            ApplyRoguePoison(attacker, target);
        }

        // 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (симметрично блоку выше, но для
        // не-игрока — у игрока MonsterPassiveSkillId всегда SkillId.None).
        ApplyMonsterPassiveOnAttack(attacker, target, result, damage);

        if (!target.IsAlive)
        {
            Log($"[Combat] {target.DisplayName} погибает.");
        }

        // 3.11 "Боевая регенерация" (Варвар): каждый N-й ПОЛУЧЕННЫЙ удар (блокированный или нет —
        // ГДД "каждые N полученных ударов", читаем как любую разрешённую атаку по цели, а не только
        // прошедшую по HP; [ПРЕДПОЛОЖЕНИЕ] — если задумывалось иначе, это расхождение с ГДД, не
        // угаданное молча) восстанавливает 6% MaxHP, если цель выжила. Урон -> потом восстановление
        // (ГДД: "сначала урон... затем, если персонаж выжил — восстановление").
        if (target.SkillCombatRegenLevel > 0)
        {
            target.HitsTakenSinceLastRegen++;
            int regenThreshold = BalanceClamps.CombatRegenHitsRequired(target.SkillCombatRegenLevel);
            if (target.HitsTakenSinceLastRegen >= regenThreshold && target.IsAlive && target.CombatRegenCooldownRemaining <= 0f)
            {
                target.HitsTakenSinceLastRegen = 0;
                float regenAmount = target.MaxHP * (BalanceClamps.CombatRegenHealPercent / 100f);
                target.CurrentHP = Mathf.Min(target.MaxHP, target.CurrentHP + regenAmount);
                target.CombatRegenCooldownRemaining = BalanceClamps.CombatRegenCooldownSeconds;
                Log($"[Combat] «Боевая регенерация» восстанавливает {target.DisplayName} {regenAmount:F1} HP (HP {target.CurrentHP:F1}/{target.MaxHP:F1}).");
            }
        }
    }

    static int FreezeMaxStacksByLevel(int level)
    {
        switch (level)
        {
            case 1: return 2;
            case 2: return 4;
            case 3: return 6;
            case 4: return 8;
            default: return 10;
        }
    }

    void ApplyFreezeOnHit(CombatantRuntime attacker, WeaponAttackState weapon, CombatantRuntime target, DamageCalculator.DamageResult result)
    {
        if (target.IsFrozen)
        {
            // Физический урон по замороженной цели "разбивает" заморозку: доп. магический урон,
            // равный фактически полученному (после защиты) физическому урону, затем иммунитет 5 сек.
            if (weapon.DamageType == DamageType.Physical && !target.FreezeImmune && !result.WasBlocked)
            {
                float bonusMagicDamage = result.DamageToHP;
                DamageCalculator.ApplyMagicalDamage(target, bonusMagicDamage);
                Log($"[Combat] Заморозка {target.DisplayName} разбивается! +{bonusMagicDamage:F1} доп. магического урона.");

                target.IsFrozen = false;
                target.FreezeStacks = 0;
                target.FreezeImmune = true;
                target.FreezeImmuneTimer = 5f;
            }

            return;
        }

        if (target.FreezeImmune || result.WasBlocked)
        {
            return;
        }

        // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель полностью игнорирует НОВЫЕ стаки заморозки.
        if (IgnoresDebuffs(target))
        {
            Log($"[Combat] «Упёртость» защищает {target.DisplayName} от заморозки.");
            return;
        }

        int maxStacks = FreezeMaxStacksByLevel(attacker.SkillFreezeLevel);
        target.FreezeStacks = Mathf.Min(target.FreezeStacks + 1, maxStacks);
        target.FreezeStackTimer = 3f;

        Log($"[Combat] {target.DisplayName} получает стак заморозки ({target.FreezeStacks}/{maxStacks}).");

        if (target.FreezeStacks >= 10)
        {
            target.IsFrozen = true;
            target.FreezeTimer = 5f;
            Log($"[Combat] {target.DisplayName} замораживается на 5 секунд!");
        }
    }

    void ApplyBleed(CombatantRuntime target, int bleedLevel)
    {
        // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель полностью игнорирует новое кровотечение.
        if (IgnoresDebuffs(target))
        {
            Log($"[Combat] «Упёртость» защищает {target.DisplayName} от кровотечения.");
            return;
        }

        bool isFreshApplication = !target.HasBleed;

        target.HasBleed = true;
        target.BleedDamagePerSecond = bleedLevel >= 4 ? 20f : bleedLevel * 5f; // 5/10/15/20, остаётся 20 на ур.5
        target.BleedTimer = bleedLevel >= 5 ? float.PositiveInfinity : 3f; // не стакается, обновляет длительность

        // Повторное наложение обновляет только длительность (см. 3.9), а не расписание тиков урона:
        // если сбрасывать аккумулятор на каждый удар, при атаках чаще раза в секунду кровотечение
        // никогда не успевало бы тикнуть.
        if (isFreshApplication)
        {
            target.BleedTickAccumulator = 0f;
            Log($"[Combat] {target.DisplayName} получает кровотечение ({target.BleedDamagePerSecond:F1}/сек).");
        }
    }

    // 3.11 (Плут) — "В глаз"/"Ускользание" накладывают/обновляют Скрытность: длительность всегда
    // фиксированные 3с (StealthStatus.DurationSeconds), повторное наложение просто обновляет таймер.
    void GrantOrRefreshStealth(CombatantRuntime combatant)
    {
        combatant.IsStealthed = true;
        combatant.StealthTimer = StealthStatus.DurationSeconds;
    }

    // 3.11 (Плут) "Отравленный клинок": собственный яд Плута на ЦЕЛИ, полностью отдельный от
    // PoisonStacks/PoisonTimer (яд Ядовитого паучка, 2.4) — см. RoguePoisonStacksOnTarget на
    // CombatantRuntime. В Скрытности стаки/максимум удваиваются.
    void ApplyRoguePoison(CombatantRuntime attacker, CombatantRuntime target)
    {
        // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель полностью игнорирует новый яд Плута.
        if (IgnoresDebuffs(target))
        {
            Log($"[Combat] «Упёртость» защищает {target.DisplayName} от «Отравленного клинка».");
            return;
        }

        int maxStacks = attacker.SkillPoisonedBladeLevel;
        int stacksToAdd = 1;
        if (attacker.IsStealthed)
        {
            maxStacks *= 2;
            stacksToAdd = 2;
        }

        target.RoguePoisonStacksOnTarget = Mathf.Min(target.RoguePoisonStacksOnTarget + stacksToAdd, maxStacks);
        target.RoguePoisonTimer = 3f;
        Log($"[Combat] «Отравленный клинок» накладывает яд на {target.DisplayName} ({target.RoguePoisonStacksOnTarget}/{maxStacks} стаков).");
    }

    // "Отравленный клинок": тикает раз в секунду, урон/сек = текущее число стаков (1:1, В ОТЛИЧИЕ
    // от монстрового яда, где урон/сек = стаки×4 — см. TickPoison). Стаки истекают все разом по
    // общему таймеру, зеркально TickPoison, но на отдельных Rogue*-полях.
    void TickRoguePoison(CombatantRuntime target, float deltaTime)
    {
        if (target.RoguePoisonStacksOnTarget <= 0)
        {
            return;
        }

        target.RoguePoisonTimer -= deltaTime;
        target.RoguePoisonTickAccumulator += deltaTime;

        float damagePerSecond = target.RoguePoisonStacksOnTarget;
        while (target.RoguePoisonTickAccumulator >= 1f && target.RoguePoisonStacksOnTarget > 0 && target.IsAlive)
        {
            target.RoguePoisonTickAccumulator -= 1f;
            target.CurrentHP -= damagePerSecond;
            HitResolved?.Invoke(target, damagePerSecond, false, false);
            Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от «Отравленного клинка» (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

            if (!target.IsAlive)
            {
                Log($"[Combat] {target.DisplayName} погибает от «Отравленного клинка».");
            }
        }

        if (target.RoguePoisonTimer <= 0f)
        {
            target.RoguePoisonStacksOnTarget = 0;
        }
    }

    // 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (в отличие от периодических — см.
    // TickMonsterPeriodicPassives). attacker всегда монстр здесь (у игрока MonsterPassiveSkillId всегда None).
    void ApplyMonsterPassiveOnAttack(CombatantRuntime attacker, CombatantRuntime target, DamageCalculator.DamageResult result, float attackDamage)
    {
        if (attacker.IsPlayer || attacker.MonsterPassiveSkillId == SkillId.None)
        {
            return;
        }

        switch (attacker.MonsterPassiveSkillId)
        {
            case SkillId.MonsterCorrosion:
                // «Коррозия»: при каждой атаке паука 15% силы удара напрямую изнашивает
                // физическую защиту цели. Эффект не зависит от того, пробил ли сам удар броню.
                // Яд прежнего паучка сохранён как вторая часть этой пассивки и, как раньше,
                // накладывается только при попадании по HP.
                float armorDamage = Mathf.Max(0f, attackDamage) * 0.15f;
                if (armorDamage > 0f)
                {
                    float armorBefore = target.PhysicalDefenseCurrent;
                    target.PhysicalDefenseCurrent = Mathf.Max(0f, armorBefore - armorDamage);
                    float armorLost = armorBefore - target.PhysicalDefenseCurrent;
                    if (armorLost > 0f)
                    {
                        Log($"[Combat] Коррозия {attacker.DisplayName} разъедает физ. защиту {target.DisplayName} на {armorLost:F1}.");
                    }
                }

                // При попадании по здоровью накладывает яд (3 сек, 4 урона/сек, до 3 стаков).
                // 3.11 «Упёртость»: при достаточной Ярости цель игнорирует только дебафф яда,
                // но не прямой урон коррозии по броне.
                if (!result.WasBlocked && !IgnoresDebuffs(target))
                {
                    target.PoisonStacks = Mathf.Min(target.PoisonStacks + 1, 3);
                    target.PoisonTimer = 3f;
                    Log($"[Combat] {target.DisplayName} получает яд ({target.PoisonStacks}/3 стаков).");
                }
                break;

            case SkillId.MonsterStunningScream:
                // "15% шанс при атаке снизить шанс крита персонажа на 20% на 4 сек." — на атаке, не на попадании.
                // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель игнорирует крит-дебафф.
                if (Random.value < 0.15f && !IgnoresDebuffs(target))
                {
                    target.CritChanceDebuffPercent = 20f;
                    target.CritChanceDebuffTimer = 4f;
                    Log($"[Combat] Оглушающий крик {attacker.DisplayName} снижает шанс крита {target.DisplayName}.");
                }
                break;

            case SkillId.MonsterSlowCurse:
                // "Если урон Колдуна проходит по здоровью персонажа, скорость атаки персонажа снижается
                // на 30% на 3 секунды (не стакается, повторное попадание обновляет длительность)."
                // 3.11 "Упёртость" (Варвар): при достаточной Ярости цель игнорирует новый дебафф скорости.
                if (!result.WasBlocked && !IgnoresDebuffs(target))
                {
                    var existing = target.ActiveDebuffs.Find(d => d.Id == "warlock_slow");
                    if (existing != null)
                    {
                        existing.RemainingTime = 3f;
                    }
                    else
                    {
                        target.ActiveDebuffs.Add(new ActiveDebuff { Id = "warlock_slow", RemainingTime = 3f, AttackSpeedMultiplier = 0.7f });
                    }
                    Log($"[Combat] Проклятие замедления {attacker.DisplayName} снижает скорость атаки {target.DisplayName} на 30% (3 сек).");
                }
                break;
        }
    }

    // "Тёмное исцеление" / "Двойной удар": пассивки на собственном периодическом таймере, независимом
    // от таймера атаки оружия (в отличие от обычных атак и мгновенных пассивок из ApplyMonsterPassiveOnAttack).
    void TickMonsterPeriodicPassives(float deltaTime)
    {
        foreach (var enemy in Enemies)
        {
            if (!enemy.IsAlive || enemy.MonsterPassiveSkillId == SkillId.None)
            {
                continue;
            }

            if (enemy.MonsterPassiveSkillId == SkillId.MonsterDarkHeal)
            {
                enemy.MonsterPassiveCooldownTimer -= deltaTime;
                if (enemy.MonsterPassiveCooldownTimer <= 0f)
                {
                    enemy.MonsterPassiveCooldownTimer = 8f;
                    var healTarget = PickDarkHealTarget(enemy);
                    if (healTarget != null)
                    {
                        float healAmount = healTarget.MaxHP * 0.10f;
                        healTarget.CurrentHP = Mathf.Min(healTarget.MaxHP, healTarget.CurrentHP + healAmount);
                        Log($"[Combat] Тёмное исцеление {enemy.DisplayName} восстанавливает {healTarget.DisplayName} {healAmount:F1} HP.");
                    }
                }
            }
            else if (enemy.MonsterPassiveSkillId == SkillId.MonsterDoubleStrike)
            {
                enemy.MonsterPassiveCooldownTimer -= deltaTime;
                if (enemy.MonsterPassiveCooldownTimer <= 0f && enemy.Weapons.Count > 0)
                {
                    enemy.MonsterPassiveCooldownTimer = 6f;
                    Log($"[Combat] {enemy.DisplayName} наносит двойной удар!");
                    ResolveAttack(enemy, enemy.Weapons[0], 1.5f);
                }
            }
        }
    }

    // "себе или ближайшему союзнику в комнате" — интерпретация: лечит того из (себя + живых союзников
    // в Enemies), у кого сейчас наименьший % HP от максимума (ближе всех к смерти = приоритетная цель
    // для лечения; при равенстве побеждает первый найденный в списке).
    CombatantRuntime PickDarkHealTarget(CombatantRuntime healer)
    {
        CombatantRuntime best = healer;
        float bestPercent = healer.MaxHP > 0f ? healer.CurrentHP / healer.MaxHP : 1f;

        foreach (var other in Enemies)
        {
            if (other == healer || !other.IsAlive)
            {
                continue;
            }

            float percent = other.MaxHP > 0f ? other.CurrentHP / other.MaxHP : 1f;
            if (percent < bestPercent)
            {
                best = other;
                bestPercent = percent;
            }
        }

        return best.CurrentHP < best.MaxHP ? best : null; // никто не ранен -> лечить некого
    }

    // "Яд": тикает раз в секунду, не зависит от таймера атаки; стаки истекают все разом по общему таймеру.
    void TickPoison(CombatantRuntime target, float deltaTime)
    {
        if (target.PoisonStacks <= 0)
        {
            return;
        }

        target.PoisonTimer -= deltaTime;
        target.PoisonTickAccumulator += deltaTime;

        float damagePerSecond = target.PoisonStacks * 4f;
        while (target.PoisonTickAccumulator >= 1f && target.PoisonStacks > 0 && target.IsAlive)
        {
            target.PoisonTickAccumulator -= 1f;
            target.CurrentHP -= damagePerSecond;
            HitResolved?.Invoke(target, damagePerSecond, false, false);
            Log($"[Combat] {target.DisplayName} получает {damagePerSecond:F1} урона от яда (HP {Mathf.Max(target.CurrentHP, 0f):F1}/{target.MaxHP:F1}).");

            if (!target.IsAlive)
            {
                Log($"[Combat] {target.DisplayName} погибает от яда.");
            }
        }

        if (target.PoisonTimer <= 0f)
        {
            target.PoisonStacks = 0;
        }
    }

    CombatantRuntime GetPlayerTarget()
    {
        if (Player.Target == null || !Player.Target.IsAlive)
        {
            Player.Target = GetDefaultTarget();
        }

        return Player.Target;
    }

    CombatantRuntime GetDefaultTarget()
    {
        return Enemies.FirstOrDefault(e => e.IsAlive);
    }

    // 4.2: программный аналог клика по противнику — ручной выбор цели игроком.
    public void SetPlayerTarget(CombatantRuntime target)
    {
        if (target != null && target.IsAlive && Enemies.Contains(target))
        {
            Player.Target = target;
        }
    }

    void CheckCombatEnd()
    {
        if (!Player.IsAlive || Enemies.All(e => !e.IsAlive))
        {
            EndCombat();
        }
    }
}
