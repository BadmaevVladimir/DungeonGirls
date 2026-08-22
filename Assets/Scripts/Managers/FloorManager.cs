using System.Collections.Generic;
using UnityEngine;

public class FloorManager : MonoBehaviour
{
    // 8.4: состав мешка комнат на этаж 1 — 7 боевых / 1 торговец / 3 ловушки / 1 особая = 12.
    // ГДД явно оставляет открытым вопрос о росте мешка на более глубоких этажах ("точные числа
    // пока не определены") — для прототипа используем тот же состав на всех 3 этажах.
    const int CombatRooms = 7;
    const int MerchantRooms = 1;
    const int TrapRooms = 3;
    const int SpecialRooms = 1;

    public FloorState CurrentFloorState { get; private set; }
    public List<RoomType> RoomBag { get; private set; } = new List<RoomType>();
    public int RoomsCompletedOnFloor { get; private set; }

    public void SetFloorState(FloorState newState)
    {
        CurrentFloorState = newState;
    }

    // Мешок/стопка (bag randomization, 8.4): все комнаты этажа перемешиваются один раз,
    // дальше вытягиваются без повторного добавления. Босс в мешок не входит — всегда последний.
    public void GenerateRoomBag()
    {
        RoomBag = new List<RoomType>();
        for (int i = 0; i < CombatRooms; i++) RoomBag.Add(RoomType.Combat);
        for (int i = 0; i < MerchantRooms; i++) RoomBag.Add(RoomType.Merchant);
        for (int i = 0; i < TrapRooms; i++) RoomBag.Add(RoomType.Trap);
        for (int i = 0; i < SpecialRooms; i++) RoomBag.Add(RoomType.Special);

        for (int i = RoomBag.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (RoomBag[i], RoomBag[j]) = (RoomBag[j], RoomBag[i]);
        }

        RoomsCompletedOnFloor = 0;
    }

    public int TotalRoomsOnFloor => RoomBag.Count + 1; // +1 за комнату босса (2.5: всегда последняя)

    public bool TryDrawNextRoom(out RoomType roomType)
    {
        if (RoomBag.Count > 0)
        {
            roomType = RoomBag[0];
            RoomBag.RemoveAt(0);
            return true;
        }

        roomType = RoomType.Boss;
        return false;
    }

    public void MarkRoomCompleted()
    {
        RoomsCompletedOnFloor++;
    }
}
