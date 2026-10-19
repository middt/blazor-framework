using System.Globalization;

namespace Middt.Framework.Model.Workflow;

/// <summary>
/// Deterministic layered DAG layout of a <see cref="WorkflowDefinitionModel"/> combined with the runtime state and
/// transition history of a <see cref="WorkflowInstanceModel"/> for SVG workflow diagrams.
/// </summary>
public sealed class WorkflowGraph
{
    public const double NodeWidth = 176;
    public const double NodeHeight = 60;
    public const double ColumnGap = 60;
    public const double RowGap = 36;
    public const double PaddingLeft = 64;
    public const double PaddingRight = 24;
    public const double PaddingTop = 52;
    public const double PaddingBottom = 48;
    public const double StartRadius = 9;

    public double Width { get; init; }
    public double Height { get; init; }
    public double StartX { get; init; }
    public double StartY { get; init; }
    public IReadOnlyList<WorkflowGraphNode> Nodes { get; init; } = [];
    public IReadOnlyList<WorkflowGraphEdge> Edges { get; init; } = [];

    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Builds the positioned workflow graph for <paramref name="definition"/>, <paramref name="instance"/>, and <paramref name="history"/>.</summary>
    public static WorkflowGraph Build(
        WorkflowDefinitionModel? definition,
        WorkflowInstanceModel? instance = null,
        IReadOnlyList<WorkflowHistoryModel>? history = null)
    {
        if (definition is null || definition.States.Count == 0)
        {
            return new WorkflowGraph();
        }

        var orderedHistory = history ?? [];
        var stateByKey = new Dictionary<string, WorkflowStateModel>(StringComparer.Ordinal);
        foreach (var state in definition.States)
        {
            if (!string.IsNullOrWhiteSpace(state.Key))
            {
                stateByKey.TryAdd(state.Key, state);
            }
        }

        if (stateByKey.Count == 0)
        {
            return new WorkflowGraph();
        }

        var startKey = !string.IsNullOrWhiteSpace(definition.StartState) && stateByKey.ContainsKey(definition.StartState)
            ? definition.StartState
            : definition.States[0].Key;

        // 1. Order states deterministically: StartState first, then non-Finish states in declaration order, then Finish states.
        var orderedStates = new List<WorkflowStateModel>(stateByKey.Count);
        if (stateByKey.TryGetValue(startKey, out var startStateModel))
        {
            orderedStates.Add(startStateModel);
        }

        foreach (var state in definition.States)
        {
            if (state.Key != startKey && !IsFinishState(state))
            {
                orderedStates.Add(state);
            }
        }

        foreach (var state in definition.States)
        {
            if (state.Key != startKey && IsFinishState(state))
            {
                orderedStates.Add(state);
            }
        }

        var stateOrder = new Dictionary<string, int>(orderedStates.Count, StringComparer.Ordinal);
        for (var i = 0; i < orderedStates.Count; i++)
        {
            stateOrder[orderedStates[i].Key] = i;
        }

        // 2. Collect normalized transitions (resolving "$self" and synthesizing any traversed shared/timeout edges from history).
        var edgeGroups = new Dictionary<(string From, string Target), List<WorkflowTransitionModel>>();
        var edgeGroupOrder = new List<(string From, string Target)>();

        void AddTransitionEdge(string from, string target, WorkflowTransitionModel transition)
        {
            if (!stateByKey.ContainsKey(from) || !stateByKey.ContainsKey(target))
            {
                return;
            }

            var key = (from, target);
            if (!edgeGroups.TryGetValue(key, out var list))
            {
                list = [];
                edgeGroups[key] = list;
                edgeGroupOrder.Add(key);
            }

            list.Add(transition);
        }

        foreach (var transition in definition.Transitions)
        {
            if (string.IsNullOrWhiteSpace(transition.From))
            {
                continue;
            }

            var target = transition.Target == "$self" ? transition.From : transition.Target;
            if (!string.IsNullOrWhiteSpace(target))
            {
                AddTransitionEdge(transition.From, target, transition);
            }
        }

        foreach (var entry in orderedHistory)
        {
            if (string.IsNullOrWhiteSpace(entry.FromState) || string.IsNullOrWhiteSpace(entry.ToState))
            {
                continue;
            }

            var pair = (entry.FromState, entry.ToState);
            if (!edgeGroups.ContainsKey(pair) && stateByKey.ContainsKey(entry.FromState) && stateByKey.ContainsKey(entry.ToState))
            {
                var shared = definition.Transitions.FirstOrDefault(t => t.Key == entry.TransitionKey);
                AddTransitionEdge(entry.FromState, entry.ToState, new WorkflowTransitionModel
                {
                    Key = entry.TransitionKey,
                    Title = shared?.Title ?? (entry.Trigger == "Timeout" ? "Zaman aşımı" : entry.TransitionKey),
                    From = entry.FromState,
                    Target = entry.ToState,
                    Trigger = entry.Trigger,
                });
            }
        }

        // 3. Assign columns via longest-path layering over forward DAG edges (stateOrder[u] < stateOrder[v]).
        var rawCol = new Dictionary<string, int>(stateByKey.Count, StringComparer.Ordinal);
        foreach (var state in orderedStates)
        {
            rawCol[state.Key] = 0;
        }

        foreach (var state in orderedStates)
        {
            if (IsFinishState(state))
            {
                continue;
            }

            var u = state.Key;
            var uOrder = stateOrder[u];
            var uCol = rawCol[u];
            foreach (var (from, target) in edgeGroupOrder)
            {
                if (from != u || target == u)
                {
                    continue;
                }

                var targetState = stateByKey[target];
                if (!IsFinishState(targetState) && stateOrder[target] > uOrder)
                {
                    rawCol[target] = Math.Max(rawCol[target], uCol + 1);
                }
            }
        }

        var maxNonFinishCol = orderedStates
            .Where(s => !IsFinishState(s))
            .Select(s => rawCol[s.Key])
            .DefaultIfEmpty(-1)
            .Max();
        var finishCol = maxNonFinishCol + 1;
        foreach (var state in orderedStates.Where(IsFinishState))
        {
            rawCol[state.Key] = finishCol;
        }

        // Compact columns so indices are contiguous 0..C-1.
        var distinctCols = rawCol.Values.Distinct().OrderBy(c => c).ToList();
        var colMap = new Dictionary<int, int>(distinctCols.Count);
        for (var i = 0; i < distinctCols.Count; i++)
        {
            colMap[distinctCols[i]] = i;
        }

        var columns = new List<List<WorkflowStateModel>>(distinctCols.Count);
        for (var i = 0; i < distinctCols.Count; i++)
        {
            columns.Add([]);
        }

        foreach (var state in orderedStates)
        {
            columns[colMap[rawCol[state.Key]]].Add(state);
        }

        // Sort rows within each column: Success first, then normal/human, then Cancelled/Timeout/Error, then declaration order.
        foreach (var colStates in columns)
        {
            colStates.Sort((a, b) =>
            {
                var prio = SubTypePriority(a.SubType).CompareTo(SubTypePriority(b.SubType));
                return prio != 0 ? prio : stateOrder[a.Key].CompareTo(stateOrder[b.Key]);
            });
        }

        // 4. Analyze history visits & step numbers (1-based).
        var visitStepsByState = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var visitedStates = new HashSet<string>(StringComparer.Ordinal);
        var stepNumbersByEdge = new Dictionary<(string? From, string Target), List<int>>();
        var lastTransitionTitleByEdge = new Dictionary<(string From, string Target), string>();

        for (var i = 0; i < orderedHistory.Count; i++)
        {
            var entry = orderedHistory[i];
            var stepNo = i + 1;
            if (!string.IsNullOrWhiteSpace(entry.FromState))
            {
                visitedStates.Add(entry.FromState);
            }

            if (!string.IsNullOrWhiteSpace(entry.ToState))
            {
                visitedStates.Add(entry.ToState);
                if (!visitStepsByState.TryGetValue(entry.ToState, out var steps))
                {
                    steps = [];
                    visitStepsByState[entry.ToState] = steps;
                }

                steps.Add(stepNo);
            }

            var fromKey = string.IsNullOrWhiteSpace(entry.FromState) || entry.Trigger == "Start" ? null : entry.FromState;
            var edgeKey = (fromKey, entry.ToState);
            if (!stepNumbersByEdge.TryGetValue(edgeKey, out var edgeSteps))
            {
                edgeSteps = [];
                stepNumbersByEdge[edgeKey] = edgeSteps;
            }

            edgeSteps.Add(stepNo);

            if (fromKey is not null)
            {
                var trTitle = definition.Transitions
                    .FirstOrDefault(t => t.Key == entry.TransitionKey && !string.IsNullOrWhiteSpace(t.Title))?.Title;
                if (!string.IsNullOrWhiteSpace(trTitle))
                {
                    lastTransitionTitleByEdge[(fromKey, entry.ToState)] = trTitle;
                }
            }
        }

        if (instance is not null && !string.IsNullOrWhiteSpace(instance.State))
        {
            visitedStates.Add(instance.State);
            visitedStates.Add(startKey);
        }

        var availableTargetsFromCurrent = new HashSet<string>(StringComparer.Ordinal);
        if (instance is { Status: WorkflowInstanceStatus.Active } && instance.AvailableTransitions.Count > 0)
        {
            foreach (var avail in instance.AvailableTransitions)
            {
                var target = avail.Target == "$self" ? instance.State : avail.Target;
                if (!string.IsNullOrWhiteSpace(target))
                {
                    availableTargetsFromCurrent.Add(target);
                }
            }
        }

        // 5. Compute node coordinates.
        var maxRows = Math.Max(1, columns.Max(c => c.Count));
        var contentHeight = (maxRows * NodeHeight) + ((maxRows - 1) * RowGap);
        var totalWidth = PaddingLeft + (columns.Count * NodeWidth) + ((columns.Count - 1) * ColumnGap) + PaddingRight;
        var totalHeight = PaddingTop + contentHeight + PaddingBottom;

        var nodes = new List<WorkflowGraphNode>(stateByKey.Count);
        var nodeByKey = new Dictionary<string, WorkflowGraphNode>(stateByKey.Count, StringComparer.Ordinal);

        for (var colIndex = 0; colIndex < columns.Count; colIndex++)
        {
            var colStates = columns[colIndex];
            var colHeight = (colStates.Count * NodeHeight) + ((colStates.Count - 1) * RowGap);
            var startYOffset = PaddingTop + ((contentHeight - colHeight) / 2.0);
            var x = PaddingLeft + (colIndex * (NodeWidth + ColumnGap));

            for (var rowIndex = 0; rowIndex < colStates.Count; rowIndex++)
            {
                var state = colStates[rowIndex];
                var y = startYOffset + (rowIndex * (NodeHeight + RowGap));
                var isCurrent = instance is not null && string.Equals(instance.State, state.Key, StringComparison.Ordinal);
                var isVisited = isCurrent || visitedStates.Contains(state.Key);
                var visitSteps = visitStepsByState.TryGetValue(state.Key, out var vs) ? vs : (isCurrent ? [1] : []);
                var visitCount = visitSteps.Count;
                var statusKind = ResolveNodeStatusKind(state, instance, isCurrent, isVisited);
                var badgeText = ResolveNodeBadge(state, isCurrent, isVisited, visitCount);
                var title = string.IsNullOrWhiteSpace(state.Title) ? state.Key : state.Title;
                var statusLabel = isCurrent
                    ? "Şu anki adım"
                    : isVisited
                        ? (visitCount > 1 ? $"Geçildi ({visitCount} kez)" : "Geçildi")
                        : "Henüz geçilmedi";

                var node = new WorkflowGraphNode(
                    Key: state.Key,
                    Title: title,
                    Type: state.Type,
                    SubType: state.SubType,
                    Step: state.Step,
                    Column: colIndex,
                    Row: rowIndex,
                    X: Math.Round(x, 1),
                    Y: Math.Round(y, 1),
                    Width: NodeWidth,
                    Height: NodeHeight,
                    IsStart: state.Key == startKey,
                    IsFinish: IsFinishState(state),
                    IsCurrent: isCurrent,
                    IsVisited: isVisited,
                    VisitCount: visitCount,
                    VisitSteps: visitSteps,
                    StatusKind: statusKind,
                    BadgeText: badgeText,
                    Tooltip: $"{title} ({state.Key}) — {statusLabel}");

                nodes.Add(node);
                nodeByKey[state.Key] = node;
            }
        }

        var startNode = nodeByKey[startKey];
        var startX = 20.0;
        var startY = Math.Round(startNode.Y + (NodeHeight / 2.0), 1);

        // 6. Build edges (including the initial Start edge).
        var edges = new List<WorkflowGraphEdge>(edgeGroupOrder.Count + 1);
        var startSteps = stepNumbersByEdge.TryGetValue((null, startKey), out var ss)
            ? ss
            : (instance is not null ? [1] : []);
        var startTraversed = startSteps.Count > 0 || instance is not null;
        var startPath = F($"M {startX + StartRadius} {startY} L {startNode.X} {startY}");
        edges.Add(new WorkflowGraphEdge(
            Id: $"__start->{startKey}",
            From: null,
            Target: startKey,
            Label: "Başlangıç",
            Trigger: "Start",
            Kind: "start",
            Path: startPath,
            LabelX: Math.Round((startX + StartRadius + startNode.X) / 2.0, 1),
            LabelY: Math.Round(startY - 10, 1),
            IsStart: true,
            IsSelfLoop: false,
            IsBackEdge: false,
            IsTraversed: startTraversed,
            IsAvailable: false,
            TraversalCount: startSteps.Count,
            StepNumbers: startSteps,
            OrderBadge: FormatStepBadge(startSteps),
            Tone: startTraversed ? "traversed" : "upcoming",
            Tooltip: $"Başlangıç → {startNode.Title}"));

        foreach (var (fromKey, targetKey) in edgeGroupOrder)
        {
            var fromNode = nodeByKey[fromKey];
            var toNode = nodeByKey[targetKey];
            var transitions = edgeGroups[(fromKey, targetKey)];
            var steps = stepNumbersByEdge.TryGetValue((fromKey, targetKey), out var es) ? es : [];
            var isTraversed = steps.Count > 0;
            var isAvailable = instance is { Status: WorkflowInstanceStatus.Active }
                && string.Equals(instance.State, fromKey, StringComparison.Ordinal)
                && availableTargetsFromCurrent.Contains(targetKey);

            var label = lastTransitionTitleByEdge.TryGetValue((fromKey, targetKey), out var matchedTitle)
                ? matchedTitle
                : ResolveEdgeLabel(transitions);
            var trigger = transitions[0].Trigger;
            var isSelf = fromKey == targetKey;
            var isBack = !isSelf && toNode.Column <= fromNode.Column;
            var isSkip = !isSelf && !isBack && (toNode.Column > fromNode.Column + 1);
            var kind = isSelf ? "self" : isBack ? "back" : isSkip ? "skip" : "forward";

            var (path, labelX, labelY) = ComputeEdgeGeometry(fromNode, toNode, kind);
            var tone = isTraversed
                ? (toNode.SubType == "Error" ? "traversed-error" : "traversed")
                : isAvailable
                    ? "available"
                    : "upcoming";
            var statusDesc = isTraversed
                ? $"Geçildi ({string.Join(", ", steps.Select(s => $"{s}. adım"))})"
                : isAvailable
                    ? "Kullanılabilir geçiş"
                    : "Henüz geçilmedi";

            edges.Add(new WorkflowGraphEdge(
                Id: $"{fromKey}->{targetKey}",
                From: fromKey,
                Target: targetKey,
                Label: label,
                Trigger: trigger,
                Kind: kind,
                Path: path,
                LabelX: Math.Round(labelX, 1),
                LabelY: Math.Round(labelY, 1),
                IsStart: false,
                IsSelfLoop: isSelf,
                IsBackEdge: isBack,
                IsTraversed: isTraversed,
                IsAvailable: isAvailable,
                TraversalCount: steps.Count,
                StepNumbers: steps,
                OrderBadge: FormatStepBadge(steps),
                Tone: tone,
                Tooltip: $"{fromNode.Title} → {toNode.Title} ({label}) — {statusDesc}"));
        }

        return new WorkflowGraph
        {
            Width = Math.Round(totalWidth, 1),
            Height = Math.Round(totalHeight, 1),
            StartX = startX,
            StartY = startY,
            Nodes = nodes,
            Edges = edges,
        };
    }

