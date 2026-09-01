using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

public class FloorMapGeneratorTests
{
    FloorMap map;

    [SetUp]
    public void SetUp() => map = FloorMapGenerator.Generate(3, 123456);

    [Test]
    public void Generate_CreatesRequiredNodeLayout()
    {
        Assert.AreEqual(38, map.Nodes.Count);
        Assert.AreEqual(1, map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Start));
        Assert.AreEqual(1, map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Boss));
        Assert.AreEqual(36, map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal));
        for (int depth = 1; depth <= FloorMapGenerator.BranchDepthCount; depth++)
            Assert.AreEqual(4, map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal && node.Depth == depth), $"depth {depth}");
    }

    [Test]
    public void Generate_CreatesStartStraightAndBossEdges()
    {
        var start = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start);
        var boss = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Boss);
        var startEdges = map.Edges.Where(edge => edge.SourceNodeId == start.Id).ToList();
        Assert.AreEqual(4, startEdges.Count);
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, startEdges.Select(edge => map.GetNode(edge.TargetNodeId).PathIndex));
        Assert.IsTrue(startEdges.All(edge => map.GetNode(edge.TargetNodeId).Depth == 1));

        for (int path = 0; path < FloorMapGenerator.PathCount; path++)
        {
            for (int depth = 1; depth < FloorMapGenerator.BranchDepthCount; depth++)
            {
                string source = FloorMapGenerator.NodeId(path, depth);
                string target = FloorMapGenerator.NodeId(path, depth + 1);
                Assert.IsTrue(map.Edges.Any(edge => edge.SourceNodeId == source && edge.TargetNodeId == target && edge.Kind == FloorMapEdgeKind.Straight));
            }
            Assert.IsTrue(map.Edges.Any(edge => edge.SourceNodeId == FloorMapGenerator.NodeId(path, 9) && edge.TargetNodeId == boss.Id));
        }
    }

    [Test]
    public void Generate_CreatesExactlyTwelveValidDirectedCrossEdges()
    {
        var cross = map.Edges.Where(edge => edge.Kind == FloorMapEdgeKind.CrossPath).ToList();
        Assert.AreEqual(12, cross.Count);
        Assert.AreEqual(12, cross.Select(edge => edge.SourceNodeId + ">" + edge.TargetNodeId).Distinct().Count());

        var expectedDirections = new[] { (0, 1), (1, 0), (1, 2), (2, 1), (2, 3), (3, 2) };
        foreach (var direction in expectedDirections)
        {
            var edges = cross.Where(edge => map.GetNode(edge.SourceNodeId).PathIndex == direction.Item1 && map.GetNode(edge.TargetNodeId).PathIndex == direction.Item2).ToList();
            Assert.AreEqual(2, edges.Count, $"direction {direction.Item1}->{direction.Item2}");
            Assert.AreEqual(1, edges.Count(edge => map.GetNode(edge.SourceNodeId).Depth >= FloorMapGenerator.FirstHalfMinSourceDepth && map.GetNode(edge.SourceNodeId).Depth <= FloorMapGenerator.FirstHalfMaxSourceDepth));
            Assert.AreEqual(1, edges.Count(edge => map.GetNode(edge.SourceNodeId).Depth >= FloorMapGenerator.SecondHalfMinSourceDepth && map.GetNode(edge.SourceNodeId).Depth <= FloorMapGenerator.SecondHalfMaxSourceDepth));
        }

        foreach (var edge in cross)
        {
            var source = map.GetNode(edge.SourceNodeId);
            var target = map.GetNode(edge.TargetNodeId);
            Assert.AreEqual(1, target.Depth - source.Depth);
            Assert.AreEqual(1, System.Math.Abs(target.PathIndex - source.PathIndex));
            Assert.AreEqual(FloorMapNodeKind.Normal, source.Kind);
            Assert.AreEqual(FloorMapNodeKind.Normal, target.Kind);
        }
    }

    [Test]
    public void Generate_RestrictsTripleForksAndNeverMovesBackward()
    {
        Assert.LessOrEqual(FloorMapGenerator.CountTripleForks(map), 1);
        foreach (var edge in map.Edges)
            Assert.Greater(map.GetNode(edge.TargetNodeId).Depth, map.GetNode(edge.SourceNodeId).Depth, edge.SourceNodeId + " -> " + edge.TargetNodeId);
    }

    [Test]
    public void Generate_EveryNodeCanReachBoss()
    {
        var boss = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Boss);
        foreach (var start in map.Nodes)
            Assert.IsTrue(CanReach(start.Id, boss.Id), start.Id);
    }

    [Test]
    public void Generate_AssignsStableSeedsForLaterConcreteResolution()
    {
        var original = map.Nodes.Select(node => (node.Id, node.RoomType, node.ContentKey, node.ContentSeed)).ToArray();
        var reread = map.Nodes.Select(node => (node.Id, node.RoomType, node.ContentKey, node.ContentSeed)).ToArray();
        CollectionAssert.AreEqual(original, reread);

        var regenerated = FloorMapGenerator.Generate(3, 123456);
        CollectionAssert.AreEqual(original, regenerated.Nodes.Select(node => (node.Id, node.RoomType, node.ContentKey, node.ContentSeed)).ToArray());
    }

    [Test]
    public void ValidateResolvedContent_RequiresConcreteContentForEveryNode()
    {
        foreach (var node in map.Nodes)
        {
            node.ContentResolved = true;
            node.ContentKey = node.RoomType.ToString().ToLowerInvariant() + ":test";
            if (node.RoomType == RoomType.Combat) node.ResolvedMonsterIds.Add("Test Monster");
            if (node.RoomType == RoomType.Merchant)
            {
                for (int i = 0; i < 5; i++)
                    node.ResolvedMerchantOffers.Add(new FloorMerchantOfferState { ItemName = "Test Item " + i });
            }
        }

        Assert.IsEmpty(FloorMapGenerator.ValidateResolvedContent(map));
        map.Nodes[0].ContentResolved = false;
        Assert.IsNotEmpty(FloorMapGenerator.ValidateResolvedContent(map));
    }

    [Test]
    public void FloorMap_SerializationRoundTrip_PreservesResolvedGraphAndRunPosition()
    {
        map.Nodes.First(node => node.Id == "start").Visited = true;
        map.CurrentNodeId = FloorMapGenerator.NodeId(2, 1);
        string json = UnityEngine.JsonUtility.ToJson(map);
        var restored = UnityEngine.JsonUtility.FromJson<FloorMap>(json);

        Assert.AreEqual(map.Seed, restored.Seed);
        Assert.AreEqual(map.CurrentNodeId, restored.CurrentNodeId);
        Assert.AreEqual(map.Nodes.Count, restored.Nodes.Count);
        Assert.AreEqual(map.Edges.Count, restored.Edges.Count);
        CollectionAssert.AreEqual(
            map.Nodes.Select(node => (node.Id, node.RoomType, node.ContentKey, node.ContentSeed, node.Visited)).ToArray(),
            restored.Nodes.Select(node => (node.Id, node.RoomType, node.ContentKey, node.ContentSeed, node.Visited)).ToArray());
        CollectionAssert.AreEqual(
            map.Edges.Select(edge => (edge.SourceNodeId, edge.TargetNodeId, edge.Kind)).ToArray(),
            restored.Edges.Select(edge => (edge.SourceNodeId, edge.TargetNodeId, edge.Kind)).ToArray());
        Assert.IsEmpty(FloorMapGenerator.Validate(restored));
    }

    [Test]
    public void Generate_EnforcesExplicitRoomLimits()
    {
        Assert.LessOrEqual(map.Nodes.Count(node => node.RoomType == RoomType.Trap), FloorMapGenerator.GlobalTrapLimit);
        Assert.LessOrEqual(map.Nodes.Count(node => node.RoomType == RoomType.Special), FloorMapGenerator.GlobalSpecialLimit);
        Assert.AreNotEqual(RoomType.Merchant, map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start).RoomType);
        Assert.GreaterOrEqual(
            map.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal && node.RoomType == RoomType.Merchant),
            FloorMapGenerator.MinShopsPerFloor);
        for (int path = 0; path < FloorMapGenerator.PathCount; path++)
            Assert.LessOrEqual(map.Nodes.Count(node => node.PathIndex == path && node.RoomType == RoomType.Merchant), FloorMapGenerator.MaxShopsPerPath);
    }

    [Test]
    public void Generate_EveryFloorAndSeed_HasAtLeastOneBranchShop()
    {
        for (int floor = 1; floor <= DungeonManager.TotalFloors; floor++)
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var generated = FloorMapGenerator.Generate(floor, seed);
                Assert.GreaterOrEqual(
                    generated.Nodes.Count(node => node.Kind == FloorMapNodeKind.Normal && node.RoomType == RoomType.Merchant),
                    FloorMapGenerator.MinShopsPerFloor,
                    $"floor {floor}, seed {seed}");
                Assert.AreNotEqual(RoomType.Merchant, generated.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start).RoomType);
            }
        }
    }

    [Test]
    public void Navigation_OnlyAllowsTargetsOfCurrentOutgoingEdges()
    {
        var start = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start);
        map.CurrentNodeId = start.Id;
        var reachable = map.GetOutgoingNodes(start.Id);
        Assert.AreEqual(4, reachable.Count);
        Assert.IsTrue(reachable.All(node => map.CanMoveTo(node.Id)));
        Assert.IsFalse(map.CanMoveTo(FloorMapGenerator.NodeId(0, 2)));

        map.CurrentNodeId = FloorMapGenerator.NodeId(0, 9);
        var boss = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Boss);
        Assert.AreEqual(1, map.GetOutgoingNodes(map.CurrentNodeId).Count);
        Assert.IsTrue(map.CanMoveTo(boss.Id));
    }

    [Test]
    public void Navigation_StraightRoute_VisitsTenNormalRoomsBeforeBoss()
    {
        int normalRooms = 1; // shared Start
        map.CurrentNodeId = "start";
        for (int depth = 1; depth <= FloorMapGenerator.BranchDepthCount; depth++)
        {
            string next = FloorMapGenerator.NodeId(0, depth);
            Assert.IsTrue(map.CanMoveTo(next));
            map.CurrentNodeId = next;
            normalRooms++;
        }

        var boss = map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Boss);
        Assert.AreEqual(10, normalRooms);
        Assert.IsTrue(map.CanMoveTo(boss.Id));
    }

    [Test]
    public void FloorManager_TracksVisitedCurrentAndRejectsNonEdgeSelection()
    {
        var gameObject = new UnityEngine.GameObject("FloorManagerTest");
        try
        {
            var manager = gameObject.AddComponent<FloorManager>();
            manager.GenerateFloorMap(2, 77);
            Assert.AreEqual("start", manager.CurrentNode.Id);

            manager.MarkCurrentRoomCompleted();
            manager.MarkCurrentRoomCompleted();
            Assert.IsTrue(manager.CurrentNode.Visited);
            Assert.AreEqual(1, manager.RoomsCompletedOnFloor);
            Assert.IsFalse(manager.TrySelectNextNode(FloorMapGenerator.NodeId(0, 2)));
            Assert.IsTrue(manager.TrySelectNextNode(FloorMapGenerator.NodeId(3, 1)));
            Assert.AreEqual(FloorMapGenerator.NodeId(3, 1), manager.CurrentNode.Id);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void Generate_OneThousandFloors_AllValidate()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var generated = FloorMapGenerator.Generate(seed % DungeonManager.TotalFloors + 1, seed);
            var errors = FloorMapGenerator.Validate(generated);
            Assert.IsEmpty(errors, $"seed {seed}: {string.Join("; ", errors)}");
        }
    }

    [Test]
    public void GameRoot_ContainsFloorMapPresentationElements()
    {
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/UI/GameRoot.uxml");
        Assert.NotNull(asset);
        var root = asset.CloneTree();
        Assert.NotNull(root.Q<UnityEngine.UIElements.VisualElement>("MapPanel"));
        Assert.NotNull(root.Q<UnityEngine.UIElements.Label>("MapStatusLabel"));
        Assert.NotNull(root.Q<UnityEngine.UIElements.ScrollView>("MapGraphScroll"));
        Assert.NotNull(root.Q<UnityEngine.UIElements.VisualElement>("MapGraphContainer"));
        Assert.NotNull(root.Q<UnityEngine.UIElements.Button>("MapEnterCurrentButton"));
    }

    bool CanReach(string sourceId, string targetId)
    {
        var pending = new Queue<string>();
        var visited = new HashSet<string>();
        pending.Enqueue(sourceId);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == targetId) return true;
            foreach (var node in map.GetOutgoingNodes(current)) pending.Enqueue(node.Id);
        }
        return false;
    }
}
