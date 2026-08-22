using UnityEngine;

public static class CombatantFactory
{
    public static CombatantRuntime CreatePlayerCombatant(CharacterData character, int level)
    {
        var runtime = new CombatantRuntime
        {
            DisplayName = character.characterName,
            IsPlayer = true
        };

        int levelIndex = Mathf.Max(level, 1);
        runtime.MaxHP = character.baseHealth + character.healthPerLevel * (levelIndex - 1);
        runtime.CurrentHP = runtime.MaxHP;

        AggregateEquipmentStats(
            character.startingEquipment,
            out float weaponDamage,
            out float attackSpeed,
            out DamageType damageType,
            out float physicalDefense,
            out float maxPhysicalDefenseBonus,
            out float magicShield);

        runtime.DamageMin = weaponDamage;
        runtime.DamageMax = weaponDamage;
        runtime.DamageType = damageType;
        runtime.AttackSpeed = attackSpeed;

        runtime.PhysicalDefenseMax = physicalDefense + maxPhysicalDefenseBonus;
        runtime.PhysicalDefenseCurrent = runtime.PhysicalDefenseMax;

        runtime.MagicShieldMax = magicShield;
        runtime.MagicShieldCurrent = magicShield;

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
            DamageMin = monster.damageMin * damageMultiplier,
            DamageMax = monster.damageMax * damageMultiplier,
            DamageType = monster.damageType,
            AttackSpeed = monster.attackSpeed,
            PhysicalDefenseMax = monster.physicalDefense,
            PhysicalDefenseCurrent = monster.physicalDefense,
            MagicShieldMax = monster.magicDefense,
            MagicShieldCurrent = monster.magicDefense
        };

        runtime.CurrentHP = runtime.MaxHP;
        return runtime;
    }

    // 3.3: только предмет в слоте "Броня" несёт значение физ. защиты, остальные слоты
    // (шлем/сапоги/щит/кольца/аксессуары) только увеличивают её максимум.
    static void AggregateEquipmentStats(
        ItemData[] items,
        out float weaponDamage,
        out float attackSpeed,
        out DamageType damageType,
        out float physicalDefense,
        out float maxPhysicalDefenseBonus,
        out float magicShield)
    {
        weaponDamage = 0f;
        attackSpeed = 0f;
        damageType = DamageType.Physical;
        physicalDefense = 0f;
        maxPhysicalDefenseBonus = 0f;
        magicShield = 0f;

        if (items == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            if (item.slot == EquipmentSlot.Weapon && item.weaponSubtype != WeaponSubtype.None && item.weaponSubtype != WeaponSubtype.Shield)
            {
                weaponDamage += item.baseDamage;
                damageType = item.damageType;
                attackSpeed = Mathf.Max(attackSpeed, item.attackSpeed);
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
                magicShield += item.bonusStat.baseValue;
            }
        }
    }
}
