using UnityEngine;

public class DungeonManager : MonoBehaviour
{
    // 2.1: 10 этажей, фиксировано для прототипа (ОБНОВЛЕНО 2026-08-25 — было 3, расширено для
    // более плавной кривой сложности; см. ГДД 2.1 для дизайн-обоснования).
    public const int TotalFloors = 10;

    public RunState CurrentRunState { get; private set; }
    public int CurrentFloorNumber { get; private set; } = 1;

    public void SetRunState(RunState newState)
    {
        CurrentRunState = newState;
    }

    public void GenerateDungeon()
    {
        CurrentFloorNumber = 1;
    }

    public bool AdvanceToNextFloor()
    {
        if (CurrentFloorNumber >= TotalFloors)
        {
            return false;
        }

        CurrentFloorNumber++;
        return true;
    }
}