    private static bool IsFinishState(WorkflowStateModel state) =>
        string.Equals(state.Type, "Finish", StringComparison.OrdinalIgnoreCase);

    private static int SubTypePriority(string? subType) => subType switch
    {
        "Success" => 0,
        "Human" or "None" or null or "" => 1,
        "Cancelled" => 2,
        "Timeout" => 3,
        "Error" => 4,
        _ => 1,
    };

    private static string ResolveNodeStatusKind(
        WorkflowStateModel state,
        WorkflowInstanceModel? instance,
        bool isCurrent,
        bool isVisited)
    {
        if (isCurrent && instance is not null)
        {
            return instance.Status switch
            {
                WorkflowInstanceStatus.Faulted => "faulted",
                WorkflowInstanceStatus.Busy => "busy",
                WorkflowInstanceStatus.Cancelled => "cancelled",
                WorkflowInstanceStatus.Completed when state.SubType == "Error" => "error",
                WorkflowInstanceStatus.Completed when state.SubType is "Cancelled" or "Timeout" => "cancelled",
                WorkflowInstanceStatus.Completed => "completed",
                _ => "active",
            };
        }

        if (isVisited)
        {
            return state.SubType == "Error" ? "error" : "visited";
        }

        return "upcoming";
    }

    private static string ResolveNodeBadge(
        WorkflowStateModel state,
        bool isCurrent,
        bool isVisited,
        int visitCount)
    {
        if (isCurrent)
        {
            return visitCount > 1 ? $"Şu an ({visitCount}×)" : "Şu an";
        }

        if (isVisited)
        {
            return visitCount > 1 ? $"✓ {visitCount}×" : "✓";
        }

        return state.Step is { } step ? $"#{step.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
    }

