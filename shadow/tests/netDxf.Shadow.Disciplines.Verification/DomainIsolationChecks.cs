using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Symbols;

internal static class DomainIsolationChecks
{
    public static int Run()
    {
        var checks = 0;
        // Independent Floyd distances over all directed three-port graphs; delete each edge and each non-source port.
        var arcs = new[] { (0, 1), (0, 2), (1, 0), (1, 2), (2, 0), (2, 1) };
        for (var mask = 0; mask < 64; mask++)
        {
            var passages = arcs.Where((_, i) => (mask & (1 << i)) != 0).Select(a => new DomainPassage("p" + a.Item1, "p" + a.Item2, "forward")).ToArray();
            var graph = Build(Record("oracle", 3, passages));
            foreach (var direction in new[] { "forward", "reverse", "both" })
            {
                Verify(graph, [], [], direction);
                foreach (var edge in graph.Edges) Verify(graph, [edge.Id], [], direction);
                foreach (var node in graph.Nodes.Skip(1)) Verify(graph, [], [node.Id], direction);
            }
        }
        var reroute = Build(Record("paths", 4, new("p0", "p1", "forward"), new("p1", "p3", "forward"), new("p0", "p2", "forward"), new("p2", "p3", "forward")));
        var first = reroute.Edges.Single(e => e.Source == "paths/p0" && e.Target == "paths/p1").Id;
        var result = reroute.AnalyzeIsolation("paths/p0", "paths/p3", [first]);
        Check(result.Before.TargetPathLength == 2 && result.After.TargetPathLength == 2 && !result.ProofTo("paths/p3").Edges.Contains(first), "Alternative path follows retained exact links.");
        Check(result.Exclusions.Single().Value == first && result.Assessments.Single(a => a.Id == "paths/p1").Status == "lost", "One branch lost while alternate target survives.");
        var targetExcluded = reroute.AnalyzeIsolation("paths/p0", "paths/p3", excludedPorts: ["paths/p3"]);
        Check(targetExcluded.After.TargetStatus == "excluded" && targetExcluded.Before.TargetStatus == "reachable", "Excluded target is distinct from disconnected target.");
        Throws<ArgumentException>(() => targetExcluded.ProofTo("paths/p3"));
        Throws<ArgumentException>(() => reroute.AnalyzeIsolation("paths/p0", excludedPorts: ["paths/p0"]));
        Throws<ArgumentException>(() => reroute.AnalyzeIsolation("missing"));
        Throws<ArgumentException>(() => reroute.AnalyzeIsolation("paths/p0", excludedConnections: ["missing"]));
        Throws<ArgumentException>(() => reroute.AnalyzeIsolation("paths/p0", excludedPorts: Enumerable.Repeat("paths/p1", 2001).ToImmutableArray()));
        foreach (var bounds in new[] { new DomainIsolationOptions(0), new DomainIsolationOptions(MaximumDepth: 1001), new DomainIsolationOptions(MaximumExaminedEdges: 0) })
            Throws<ArgumentException>(() => reroute.AnalyzeIsolation("paths/p0", options: bounds));
        var duplicate = reroute.AnalyzeIsolation("paths/p0", excludedConnections: [first, first], excludedPorts: ["paths/p1", "paths/p1"]);
        Check(duplicate.Exclusions.Length == 2, "Exclusions are deduplicated deterministically.");
        foreach (var bounds in new[] { new DomainIsolationOptions(1), new DomainIsolationOptions(MaximumDepth: 0), new DomainIsolationOptions(MaximumExaminedEdges: 1) })
        {
            var partial = reroute.AnalyzeIsolation("paths/p0", "paths/p3", options: bounds);
            Check(!partial.Before.Complete && !partial.After.Complete && partial.After.TargetStatus == "unknown", "Traversal cap cannot imply disconnection.");
            Check(partial.Assessments.All(a => a.Status is "reachable" or "unknown"), "Bounded absence remains unknown.");
            Check(partial.After.ExaminedEdges <= bounds.MaximumExaminedEdges && partial.After.VisitedCount <= bounds.MaximumVisits, "Budgets respected independently.");
        }
        var missing = Record("legacy", 2) with { Passages = null };
        var incomplete = Build(Record("paths", 4, new("p0", "p1", "forward"), new("p1", "p3", "forward")), missing);
        var cut = incomplete.Edges.First().Id;
        var unknown = incomplete.AnalyzeIsolation("paths/p0", "paths/p3", [cut]);
        Check(!unknown.ScopeComplete && unknown.Before.TargetStatus == "reachable" && unknown.After.TargetStatus == "unknown", "Missing declarations prohibit lost claim despite before proof.");
        Check(unknown.Assessments.Single(a => a.Id == "paths/p3").Status == "unknown", "Partial source scope never proves lost.");
        var truncated = DomainConnectivity.Build(new ShadowSession(Document(Record("one", 2, new DomainPassage("p0", "p1", "forward")), Record("two", 1))).Current,
            "pid", options: new(MaximumObjects: 1));
        Check(!truncated.AnalyzeIsolation("one/p0", "one/p1", [truncated.Edges[0].Id]).After.Complete, "Bounded scan remains partial.");
        var parallel = Build(Record("parallel", 2, new("p0", "p1", "both"), new("p0", "p1", "forward")));
        Check(parallel.AnalyzeIsolation("parallel/p0", "parallel/p1", [parallel.Edges[0].Id]).After.TargetStatus == "reachable", "Parallel declared connection remains after removing one ID.");
        var unicode = new string('界', 120);
        var longGraph = Build(Record(unicode, 2, new DomainPassage("p0", "p1", "both")));
        Check(longGraph.AnalyzeIsolation(longGraph.Nodes[0].Id, excludedConnections: [longGraph.Edges[0].Id]).Exclusions.Length == 1, "Escaped Unicode connection IDs accepted.");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Throws<OperationCanceledException>(() => reroute.AnalyzeIsolation("paths/p0", cancellationToken: cancel.Token));
            Throws<OperationCanceledException>(() => result.ProofTo("paths/p3", cancellationToken: cancel.Token));
        }
        var doc = Document(Record("roundtrip", 3, new("p0", "p1", "forward"), new("p1", "p2", "forward"), new("p0", "p2", "forward")));
        var before = DomainConnectivity.Build(new ShadowSession(doc).Current, "pid");
        foreach (var binary in new[] { false, true })
        {
            using var stream = new MemoryStream(); doc.Save(stream, binary); stream.Position = 0;
            var imported = DomainConnectivity.Build(new ShadowSession(DxfDocument.Load(stream)).Current, "pid");
            var original = before.AnalyzeIsolation("roundtrip/p0", "roundtrip/p2", [before.Edges[0].Id]);
            var reloaded = imported.AnalyzeIsolation("roundtrip/p0", "roundtrip/p2", [before.Edges[0].Id]);
            Check(original.Assessments.SequenceEqual(reloaded.Assessments) && original.ProofTo("roundtrip/p2").Edges.SequenceEqual(reloaded.ProofTo("roundtrip/p2").Edges), "Native DXF preserves declared scenario IDs/proofs.");
        }
        Console.WriteLine($"Domain isolation: {checks} checks passed.");
        return checks;

