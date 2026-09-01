using System;
using System.Collections.Generic;

public enum FloorMapNodeKind
{
    Start,
    Normal,
    Boss
}

public enum FloorMapEdgeKind
{
    Start,
    Straight,
    CrossPath,
    Boss
}

[Serializable]
public sealed class FloorMapNode
{
    public string Id;
    public int Depth;
    public int PathIndex;
    public RoomType RoomType;
    public FloorMapNodeKind Kind;
    public string ContentKey;
    public int ContentSeed;
    public bool ContentResolved;
    public List<string> ResolvedMonsterIds = new List<string>();
    public List<FloorMerchantOfferState> ResolvedMerchantOffers = new List<FloorMerchantOfferState>();
    public bool Visited;
}

[Serializable]
public sealed class FloorMerchantOfferState
{
    public string ItemName;
    public ItemTier ItemTier;
    public WeaponSubtype WeaponSubtype;
    public int ItemLevel;
    public int OriginalPrice;
    public int Price;
    public bool HasDiscount;
}

[Serializable]
public sealed class FloorMapEdge
{
    public string SourceNodeId;
    public string TargetNodeId;
    public FloorMapEdgeKind Kind;
}

[Serializable]
public sealed class FloorMap
{
    public int FloorNumber;
    public int Seed;
    public string CurrentNodeId;
    public List<FloorMapNode> Nodes = new List<FloorMapNode>();
    public List<FloorMapEdge> Edges = new List<FloorMapEdge>();

    public FloorMapNode GetNode(string nodeId) =>
        Nodes.Find(node => node != null && string.Equals(node.Id, nodeId, StringComparison.Ordinal));

    public FloorMapNode GetNode(int pathIndex, int depth) =>
        Nodes.Find(node => node != null && node.PathIndex == pathIndex && node.Depth == depth);

    public List<FloorMapNode> GetOutgoingNodes(string sourceNodeId)
    {
        var result = new List<FloorMapNode>();
        foreach (var edge in Edges)
        {
            if (edge == null || !string.Equals(edge.SourceNodeId, sourceNodeId, StringComparison.Ordinal)) continue;
            var target = GetNode(edge.TargetNodeId);
            if (target != null) result.Add(target);
        }
        return result;
    }

    public bool CanMoveTo(string targetNodeId)
    {
        if (string.IsNullOrEmpty(CurrentNodeId) || string.IsNullOrEmpty(targetNodeId)) return false;
        return Edges.Exists(edge => edge != null &&
            string.Equals(edge.SourceNodeId, CurrentNodeId, StringComparison.Ordinal) &&
            string.Equals(edge.TargetNodeId, targetNodeId, StringComparison.Ordinal));
    }
}
