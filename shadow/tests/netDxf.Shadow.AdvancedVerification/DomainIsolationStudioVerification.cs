using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;
using netDxf.Shadow.Symbols;

internal static class DomainIsolationStudioVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("discipline-isolation/adaptive streams cover imported tree and preserve identity", () =>
        {
            using var studio = new Studio(false); using var bytes = TreeDrawing(); studio.Load(bytes);
            studio.CadSetModule(new(studio.CadRevision, "pid", true));
            var state = JsonSerializer.SerializeToElement(studio.CadState(), StudioJson.CreateOptions()).GetProperty("state");
            var identity = new StudioRequestIdentity(state.GetProperty("documentId").GetGuid(), state.GetProperty("workspaceId").GetString()!, state.GetProperty("viewVersion").GetInt64());
            var registry = studio.CreateMcpRegistry();
            var request = new CadModuleConnectivityRequest(studio.CadRevision, "pid", "isolation", "tree.0/start", "tree.1299/end", "forward", Limit: 500,
                ExcludedPorts: Enumerable.Range(1, 400).Select(i => "tree." + i + "/end").ToImmutableArray());
            var totals = new Dictionary<string, HashSet<string>>(); var iterations = 0; CadModuleConnectivityResult result;
            while (true)
            {
                var response = registry.ExecuteAsync("studio_module_connectivity", McpJson.Element(new { identity, request })).AsTask().GetAwaiter().GetResult();
                Require(!response.IsError && response.Text.Length <= registry.MaximumResultCharacters, "Exact isolation receipt fits MCP budget without archive fallback.");
                result = JsonSerializer.Deserialize<CadModuleConnectivityResult>(response.Text, McpJson.Options)!;
                Require(result.Isolation is { ScopeComplete: true, Before.Complete: true, After.Complete: true }, "Tree traversals complete within independent budgets.");
                Collect("nodes", result.Nodes.Items.Select(v => v.Id)); Collect("edges", result.Edges.Items.Select(v => v.Id));
                Collect("before", result.BeforeVisits.Items.Select(v => v.Id)); Collect("after", result.AfterVisits.Items.Select(v => v.Id));
                Collect("assessment", result.Assessments.Items.Select(v => v.Id)); Collect("exclusions", result.Exclusions.Items.Select(v => v.Id));
                var cursors = new[] { result.Nodes.NextOffset, result.Edges.NextOffset, result.Issues.NextOffset, result.Components.NextOffset,
                    result.Visits.NextOffset, result.Assessments.NextOffset, result.BeforeVisits.NextOffset, result.AfterVisits.NextOffset, result.Exclusions.NextOffset };
                if (cursors.All(v => v is null)) break;
                request = request with { GraphKey = result.GraphKey, NodeOffset = result.Nodes.NextOffset ?? result.Nodes.Total, EdgeOffset = result.Edges.NextOffset ?? result.Edges.Total,
                    IssueOffset = result.Issues.NextOffset ?? result.Issues.Total, ComponentOffset = result.Components.NextOffset ?? result.Components.Total,
                    VisitOffset = result.Visits.NextOffset ?? result.Visits.Total, AssessmentOffset = result.Assessments.NextOffset ?? result.Assessments.Total,
                    BeforeVisitOffset = result.BeforeVisits.NextOffset ?? result.BeforeVisits.Total, AfterVisitOffset = result.AfterVisits.NextOffset ?? result.AfterVisits.Total,
                    ExclusionOffset = result.Exclusions.NextOffset ?? result.Exclusions.Total };
                Require(++iterations < 100, "Independent streams terminate.");
            }
            Require(totals["nodes"].Count == 2600 && totals["assessment"].Count == 2600 && totals["before"].Count == 2600 && totals["after"].Count == 2200 && totals["exclusions"].Count == 400,
                "All assessment, assumption and proof rows survive adaptive paging.");
            Require(iterations > 5, "Fixture actually exercises adaptive continuation.");
            Throws<InvalidOperationException>(() => studio.CadModuleConnectivity(request with { ExcludedPorts = [] }));
            Throws<InvalidOperationException>(() => studio.CadModuleConnectivity(request with { IsolationOptions = new(MaximumVisits: 1) }));
            Throws<ArgumentException>(() => studio.CadModuleConnectivity(request with { GraphKey = null, AssessmentOffset = 1 }));
            var after = JsonSerializer.SerializeToElement(studio.CadState(), StudioJson.CreateOptions()).GetProperty("state");
            Require(after.GetProperty("revision").GetInt64() == state.GetProperty("revision").GetInt64() && after.GetProperty("undoCount").GetInt32() == state.GetProperty("undoCount").GetInt32(), "Scenario analysis never edits native source/history.");
            studio.CadSetModule(new(studio.CadRevision, "pid", false));
            Throws<InvalidOperationException>(() => studio.CadModuleConnectivity(request));
            void Collect(string stream, IEnumerable<string> ids)
            {
                if (!totals.TryGetValue(stream, out var set)) totals[stream] = set = new(StringComparer.Ordinal);
                foreach (var id in ids) Require(set.Add(id), "No duplicate " + stream + " ID.");
            }
        }));
        tests.Add(("discipline-isolation/invalid exclusions and cancellation preserve cached scope", () =>
        {
            using var studio = new Studio(); studio.CadSetModule(new(studio.CadRevision, "pid", true));
            var network = studio.CadModuleConnectivity(new(studio.CadRevision, "pid"));
            var source = network.Nodes.Items[0].Id;
            var request = new CadModuleConnectivityRequest(studio.CadRevision, "pid", "isolation", source, source);
            var valid = studio.CadModuleConnectivity(request);
            Require(valid.Isolation?.After.TargetStatus == "reachable" && valid.Isolation.After.TargetPathLength == 0, "Retained source has zero-link proof.");
            Throws<ArgumentException>(() => studio.CadModuleConnectivity(request with { ExcludedPorts = [source] }));
            Throws<ArgumentException>(() => studio.CadModuleConnectivity(request with { ExcludedConnections = ["wrong"] }));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Throws<OperationCanceledException>(() => studio.CadModuleConnectivity(request, cancel.Token));
            Require(studio.CadModuleConnectivity(request with { GraphKey = valid.GraphKey }).GraphKey == valid.GraphKey, "Failed scenarios leave the prior exact scope cached.");
        }));
    }
    private static MemoryStream TreeDrawing()
    {
        const int count = 1300; var doc = new DxfDocument();
        for (var i = 0; i < count; i++)
        {
            var line = new Line(new Vector2(i, 0), new Vector2(i, 1)); doc.Entities.Add(line);
            var references = ImmutableArray.CreateBuilder<DomainReference>();
            if (i > 0) references.Add(new("start", "tree." + ((i - 1) / 2), "start"));
            for (var child = i * 2 + 1; child <= i * 2 + 2 && child < count; child++) references.Add(new("start", "tree." + child, "start"));
            DomainMetadata.Write(line, new("pid", "pipe", "tree." + i, "P-" + i,
                Ports: [new("start", "process", new(i, 0)), new("end", "process", new(i, 1))], References: references.ToImmutable())
                { Passages = [new("start", "end")] });
        }
        var stream = new MemoryStream(); Require(doc.Save(stream), "Tree fixture saved."); stream.Position = 0; return stream;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
