using System.Collections.Generic;
using UnityEngine;

public static class CombatantFactory
{
    // progress == null: поведение как в Фазе 3 (голые статы снаряжения, без навыков).
    // progress != null: дополнительно применяются уровни известных навыков (3.9) — как к базовым
    // статам (Прочный, "Я — стена", Амбидекстрия), так и к рантайм-полям для динамических эффектов
    // боя (Заморозка/Уклонение/Крит/Шипы/Несгибаемый/Кровотечение — см. CombatManager).
    // equipment == null: используется character.startingEquipment (стартовый лоадаут); иначе —
    // переданный список текущего снаряжения персонажа за забег (3.4, после эквипа новых предметов).
    // tavernLevel: 8.1 — Таверна ур.1 добавляет флэт-урон ко всем атакам оружия персонажа.
    public static CombatantRuntime CreatePlayerCombatant(CharacterData character, int level, RunCharacterProgress progress = null, IReadOnlyList<ItemData> equipment = null, int tavernLevel = 0)
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

        ItemData[] items = equipment != null ? new List<ItemData>(equipment).ToArray() : character.startingEquipment;

        AggregateEquipmentStats(
            items,
            ambidexterityLevel,
            tavernLevel,
            out List<WeaponAttackState> weapons,
            out float physicalDefense,
            out float maxPhysicalDefenseBonus,
            out float magicShield,
            out float critChanceBonus,
            out int elusivenessLevel,
            out int goldenTouchLevel,
            out int toughSoleLevel,
            out int repairLevel);

        runtime.Weapons = weapons;

        runtime.PhysicalDefenseMax = physicalDefense + maxPhysicalDefenseBonus;
        runtime.PhysicalDefenseCurrent = runtime.PhysicalDefenseMax;

        runtime.MagicShieldMax = magicShield;
        runtime.MagicShieldCurrent = magicShield;

        runtime.CritChanceBonusFromItems = critChanceBonus;

        runtime.ItemElusivenessLevel = elusivenessLevel;
        runtime.ItemGoldenTouchLevel = goldenTouchLevel;
        runtime.ItemToughSoleLevel = toughSoleLevel;
        runtime.ItemRepairLevel = repairLevel;

        if (progress != null)
        {
            ApplyCharacterSkills(runtime, progress, items);
        }

