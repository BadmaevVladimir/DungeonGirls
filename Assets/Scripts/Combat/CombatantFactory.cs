using System.Collections.Generic;
using UnityEngine;

public static class CombatantFactory
{
    // progress == null: поведение как в Фазе 3 (голые статы снаряжения, без навыков).
    // progress != null: дополнительно применяются уровни известных навыков (3.9) — как к базовым
    // статам (Прочный, "Я — стена", Амбидекстрия), так и к рантайм-полям для динамических эффектов
    // боя (Заморозка/Уклонение/Крит/Шипы/Несгибаемый/Кровотечение — см. CombatManager).
    public static CombatantRuntime CreatePlayerCombatant(CharacterData character, int level, RunCharacterProgress progress = null)
    {
        var runtime = new CombatantRuntime
        {
            DisplayName = character.characterName,
            IsPlayer = true
        };

        int levelIndex = Mathf.Max(level, 1);
        runtime.MaxHP = character.baseHealth + character.healthPerLevel * (levelIndex - 1);
        runtime.CurrentHP = runtime.MaxHP;

        int ambidexterityLevel = progress != null ? progress.GetSkillLevel(SkillEffectMap.Ambidexterity) : 0;

        AggregateEquipmentStats(
            character.startingEquipment,
            ambidexterityLevel,
            out List<WeaponAttackState> weapons,
            out float physicalDefense,
            out float maxPhysicalDefenseBonus,
            out float magicShield,
            out float critChanceBonus);

        runtime.Weapons = weapons;

        runtime.PhysicalDefenseMax = physicalDefense + maxPhysicalDefenseBonus;
        runtime.PhysicalDefenseCurrent = runtime.PhysicalDefenseMax;

        runtime.MagicShieldMax = magicShield;
        runtime.MagicShieldCurrent = magicShield;

        runtime.CritChanceBonusFromItems = critChanceBonus;

        if (progress != null)
        {
            ApplyCharacterSkills(runtime, progress, character.startingEquipment);
        }

        return runtime;
    }

    // 2.6: HP x1.25 и урон x1.15 за этаж, множители накапливаются. Скорость атаки и защита не масштабируются.
    public static CombatantRuntime CreateMonsterCombatant(MonsterData monster, int floorNumber)
    {
        int floorIndex = Mathf.Max(floorNumber, 1);
        float hpMultiplier = Mathf.Pow(1.25f, floorIndex - 1);
        float damageMultiplier = Mathf.Pow(1.15f, floorIndex - 1);

        var runtime = new CombatantRuntime
        {
            DisplayName = monster.monsterName,
            IsPlayer = false,
            MaxHP = monster.hp * hpMultiplier,
            PhysicalDefenseMax = monster.physicalDefense,
            PhysicalDefenseCurrent = monster.physicalDefense,
            MagicShieldMax = monster.magicDefense,
            MagicShieldCurrent = monster.magicDefense
        };
        runtime.CurrentHP = runtime.MaxHP;

        runtime.Weapons.Add(new WeaponAttackState
        {
            DamageMin = monster.damageMin * damageMultiplier,
            DamageMax = monster.damageMax * damageMultiplier,
            DamageType = monster.damageType,
            AttackSpeed = monster.attackSpeed
        });

        return runtime;
    }