    private static string ResolveEdgeLabel(List<WorkflowTransitionModel> transitions)
    {
        var first = transitions.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Title))?.Title ?? transitions[0].Key;
        return transitions.Count > 1 ? $"{first} (+{transitions.Count - 1})" : first;
    }

    private static string FormatStepBadge(List<int> steps) =>
        steps.Count == 0
            ? string.Empty
            : steps.Count <= 2
                ? string.Join(",", steps.Select(s => s.ToString(CultureInfo.InvariantCulture)))
                : $"{steps[0].ToString(CultureInfo.InvariantCulture)}..{steps[^1].ToString(CultureInfo.InvariantCulture)}";

    private static (string Path, double LabelX, double LabelY) ComputeEdgeGeometry(
        WorkflowGraphNode from,
        WorkflowGraphNode to,
        string kind)
    {
        var fromCenterY = from.Y + (NodeHeight / 2.0);
        var toCenterY = to.Y + (NodeHeight / 2.0);

        if (kind == "self")
        {
            var x1 = from.X + (NodeWidth * 0.34);
            var x2 = from.X + (NodeWidth * 0.66);
            var y = from.Y;
            var peakY = y - 34;
            return (
                F($"M {x1} {y} C {x1 - 8} {peakY}, {x2 + 8} {peakY}, {x2} {y}"),
                from.X + (NodeWidth * 0.5),
                peakY + 5);
        }

        if (kind == "back")
        {
            var x1 = from.X + (NodeWidth * 0.5);
            var y1 = from.Y + NodeHeight;
            var x2 = to.X + (NodeWidth * 0.5);
            var y2 = to.Y + NodeHeight;
            var span = Math.Max(1, from.Column - to.Column);
            var troughY = Math.Max(y1, y2) + 28 + (span * 6);
            return (
                F($"M {x1} {y1} C {x1} {troughY}, {x2} {troughY}, {x2} {y2}"),
                (x1 + x2) / 2.0,
                troughY - 5);
        }

        if (kind == "skip")
        {
            if (toCenterY <= fromCenterY)
            {
                var x1 = from.X + (NodeWidth * 0.78);
                var y1 = from.Y;
                var sameRow = Math.Abs(toCenterY - fromCenterY) <= 12;
                var x2 = sameRow ? to.X + (NodeWidth * 0.22) : to.X;
                var y2 = sameRow ? to.Y : toCenterY;
                var peakY = Math.Min(from.Y, to.Y) - 32;
                return (
                    F($"M {x1} {y1} C {x1 + 32} {peakY}, {x2 - 32} {peakY}, {x2} {y2}"),
                    (x1 + x2) / 2.0,
                    peakY + 6);
            }
            else
            {
                var x1 = from.X + (NodeWidth * 0.78);
                var y1 = from.Y + NodeHeight;
                var x2 = to.X;
                var y2 = toCenterY;
                var troughY = Math.Max(y1 + 28, y2 + 12);
                return (
                    F($"M {x1} {y1} C {x1 + 36} {troughY}, {x2 - 44} {y2}, {x2} {y2}"),
                    (x1 + x2) / 2.0,
                    (y1 + troughY + y2) / 3.0);
            }
        }

        // Standard forward edge between adjacent columns
        var startY = toCenterY < fromCenterY - 8
            ? fromCenterY - 10
            : toCenterY > fromCenterY + 8
                ? fromCenterY + 10
                : fromCenterY;
        var endY = fromCenterY < toCenterY - 8
            ? toCenterY - 8
            : fromCenterY > toCenterY + 8
                ? toCenterY + 8
                : toCenterY;
        var startX = from.X + NodeWidth;
        var endX = to.X;
        var dx = (endX - startX) * 0.45;
        return (
            F($"M {startX} {startY} C {startX + dx} {startY}, {endX - dx} {endY}, {endX} {endY}"),
            (startX + endX) / 2.0,
            ((startY + endY) / 2.0) - 10);
    }

    private static string F(FormattableString formattable) => formattable.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Positioned state node in a <see cref="WorkflowGraph"/>.</summary>
public sealed record WorkflowGraphNode(
    string Key,
    string Title,
    string Type,
    string SubType,
    int? Step,
    int Column,
    int Row,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsStart,
    bool IsFinish,
    bool IsCurrent,
    bool IsVisited,
    int VisitCount,
    IReadOnlyList<int> VisitSteps,
    string StatusKind,
    string BadgeText,
    string Tooltip);

/// <summary>Positioned directed transition edge in a <see cref="WorkflowGraph"/>.</summary>
public sealed record WorkflowGraphEdge(
    string Id,
    string? From,
    string Target,
    string Label,
    string Trigger,
    string Kind,
    string Path,
    double LabelX,
    double LabelY,
    bool IsStart,
    bool IsSelfLoop,
    bool IsBackEdge,
    bool IsTraversed,
    bool IsAvailable,
    int TraversalCount,
    IReadOnlyList<int> StepNumbers,
    string OrderBadge,
    string Tone,
    string Tooltip);