        return runtime;
    }

    // 2.6: HP x1.25, урон x1.15, физ. защита x1.8 за этаж — три независимых множителя, каждый
    // накапливается степенью (этаж 1 = база). Скорость атаки и маг. защита не масштабируются.
    //
    // 2.7 [DRAFT, авторский выбор — точная формула в ГДД была открытым вопросом]: у монстра
    // из обычной боевой комнаты есть свой уровень, растущий с позицией комнаты в мешке этажа
    // (8.4): monsterLevel = 1 + (позиция / 3), где позиция — число уже пройденных комнат этажа
    // (RunFlowController передаёт floorManager.RoomsCompletedOnFloor). При составе мешка 12
    // комнат (8.4) это даёт уровни 1 (комнаты 0-2) → 2 (3-5) → 3 (6-8) → 4 (9-11), т.е. плавный
    // рост к боссу без баланс-выбросов. Уровень масштабирует HP/урон/броню через ту же формулу
    // мин.+1/уровень, что и предметы (StatScaling, см. 3.10), применяется ПОВЕРХ уже
    // отмасштабированных по этажу (2.6) значений — прирост считается от уже увеличенного этажом
    // числа. Босс уровня не получает (monsterLevel по умолчанию 1 = формула не меняет базу) —
    // гейт на босса держится отдельно (см. 2.2).
    public static CombatantRuntime CreateMonsterCombatant(MonsterData monster, int floorNumber, int monsterLevel = 1)
    {
        int floorIndex = Mathf.Max(floorNumber, 1);
        int level = Mathf.Max(monsterLevel, 1);
        float hpMultiplier = FloorScalingMultiplier(1.25f, floorIndex);
        float damageMultiplier = FloorScalingMultiplier(1.15f, floorIndex);
        float armorMultiplier = FloorScalingMultiplier(1.8f, floorIndex);

        float hp = StatScaling.ApplyLevelBonus(monster.hp * hpMultiplier, level);
        float armor = StatScaling.ApplyLevelBonus(monster.physicalDefense * armorMultiplier, level);
        float damageMin = StatScaling.ApplyLevelBonus(monster.damageMin * damageMultiplier, level);
        float damageMax = StatScaling.ApplyLevelBonus(monster.damageMax * damageMultiplier, level);

        var runtime = new CombatantRuntime
        {
            DisplayName = monster.monsterName,
            IsPlayer = false,
            MaxHP = hp,
            PhysicalDefenseMax = armor,
            PhysicalDefenseCurrent = armor,
            MagicShieldMax = monster.magicDefense,
            MagicShieldCurrent = monster.magicDefense
        };
        runtime.CurrentHP = runtime.MaxHP;

        runtime.Weapons.Add(new WeaponAttackState
        {
            DamageMin = damageMin,
            DamageMax = damageMax,
            DamageType = monster.damageType,
            AttackSpeed = monster.attackSpeed
        });

        return runtime;
    }

    // 2.6: общая формула масштабирования по этажам — множитель за этаж накапливается степенью,
    // общая для HP/урона/брони монстров (каждому передаётся свой per-floor коэффициент).
    static float FloorScalingMultiplier(float perFloorMultiplier, int floorIndex) => Mathf.Pow(perFloorMultiplier, floorIndex - 1);

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
                sum += item.EffectiveMaxDefenseBonus;
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
        int tavernLevel,
        out List<WeaponAttackState> weapons,
        out float physicalDefense,
        out float maxPhysicalDefenseBonus,
        out float magicShield,
        out float critChanceBonus,
        out int elusivenessLevel,
        out int goldenTouchLevel,
        out int toughSoleLevel,
        out int repairLevel)
    {
        weapons = new List<WeaponAttackState>();
        physicalDefense = 0f;
        maxPhysicalDefenseBonus = 0f;
        magicShield = 0f;
        critChanceBonus = 0f;
        elusivenessLevel = 0;
        goldenTouchLevel = 0;
        toughSoleLevel = 0;
        repairLevel = 0;

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

        float tavernFlatDamage = BuildingCatalog.TavernFlatDamageBonus(tavernLevel); // 8.1: ур.1, до диапазона/брони

        foreach (var item in realWeaponItems)
        {
            float itemDamage = item.EffectiveDamage; // 3.10: основной стат уже с бонусом уровня
            if (isDualWielding)
            {
                itemDamage *= dualWieldMultiplier;
            }

            itemDamage += tavernFlatDamage; // 8.1: флэт-бонус Таверны, независимо от Кузницы

            // 3.2: фиксированный урон -> диапазон [ПОЛ(база×0.8); ОКРУГЛВВЕРХ(база×1.2)].
            DamageCalculator.ComputeDamageRange(itemDamage, out float damageMin, out float damageMax);

            string passiveName = item.passiveSkill != null ? item.passiveSkill.skillName : null;
            weapons.Add(new WeaponAttackState
            {
                DamageMin = damageMin,
                DamageMax = damageMax,
                DamageType = item.damageType,
                AttackSpeed = item.attackSpeed,
                VampirismLevel = passiveName == SkillEffectMap.Vampirism ? item.itemLevel : 0,
                ArmorBreakLevel = passiveName == SkillEffectMap.ArmorBreak ? item.itemLevel : 0,
                PiercingLevel = passiveName == SkillEffectMap.Piercing ? item.itemLevel : 0
            });

            if (passiveName == SkillEffectMap.Repair)
            {
                repairLevel += item.itemLevel;
            }
        }

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            if (item.slot == EquipmentSlot.Armor)
            {
                physicalDefense += item.EffectiveDefense;
            }
            else
            {
                maxPhysicalDefenseBonus += item.EffectiveMaxDefenseBonus;
            }

            if (item.bonusStat != null && item.bonusStat.type == BonusStatType.MagicShieldFlat)
            {
                magicShield += item.bonusStat.baseValue * item.itemLevel;
            }

            if (item.bonusStat != null && item.bonusStat.type == BonusStatType.CritChancePercent)
            {
                critChanceBonus += item.bonusStat.baseValue * item.itemLevel;
            }

            string passiveName = item.passiveSkill != null ? item.passiveSkill.skillName : null;
            if (passiveName == SkillEffectMap.Elusiveness)
            {
                elusivenessLevel += item.itemLevel;
            }
            else if (passiveName == SkillEffectMap.GoldenTouch)
            {
                goldenTouchLevel += item.itemLevel;
            }
            else if (passiveName == SkillEffectMap.ToughSole)
            {
                toughSoleLevel += item.itemLevel;
            }
        }
    }
}
