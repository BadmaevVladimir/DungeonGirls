using UnityEngine;

public static class DamageCalculator
{
    // Final damage and HP use whole points; midpoint values round up.
    public static float RoundPoints(float value) => Mathf.Floor(Mathf.Max(0f, value) + 0.5f);

    public static float ApplyDirectDamage(CombatantRuntime target, float damage)
    {
        if (target.IsInvulnerable) return 0f;
        float before = RoundPoints(target.CurrentHP);
        float dealt = Mathf.Min(before, RoundPoints(damage));
        target.CurrentHP = before - dealt;
        if (dealt > 0f) target.NotifyHpDamageResolved();
        return dealt;
    }
    public struct DamageResult
    {
        public float DamageToHP;
        public bool WasBlocked;
        // true, если урон был полностью заблокирован, но всё равно износил броню.
        // Магический щит эту логику не использует — у ApplyMagicalDamage всегда false.
        public bool ArmorWornOnBlock;
        // Boss framework (минимальный слайс) — сколько урона поглотил CombatantRuntime.ShieldPool*
        // ДО брони/щита (см. ApplyDamage ниже). Только для UI/лога; WasBlocked/ArmorWornOnBlock не
        // учитывают это отдельно — если shield pool поглотил ВЕСЬ урон, остаток = 0 естественно
        // проходит через обычную ветку "полный блок" ApplyPhysicalDamage/ApplyMagicalDamage.
        public float ShieldPoolDamageAbsorbed;
    }

    // 3.3 [ОБНОВЛЕНО после анализа брони]: любой положительный физический удар гарантированно
    // изнашивает броню. Износ = max(1, floor(урон / 20)); при полном пробитии сохраняется минимум
    // 2 единицы. Благодаря этому слабые враги больше не могут бесконечно бить броню без последствий,
    // а сильные удары заметно быстрее расходуют слишком большой запас.
    // 3.11 Часть 2 (НОВОЕ): armorIgnorePercent — только для Клинка (Зазубренный/Моменто Мори,
    // см. WeaponAttackState.ArmorIgnorePercent) — снижает ЭФФЕКТИВНУЮ броню для целей проверки
    // блок/пробитие/износ, но НЕ влияет на то, сколько единиц брони теряется при пробитии (те
    // правила остаются про АБСОЛЮТНУЮ броню, не эффективную).
    public static DamageResult ApplyPhysicalDamage(CombatantRuntime target, float incomingDamage, float armorIgnorePercent = 0f)
    {
        incomingDamage = Mathf.Max(0f, incomingDamage);
        float cursedDefenseMultiplier = CursedItemRules.IsCurseActive(target, CursedEffectId.RecklessCharge)
            ? CursedItemRules.RecklessDefenseMultiplier(target.CursedRecklessStacks) : 1f;
        float foodArmorMultiplier = 1f + target.FoodArmorEffectivenessPercent / 100f;
        float effectiveDefense = target.PhysicalDefenseCurrent * foodArmorMultiplier * cursedDefenseMultiplier * (1f - Mathf.Clamp01(armorIgnorePercent / 100f));
        float armorLoss = incomingDamage > 0f ? Mathf.Max(1f, Mathf.Floor(incomingDamage / 20f)) : 0f;

        if (incomingDamage <= effectiveDefense)
        {
            bool armorWorn = armorLoss > 0f && target.PhysicalDefenseCurrent > 0f;
            target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - armorLoss);

            return new DamageResult { DamageToHP = 0f, WasBlocked = true, ArmorWornOnBlock = armorWorn };
        }

        float remainder = incomingDamage - effectiveDefense;
        if (effectiveDefense > 0f && incomingDamage >= effectiveDefense * 2f)
        {
            armorLoss = Mathf.Max(2f, armorLoss);
        }
        target.PhysicalDefenseCurrent = Mathf.Max(0f, target.PhysicalDefenseCurrent - armorLoss);
        remainder = ApplyDirectDamage(target, remainder);

