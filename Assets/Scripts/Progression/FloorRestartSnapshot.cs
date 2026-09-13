using System.Collections.Generic;
using UnityEngine;

// ГДД 8.1, Храм ур.5 (G03/R07), решение D03a от 07.09.2026: при смерти игрок возвращается к началу
// текущего этажа. Состояние снимается перед входом в первую комнату этажа и восстанавливается из
// этого снимка.
//
// Снимок делается ТОЛЬКО когда у игрока есть неизрасходованный перезапуск — иначе игра ничего не
// копирует и не платит за неиспользуемую возможность.
//
// [ПРЕДПОЛОЖЕНИЕ] Объём снимка выбран мной; D03a утвердил механику, но не детали. Ограничения
// зафиксированы намеренно и перечислены здесь, чтобы их было видно одним местом:
//  - Постоянные ресурсы (ингредиенты, материалы кузницы) НЕ откатываются. Они пишутся в SaveData
//    прямо по ходу забега как отдельный контракт (ГДД 4, «Контракт постоянных ресурсов»), и откат
//    означал бы разворот уже совершённых дисковых транзакций. Побочный эффект: за один перезапуск
//    можно повторно собрать ресурсы этажа.
//  - Карта этажа повторяется та же, вместе с содержимым узлов: это перезапуск, а не новый этаж.
//    Перегенерация позволила бы перебрасывать неудачный этаж, что сильнее задуманного эффекта.
//  - Временные эффекты на комнаты (блюдо, бонус привала, отравление грибами) сбрасываются, а не
//    восстанавливаются: их внутреннее состояние привязано к боевому рантайму, и половинчатое
//    восстановление давало бы неистекающие модификаторы. Игрок теряет активное блюдо — это
//    известное упрощение первой версии.
public sealed class FloorRestartSnapshot
{
    // ==================== Персонаж ====================
    public CharacterRunStateSnapshot Character;

    // ==================== Привал ====================
    public int RationsRemaining;

    // ==================== Этаж ====================
    public FloorMap Map;
    public int RoomsCompletedOnFloor;

    // ==================== Одноразовое содержимое забега ====================
    // Без них перезапуск открывал бы повторный доступ к личной комнате/«Добыче»/«Мечу в камне»
    // (см. R01) — одноразовость забега должна пережить перезапуск этажа ровно в том состоянии,
    // в каком она была на входе.
    public bool HotSpringsTriggered;
    public bool VioletTrapRoomTriggered;
    public bool SashaBeerCellarTriggered;
    public bool HuntQuestTriggered;
    public bool SwordInStoneSucceeded;
    public bool CampSceneTriggered;
}

// Снимок состояния персонажа. Отдельный тип, чтобы объём копируемого был виден списком, а не
// размазан по вызывающему коду.
public sealed class CharacterRunStateSnapshot
{
    public RunCharacterProgress Progress;
    public CombatantRuntime Combatant;
    public List<ItemData> EquippedItems;
    public RunModifiers Modifiers;
    public int RunCurrency;
    public int RoomsClearedThisRun;
    public int RoomsClearedOnCurrentFloor;
}

public static class RunStateClone
{
    // FloorMap целиком [Serializable] и состоит только из сериализуемых полей, поэтому JsonUtility
    // даёт настоящую глубокую копию — включая ContentKey/ResolvedMonsterIds/офферы торговца.
    public static FloorMap Clone(FloorMap source) =>
        source == null ? null : JsonUtility.FromJson<FloorMap>(JsonUtility.ToJson(source));

    public static RunModifiers Clone(RunModifiers source)
    {
        if (source == null) return null;
        return new RunModifiers
        {
            NextCombatDamageMultiplier = source.NextCombatDamageMultiplier,
            NextCombatAttackSpeedMultiplier = source.NextCombatAttackSpeedMultiplier,
            NextCombatMonsterDamageBuff10Percent = source.NextCombatMonsterDamageBuff10Percent,
            NextChestCurrencyBonus = source.NextChestCurrencyBonus,
            NextChestNoCurrency = source.NextChestNoCurrency,
            NextCampHealMultiplier = source.NextCampHealMultiplier
        };
    }

    // Копируется поимённо, а не через сериализацию: KnownSkillLevels — словарь по ссылкам на
    // ScriptableObject, который JsonUtility не переживает. Сами навыки/предметы — общие ассеты,
    // их клонировать не нужно и нельзя.
    public static RunCharacterProgress Clone(RunCharacterProgress source)
    {
        if (source == null) return null;
        var clone = new RunCharacterProgress(source.Character)
        {
            Level = source.Level,
            Experience = source.Experience,
            KnownSkillLevels = new Dictionary<PassiveSkillData, int>(source.KnownSkillLevels),
            DefeatedBosses = new List<MonsterData>(source.DefeatedBosses),
            UniquePassiveLevel = source.UniquePassiveLevel,
            UniqueActiveLevel = source.UniqueActiveLevel,
            MentorMagicDamageBonusPercent = source.MentorMagicDamageBonusPercent,
            MentorUniquePassiveSkillName = source.MentorUniquePassiveSkillName,
            MentorUniquePassiveLevel = source.MentorUniquePassiveLevel
        };
        clone.SetLevelUpRerolls(source.LevelUpRerollsRemaining);
        clone.RestoreLastAutoActiveUpgradeLevel(source.LastAutoActiveUpgradeLevel);
        return clone;
    }
}
