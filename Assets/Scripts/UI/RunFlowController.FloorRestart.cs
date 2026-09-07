using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// ГДД 8.1, Храм ур.5 (G03/R07), решение D03a: при смерти игрок возвращается к началу текущего
// этажа. Снимок состояния делается перед входом в первую комнату этажа.
public partial class RunFlowController
{
    int floorRestartsRemaining;

    public int FloorRestartsRemaining => floorRestartsRemaining;

    void BeginRunFloorRestarts() =>
        floorRestartsRemaining = BuildingCatalog.TempleFloorRestarts(characterManager.TempleLevelThisRun);

    // Снимок делается только при наличии неизрасходованного перезапуска: без Храма ур.5 забег не
    // платит за копирование состояния вообще.
    FloorRestartSnapshot CaptureFloorRestartSnapshot()
    {
        if (floorRestartsRemaining <= 0) return null;
        return new FloorRestartSnapshot
        {
            Character = characterManager.CaptureRunState(),
            RationsRemaining = campManager.RationsRemaining,
            Map = RunStateClone.Clone(floorManager.CurrentMap),
            RoomsCompletedOnFloor = floorManager.RoomsCompletedOnFloor,
            HotSpringsTriggered = hotSpringsTriggeredThisRun,
            VioletTrapRoomTriggered = violetTrapRoomTriggeredThisRun,
            SashaBeerCellarTriggered = sashaBeerCellarTriggeredThisRun,
            HuntQuestTriggered = huntQuestTriggeredThisRun,
            SwordInStoneSucceeded = swordInStoneSucceededThisRun,
            CampSceneTriggered = campSceneTriggeredThisRun
        };
    }

    bool CanRestartFloor(FloorRestartSnapshot snapshot) => snapshot != null && floorRestartsRemaining > 0;

    IEnumerator FloorRestartFlow(FloorRestartSnapshot snapshot)
    {
        floorRestartsRemaining--;
        RestoreFloorRestartSnapshot(snapshot);

        ShowOnly(eventPopup);
        eventChoicesContainer.Clear();
        eventDescriptionLabel.text =
            $"Милость Храма возвращает {characterManager.Character.characterName} к началу {dungeonManager.CurrentFloorNumber}-го этажа.\n\n" +
            "Этаж пройден заново с тем же составом комнат. Собранные ингредиенты и материалы остаются у вас, " +
            "но приготовленное блюдо и бонус привала утрачены." +
            (floorRestartsRemaining > 0
                ? $"\nОсталось перезапусков: {floorRestartsRemaining}."
                : "\nЭто был последний перезапуск в этом забеге.");

        var continueButton = new Button { text = "Начать этаж заново" };
        continueButton.AddToClassList("button-primary");
        eventChoicesContainer.Add(continueButton);
        LogEvent($"[Храм] Перезапуск {dungeonManager.CurrentFloorNumber}-го этажа. Осталось перезапусков: {floorRestartsRemaining}.");
        yield return WaitForClick(continueButton);
        eventChoicesContainer.Clear();
    }

    void RestoreFloorRestartSnapshot(FloorRestartSnapshot snapshot)
    {
        characterManager.RestoreRunState(snapshot.Character);
        campManager.RestoreRations(snapshot.RationsRemaining);

        // Клонируем и на восстановлении: сам снимок должен пережить перезапуск неизменным, иначе
        // повторный перезапуск получил бы уже отыгранную карту.
        floorManager.RestoreFloorMap(RunStateClone.Clone(snapshot.Map), snapshot.RoomsCompletedOnFloor);

        hotSpringsTriggeredThisRun = snapshot.HotSpringsTriggered;
        violetTrapRoomTriggeredThisRun = snapshot.VioletTrapRoomTriggered;
        sashaBeerCellarTriggeredThisRun = snapshot.SashaBeerCellarTriggered;
        huntQuestTriggeredThisRun = snapshot.HuntQuestTriggered;
        swordInStoneSucceededThisRun = snapshot.SwordInStoneSucceeded;
        campSceneTriggeredThisRun = snapshot.CampSceneTriggered;

        // Незавершённые награды предыдущей попытки не должны выдаться после перезапуска.
        ResetPendingRoomRewards();
        skipNextAutoCamp = false;
        selectedPreparedDish = null;
    }
}
