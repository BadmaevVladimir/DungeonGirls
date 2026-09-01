using System;
using System.Collections.Generic;
using UnityEngine;

public class FloorManager : MonoBehaviour
{
    public FloorState CurrentFloorState { get; private set; }
    public FloorMap CurrentMap { get; private set; }
    public FloorMapNode CurrentNode => CurrentMap?.GetNode(CurrentMap.CurrentNodeId);
    public int RoomsCompletedOnFloor { get; private set; }
    public int TotalRoomsOnFloor => FloorMapGenerator.RoomsBeforeBoss + 1;

    public void SetFloorState(FloorState newState) => CurrentFloorState = newState;

    public void GenerateFloorMap(int floorNumber, int? seed = null)
    {
        int resolvedSeed = seed ?? UnityEngine.Random.Range(1, int.MaxValue);
        CurrentMap = FloorMapGenerator.Generate(floorNumber, resolvedSeed);
        RoomsCompletedOnFloor = 0;
    }

    public void FinalizeGeneratedContent()
    {
        var errors = FloorMapGenerator.ValidateResolvedContent(CurrentMap);
        if (errors.Count > 0) throw new InvalidOperationException($"Floor map content resolution failed: {string.Join("; ", errors)}");
        Debug.Log($"[FloorMap] Generated floor {CurrentMap.FloorNumber}, seed {CurrentMap.Seed}.\n{FloorMapGenerator.Dump(CurrentMap)}");
    }

    // Serialization-ready restore hook for future mid-run persistence. No room or edge is rerolled.
    public void RestoreFloorMap(FloorMap map, int roomsCompleted)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        var errors = FloorMapGenerator.Validate(map);
        if (errors.Count > 0) throw new ArgumentException($"Invalid floor map: {string.Join("; ", errors)}", nameof(map));
        var contentErrors = FloorMapGenerator.ValidateResolvedContent(map);
        if (contentErrors.Count > 0) throw new ArgumentException($"Invalid resolved floor content: {string.Join("; ", contentErrors)}", nameof(map));
        if (map.GetNode(map.CurrentNodeId) == null) throw new ArgumentException("Current node is missing.", nameof(map));
        CurrentMap = map;
        RoomsCompletedOnFloor = Mathf.Clamp(roomsCompleted, 0, TotalRoomsOnFloor);
    }

    public List<FloorMapNode> GetReachableNodes() =>
        CurrentMap == null ? new List<FloorMapNode>() : CurrentMap.GetOutgoingNodes(CurrentMap.CurrentNodeId);

    public bool TrySelectNextNode(string targetNodeId)
    {
        if (CurrentMap == null || !CurrentMap.CanMoveTo(targetNodeId)) return false;
        CurrentMap.CurrentNodeId = targetNodeId;
        return true;
    }

    public void MarkCurrentRoomCompleted()
    {
        if (CurrentNode == null) throw new InvalidOperationException("Cannot complete a floor map without a current node.");
        if (CurrentNode.Visited) return;
        CurrentNode.Visited = true;
        RoomsCompletedOnFloor++;
    }
}