    // 3.9: "Прочный" (% к физ. защите) и "Я — стена" (часть бонуса брони от щита -> флэт урон)
    // запекаются в базовые статы один раз при постройке боевого юнита. Остальные навыки —
    // динамические эффекты боя, поэтому здесь только копируются их уровни на рантайм-объект.
    static void ApplyCharacterSkills(CombatantRuntime runtime, RunCharacterProgress progress, ItemData[] equippedItems)
    {
        int sturdyLevel = progress.GetSkillLevel(SkillEffectMap.Sturdy);
        if (sturdyLevel > 0)
        {
            float bonusPct = sturdyLevel * 0.05f; // 5/10/15/20/25%
            runtime.PhysicalDefenseMax *= 1f + bonusPct;
            runtime.PhysicalDefenseCurrent = runtime.PhysicalDefenseMax;
        }

        int wallLevel = progress.GetSkillLevel(SkillEffectMap.IAmTheWall);
        if (wallLevel > 0)
        {
            // Щит занимает второй слот "оружие" (3.1), поэтому персонаж со щитом физически не может
            // дуал-вилдить двумя настоящими оружиями — flat-бонус всегда достаётся единственному
            // экипированному оружию, конфликта с "Амбидекстрией" не возникает.
            float bonusPct = wallLevel * 0.10f; // 10/20/30/40/50%
            float shieldDefenseBonus = SumShieldMaxDefenseBonus(equippedItems);
            float flatDamage = shieldDefenseBonus * bonusPct;

            foreach (var weapon in runtime.Weapons)
            {
                weapon.DamageMin += flatDamage;
                weapon.DamageMax += flatDamage;
            }
        }

        runtime.SkillFreezeLevel = progress.GetSkillLevel(SkillEffectMap.Freeze);
        runtime.SkillLuckLevel = progress.GetSkillLevel(SkillEffectMap.Luck);
        runtime.SkillEvasionLevel = progress.GetSkillLevel(SkillEffectMap.Evasion);
        runtime.SkillSturdyLevel = sturdyLevel;
        runtime.SkillCriticalHitsLevel = progress.GetSkillLevel(SkillEffectMap.CriticalHits);
        runtime.SkillIAmTheWallLevel = wallLevel;
        runtime.SkillAmbidexterityLevel = progress.GetSkillLevel(SkillEffectMap.Ambidexterity);
        runtime.SkillThornsLevel = progress.GetSkillLevel(SkillEffectMap.Thorns);
        runtime.SkillUnyieldingLevel = progress.GetSkillLevel(SkillEffectMap.Unyielding);
        runtime.SkillBleedLevel = progress.GetSkillLevel(SkillEffectMap.Bleed);
    }

    static float SumShieldMaxDefenseBonus(ItemData[] items)
    {
        float sum = 0f;
        if (items == null)
        {
            return sum;
        }

        foreach (var item in items)
        {
            if (item != null && item.slot == EquipmentSlot.Weapon && item.weaponSubtype == WeaponSubtype.Shield)
            {
                sum += item.maxPhysicalDefenseBonus;
            }
        }

        return sum;
    }

    // 3.3: только предмет в слоте "Броня" несёт значение физ. защиты, остальные слоты
    // (шлем/сапоги/щит/кольца/аксессуары) только увеличивают её максимум.
    //
    // 3.9 "Амбидекстрия": при экипировке 2 настоящих оружий (не щита) урон каждого множится на
    // штраф (75% без навыка, 90/100/110/120/130% по уровням 1-5), но КАЖДОЕ оружие получает
    // отдельный WeaponAttackState со своей собственной скоростью атаки и независимым таймером —
    // они бьют по своему графику (подтверждено в ГДД), а не синхронно и не слитно в одно число.
    static void AggregateEquipmentStats(
        ItemData[] items,
        int ambidexterityLevel,
        out List<WeaponAttackState> weapons,
        out float physicalDefense,
        out float maxPhysicalDefenseBonus,
        out float magicShield,
        out float critChanceBonus)
    {
        weapons = new List<WeaponAttackState>();
        physicalDefense = 0f;
        maxPhysicalDefenseBonus = 0f;
        magicShield = 0f;
        critChanceBonus = 0f;

        if (items == null)
        {
            return;
        }

        var realWeaponItems = new List<ItemData>();
        foreach (var item in items)
        {
            if (item != null && item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield)
            {
                realWeaponItems.Add(item);
            }
        }

        bool isDualWielding = realWeaponItems.Count >= 2;
        float dualWieldMultiplier = ambidexterityLevel switch
        {
            1 => 0.90f,
            2 => 1.00f,
            3 => 1.10f,
            4 => 1.20f,
            5 => 1.30f,
            _ => 0.75f // базовый штраф без навыка
        };

        foreach (var item in realWeaponItems)
        {
            float itemDamage = item.baseDamage;
            if (isDualWielding)
            {
                itemDamage *= dualWieldMultiplier;
            }

            weapons.Add(new WeaponAttackState
            {
                DamageMin = itemDamage,
                DamageMax = itemDamage,
                DamageType = item.damageType,
                AttackSpeed = item.attackSpeed
            });
        }

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            if (item.slot == EquipmentSlot.Armor)
            {
                physicalDefense += item.physicalDefense;
            }
            else
            {
                maxPhysicalDefenseBonus += item.maxPhysicalDefenseBonus;
            }

            if (item.bonusStat != null && item.bonusStat.type == BonusStatType.MagicShieldFlat)
            {
                magicShield += item.bonusStat.baseValue * item.itemLevel;
            }

            if (item.bonusStat != null && item.bonusStat.type == BonusStatType.CritChancePercent)
            {
                critChanceBonus += item.bonusStat.baseValue * item.itemLevel;
            }
        }
    }
}