        void Verify(DomainConnectivity graph, ImmutableArray<string> excludedEdges, ImmutableArray<string> excludedPorts, string direction)
        {
            var expectedBefore = Distances(graph, [], [], direction); var expectedAfter = Distances(graph, excludedEdges, excludedPorts, direction);
            var actual = graph.AnalyzeIsolation(graph.Nodes[0].Id, graph.Nodes[^1].Id, excludedEdges, excludedPorts, direction);
            Check(actual.Before.Complete && actual.After.Complete, "Small complete graph has complete traversals.");
            foreach (var item in actual.Assessments)
            {
                var i = Array.FindIndex(graph.Nodes.ToArray(), n => n.Id == item.Id);
                var b = expectedBefore[0, i] < 999 ? "reachable" : "unreachable";
                var a = excludedPorts.Contains(item.Id) ? "excluded" : expectedAfter[0, i] < 999 ? "reachable" : "unreachable";
                var comparison = a == "excluded" ? "excluded" : a == "reachable" ? "reachable" : b == "reachable" ? "lost" : "unreachable";
                Check(item.Before == b && item.After == a && item.Status == comparison, "Independent reachability/removal oracle.");
                if (a == "reachable")
                {
                    var proof = actual.ProofTo(item.Id);
                    Check(proof.Edges.Length == expectedAfter[0, i] && proof.Nodes[0] == graph.Nodes[0].Id && proof.Nodes[^1] == item.Id, "Independent minimum-link distance/proof endpoints.");
                    Check(!proof.Edges.Any(excludedEdges.Contains) && !proof.Nodes.Any(excludedPorts.Contains), "Scenario proofs contain no excluded IDs.");
                    for (var j = 0; j < proof.Edges.Length; j++)
                    {
                        var e = graph.Edges.Single(e => e.Id == proof.Edges[j]);
                        Check(Arcs(e, direction).Any(a => a.Item1 == proof.Nodes[j] && a.Item2 == proof.Nodes[j + 1]), "Proof follows declared direction.");
                    }
                }
            }
        }
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    }
    private static int[,] Distances(DomainConnectivity graph, ImmutableArray<string> excludedEdges, ImmutableArray<string> excludedPorts, string direction)
    {
        var n = graph.Nodes.Length; var indices = graph.Nodes.Select((node, i) => (node.Id, i)).ToDictionary(p => p.Id, p => p.i); var distances = new int[n, n];
        for (var a = 0; a < n; a++) for (var b = 0; b < n; b++) distances[a, b] = a == b ? 0 : 999;
        foreach (var edge in graph.Edges.Where(e => !excludedEdges.Contains(e.Id) && !excludedPorts.Contains(e.Source) && !excludedPorts.Contains(e.Target)))
            foreach (var (a, b) in Arcs(edge, direction)) distances[indices[a], indices[b]] = 1;
        for (var k = 0; k < n; k++) for (var a = 0; a < n; a++) for (var b = 0; b < n; b++) distances[a, b] = Math.Min(distances[a, b], distances[a, k] + distances[k, b]);
        return distances;
    }
    private static IEnumerable<(string, string)> Arcs(DomainConnection edge, string direction)
    {
        if (direction == "both" || edge.Direction == "both" || direction == edge.Direction) yield return (edge.Source, edge.Target);
        if (direction == "both" || edge.Direction == "both" || direction != edge.Direction) yield return (edge.Target, edge.Source);
    }
    private static DomainRecord Record(string key, int count, params DomainPassage[] passages) => new("pid", "test", key, key,
        Ports: Enumerable.Range(0, count).Select(i => new SymbolPort("p" + i, "process", new(i, 0))).ToImmutableArray(), References: [])
        { Passages = passages.ToImmutableArray() };
    private static DxfDocument Document(params DomainRecord[] records)
    {
        var doc = new DxfDocument(); foreach (var record in records) { var line = new Line(Vector2.Zero, Vector2.UnitX); DomainMetadata.Write(line, record); doc.Entities.Add(line); } return doc;
    }
    private static DomainConnectivity Build(params DomainRecord[] records) => DomainConnectivity.Build(new ShadowSession(Document(records)).Current, "pid");
}
