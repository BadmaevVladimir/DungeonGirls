using System.Collections.Generic;
using UnityEngine;

// 2.8: каталог из 4 модификаторов монстров + формула шанса/лимита по этажам + русские формы
// прилагательных по роду (MonsterData.gender).
public static class MonsterModifierCatalog
{
    static readonly MonsterModifierType[] AllTypes =
    {
        MonsterModifierType.Fast, MonsterModifierType.Big, MonsterModifierType.Armored, MonsterModifierType.Fierce
    };

    // 2.8: лимит модификаторов на монстра по этажам. Этаж 1 = 0; 2-5 = 1; 6-9 = 2; 10 = без лимита
    // (реалистичный потолок = 4, размер каталога — дублирование одного модификатора не предусмотрено).
    public static int ModifierCapForFloor(int floorNumber)
    {
        if (floorNumber <= 1) return 0;
        if (floorNumber <= 5) return 1;
        if (floorNumber <= 9) return 2;
        return AllTypes.Length;
    }

    // 2.8: шанс = 0%/10%/20%/30% на уровнях монстра 1/2/3/4 (см. 2.7 — диапазон уровня 1-4).
    public static float RollChancePercentForLevel(int monsterLevel)
    {
        int clampedLevel = Mathf.Clamp(monsterLevel, 1, 4);
        return (clampedLevel - 1) * 10f;
    }

    // 2.8: последовательные независимые роллы до лимита этажа; первый провал останавливает
    // дальнейшие роллы (не пропускает слот и не пробует следующий).
    public static List<MonsterModifierType> RollModifiers(int floorNumber, int monsterLevel)
    {
        var result = new List<MonsterModifierType>();
        int cap = ModifierCapForFloor(floorNumber);
        float chancePercent = RollChancePercentForLevel(monsterLevel);

        if (cap <= 0 || chancePercent <= 0f)
        {
            return result;
        }

        var remaining = new List<MonsterModifierType>(AllTypes);
        for (int i = 0; i < cap; i++)
        {
            if (Random.value * 100f >= chancePercent)
            {
                break; // провал ролла останавливает дальнейшие слоты
            }

            int index = Random.Range(0, remaining.Count);
            result.Add(remaining[index]);
            remaining.RemoveAt(index);

            if (remaining.Count == 0)
            {
                break;
            }
        }

        return result;
    }

    // 2.8: применяется ПОВЕРХ уже отмасштабированных по этажу (2.6) и уровню монстра (2.7) статов.
    public static void ApplyToRuntime(CombatantRuntime runtime, MonsterModifierType modifier)
    {
        switch (modifier)
        {
            case MonsterModifierType.Fast:
                foreach (var weapon in runtime.Weapons)
                {
                    weapon.AttackSpeed *= 1.25f;
                }
                break;

            case MonsterModifierType.Big:
                float oldMax = runtime.MaxHP;
                runtime.MaxHP *= 1.5f;
                runtime.CurrentHP += runtime.MaxHP - oldMax; // монстр только что создан на полном HP
                break;

            case MonsterModifierType.Armored:
                runtime.PhysicalDefenseMax += 5f;
                runtime.PhysicalDefenseCurrent += 5f;
                break;

            case MonsterModifierType.Fierce:
                foreach (var weapon in runtime.Weapons)
                {
                    weapon.DamageMin *= 1.25f;
                    weapon.DamageMax *= 1.25f;
                }
                break;
        }
    }

    // 2.8: согласование рода. Полный явный switch по (modifier, gender) — надёжнее короткой основы
    // + суффикса для 4 фиксированных прилагательных, меньше риск опечатки на рантайме.
    public static string AdjectiveFor(MonsterModifierType modifier, MonsterGender gender)
    {
        switch (modifier)
        {
            case MonsterModifierType.Fast:
                return gender == MonsterGender.Masculine ? "Быстрый" : gender == MonsterGender.Feminine ? "Быстрая" : "Быстрое";
            case MonsterModifierType.Big:
                return gender == MonsterGender.Masculine ? "Большой" : gender == MonsterGender.Feminine ? "Большая" : "Большое";
            case MonsterModifierType.Armored:
                return gender == MonsterGender.Masculine ? "Бронированный" : gender == MonsterGender.Feminine ? "Бронированная" : "Бронированное";
            default:
                return gender == MonsterGender.Masculine ? "Свирепый" : gender == MonsterGender.Feminine ? "Свирепая" : "Свирепое";
        }
    }
}
