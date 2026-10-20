using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Graph;
using netDxf.Shadow.Mcp;

internal static class ReferenceAnalysisVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Reference analysis: " + message); checks++; }
        void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
        ShadowSnapshot Snapshot(int count, IEnumerable<(int A, int B)> edges)
        {
            var grouped = edges.Select((edge, index) => (edge, index)).ToLookup(e => e.edge.A);
            return new(Guid.NewGuid(), 0, new("n0"), Enumerable.Range(0, count).Select(i => new ShadowObject(new("n" + i), "Fixture.Object",
                ShadowObjectKind.Object, grouped[i].ToImmutableDictionary(e => "edge" + e.index.ToString("D6"), e => (ShadowValue)new ShadowValue.Reference(new("n" + e.edge.B))),
                ImmutableHashSet<string>.Empty)).ToImmutableDictionary(n => n.Id), [], []);
        }
        // Independent transitive closure and distance oracle across every directed three-node graph, including loops.
        for (var mask = 0; mask < 512; mask++)
        {
            var edges = (from a in Enumerable.Range(0, 3) from b in Enumerable.Range(0, 3) where (mask & (1 << (a * 3 + b))) != 0 select (a, b)).ToArray();
            var snapshot = Snapshot(3, edges); var index = new ReferenceGraphIndex(snapshot);
            var result = index.StronglyConnectedComponents(new() { Roots = [new("n0"), new("n1"), new("n2")], MaximumDepth = 0 });
            var distance = new int[3, 3];
            for (var a = 0; a < 3; a++) for (var b = 0; b < 3; b++) distance[a, b] = a == b ? 0 : edges.Contains((a, b)) ? 1 : 99;
            for (var k = 0; k < 3; k++) for (var a = 0; a < 3; a++) for (var b = 0; b < 3; b++) distance[a, b] = Math.Min(distance[a, b], distance[a, k] + distance[k, b]);
            int Component(int id) => result.Components.Single(c => c.Nodes.Contains("n" + id)).Id;
            Check(result.Complete && result.Scope.Nodes.Length == 3, "all-root scope is complete");
            for (var a = 0; a < 3; a++) for (var b = 0; b < 3; b++)
            {
                Check((Component(a) == Component(b)) == (distance[a, b] < 99 && distance[b, a] < 99), "SCC agrees with mutual reachability oracle");
                var path = index.ShortestPath(new("n" + a), new("n" + b));
                Check(distance[a, b] == 99 ? path.Status == ReferencePathStatus.Unreachable : path.Status == ReferencePathStatus.Found && path.Edges.Length == distance[a, b], "shortest path agrees with closure distance");
                Check(path.Edges.Select((e, i) => e.Source == path.Nodes[i].Id && e.Target == path.Nodes[i + 1].Id).All(v => v), "ordered forward proof contains original references");
                var incoming = index.ShortestPath(new("n" + a), new("n" + b), new() { Direction = ReferenceGraphDirection.Incoming });
                Check(distance[b, a] == 99 ? incoming.Status == ReferencePathStatus.Unreachable : incoming.Status == ReferencePathStatus.Found && incoming.Edges.Length == distance[b, a], "incoming path agrees with reverse oracle");
            }
            foreach (var component in result.Components)
                Check(component.Cyclic == (component.Nodes.Length > 1 || edges.Any(e => e.a == e.b && component.Nodes.Contains("n" + e.a))), "cycles include self references");
            foreach (var link in result.CondensationEdges)
                Check(link.Source != link.Target && link.ReferenceCount == edges.Count(e => Component(e.a) == link.Source && Component(e.b) == link.Target), "condensation aggregates exact cross-component references");
            var impact = index.Impact(new() { Roots = [new("n2")], MaximumDepth = 3 });
            Check(impact.Complete && impact.Visits.Length == Enumerable.Range(0, 3).Count(a => distance[a, 2] < 99), "impact includes precisely all reachable referrers");
            foreach (var visit in impact.Visits)
            {
                var proof = impact.PathToRoot(visit.Id);
                Check(proof.Length == distance[int.Parse(visit.Id[1..]), 2] && (proof.Length == 0 || proof[0].Source == visit.Id && proof[^1].Target == "n2"), "impact proof is a shortest source-to-seed path");
            }
        }
        var diamond = Snapshot(6, [(0, 1), (0, 2), (1, 3), (2, 3), (3, 4), (0, 1), (4, 4)]);
        var graph = new ReferenceGraphIndex(diamond);
        var chosen = graph.ShortestPath(new("n0"), new("n3"));
        Check(chosen.Nodes.Select(n => n.Id).SequenceEqual(["n0", "n1", "n3"]), "tie resolves deterministic property order, retaining parallel paths");
        Check(chosen.Edges[0].Path == "/edge000000", "exact path identifies parallel reference");
        Check(graph.ShortestPath(new("n0"), new("n5")).Status == ReferencePathStatus.Unreachable, "unreachable requires complete exhaustion");
        foreach (var options in new[] { new ReferencePathOptions { MaximumNodes = 2 }, new ReferencePathOptions { MaximumEdges = 1 }, new ReferencePathOptions { MaximumDepth = 0 } })
            Check(graph.ShortestPath(new("n0"), new("n3"), options).Status == ReferencePathStatus.Incomplete, "each exhausted bound is incomplete, never unreachable");
        var partial = graph.StronglyConnectedComponents(new() { MaximumDepth = 0 });
        Check(!partial.Complete && partial.Scope.DepthLimitReached, "bounded acyclic fragment does not claim global completeness");
        var edgePartial = graph.StronglyConnectedComponents(new() { MaximumDepth = 4, MaximumEdges = 1 });
        Check(!edgePartial.Complete && edgePartial.Scope.EdgeLimitReached, "missing internal edges disclose partial SCC scope");
        var impactPartial = graph.Impact(new() { Roots = [new("n3")], MaximumNodes = 2, MaximumDepth = 4 });
        Check(!impactPartial.Complete && impactPartial.Scope.NodeLimitReached && impactPartial.Visits.All(v => v.Via is null || impactPartial.Scope.Edges.Contains(v.Via)), "partial impact has valid complete predecessor chains");
        Throws<ArgumentException>(() => graph.ShortestPath(new("unknown"), new("n0")));
        Throws<ArgumentException>(() => graph.ShortestPath(new("n0"), new("unknown")));
        Throws<ArgumentOutOfRangeException>(() => graph.ShortestPath(new("n0"), new("n1"), new() { MaximumDepth = 17 }));
        Throws<ArgumentException>(() => impactPartial.PathToRoot("unknown"));
        foreach (var malformed in new ImmutableArray<ReferenceImpactVisit>[] {
            [new("n3", 1, "n3", new("n3", "/self", "n3"))],
            [new("n3", 1, "missing", new("n3", "/x", "missing"))],
            [new("n3", 1, "n1", new("n1", "/wrong", "n3")), new("n1", 0, null, null)],
            [new("n3", 0, null, null), new("n3", 0, null, null)],
            [new("n3", 2, null, null)] })
            Throws<InvalidOperationException>(() => (impactPartial with { Visits = malformed }).PathToRoot("n3"));
        var genuine = graph.Impact(new() { Roots = [new("n3")], MaximumDepth = 4 });
        Throws<InvalidOperationException>(() => (genuine with { Visits = [new("foreign", 0, null, null)] }).PathToRoot("foreign"));
        Throws<InvalidOperationException>(() => (genuine with { Visits = [new("n1", 0, null, null)] }).PathToRoot("n1"));
        Throws<InvalidOperationException>(() => (genuine with { Visits = genuine.Visits.Select(v => v.Id == "n1" ? v with { Via = v.Via! with { Path = "/invented" } } : v).ToImmutableArray() }).PathToRoot("n1"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(() => graph.StronglyConnectedComponents(cancellationToken: cancelled.Token));
        Throws<OperationCanceledException>(() => graph.ShortestPath(new("n0"), new("n1"), cancellationToken: cancelled.Token));
        Throws<OperationCanceledException>(() => graph.Impact(cancellationToken: cancelled.Token));
        Throws<OperationCanceledException>(() => impactPartial.PathToRoot("n3", cancelled.Token));
        // A deep strongly connected scope verifies nonrecursive traversal without increasing public bounds.
        var chain = Snapshot(1000, Enumerable.Range(0, 1000).Select(i => (i, (i + 1) % 1000)));
        var chainIndex = new ReferenceGraphIndex(chain);
        // Seed every fifth node to fit the 200-root cap while covering the full cycle at depth five.
        var cycle = chainIndex.StronglyConnectedComponents(new() { Roots = Enumerable.Range(0, 200).Select(i => new ObjectId("n" + (i * 5))).ToImmutableArray(), MaximumDepth = 5, MaximumNodes = 1000, MaximumEdges = 4000 });
        Check(cycle.Complete && cycle.Components.Length == 1 && cycle.Components[0].Nodes.Length == 1000 && cycle.CondensationEdges.IsEmpty, "iterative SCC handles full supported scope");

        var editor = new EditorSession(new ShadowMemoryStore(diamond)); var view = 1L; var workspace = "live";
        var registry = new McpToolRegistry(maximumResultCharacters: 4096);
        ShadowMcpTools.Register(registry, new(() => editor) { Identity = () => new(editor.Snapshot.DocumentId, workspace, view) });
        async Task<JsonElement> Query(string tool, object args)
        {
            var response = await registry.ExecuteAsync(tool, McpJson.Element(args));
            Check(!response.IsError && response.Text.Length <= 4096, "serialized analysis receipt is bounded: " + tool + " " + (response.IsError ? response.Text : ""));
            return McpJson.Parse(response.Text).GetProperty("data");
        }
        var first = await Query("graph_reference_components", new { depth = 4 }); var key = first.GetProperty("referenceKey").GetString();
        Check(first.GetProperty("analysis").GetString() == "components" && first.GetProperty("results").GetProperty("total").GetInt32() == 5, "component tool exposes summaries plus node memberships");
        foreach (var args in new object[] { new { depth = 3, referenceKey = key }, new { depth = 4, roots = new[] { "n1" }, referenceKey = key }, new { depth = 4, direction = "incoming", referenceKey = key } })
            Check((await registry.ExecuteAsync("graph_reference_components", McpJson.Element(args))).IsError, "changed scope key rejected");
        view++; Check((await registry.ExecuteAsync("graph_reference_components", McpJson.Element(new { depth = 4, referenceKey = key }))).IsError, "view key rejected"); view--;
        workspace = "other"; Check((await registry.ExecuteAsync("graph_reference_components", McpJson.Element(new { depth = 4, referenceKey = key }))).IsError, "workspace key rejected"); workspace = "live";
        var pathData = await Query("graph_reference_path", new { source = "n0", target = "n3" });
        Check(pathData.GetProperty("status").GetString() == "found" && pathData.GetProperty("edgeCount").GetInt32() == 2, "path tool proves ordered hops");
        Check((await registry.ExecuteAsync("graph_reference_impact", McpJson.Element(new { referenceKey = key, depth = 4 }))).IsError, "keys cannot cross analysis modes");
        var impactData = await Query("graph_reference_impact", new { roots = new[] { "n3" }, depth = 4 });
        Check(impactData.GetProperty("direction").GetString() == "incoming" && impactData.GetProperty("nodes").GetProperty("items").EnumerateArray().Any(n => n.GetProperty("predecessor").ValueKind == JsonValueKind.String), "impact nodes retain proof forest");
        editor = new(new ShadowMemoryStore(diamond with { Objects = diamond.Objects.Remove(new("n5")) }));
        Check((await registry.ExecuteAsync("graph_reference_components", McpJson.Element(new { depth = 4, referenceKey = key }))).IsError, "same revision replaced snapshot invalidates analysis");

        // Four independent streams adapt to small host budgets with lossless continuation.
        var fan = Snapshot(150, Enumerable.Range(1, 149).Select(i => (0, i)));
        editor = new(new ShadowMemoryStore(fan));
        var offsets = new Dictionary<string, int> { ["node"] = 0, ["edge"] = 0, ["result"] = 0, ["link"] = 0 };
        var totals = new Dictionary<string, int>(); var identities = offsets.Keys.ToDictionary(k => k, _ => new HashSet<string>());
        key = null; var pages = 0;
        while (true)
        {
            var args = new Dictionary<string, object> { ["depth"] = 1, ["maximumNodes"] = 200, ["maximumEdges"] = 500 };
            foreach (var p in offsets) args[p.Key + "Offset"] = p.Value;
            if (key is not null) args["referenceKey"] = key;
            var result = await Query("graph_reference_components", args); key ??= result.GetProperty("referenceKey").GetString();
            var more = false;
            foreach (var stream in offsets.Keys.ToArray())
            {
                var page = result.GetProperty(stream == "link" ? "links" : stream + "s"); var total = page.GetProperty("total").GetInt32();
                if (totals.TryGetValue(stream, out var previous)) Check(previous == total, "page total remains stable"); else totals[stream] = total;
                foreach (var item in page.GetProperty("items").EnumerateArray()) Check(identities[stream].Add(item.GetRawText()), "every stream item appears once");
                var next = page.GetProperty("nextOffset"); more |= next.ValueKind != JsonValueKind.Null;
                offsets[stream] = next.ValueKind == JsonValueKind.Null ? total : next.GetInt32();
            }
            Check(++pages < 200, "all adaptive streams make progress"); if (!more) break;
        }
        Check(pages > 1 && identities.All(p => p.Value.Count == totals[p.Key]) && totals["node"] == 150 && totals["link"] == 149, "four streams finish without loss");
        Check(editor.State.UndoCount == 0 && editor.Snapshot.Revision == 0, "analysis never edits source/history");
        Console.WriteLine($"Reference analysis verification passed: {checks} checks."); return checks;
    }
}
