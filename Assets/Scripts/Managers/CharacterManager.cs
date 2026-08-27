using System.Collections.Generic;
using UnityEngine;

// Держит живое состояние персонажа в текущем забеге: прогресс уровня/навыков (3.5),
// боевой рантайм (HP/защита/оружие, см. Фаза 3) и временные модификаторы от ловушек/квестов.
public class CharacterManager : MonoBehaviour
{
    public CharacterData Character;
    public RunCharacterProgress Progress { get; private set; }
    public CombatantRuntime Combatant { get; private set; }
    public RunModifiers Modifiers { get; private set; } = new RunModifiers();
    public int RunCurrency { get; private set; } // 5.2/8.2: валюта забега, обнуляется в конце забега

    // 8.5: сколько комнат пройдено ЗА ВЕСЬ ЗАБЕГ (все этажи), считая только комнаты, в которых
    // персонаж выжил — комната, в которой персонаж погиб, в счёт не идёт (см. RewardManager).
    public int RoomsClearedThisRun { get; private set; }

    // 8.5: комнат пройдено (персонаж выжил) НА ТЕКУЩЕМ этаже — используется формулой мета-валюты
    // за поражение, которая теперь считает "этаж смерти" отдельно от общего числа комнат за забег.
    // Сбрасывается на старте каждого нового этажа (см. BeginFloor, вызывается из RunFlowController).
    public int RoomsClearedOnCurrentFloor { get; private set; }

    // 3.4 "Без инвентаря": текущее снаряжение персонажа за забег. Начинается со стартового
    // лоадаута и меняется только через EquipItem (сравнение со слотом, без склада/хранилища).
    public List<ItemData> EquippedItems { get; private set; } = new List<ItemData>();

    public int CurrentHP => Mathf.CeilToInt(Combatant != null ? Combatant.CurrentHP : 0f);
    public int Level => Progress != null ? Progress.Level : 1;

    // 8.1 (ФИКС): уровни зданий на момент старта забега — здания не меняются посреди забега,
    // поэтому достаточно прочитать их один раз в BeginRun и переиспользовать в RefreshCombatStats/
    // на привале. Раньше только tavernLevelThisRun кэшировался и то использовался частично
    // (только флэт-урон, не рационы/лечение); Кузница/Храм не читались за пределами стартового
    // снаряжения вовсе.
    int tavernLevelThisRun;
    int forgeLevelThisRun;
    int templeLevelThisRun;
    public int TavernLevelThisRun => tavernLevelThisRun;
    public int ForgeLevelThisRun => forgeLevelThisRun;
    public int TempleLevelThisRun => templeLevelThisRun;

    // equipmentManager/saveManager опциональны (могут быть null, напр. в тестах) — тогда бонусы
    // от Кузницы/Таверны/Храма/гачи (3.5/8.1) не применяются, персонаж стартует с базовым лоадаутом.
    public void BeginRun(CharacterData character, EquipmentManager equipmentManager = null, SaveManager saveManager = null)
    {
        Character = character;
        Progress = new RunCharacterProgress(character);
        Modifiers = new RunModifiers();
        RunCurrency = 0;
        RoomsClearedThisRun = 0;
        tavernLevelThisRun = saveManager != null ? saveManager.GetBuildingLevel(BuildingType.Tavern) : 0;
        forgeLevelThisRun = saveManager != null ? saveManager.GetBuildingLevel(BuildingType.Forge) : 0;
        templeLevelThisRun = saveManager != null ? saveManager.GetBuildingLevel(BuildingType.Temple) : 0;

        if (equipmentManager != null)
        {
            int copyCount = saveManager != null ? saveManager.GetCharacterCopies(character.characterName) : 0;
            EquippedItems = equipmentManager.GetEffectiveStartingEquipment(character, forgeLevelThisRun, copyCount);
            Progress.ApplyGachaStartingBonus(GachaCopyBonusCalculator.CalculateBonus(copyCount));
        }
        else
        {
            EquippedItems = new List<ItemData>(character.startingEquipment ?? new ItemData[0]);
        }

        Combatant = CombatantFactory.CreatePlayerCombatant(character, Progress.Level, Progress, EquippedItems, tavernLevelThisRun, forgeLevelThisRun, templeLevelThisRun);
    }

    public void BeginFloor()
    {
        RoomsClearedOnCurrentFloor = 0;
    }

    // 3.4: сколько предметов слота/подтипа может быть надето одновременно (Оружие и Кольца — по 2,
    // остальные слоты — по 1).
    static int SlotCapacity(EquipmentSlot slot) => slot == EquipmentSlot.Weapon || slot == EquipmentSlot.Ring ? 2 : 1;

