using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public partial class RunFlowController
{
    const float MapDragThreshold = 8f;
    const float MapWheelSpeed = 3f;

    bool mapScrollInteractionsSetUp;
    bool mapDragActive;
    bool mapDragCaptured;
    int mapDragPointerId;
    Vector2 mapDragStartPointerPos;
    Vector2 mapDragStartScrollOffset;

    // Карта — это единственный горизонтальный ScrollView с одной кнопкой-скроллбаром снизу;
    // добавляем колесо мыши и драг ЛКМ поверх стандартного UI Toolkit ScrollView, который сам
    // этого не умеет. Регистрируется один раз за жизнь компонента — CacheElements зовётся в
    // каждом OnEnable, а VisualElement переживает повторные OnEnable/OnDisable.
    void SetupMapGraphScrollInteractions()
    {
        if (mapScrollInteractionsSetUp || mapGraphScroll == null) return;
        mapScrollInteractionsSetUp = true;

        mapGraphScroll.RegisterCallback<WheelEvent>(evt =>
        {
            mapGraphScroll.scrollOffset += new Vector2(evt.delta.y * MapWheelSpeed, 0f);
            evt.StopPropagation();
        });

        mapGraphScroll.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            mapDragActive = true;
            mapDragCaptured = false;
            mapDragPointerId = evt.pointerId;
            mapDragStartPointerPos = evt.position;
            mapDragStartScrollOffset = mapGraphScroll.scrollOffset;
        }, TrickleDown.TrickleDown);

        mapGraphScroll.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!mapDragActive || evt.pointerId != mapDragPointerId) return;
            var delta = (Vector2)evt.position - mapDragStartPointerPos;
            if (!mapDragCaptured)
            {
                if (Mathf.Abs(delta.x) < MapDragThreshold) return;
                mapDragCaptured = true;
                mapGraphScroll.CapturePointer(mapDragPointerId);
            }
            mapGraphScroll.scrollOffset = mapDragStartScrollOffset - new Vector2(delta.x, 0f);
        }, TrickleDown.TrickleDown);

        mapGraphScroll.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.pointerId != mapDragPointerId) return;
            if (mapDragCaptured) mapGraphScroll.ReleasePointer(mapDragPointerId);
            mapDragActive = false;
            mapDragCaptured = false;
        }, TrickleDown.TrickleDown);

        mapGraphScroll.RegisterCallback<PointerCaptureOutEvent>(evt =>
        {
            mapDragActive = false;
            mapDragCaptured = false;
        });
    }

    IEnumerator MapPreviewFlow()
    {
        floorManager.SetFloorState(FloorState.MapChoice);
        ShowOnly(mapPanel);
        tutorialManager?.QueueOnce(TutorialContent.Map);
        mapStatusLabel.text = "Осмотри маршруты и заходи в стартовую комнату.";
        mapEnterCurrentButton.RemoveFromClassList("hidden");
        BuildMapGraph(new List<FloorMapNode>(), out _, out _);
        yield return WaitForClick(mapEnterCurrentButton);
        mapEnterCurrentButton.AddToClassList("hidden");
    }

    IEnumerator MapChoiceFlow()
    {
        floorManager.SetFloorState(FloorState.MapChoice);
        var reachable = floorManager.GetReachableNodes();
        if (reachable.Count == 0)
            throw new System.InvalidOperationException($"Floor map node {floorManager.CurrentNode?.Id} has no reachable successor.");

        ShowOnly(mapPanel);
        tutorialManager?.QueueOnce(TutorialContent.Map);
        mapEnterCurrentButton.AddToClassList("hidden");
        mapStatusLabel.text = reachable.Count == 1 && reachable[0].Kind == FloorMapNodeKind.Boss
            ? "Все дороги сошлись у босса — дальше только он."
            : "Выбери комнату, в которую ведёт стрелка от текущей.";

        BuildMapGraph(reachable, out var reachableButtons, out var reachableNodes);
        yield return WaitForAnyClick(reachableButtons.ToArray());

        var selected = reachableNodes[clickedIndex];
        if (!floorManager.TrySelectNextNode(selected.Id))
            throw new System.InvalidOperationException($"Map UI attempted an unavailable move to {selected.Id}.");
        LogEvent($"[Карта] Идём в комнату: {RoomTypeLabel(selected.RoomType)}.");
        UpdateTopBar();
    }

    void BuildMapGraph(List<FloorMapNode> reachable, out List<Button> reachableButtons, out List<FloorMapNode> reachableNodes)
    {
        var map = floorManager.CurrentMap;
        var reachableIds = new HashSet<string>(reachable.Select(node => node.Id));
        reachableButtons = new List<Button>();
        reachableNodes = new List<FloorMapNode>();
        mapGraphContainer.Clear();

        var strip = new VisualElement();
        strip.AddToClassList("floor-map-strip");
        mapGraphContainer.Add(strip);

        AddNodeColumn(strip, "Старт", new[] { map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Start) }, reachableIds, reachableButtons, reachableNodes, true);
        for (int depth = 1; depth <= FloorMapGenerator.BranchDepthCount; depth++)
        {
            AddTransitionColumn(strip, depth - 1, depth);
            AddNodeColumn(strip, depth.ToString(), map.Nodes.Where(node => node.Kind == FloorMapNodeKind.Normal && node.Depth == depth).OrderBy(node => node.PathIndex),
                reachableIds, reachableButtons, reachableNodes, false);
        }
        AddTransitionColumn(strip, FloorMapGenerator.BranchDepthCount, FloorMapGenerator.BossDepth);
        AddNodeColumn(strip, "Босс", new[] { map.Nodes.Single(node => node.Kind == FloorMapNodeKind.Boss) }, reachableIds, reachableButtons, reachableNodes, true);
    }

    void AddNodeColumn(VisualElement strip, string title, IEnumerable<FloorMapNode> nodes, HashSet<string> reachableIds,
        List<Button> reachableButtons, List<FloorMapNode> reachableNodes, bool commonNode)
    {
        var column = new VisualElement();
        column.AddToClassList("floor-map-node-column");
        if (commonNode) column.AddToClassList("floor-map-common-column");
        var titleLabel = new Label(title);
        titleLabel.AddToClassList("floor-map-depth-label");
        column.Add(titleLabel);

        foreach (var node in nodes)
        {
            bool isReachable = reachableIds.Contains(node.Id);
            var button = new Button { text = $"{RoomTypeIcon(node.RoomType)}\n{RoomTypeLabel(node.RoomType)}" };
            // Раньше здесь был служебный «Путь 1, глубина 3». Игроку нужно знать, что его ждёт в
            // комнате, а не её координаты в графе. Узлы пересобираются при каждом показе карты.
            tutorialManager?.BindTransientTooltip(button, RoomTypeLabel(node.RoomType), TutorialContent.RoomTypeHint(node.RoomType));
            button.AddToClassList("floor-map-node");
            button.EnableInClassList("floor-map-node-visited", node.Visited);
            button.EnableInClassList("floor-map-node-current", node.Id == floorManager.CurrentMap.CurrentNodeId);
            button.EnableInClassList("floor-map-node-reachable", isReachable);
            button.EnableInClassList("floor-map-node-future", !node.Visited && !isReachable && node.Id != floorManager.CurrentMap.CurrentNodeId);
            button.SetEnabled(isReachable);
            column.Add(button);

            if (isReachable)
            {
                reachableButtons.Add(button);
                reachableNodes.Add(node);
            }
        }
        strip.Add(column);
    }

    void AddTransitionColumn(VisualElement strip, int sourceDepth, int targetDepth)
    {
        var column = new VisualElement();
        column.AddToClassList("floor-map-edge-column");
        column.Add(new Label(string.Empty) { name = "EdgeColumnSpacer" });

        if (sourceDepth == 0)
        {
            for (int path = 0; path < FloorMapGenerator.PathCount; path++) AddEdgeLabel(column, $"→ {path + 1}");
        }
        else
        {
            for (int path = 0; path < FloorMapGenerator.PathCount; path++)
            {
                var source = floorManager.CurrentMap.GetNode(path, sourceDepth);
                var targets = floorManager.CurrentMap.Edges
                    .Where(edge => edge.SourceNodeId == source.Id)
                    .Select(edge => floorManager.CurrentMap.GetNode(edge.TargetNodeId))
                    .Where(node => node != null && node.Depth == targetDepth)
                    .OrderBy(node => node.PathIndex)
                    .Select(node => node.Kind == FloorMapNodeKind.Boss ? "→ босс" : node.PathIndex < path ? $"↗ {node.PathIndex + 1}" : node.PathIndex > path ? $"↘ {node.PathIndex + 1}" : $"→ {node.PathIndex + 1}");
                AddEdgeLabel(column, string.Join("  ", targets));
            }
        }
        strip.Add(column);
    }

    static void AddEdgeLabel(VisualElement column, string text)
    {
        var label = new Label(text);
        label.AddToClassList("floor-map-edge-label");
        column.Add(label);
    }

    static string RoomTypeIcon(RoomType type) => type switch
    {
        RoomType.Combat => "⚔",
        RoomType.Merchant => "¤",
        RoomType.Trap => "⚠",
        RoomType.Special => "✦",
        RoomType.Boss => "♛",
        _ => "●"
    };

    static string RoomTypeLabel(RoomType type) => type switch
    {
        RoomType.Combat => "Бой",
        RoomType.Merchant => "Магазин",
        RoomType.Trap => "Ловушка",
        RoomType.Special => "Событие",
        RoomType.Boss => "Босс",
        _ => type.ToString()
    };
}
