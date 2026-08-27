using System.Collections.Generic;
using UnityEngine;

public class FloorManager : MonoBehaviour
{
    // 8.4: на этажах 2-10 — 8 боевых / 1 торговец / 2 ловушки / 1 особая = 12 комнат.
    // На первом этаже торговца нет: до первой награды у игрока нет валюты для покупки.
    const int CombatRooms = 8;
    const int MerchantRooms = 1;
    const int TrapRooms = 2;
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
    public void GenerateRoomBag(int floorNumber = 1)
    {
        RoomBag = new List<RoomType>();
        for (int i = 0; i < CombatRooms; i++) RoomBag.Add(RoomType.Combat);
        if (floorNumber > 1)
        {
            for (int i = 0; i < MerchantRooms; i++) RoomBag.Add(RoomType.Merchant);
        }
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