    // 3.4: возвращает все физические слоты, куда может встать newItem — по одному элементу на
    // каждый слот его типа (2 для Оружия/Колец, 1 для остальных). Элемент — предмет, который там
    // сейчас надет, либо null, если слот свободен. Игрок выбирает сам, какой из них занять —
    // никакого автовыбора по правилу.
    public List<ItemData> GetComparisonCandidates(ItemData newItem)
    {
        var sameSlot = EquippedItems.FindAll(i => i != null && i.slot == newItem.slot);

        // Финальный ревью-фикс #1 (3.11, Варвар/Двуручное оружие): если сейчас надето двуручное
        // оружие, оно одно занимает ОБА физических слота Оружия — единственный корректный кандидат
        // на замену это оно само, а не оно + иллюзорный пустой второй слот (который, будучи занят
        // одноручником, привёл бы к одновременному ношению двуручного + одноручного оружия — см.
        // CombatantFactory.AggregateEquipmentStats, который тогда увидел бы 2 "настоящих" оружия и
        // применил дуал-вилд логику к оружию, которое должно занимать руки в одиночку).
        if (newItem.slot == EquipmentSlot.Weapon && sameSlot.Exists(i => i.isTwoHanded))
        {
            return sameSlot;
        }

        int capacity = SlotCapacity(newItem.slot);

        var candidates = new List<ItemData>(sameSlot);
        while (candidates.Count < capacity)
        {
            candidates.Add(null);
        }

        return candidates;
    }

    // 3.4: надеть newItem, при необходимости заменив replacing (старый предмет просто исчезает —
    // склада/хранилища в прототипе нет).
    public void EquipItem(ItemData newItem, ItemData replacing)
    {
        if (newItem.isTwoHanded)
        {
            // 3.4 исключение (Варвар, Двуручное оружие): оба текущих предмета в слотах оружия/рук
            // заменяются одновременно, а не по одному, как в обычной логике сравнения слотов.
            var currentWeapons = EquippedItems.FindAll(i => i != null && i.slot == EquipmentSlot.Weapon);
            foreach (var current in currentWeapons)
            {
                EquippedItems.Remove(current);
            }
            EquippedItems.Add(newItem);
            RefreshCombatStats();
            return;
        }

        if (replacing != null)
        {
            EquippedItems.Remove(replacing);
        }

        EquippedItems.Add(newItem);
        RefreshCombatStats();
    }

    public void AddCurrency(int amount)
    {
        RunCurrency += amount;
    }

    // 5.2: покупка у торговца тратит валюту забега; продавать снаряжение торговцу нельзя (только тратить).
    public bool TrySpendCurrency(int amount)
    {
        if (RunCurrency < amount)
        {
            return false;
        }

        RunCurrency -= amount;
        return true;
    }

    // "Карманник" (2.4): монстр ворует часть текущей валюты забега при попадании.
    public void StealCurrencyPercent(float percent)
    {
        int stolen = Mathf.RoundToInt(RunCurrency * percent / 100f);
        RunCurrency = Mathf.Max(0, RunCurrency - stolen);
    }

    // 8.5: вызывать только когда персонаж пережил комнату (см. RunFlowController.RunLoop).
    public void MarkRoomCleared()
    {
        RoomsClearedThisRun++;
        RoomsClearedOnCurrentFloor++;
    }

    // Пересобирает боевые статы персонажа (после левел-апа/нового навыка), сохраняя текущее
    // HP/физ. защиту относительно старого максимума, а не сбрасывая их к полному (3.1: левел-ап
    // "даёт" дополнительное здоровье, а не лечит целиком; аналогично трактуем прирост макс. защиты
    // от новых навыков — см. решение по этому вопросу в отчёте по Фазе 4).
    public void RefreshCombatStats()
    {
        float oldMaxHP = Combatant.MaxHP;
        float oldCurrentHP = Combatant.CurrentHP;
        float oldDefenseMax = Combatant.PhysicalDefenseMax;
        float oldDefenseCurrent = Combatant.PhysicalDefenseCurrent;

        var rebuilt = CombatantFactory.CreatePlayerCombatant(Character, Progress.Level, Progress, EquippedItems, tavernLevelThisRun, forgeLevelThisRun, templeLevelThisRun);

        rebuilt.CurrentHP = Mathf.Clamp(oldCurrentHP + (rebuilt.MaxHP - oldMaxHP), 0f, rebuilt.MaxHP);
        rebuilt.PhysicalDefenseCurrent = Mathf.Clamp(oldDefenseCurrent + (rebuilt.PhysicalDefenseMax - oldDefenseMax), 0f, rebuilt.PhysicalDefenseMax);

        Combatant = rebuilt;
    }

    public bool IsAlive => Combatant != null && Combatant.IsAlive;

    public void ApplyDirectDamage(float amount)
    {
        Combatant.CurrentHP = Mathf.Max(0f, Combatant.CurrentHP - amount);
    }

    public void ApplyDirectArmorLoss(float amount)
    {
        Combatant.PhysicalDefenseCurrent = Mathf.Max(0f, Combatant.PhysicalDefenseCurrent - amount);
    }

    public List<int> GrantExperience(RewardManager rewardManager, ExperienceSource source, int floorNumber)
    {
        return rewardManager.GrantExperience(Progress, source, floorNumber);
    }
}