        return new DamageResult { DamageToHP = remainder, WasBlocked = remainder <= 0f };
    }

    // 3.3: магический урон поглощается магическим щитом, пока тот не закончится.
    public static DamageResult ApplyMagicalDamage(CombatantRuntime target, float incomingDamage)
    {
        incomingDamage = RoundPoints(incomingDamage);
        target.MagicShieldCurrent = RoundPoints(target.MagicShieldCurrent);
        if (target.MagicShieldCurrent >= incomingDamage)
        {
            target.MagicShieldCurrent -= incomingDamage;
            return new DamageResult { DamageToHP = 0f, WasBlocked = true };
        }

        float remainder = incomingDamage - target.MagicShieldCurrent;
        target.MagicShieldCurrent = 0f;
        remainder = ApplyDirectDamage(target, remainder);

        return new DamageResult { DamageToHP = remainder, WasBlocked = remainder <= 0f };
    }

    // 3.2 [ОБНОВЛЕНО после плейтеста]: любое ранее фиксированное значение урона снаряжения теперь
    // диапазон вокруг базового значения — [ПОЛ(база×0.8); ОКРУГЛВВЕРХ(база×1.2)]. Общее правило,
    // применяется в CombatantFactory при сборке WeaponAttackState из ItemData.EffectiveDamage.
    // Урон монстров НЕ идёт через эту формулу — MonsterData уже хранит явные diapazon-поля
    // damageMin/damageMax (не единое "базовое" значение), их не трогаем.
    public static void ComputeDamageRange(float baseDamage, out float min, out float max)
    {
        min = Mathf.Floor(baseDamage * 0.8f);
        max = Mathf.Ceil(baseDamage * 1.2f);
    }

    // 3.11 Часть 2 (НОВОЕ): "% сопротивления урону" — общий множитель, первый шаг в цепочке расчёта,
    // ДО брони/щита. Суммируется по всем источникам одного типа урона, клампится на 100% (0 урона
    // дальше по цепочке, а не отрицательный урон).
    public static DamageResult ApplyDamage(CombatantRuntime target, float incomingDamage, DamageType damageType, float armorIgnorePercent = 0f)
    {
        // Boss framework: неуязвимость (Свечник, пока горит хоть одна свеча) режет урон до нуля
        // в самом начале цепочки — ни броня, ни щиты не изнашиваются, попадание просто ничего не даёт.
        if (target.IsInvulnerable)
        {
            return new DamageResult { DamageToHP = 0f, WasBlocked = true };
        }

        float resistancePercent = damageType == DamageType.Physical ? target.PhysicalResistancePercent : target.MagicalResistancePercent;
        float receivedMultiplier = 1f;
        var berserkerWeapon = target.FindCursedWeapon(CursedEffectId.BerserkerAxe);
        if (berserkerWeapon != null && CursedItemRules.IsCurseActive(target, CursedEffectId.BerserkerAxe))
            receivedMultiplier *= 1f + CursedItemRules.StackBonusPercent(berserkerWeapon.ItemRank, berserkerWeapon.CursedStacks) / 100f;
        // Boss framework: окно повышенного получаемого урона (DamageTakenBuff). Складывается в
        // тот же receivedMultiplier, что и проклятый BerserkerAxe — то есть применяется ДО
        // сопротивления, брони и щитов, как и всякий множитель получаемого урона.
        // Boss framework: «Связь» — пока жив союзник по группе, участник получает меньше урона.
        // Кламп на 90%, чтобы кривой ассет не сделал связку неубиваемой.
        if (target.BossGroupDamageReductionPercent > 0f)
            receivedMultiplier *= 1f - Mathf.Clamp(target.BossGroupDamageReductionPercent, 0f, 90f) / 100f;

        if (target.DamageTakenBonusPercent > 0f)
            receivedMultiplier *= 1f + target.DamageTakenBonusPercent / 100f;
        float damageAfterResistance = Mathf.Max(0f, incomingDamage * receivedMultiplier * (1f - Mathf.Clamp01(resistancePercent / 100f)));

        // Boss framework (минимальный слайс) — shield pool (BossAbilityEffectKind.ShieldPool) поглощает
        // урон ЛЮБОГО типа ДО брони/маг. щита, отдельно от них. Когда ShieldPoolCurrent==0 (подавляющее
        // большинство целей — обычные монстры/игрок без активного щита-способности) эта ветка не
        // меняет ничего: shieldAbsorbed=0, поведение байт-в-байт как раньше.
        float shieldAbsorbed = 0f;
        target.ShieldPoolCurrent = RoundPoints(target.ShieldPoolCurrent);
        if (target.ShieldPoolCurrent > 0f && damageAfterResistance > 0f)
        {
            shieldAbsorbed = RoundPoints(Mathf.Min(target.ShieldPoolCurrent, damageAfterResistance));
            target.ShieldPoolCurrent -= shieldAbsorbed;
            damageAfterResistance = Mathf.Max(0f, damageAfterResistance - shieldAbsorbed);
        }

        var result = damageType == DamageType.Physical
            ? ApplyPhysicalDamage(target, damageAfterResistance, armorIgnorePercent)
            : ApplyMagicalDamage(target, damageAfterResistance);
        result.ShieldPoolDamageAbsorbed = shieldAbsorbed;
        return result;
    }

    // The ordinary physical hit is unchanged. A separate shield-only bonus strips magic shield;
    // it cannot overflow to HP or steal damage from the physical hit.
    public static DamageResult ApplySpellEaterPhysicalDamage(CombatantRuntime target, float incomingDamage,
        float armorIgnorePercent, out float magicShieldRemoved)
    {
        magicShieldRemoved = 0f;
        if (target.IsInvulnerable) return new DamageResult { WasBlocked = true };
        var result = ApplyDamage(target, incomingDamage, DamageType.Physical, armorIgnorePercent);
        target.MagicShieldCurrent = RoundPoints(target.MagicShieldCurrent);
        magicShieldRemoved = Mathf.Min(target.MagicShieldCurrent, RoundPoints(incomingDamage));
        target.MagicShieldCurrent -= magicShieldRemoved;
        return result;
    }
}
