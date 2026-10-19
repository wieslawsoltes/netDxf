using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Health;

namespace netDxf.Shadow.Studio;

public sealed record CadHealthRequest(long Revision, DrawingHealthOptions? Options = null);
public sealed record CadHealthSelectRequest(long Revision, string ReportId, string IssueId);

public sealed partial class Studio
{
    private sealed record HealthReceipt(ShadowSnapshot Snapshot, string Workspace, long View, long FontRevision, DrawingHealthReport Report);
    private readonly Dictionary<string, HealthReceipt> healthReports = new(StringComparer.Ordinal);
    public object CadHealth(CadHealthRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision);
            var report = AnalyzeHealth(Current, request.Options, token); var id = Guid.NewGuid().ToString("N");
            healthReports.Clear(); healthReports.Add(id, new(Current, WorkspaceId, viewVersion, CurrentFonts.Revision, report));
            return new { reportId = id, documentId = Current.DocumentId, workspaceId = WorkspaceId, viewVersion, report };
        }
    }
    private DrawingHealthReport AnalyzeHealth(ShadowSnapshot snapshot, DrawingHealthOptions? options, CancellationToken token)
        => DrawingHealth.Analyze(snapshot, options, new(CurrentFonts.Fonts.Keys.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)), token);

    /// <summary>Selects visible source entities affected by a finding and returns optional focus bounds.</summary>
    public object CadHealthSelect(CadHealthSelectRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!healthReports.TryGetValue(request.ReportId, out var receipt) || !ReferenceEquals(receipt.Snapshot, Current)
                || receipt.Workspace != WorkspaceId || receipt.View != viewVersion || receipt.FontRevision != CurrentFonts.Revision)
                throw new ShadowConflictException("The health report expired. Run the checks again.");
            var issue = receipt.Report.Issues.FirstOrDefault(i => i.Id == request.IssueId) ?? throw new ArgumentException("Unknown health issue.");
            var ids = issue.RelatedObjects.ToHashSet(); if (issue.Object is { } id) ids.Add(id);
            // Definitions and style issues select their visible dependents, bounded by the drawing health object budget.
            var reverse = new Dictionary<ObjectId, HashSet<ObjectId>>();
            var tables = new[] { "Blocks", "Layers", "Linetypes", "TextStyles", "ShapeStyles", "Layouts" }.Select(t => Current.Document[t].Id).ToHashSet();
            foreach (var node in Current.Objects.Values)
            {
                if (node.Id == Current.Root || tables.Contains(node.Id) || node.Kind.HasFlag(ShadowObjectKind.Layout)) continue;
                foreach (var target in node.Properties.Where(p => p.Key is not ("Owner" or "BlockRecord")).SelectMany(p => p.Value.References()))
                { if (!reverse.TryGetValue(target, out var set)) reverse[target] = set = []; set.Add(node.Id); }
            }
            var queue = new Queue<ObjectId>(ids); var visited = new HashSet<ObjectId>(ids);
            while (queue.TryDequeue(out var next))
                if (reverse.TryGetValue(next, out var dependents)) foreach (var dependent in dependents)
                    if (visited.Add(dependent)) queue.Enqueue(dependent);
            var scene = Scene(); var items = scene.Items.Where(i => visited.Contains(i.Entity) || i.InsertPath.Any(visited.Contains)).ToArray();
            editor.Select(items.Select(i => i.Key)); cadSelectionOrder = items.Select(DraftingQueries.Root).Distinct().ToImmutableArray();
            Bounds2? bounds = items.Length == 0 ? issue.Position is { } point ? new(point.X, point.Y, point.X, point.Y) : null
                : items.Select(i => i.Bounds).Aggregate(Bounds2.Empty, (a, b) => a.Union(b));
            PublishChange(); return new { cad = CadState(), bounds, selectedCount = cadSelectionOrder.Length, issue.Object,
                message = items.Length == 0 ? "This finding has no visible occurrence in the current layout; inspect its source resource." : "Affected visible source occurrences selected." };
        }
    }
}
