using UnityEngine;

public class DungeonManager : MonoBehaviour
{
    // 2.1: 3 этажа, фиксировано для прототипа.
    public const int TotalFloors = 3;

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
