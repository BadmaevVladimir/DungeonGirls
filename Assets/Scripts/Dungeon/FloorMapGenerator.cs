using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class FloorMapGenerator
{
    public const int PathCount = 4;
    public const int RoomsBeforeBoss = 10;
    public const int BranchDepthCount = RoomsBeforeBoss - 1;
    public const int BossDepth = RoomsBeforeBoss;
    public const int MaxTripleForks = 1;
    public const int MinShopsPerFloor = 1;
    public const int MaxShopsPerPath = 1;
    public const int GlobalTrapLimit = 2;
    public const int GlobalSpecialLimit = 1;
    public const int FirstHalfMinSourceDepth = 1;
    public const int FirstHalfMaxSourceDepth = 4;
    public const int SecondHalfMinSourceDepth = 5;
    public const int SecondHalfMaxSourceDepth = 8;
    public const int CrossEdgesPerDirection = 2;
    public const int CrossDirectionCount = (PathCount - 1) * 2;
    public const int CrossEdgeCount = CrossDirectionCount * CrossEdgesPerDirection;
    public const int MaxCrossEdgeGenerationAttempts = 64;

    const int BagCombatRooms = 8;
    const int BagMerchantRooms = 1;
    const int BagTrapRooms = 2;
    const int BagSpecialRooms = 1;

    static readonly (int sourcePath, int targetPath)[] CrossDirections =
    {
        (0, 1), (1, 0),
        (1, 2), (2, 1),
        (2, 3), (3, 2)
    };

    public static FloorMap Generate(int floorNumber, int seed)
    {
        if (floorNumber < 1) throw new ArgumentOutOfRangeException(nameof(floorNumber));

        var random = new Random(seed);
        var map = new FloorMap { FloorNumber = floorNumber, Seed = seed };
        int trapsUsed = 0;
        int specialsUsed = 0;

        // Старт и каждый путь получают независимый экземпляр актуального распределения.
        var startBag = CreateRoomDistributionBag(random, includeMerchant: false);
        RoomType startType = startBag[0];
        TrackGlobalRareRoom(startType, ref trapsUsed, ref specialsUsed);
        var start = CreateNode("start", 0, -1, startType, FloorMapNodeKind.Start, random);
        map.Nodes.Add(start);
        map.CurrentNodeId = start.Id;

        // Each branch is filled from its own shuffled bag. Global rare-room caps are explicit;
        // a room rejected by a floor-wide cap becomes Combat in that path only.
        for (int path = 0; path < PathCount; path++)
        {
            var pathBag = CreateRoomDistributionBag(random, includeMerchant: true);
            int shopsUsed = 0;
            for (int depth = 1; depth <= BranchDepthCount; depth++)
            {
                RoomType type = pathBag[depth - 1];
                if (type == RoomType.Merchant && shopsUsed >= MaxShopsPerPath) type = RoomType.Combat;
                if (type == RoomType.Trap && trapsUsed >= GlobalTrapLimit) type = RoomType.Combat;
                if (type == RoomType.Special && specialsUsed >= GlobalSpecialLimit) type = RoomType.Combat;

                if (type == RoomType.Merchant) shopsUsed++;
                TrackGlobalRareRoom(type, ref trapsUsed, ref specialsUsed);
                map.Nodes.Add(CreateNode(NodeId(path, depth), depth, path, type, FloorMapNodeKind.Normal, random));
            }
        }
        EnsureMinimumShop(map, random);

        var boss = CreateNode("boss", BossDepth, -1, RoomType.Boss, FloorMapNodeKind.Boss, random);
        map.Nodes.Add(boss);

        AddRequiredEdges(map);
        AddCrossEdges(map, random);

        var errors = Validate(map);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Floor map generation failed for seed {seed}: {string.Join("; ", errors)}");
        }
        return map;
    }

    static List<RoomType> CreateRoomDistributionBag(Random random, bool includeMerchant)
    {
        var bag = new List<RoomType>(BagCombatRooms + BagMerchantRooms + BagTrapRooms + BagSpecialRooms);
        for (int i = 0; i < BagCombatRooms; i++) bag.Add(RoomType.Combat);
        if (includeMerchant)
        {
            for (int i = 0; i < BagMerchantRooms; i++) bag.Add(RoomType.Merchant);
        }
        for (int i = 0; i < BagTrapRooms; i++) bag.Add(RoomType.Trap);
        for (int i = 0; i < BagSpecialRooms; i++) bag.Add(RoomType.Special);
        Shuffle(bag, random);
        return bag;
    }

    static void EnsureMinimumShop(FloorMap map, Random random)
    {
        int shopCount = map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal && node.RoomType == RoomType.Merchant);
        if (shopCount >= MinShopsPerFloor) return;

        // A shop discarded from all four shuffled 12-entry bags is rare but valid random
        // output. Resolve that bounded conflict deterministically by replacing one Combat node.
        var candidates = map.Nodes
            .Where(node => node.Kind == FloorMapNodeKind.Normal && node.RoomType == RoomType.Combat)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("Could not guarantee a floor shop: no branch Combat node is available.");

        var selected = candidates[random.Next(candidates.Count)];
        selected.RoomType = RoomType.Merchant;
        selected.ContentKey = UnresolvedContentKey(selected.RoomType, selected.ContentSeed);
    }

    static void Shuffle<T>(List<T> values, Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    static void TrackGlobalRareRoom(RoomType type, ref int trapsUsed, ref int specialsUsed)
    {
        if (type == RoomType.Trap) trapsUsed++;
        if (type == RoomType.Special) specialsUsed++;
    }

    static FloorMapNode CreateNode(string id, int depth, int pathIndex, RoomType type, FloorMapNodeKind kind, Random random)
    {
        int contentSeed = random.Next(1, int.MaxValue);
        return new FloorMapNode
        {
            Id = id,
            Depth = depth,
            PathIndex = pathIndex,
            RoomType = type,
            Kind = kind,
            ContentSeed = contentSeed,
            ContentKey = UnresolvedContentKey(type, contentSeed),
            ContentResolved = false
        };
    }

    static string UnresolvedContentKey(RoomType type, int contentSeed) =>
        $"unresolved:{type.ToString().ToLowerInvariant()}-{contentSeed:x8}";

    static void AddRequiredEdges(FloorMap map)
    {
        for (int path = 0; path < PathCount; path++)
        {
            AddEdge(map, "start", NodeId(path, 1), FloorMapEdgeKind.Start);
            for (int depth = 1; depth < BranchDepthCount; depth++)
            {
                AddEdge(map, NodeId(path, depth), NodeId(path, depth + 1), FloorMapEdgeKind.Straight);
            }
            AddEdge(map, NodeId(path, BranchDepthCount), "boss", FloorMapEdgeKind.Boss);
        }
    }

    static void AddCrossEdges(FloorMap map, Random random)
    {
        for (int attempt = 0; attempt < MaxCrossEdgeGenerationAttempts; attempt++)
        {
            map.Edges.RemoveAll(edge => edge.Kind == FloorMapEdgeKind.CrossPath);
            foreach (var direction in CrossDirections)
            {
                int earlyDepth = random.Next(FirstHalfMinSourceDepth, FirstHalfMaxSourceDepth + 1);
                int lateDepth = random.Next(SecondHalfMinSourceDepth, SecondHalfMaxSourceDepth + 1);
                AddEdge(map, NodeId(direction.sourcePath, earlyDepth), NodeId(direction.targetPath, earlyDepth + 1), FloorMapEdgeKind.CrossPath);
                AddEdge(map, NodeId(direction.sourcePath, lateDepth), NodeId(direction.targetPath, lateDepth + 1), FloorMapEdgeKind.CrossPath);
            }

            if (CountTripleForks(map) <= MaxTripleForks) return;
        }

        throw new InvalidOperationException($"Could not place cross-path edges within {MaxCrossEdgeGenerationAttempts} attempts.");
    }

    static void AddEdge(FloorMap map, string source, string target, FloorMapEdgeKind kind)
    {
        map.Edges.Add(new FloorMapEdge { SourceNodeId = source, TargetNodeId = target, Kind = kind });
    }

    public static string NodeId(int pathIndex, int depth) => $"p{pathIndex}-d{depth}";

    public static int CountTripleForks(FloorMap map)
    {
        int count = 0;
        foreach (var node in map.Nodes)
        {
            if (node.Kind != FloorMapNodeKind.Normal || node.PathIndex <= 0 || node.PathIndex >= PathCount - 1) continue;
            var outgoing = map.Edges.Where(edge => edge.SourceNodeId == node.Id).ToList();
            bool straight = outgoing.Exists(edge => edge.Kind == FloorMapEdgeKind.Straight);
            bool upper = outgoing.Exists(edge =>
            {
                var target = map.GetNode(edge.TargetNodeId);
                return edge.Kind == FloorMapEdgeKind.CrossPath && target != null && target.PathIndex < node.PathIndex;
            });
            bool lower = outgoing.Exists(edge =>
            {
                var target = map.GetNode(edge.TargetNodeId);
                return edge.Kind == FloorMapEdgeKind.CrossPath && target != null && target.PathIndex > node.PathIndex;
            });
            if (straight && upper && lower) count++;
        }
        return count;
    }

    public static List<string> Validate(FloorMap map)
    {
        var errors = new List<string>();
        if (map == null) { errors.Add("Map is null."); return errors; }
        if (map.Nodes == null || map.Edges == null) { errors.Add("Nodes or edges are null."); return errors; }

        if (map.Nodes.Count != 38) errors.Add($"Expected 38 nodes, got {map.Nodes.Count}.");
        if (map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Start) != 1) errors.Add("Expected exactly one Start.");
        if (map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Boss) != 1) errors.Add("Expected exactly one Boss.");
        if (map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal) != 36) errors.Add("Expected exactly 36 branch nodes.");
        if (map.Nodes.Any(node => node.Kind == FloorMapNodeKind.Start && node.RoomType == RoomType.Merchant)) errors.Add("Start cannot be a shop.");
        if (map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal && node.RoomType == RoomType.Merchant) < MinShopsPerFloor)
            errors.Add($"Floor must contain at least {MinShopsPerFloor} branch shop.");
        if (map.Nodes.Any(node => string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.ContentKey))) errors.Add("Every node needs stable id and content key.");
        if (map.Nodes.Select(node => node.Id).Distinct().Count() != map.Nodes.Count) errors.Add("Node ids must be unique.");
        for (int depth = 1; depth <= BranchDepthCount; depth++)
        {
            if (map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal && node.Depth == depth) != PathCount)
                errors.Add($"Depth {depth} must contain {PathCount} branch nodes.");
        }

        var start = map.Nodes.FirstOrDefault(node => node.Kind == FloorMapNodeKind.Start);
        var boss = map.Nodes.FirstOrDefault(node => node.Kind == FloorMapNodeKind.Boss);
        if (start != null)
        {
            var startEdges = map.Edges.Where(edge => edge.SourceNodeId == start.Id).ToList();
            if (startEdges.Count != PathCount || startEdges.Any(edge => edge.Kind != FloorMapEdgeKind.Start)) errors.Add("Start must have four start edges.");
            if (startEdges.Select(edge => map.GetNode(edge.TargetNodeId)).Any(node => node == null || node.Depth != 1) ||
                startEdges.Select(edge => map.GetNode(edge.TargetNodeId)?.PathIndex).Distinct().Count() != PathCount)
                errors.Add("Start edges must target every unique path at depth 1.");
        }

        for (int path = 0; path < PathCount; path++)
        {
            for (int depth = 1; depth < BranchDepthCount; depth++)
            {
                string source = NodeId(path, depth);
                string expectedTarget = NodeId(path, depth + 1);
                if (!map.Edges.Exists(edge => edge.SourceNodeId == source && edge.TargetNodeId == expectedTarget && edge.Kind == FloorMapEdgeKind.Straight))
                    errors.Add($"Missing straight edge {source} -> {expectedTarget}.");
            }
            if (boss != null && !map.Edges.Exists(edge => edge.SourceNodeId == NodeId(path, BranchDepthCount) && edge.TargetNodeId == boss.Id && edge.Kind == FloorMapEdgeKind.Boss))
                errors.Add($"Path {path} does not converge on Boss.");
            if (map.Nodes.Count(node => node.PathIndex == path && node.RoomType == RoomType.Merchant) > MaxShopsPerPath)
                errors.Add($"Path {path} exceeds the shop limit.");
        }

        var crossEdges = map.Edges.Where(edge => edge.Kind == FloorMapEdgeKind.CrossPath).ToList();
        if (crossEdges.Count != CrossEdgeCount) errors.Add($"Expected {CrossEdgeCount} cross edges, got {crossEdges.Count}.");
        if (crossEdges.Select(edge => $"{edge.SourceNodeId}>{edge.TargetNodeId}").Distinct().Count() != crossEdges.Count) errors.Add("Duplicate cross edge.");
        foreach (var direction in CrossDirections)
        {
            var directed = crossEdges.Where(edge =>
            {
                var sourceNode = map.GetNode(edge.SourceNodeId);
                var targetNode = map.GetNode(edge.TargetNodeId);
                return sourceNode != null && targetNode != null && sourceNode.PathIndex == direction.sourcePath && targetNode.PathIndex == direction.targetPath;
            }).ToList();
            if (directed.Count != CrossEdgesPerDirection) errors.Add($"Direction {direction.sourcePath}->{direction.targetPath} must have two edges.");
            if (directed.Count(edge => map.GetNode(edge.SourceNodeId).Depth >= FirstHalfMinSourceDepth && map.GetNode(edge.SourceNodeId).Depth <= FirstHalfMaxSourceDepth) != 1 ||
                directed.Count(edge => map.GetNode(edge.SourceNodeId).Depth >= SecondHalfMinSourceDepth && map.GetNode(edge.SourceNodeId).Depth <= SecondHalfMaxSourceDepth) != 1)
                errors.Add($"Direction {direction.sourcePath}->{direction.targetPath} must be split across both halves.");
        }

        foreach (var edge in map.Edges)
        {
            var sourceNode = map.GetNode(edge.SourceNodeId);
            var targetNode = map.GetNode(edge.TargetNodeId);
            if (sourceNode == null || targetNode == null) { errors.Add("Edge references a missing node."); continue; }
            if (targetNode.Depth <= sourceNode.Depth) errors.Add($"Edge {sourceNode.Id}->{targetNode.Id} does not move forward.");
            if (edge.Kind == FloorMapEdgeKind.CrossPath && (targetNode.Depth != sourceNode.Depth + 1 || Math.Abs(targetNode.PathIndex - sourceNode.PathIndex) != 1))
                errors.Add($"Invalid cross edge {sourceNode.Id}->{targetNode.Id}.");
            if (edge.Kind == FloorMapEdgeKind.CrossPath && (sourceNode.Kind != FloorMapNodeKind.Normal || targetNode.Kind != FloorMapNodeKind.Normal))
                errors.Add("Cross edges cannot touch Start or Boss.");
        }

        if (CountTripleForks(map) > MaxTripleForks) errors.Add("Too many triple forks.");
        if (map.Nodes.Count(node => node.RoomType == RoomType.Trap) > GlobalTrapLimit) errors.Add("Floor exceeds trap limit.");
        if (map.Nodes.Count(node => node.RoomType == RoomType.Special) > GlobalSpecialLimit) errors.Add("Floor exceeds special-room limit.");
        if (boss != null)
        {
            foreach (var node in map.Nodes.Where(node => node.Kind != FloorMapNodeKind.Boss))
            {
                if (!CanReach(map, node.Id, boss.Id)) errors.Add($"{node.Id} cannot reach Boss.");
            }
        }
        return errors;
    }

    public static List<string> ValidateResolvedContent(FloorMap map)
    {
        var errors = new List<string>();
        if (map == null) { errors.Add("Map is null."); return errors; }
        foreach (var node in map.Nodes)
        {
            if (!node.ContentResolved || string.IsNullOrWhiteSpace(node.ContentKey) || node.ContentKey.StartsWith("unresolved:", StringComparison.Ordinal))
            {
                errors.Add($"Node {node.Id} has no resolved content.");
                continue;
            }
            if (node.RoomType == RoomType.Combat && (node.ResolvedMonsterIds == null || node.ResolvedMonsterIds.Count == 0))
                errors.Add($"Combat node {node.Id} has no resolved encounter.");
            if (node.RoomType == RoomType.Merchant && (node.ResolvedMerchantOffers == null || node.ResolvedMerchantOffers.Count != 5))
                errors.Add($"Merchant node {node.Id} must have five resolved offers.");
        }
        return errors;
    }

    static bool CanReach(FloorMap map, string sourceId, string targetId)
    {
        var pending = new Queue<string>();
        var visited = new HashSet<string>();
        pending.Enqueue(sourceId);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == targetId) return true;
            foreach (var edge in map.Edges.Where(edge => edge.SourceNodeId == current)) pending.Enqueue(edge.TargetNodeId);
        }
        return false;
    }

    public static string Dump(FloorMap map)
    {
        var builder = new StringBuilder();
        for (int depth = 0; depth <= BossDepth; depth++)
        {
            builder.Append($"Depth {depth}: ");
            var nodes = map.Nodes.Where(node => node.Depth == depth).OrderBy(node => node.PathIndex);
            builder.AppendLine(string.Join(" | ", nodes.Select(node => node.Kind == FloorMapNodeKind.Normal
                ? $"P{node.PathIndex + 1} {node.RoomType} [{node.ContentKey}]"
                : $"{node.Kind} {node.RoomType} [{node.ContentKey}]")));
        }
        builder.AppendLine("Edges:");
        foreach (var edge in map.Edges.OrderBy(edge => map.GetNode(edge.SourceNodeId).Depth).ThenBy(edge => edge.SourceNodeId).ThenBy(edge => edge.TargetNodeId))
            builder.AppendLine($"{edge.SourceNodeId} -> {edge.TargetNodeId} ({edge.Kind})");
        return builder.ToString();
    }
}
