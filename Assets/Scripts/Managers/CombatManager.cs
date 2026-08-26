using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public CombatantRuntime Player { get; private set; }
    public List<CombatantRuntime> Enemies { get; private set; } = new List<CombatantRuntime>();
    public bool IsCombatActive { get; private set; }

    // Позволяет UI подписаться на текстовый лог боя (7.2), не читая консоль Unity.
    public event System.Action<string> LogMessage;

    // "Карманник" (2.4): (жертва, украденный % текущей валюты забега).
    public event System.Action<CombatantRuntime, float> MonsterStoleCurrency;

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

    // 4.3: уникальный активный навык персонажа (3.1 "3 быстрые атаки" — единственный активный
    // навык прототипа, общего пула активных навыков в 3.9 нет). Настраивается при входе в бой,
    // т.к. зависит от текущего уровня навыка игрока.
    int activeSkillHitCount;
    float activeSkillDamageMultiplierPerHit;
    float activeSkillCooldownSeconds;
    string activeSkillName;
    bool activeSkillAutoMode = true;

    public bool IsActiveSkillConfigured { get; private set; }
    public float ActiveSkillCooldownRemaining => Player != null ? Mathf.Max(0f, Player.ActiveSkillCooldownTimer) : 0f;
    public bool IsActiveSkillReady => Player != null && Player.IsAlive && Player.ActiveSkillCooldownTimer <= 0f;

    public void ConfigureUniqueActiveSkill(int hitCount, float damageMultiplierPerHit, float cooldownSeconds, bool autoMode, string skillName)
    {
        activeSkillHitCount = hitCount;
        activeSkillDamageMultiplierPerHit = damageMultiplierPerHit;
        activeSkillCooldownSeconds = cooldownSeconds;
        activeSkillName = skillName;
        activeSkillAutoMode = autoMode;
        IsActiveSkillConfigured = true;
    }

    public void SetActiveSkillAutoMode(bool autoMode)
    {
        activeSkillAutoMode = autoMode;
    }

    // 4.3: ручной режим — доступна только по готовности кулдауна; автоматический — срабатывает
    // сама, без участия игрока (тикается в Tick()).
    public bool TryActivateUniqueActiveSkill()
    {
        if (!IsCombatActive || !IsActiveSkillConfigured || !IsActiveSkillReady || Player.Weapons.Count == 0)
        {
            return false;
        }

        ActiveSkillActivated?.Invoke(Player, activeSkillName);

        var weapon = Player.Weapons[0];
        for (int i = 0; i < activeSkillHitCount; i++)
        {
            if (!IsCombatActive || !Player.IsAlive)
            {
                break;
            }

            ResolveAttack(Player, weapon, activeSkillDamageMultiplierPerHit);
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
        }

        // 4.3 (НОВОЕ): активный навык уходит в полный кулдаун сразу при старте боя, а не в 0 —
        // без этого навык (например "3 быстрые атаки") часто срабатывал мгновенно и сносил
        // противника до того, как игрок успевал его увидеть. Обычные атаки оружием (ResetAttackTimers
        // выше) это правило не затрагивает — они по-прежнему начинаются сразу по своей скорости атаки.
        Player.ActiveSkillCooldownTimer = activeSkillCooldownSeconds;
        Player.Target = GetDefaultTarget();

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

        Log(Player.IsAlive
            ? $"[Combat] Бой окончен. {Player.DisplayName} побеждает."
            : $"[Combat] Бой окончен. {Player.DisplayName} погибает.");
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

        UpdateStatusEffects(Player, deltaTime);
        foreach (var enemy in Enemies)
        {
            UpdateStatusEffects(enemy, deltaTime);
        }

        TickMonsterPeriodicPassives(deltaTime); // "Тёмное исцеление" / "Двойной удар" (2.4)

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

        TickBleed(combatant, deltaTime);
        TickPoison(combatant, deltaTime);

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
    void ResolveAttack(CombatantRuntime attacker, WeaponAttackState weapon, float damageMultiplier = 1f)
    {
        CombatantRuntime target = attacker.IsPlayer ? GetPlayerTarget() : Player;

        if (target == null || !target.IsAlive)
        {
            return;
        }

        // "Уклонение" + пассивка предмета "Неуловимость" (3.10, Эфирный доспех) + бонусный стат
        // EvasionPercent (3.10 ФИКС, Кольцо ловкости/Амулет проворства — раньше игнорировался) — складываются:
        // шанс полностью проигнорировать атаку (любого типа урона).
        float evadeChancePercent = target.SkillEvasionLevel * 5f + target.ItemElusivenessLevel * 1f + target.MonsterEvasionPercent + target.ItemEvasionBonusPercent; // 5/10/15/20/25% + 1%/уровень предмета + "Порхание" (2.4)
        if (evadeChancePercent > 0f && Random.value * 100f < evadeChancePercent)
        {
            Log($"[Combat] {target.DisplayName} уклоняется от атаки {attacker.DisplayName}.");
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

        // "Критические атаки" + бонус крита с предметов, суммарно клампится на 75% (8.6).
        float critChancePercent = attacker.SkillCriticalHitsLevel * 10f + attacker.CritChanceBonusFromItems - attacker.CritChanceDebuffPercent; // 10/20/30/40/50% за уровень навыка - "Оглушающий крик" (2.4)
        critChancePercent = Mathf.Max(0f, critChancePercent);
        critChancePercent = BalanceClamps.ClampCritChancePercent(critChancePercent);
        bool isCrit = critChancePercent > 0f && Random.value * 100f < critChancePercent;
        if (isCrit)
        {
            damage *= 1.5f;
        }

        // 3.10 (ФИКС): "Пробивание" (Топор/Молот редкого+ тира, BonusStatType.ArmorPenetrationFlat) —
        // раньше игнорировалось. "Против бронированных целей урон считается на +N больше" — флэт-
        // бонус к урону только для целей ПРОБИТИЯ брони, добавляется прямо перед проверкой брони.
        float armorPenetrationDamage = weapon.DamageType == DamageType.Physical ? weapon.ArmorPenetrationFlat : 0f;
        var result = DamageCalculator.ApplyDamage(target, damage + armorPenetrationDamage, weapon.DamageType);

        if (result.WasBlocked)
        {
            // 3.3 «Износ брони при блокированном уроне»: лог отдельно показывает случай, когда
            // блок всё же снял с брони 1 единицу, от полной блокировки без последствий.
            string blockSuffix = result.ArmorWornOnBlock ? ", броня истёрлась (-1)" : string.Empty;
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

        // "Вампиризм" (3.10, Кровавый меч): при крите восстанавливает атакующему часть урона крита здоровьем.
        if (isCrit && weapon.VampirismLevel > 0)
        {
            float healAmount = damage * 0.02f * weapon.VampirismLevel; // 2% от урона крита за уровень предмета
            attacker.CurrentHP = Mathf.Min(attacker.MaxHP, attacker.CurrentHP + healAmount);
            Log($"[Combat] «Вампиризм» восстанавливает {attacker.DisplayName} {healAmount:F1} HP.");
        }

        // "Разрушение брони" (3.10, Рубило): при пробитии физ. защиты снижает её ещё на 1 за каждые
        // 5 уровней оружия, сверх обычной деградации на 1 из DamageCalculator.
        if (!result.WasBlocked && weapon.DamageType == DamageType.Physical && weapon.ArmorBreakLevel > 0)
        {
            int extraDegrade = weapon.ArmorBreakLevel / 5;
            if (extraDegrade > 0)
            {
                target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - extraDegrade);
                Log($"[Combat] «Разрушение брони» снижает физ. защиту {target.DisplayName} ещё на {extraDegrade}.");
            }
        }

        // "Насквозь" (3.10, Стремительное копьё): часть урона дополнительно проходит по всем
        // остальным живым противникам в комнате, помимо выбранной цели.
        if (attacker.IsPlayer && weapon.PiercingLevel > 0)
        {
            float splashDamage = damage * weapon.PiercingLevel * 0.01f; // 1% урона за уровень предмета
            if (splashDamage > 0f)
            {
                foreach (var other in Enemies)
                {
                    if (other == target || !other.IsAlive)
                    {
                        continue;
                    }

                    var splashResult = DamageCalculator.ApplyDamage(other, splashDamage, weapon.DamageType);
                    Log($"[Combat] «Насквозь» задевает {other.DisplayName}: {splashResult.DamageToHP:F1} урона по HP.");
                }
            }
        }

        // "Шипы": если атака не пробила броню (полный блок) — отражается часть заблокированного
        // урона; на 5 уровне также отражает 50% урона, даже если атака пробила броню.
        if (target.SkillThornsLevel > 0 && weapon.DamageType == DamageType.Physical)
        {
            float reflectPercent = target.SkillThornsLevel * 0.20f; // 20/40/60/80/100%
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

        // 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (симметрично блоку выше, но для
        // не-игрока — у игрока нет MonsterPassiveName).
        ApplyMonsterPassiveOnAttack(attacker, target, result);

        if (!target.IsAlive)
        {
            Log($"[Combat] {target.DisplayName} погибает.");
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

    // 2.4: пассивки монстров, срабатывающие ПРИ АТАКЕ (в отличие от периодических — см.
    // TickMonsterPeriodicPassives). attacker всегда монстр здесь (у игрока нет MonsterPassiveName).
    void ApplyMonsterPassiveOnAttack(CombatantRuntime attacker, CombatantRuntime target, DamageCalculator.DamageResult result)
    {
        if (attacker.IsPlayer || attacker.MonsterPassiveName == null)
        {
            return;
        }

        switch (attacker.MonsterPassiveName)
        {
            case MonsterSkillEffectMap.Pickpocket:
                // "При попадании по здоровью персонажа с 20% шансом ворует 5% текущей валюты забега."
                if (!result.WasBlocked && Random.value < 0.20f)
                {
                    MonsterStoleCurrency?.Invoke(target, 5f);
                    Log($"[Combat] {attacker.DisplayName} обчищает карманы {target.DisplayName} (Карманник)!");
                }
                break;

            case MonsterSkillEffectMap.Poison:
                // "При попадании по здоровью накладывает яд (3 сек, 4 урона/сек, стакается до 3 раз)."
                if (!result.WasBlocked)
                {
                    target.PoisonStacks = Mathf.Min(target.PoisonStacks + 1, 3);
                    target.PoisonTimer = 3f;
                    Log($"[Combat] {target.DisplayName} получает яд ({target.PoisonStacks}/3 стаков).");
                }
                break;

            case MonsterSkillEffectMap.StunningScream:
                // "15% шанс при атаке снизить шанс крита персонажа на 20% на 4 сек." — на атаке, не на попадании.
                if (Random.value < 0.15f)
                {
                    target.CritChanceDebuffPercent = 20f;
                    target.CritChanceDebuffTimer = 4f;
                    Log($"[Combat] Оглушающий крик {attacker.DisplayName} снижает шанс крита {target.DisplayName}.");
                }
                break;

            case MonsterSkillEffectMap.SlowCurse:
                // "Если урон Колдуна проходит по здоровью персонажа, скорость атаки персонажа снижается
                // на 30% на 3 секунды (не стакается, повторное попадание обновляет длительность)."
                if (!result.WasBlocked)
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
            if (!enemy.IsAlive || enemy.MonsterPassiveName == null)
            {
                continue;
            }

            if (enemy.MonsterPassiveName == MonsterSkillEffectMap.DarkHeal)
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
            else if (enemy.MonsterPassiveName == MonsterSkillEffectMap.DoubleStrike)
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
