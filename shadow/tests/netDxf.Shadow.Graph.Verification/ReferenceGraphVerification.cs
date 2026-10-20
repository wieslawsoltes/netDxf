using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Graph;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

internal static class ReferenceGraphVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Reference graph: " + message); checks++; }
        void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
        ShadowObject Node(string id, params (string Name, ShadowValue Value)[] properties) => new(new(id), "Fixture." + id,
            id == "root" ? ShadowObjectKind.Document : ShadowObjectKind.Object,
            properties.ToImmutableDictionary(p => p.Name, p => p.Value), ImmutableHashSet<string>.Empty);
        ShadowValue Ref(string id) => new ShadowValue.Reference(new(id));
        var objects = new[]
        {
            Node("root", ("a/b~", new ShadowValue.Structure("Nested",
                ImmutableDictionary<string, ShadowValue>.Empty.Add("items", new ShadowValue.Sequence([Ref("a"), Ref("a")]))))),
            Node("a", ("back", Ref("root")), ("child", Ref("b"))),
            Node("b", ("self", Ref("b")), ("missing", Ref("absent"))), Node("unrelated")
        };
        var snapshot = new ShadowSnapshot(Guid.NewGuid(), 0, new("root"), objects.ToImmutableDictionary(n => n.Id), [], []);
        var index = new ReferenceGraphIndex(snapshot);
        var full = index.Explore(new() { MaximumDepth = 4 });
        Check(full.Nodes.Length == 4 && full.Edges.Length == 6 && !full.Truncated, "cycles terminate and isolated unrelated objects stay out");
        Check(full.Edges.Count(e => e.Source == "root" && e.Target == "a") == 2 &&
            full.Edges.Any(e => e.Path == "/a~1b~0/items/0"), "parallel references preserve exact escaped structure/collection paths");
        Check(full.Nodes.Single(n => n.Id == "absent").Exists == false, "dangling references remain explicit");
        var reverse = index.Explore(new() { Roots = [new("b")], Direction = ReferenceGraphDirection.Incoming, MaximumDepth = 4 });
        Check(reverse.Nodes.Length == 3 && reverse.Edges.Any(e => e.Source == "a" && e.Target == "b"), "incoming traversal preserves source-to-target edge orientation");
        Check(reverse.Nodes.Single(n => n.Id == "a").Depth == 1 && reverse.Nodes.Single(n => n.Id == "root").Depth == 2, "incoming breadth-first depths");
        Check(index.Explore(new() { MaximumDepth = 0 }).DepthLimitReached, "depth frontier is disclosed");
        Check(index.Explore(new() { MaximumNodes = 1 }).NodeLimitReached, "node truncation is disclosed");
        Check(index.Explore(new() { MaximumDepth = 4, MaximumEdges = 1 }).EdgeLimitReached, "edge truncation is disclosed");
        Check(!index.Explore(new() { Roots = [new("unrelated")], MaximumDepth = 0 }).Truncated, "empty frontier is complete at depth zero");
        Throws<ArgumentException>(() => index.Explore(new() { Roots = [new("unknown")] }));
        Throws<ArgumentException>(() => index.Explore(new() { Roots = [new("a"), new("b")], MaximumNodes = 1 }));
        Throws<ArgumentOutOfRangeException>(() => index.Explore(new() { MaximumEdges = 0 }));
        Throws<InvalidOperationException>(() => new ReferenceGraphIndex(snapshot, maximumObjects: 1));
        Throws<InvalidOperationException>(() => new ReferenceGraphIndex(snapshot, maximumValues: 1));
        ShadowValue nested = ShadowValue.Null;
        for (var i = 0; i < 4; i++)
            nested = new ShadowValue.Sequence(Enumerable.Repeat(ShadowValue.Null, 9000).ToImmutableArray().SetItem(0, nested));
        var wide = Node("root", ("items", nested));
        Throws<InvalidOperationException>(() => new ReferenceGraphIndex(snapshot with { Objects = snapshot.Objects.SetItem(wide.Id, wide) }, maximumValues: 10000));
        var longPaths = Node("root", (new string('x', 4000), new ShadowValue.Sequence(Enumerable.Repeat(Ref("a"), 5000).ToImmutableArray())));
        try { _ = new ReferenceGraphIndex(snapshot with { Objects = snapshot.Objects.SetItem(longPaths.Id, longPaths) }); throw new InvalidOperationException("Expected path budget failure."); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("path-character", StringComparison.Ordinal), "cumulative path text is bounded before large child allocation"); }
        var overlong = Node("root", (new string('x', 8192), Ref("a")));
        Throws<InvalidOperationException>(() => new ReferenceGraphIndex(snapshot with { Objects = snapshot.Objects.SetItem(overlong.Id, overlong) }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Throws<OperationCanceledException>(() => index.Explore(cancellationToken: cancellation.Token));
        Throws<OperationCanceledException>(() => new ReferenceGraphIndex(snapshot, cancellationToken: cancellation.Token));

        var editor = new EditorSession(new ShadowMemoryStore(snapshot));
        var workspace = "live"; long view = 1;
        var registry = new McpToolRegistry(maximumResultCharacters: 4096);
        ShadowMcpTools.Register(registry, new(() => editor) { Identity = () => new(editor.Snapshot.DocumentId, workspace, view) });
        async Task<JsonElement> Query(object args)
        {
            var result = await registry.ExecuteAsync("graph_reference_graph", McpJson.Element(args));
            Check(!result.IsError && result.Text.Length <= 4096, "bounded graph receipt");
            return McpJson.Parse(result.Text).GetProperty("data");
        }
        var first = await Query(new { depth = 4, nodeLimit = 1, edgeLimit = 2 });
        var key = first.GetProperty("referenceKey").GetString();
        var nodes = new HashSet<string>(); var edges = new HashSet<string>(); var page = first;
        while (true)
        {
            foreach (var n in page.GetProperty("nodes").GetProperty("items").EnumerateArray()) Check(nodes.Add(n.GetProperty("id").GetString()!), "node pages never repeat");
            foreach (var e in page.GetProperty("edges").GetProperty("items").EnumerateArray()) Check(edges.Add(e.GetRawText()), "edge pages never repeat");
            var nextNode = page.GetProperty("nodes").GetProperty("nextOffset"); var nextEdge = page.GetProperty("edges").GetProperty("nextOffset");
            if (nextNode.ValueKind == JsonValueKind.Null && nextEdge.ValueKind == JsonValueKind.Null) break;
            page = await Query(new { depth = 4, referenceKey = key, nodeLimit = 1, edgeLimit = 2,
                nodeOffset = nextNode.ValueKind == JsonValueKind.Null ? full.Nodes.Length : nextNode.GetInt32(),
                edgeOffset = nextEdge.ValueKind == JsonValueKind.Null ? full.Edges.Length : nextEdge.GetInt32() });
        }
        Check(nodes.Count == full.Nodes.Length && edges.Count == full.Edges.Length, "paged output covers bounded exploration exactly");
        async Task Reject(object args) => Check((await registry.ExecuteAsync("graph_reference_graph", McpJson.Element(args))).IsError, "changed identity/scope rejected");
        await Reject(new { depth = 3, referenceKey = key });
        await Reject(new { depth = 4, roots = new[] { "a" }, referenceKey = key });
        await Reject(new { depth = 4, direction = "incoming", referenceKey = key });
        view++; await Reject(new { depth = 4, referenceKey = key }); view--;
        workspace = "branch"; await Reject(new { depth = 4, referenceKey = key }); workspace = "live";
        editor = new EditorSession(new ShadowMemoryStore(snapshot with { Objects = snapshot.Objects.Remove(new("unrelated")) }));
        await Reject(new { depth = 4, referenceKey = key });
        Check(snapshot.Objects.Count == 4 && editor.State.UndoCount == 0, "reference reads never mutate source/history");

        var leafNodes = Enumerable.Range(0, 301).Select(i => Node("leaf:" + i)).ToArray();
        var many = Node("root", ("items", new ShadowValue.Sequence(leafNodes.Select(n => Ref(n.Id.Value)).ToImmutableArray())));
        editor = new EditorSession(new ShadowMemoryStore(snapshot with { Objects = leafNodes.Append(many).ToImmutableDictionary(n => n.Id) }));
        var nodeIds = new HashSet<string>(); var edgeIds = new HashSet<string>();
        int nodeOffset = 0, edgeOffset = 0, pages = 0; string? adaptiveKey = null;
        while (true)
        {
            var args = new Dictionary<string, object> { ["depth"] = 1, ["maximumNodes"] = 400, ["maximumEdges"] = 500,
                ["nodeOffset"] = nodeOffset, ["edgeOffset"] = edgeOffset };
            if (adaptiveKey is not null) args["referenceKey"] = adaptiveKey;
            var result = await Query(args); adaptiveKey ??= result.GetProperty("referenceKey").GetString();
            var np = result.GetProperty("nodes"); var ep = result.GetProperty("edges");
            foreach (var item in np.GetProperty("items").EnumerateArray()) Check(nodeIds.Add(item.GetProperty("id").GetString()!), "adaptive nodes preserved once");
            foreach (var item in ep.GetProperty("items").EnumerateArray()) Check(edgeIds.Add(item.GetRawText()), "adaptive reference paths preserved once");
            var nn = np.GetProperty("nextOffset"); var en = ep.GetProperty("nextOffset");
            pages++;
            if (nn.ValueKind == JsonValueKind.Null && en.ValueKind == JsonValueKind.Null) break;
            Check(pages < 100, "adaptive cursor progress bounded");
            nodeOffset = nn.ValueKind == JsonValueKind.Null ? 302 : nn.GetInt32();
            edgeOffset = en.ValueKind == JsonValueKind.Null ? 301 : en.GetInt32();
        }
        Check(pages > 1 && nodeIds.Count == 302 && edgeIds.Count == 301, "small-host adaptive pages preserve the complete source graph");

        // A long property path makes the minimum edge page larger than the host budget.
        var large = Node("root", (new string('x', 5000), Ref("a")));
        editor = new EditorSession(new ShadowMemoryStore(snapshot with { Objects = snapshot.Objects.SetItem(large.Id, large) }));
        var tooLarge = await registry.ExecuteAsync("graph_reference_graph", McpJson.EmptyObject);
        Check(tooLarge.IsError && McpJson.Parse(tooLarge.Text).GetProperty("error").GetProperty("code").GetString() == "result_too_large",
            "unrepresentable minimum page is an explicit error");
        using var studio = new Studio(false);
        var response = McpJson.Element(studio.GraphQuery(new("graph_reference_graph", McpJson.EmptyObject)));
        Check(!response.GetProperty("isError").GetBoolean() && response.GetProperty("result").GetProperty("data").GetProperty("roots").GetArrayLength() == 1,
            "existing guarded Studio route exposes reference graph without a second dispatcher");
        Console.WriteLine($"Reference graph verification passed: {checks} checks.");
        return checks;
    }
}
