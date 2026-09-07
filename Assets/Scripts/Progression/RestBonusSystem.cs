using System;
using UnityEngine;

// ГДД 8.1, Таверна ур.5 (G04/R07): после привала героиня получает случайный бонус на 3 комнаты.
// Решение D03b от 07.09.2026: бонус живёт в ОТДЕЛЬНОМ слоте и складывается с эффектом съеденного
// блюда — одно другое не вытесняет. Поэтому здесь свои поля на CombatantRuntime, а не
// переиспользование Food*-полей: ActiveFoodBuff присваивает их абсолютными значениями и обнуляет
// при истечении, так что любой сторонний вклад в них был бы затёрт.
public enum RestBonusId
{
    None,
    Damage,
    AttackSpeed,
    ReceivedHealing,
    CriticalChance
}

public readonly struct RestBonusDefinition
{
    public readonly RestBonusId Id;
    public readonly string DisplayName;
    public readonly float Value;
    public readonly string ValueSuffix;

    public RestBonusDefinition(RestBonusId id, string displayName, float value, string valueSuffix)
    {
        Id = id;
        DisplayName = displayName;
        Value = value;
        ValueSuffix = valueSuffix;
    }

    public string Describe() => $"{DisplayName} +{Value:F0}{ValueSuffix}";
}

// [ПРЕДПОЛОЖЕНИЕ] Состав пула, величины и равные веса выбраны здесь, а не дизайнером: D03b
// утвердил механику («случайный бонус, отдельный слот, 3 комнаты»), но не числа. Величины взяты
// в диапазоне эффектов блюд, чтобы бонус ощущался, но не перебивал сборку. Меняются здесь одним
// местом.
public static class RestBonusCatalog
{
    public const int DurationRooms = 3;

    public static readonly RestBonusDefinition[] All =
    {
        new RestBonusDefinition(RestBonusId.Damage, "Урон", 15f, "%"),
        new RestBonusDefinition(RestBonusId.AttackSpeed, "Скорость атаки", 15f, "%"),
        new RestBonusDefinition(RestBonusId.ReceivedHealing, "Получаемое лечение", 25f, "%"),
        new RestBonusDefinition(RestBonusId.CriticalChance, "Шанс критического удара", 8f, " п.п.")
    };

    // Равные веса: специального распределения дизайнер не задавал.
    public static RestBonusDefinition Roll(IRewardRandom random)
    {
        random ??= new UnityRewardRandom();
        return All[Mathf.Clamp(random.Range(0, All.Length), 0, All.Length - 1)];
    }

    public static RestBonusDefinition Find(RestBonusId id)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Id == id) return All[i];
        return default;
    }
}

// Форма повторяет ActiveRunRoomDebuff: аддитивная привязка к CombatantRuntime, счётчик комнат,
// переустановка при новом привале. Новый бонус заменяет предыдущий бонус привала (но не блюдо).
public sealed class ActiveRestBonus
{
    public RestBonusId Id { get; private set; }
    public float Value { get; private set; }
    public int RemainingRooms { get; private set; }
    public bool IsActive => Id != RestBonusId.None && RemainingRooms > 0;

    public string DisplayName => IsActive ? RestBonusCatalog.Find(Id).DisplayName : string.Empty;
    public string Describe() => IsActive
        ? $"{RestBonusCatalog.Find(Id).Describe()} (ещё {RemainingRooms} комн.)"
        : string.Empty;

    CombatantRuntime boundRuntime;

    public void Activate(RestBonusDefinition definition, CombatantRuntime runtime)
    {
        Clear();
        if (definition.Id == RestBonusId.None) return;
        Id = definition.Id;
        Value = definition.Value;
        RemainingRooms = RestBonusCatalog.DurationRooms;
        Bind(runtime);
    }

    public void Bind(CombatantRuntime runtime)
    {
        Unbind();
        if (!IsActive || runtime == null) return;
        boundRuntime = runtime;
        switch (Id)
        {
            case RestBonusId.Damage: boundRuntime.RestBonusDamagePercent += Value; break;
            case RestBonusId.AttackSpeed: boundRuntime.RestBonusAttackSpeedPercent += Value; break;
            case RestBonusId.ReceivedHealing: boundRuntime.RestBonusReceivedHealingPercent += Value; break;
            case RestBonusId.CriticalChance: boundRuntime.RestBonusCritChancePoints += Value; break;
        }
    }

    // Привал сам по себе комнатой не является, поэтому бонус, выданный на привале, доживает ровно
    // до конца третьей последующей комнаты — так же, как блюдо.
    public void CompleteRoom()
    {
        if (!IsActive) return;
        RemainingRooms--;
        if (RemainingRooms <= 0) Clear();
    }

    public void Clear()
    {
        Unbind();
        Id = RestBonusId.None;
        Value = 0f;
        RemainingRooms = 0;
    }

    void Unbind()
    {
        if (boundRuntime == null) return;
        switch (Id)
        {
            case RestBonusId.Damage: boundRuntime.RestBonusDamagePercent -= Value; break;
            case RestBonusId.AttackSpeed: boundRuntime.RestBonusAttackSpeedPercent -= Value; break;
            case RestBonusId.ReceivedHealing: boundRuntime.RestBonusReceivedHealingPercent -= Value; break;
            case RestBonusId.CriticalChance: boundRuntime.RestBonusCritChancePoints -= Value; break;
        }
        boundRuntime = null;
    }
}
