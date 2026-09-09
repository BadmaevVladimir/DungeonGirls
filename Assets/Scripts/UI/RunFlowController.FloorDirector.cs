using UnityEngine;

public partial class RunFlowController
{
    void BeginFloorDirectorObservation()
    {
        var combatant = characterManager.Combatant;
        floorDirectorSession.BeginFloor(
            dungeonManager.CurrentFloorNumber,
            floorManager.CurrentMap.Seed,
            characterManager.Character != null ? characterManager.Character.characterId : string.Empty,
            combatant != null ? combatant.CurrentHP : 0f,
            combatant != null ? combatant.MaxHP : 1f,
            campManager.RationsRemaining);
    }

    void CompleteFloorDirectorObservation()
    {
        var combatant = characterManager.Combatant;
        FloorDirectorPlan plan = floorDirectorSession.CompleteFloor(
            combatant != null ? combatant.CurrentHP : 0f,
            combatant != null ? combatant.MaxHP : 1f,
            campManager.RationsRemaining);
        Debug.Log(plan.ToDiagnosticString());
    }
}
