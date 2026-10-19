using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;

internal static class DrawingVerification
{
    internal static async Task Run(Action<bool, string> assert)
    {
        var document = new DxfDocument();
        var source = new ShadowSession(document);
        var editor = new EditorSession(source);
        var changes = 0;
        var registry = ShadowMcpTools.Create(new(() => editor, () => source) { Changed = () => changes++ });
        async Task<McpToolResult> Call(string name, object input, bool mutation = true)
        {
            var values = McpJson.Element(input).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            if (mutation) values["revision"] = editor.Snapshot.Revision;
            return await registry.ExecuteAsync(name, McpJson.Element(values));
        }
        async Task<JsonElement> Success(string name, object input, bool mutation = true)
        {
            var result = await Call(name, input, mutation);
            assert(!result.IsError, name + ": " + result.Text);
            return McpJson.Parse(result.Text).GetProperty("data");
        }
        object Vertex(double x, double y) => new { Position = new { X = x, Y = y } };
        var initial = source.Current;
        var preview = await Success("shadow_create_entities", new
        {
            preview = true, type = "netDxf.Entities.Line", entities = new[] { new { properties = new { EndPoint = new { X = 9, Y = 0, Z = 0 } } } }
        });
        assert(ReferenceEquals(source.Current, initial) && !document.Entities.All.Any() && editor.State.UndoCount == 0 && changes == 0 && preview.GetProperty("preview").GetBoolean(),
            "Rapid creation preview mutated source, history or observers.");
        var created = await Success("shadow_create_entities", new
        {
            resources = new[] { new { type = "Layer", alias = "@layer", properties = new { Name = "MCP drawing" } } },
            properties = new { Layer = new Dictionary<string, string> { ["$ref"] = "@layer" } },
            entities = new object[]
            {
                new { type = "netDxf.Entities.Line", alias = "@line", properties = new { StartPoint = new { X = 1, Y = 2, Z = 0 }, EndPoint = new { X = 20, Y = 30, Z = 0 } } },
                new { type = "netDxf.Entities.Polyline2D", alias = "@polyline", properties = new { Vertexes = new[] { Vertex(0, 0), Vertex(10, 0), Vertex(10, 10) }, IsClosed = true } },
                new { type = "netDxf.Entities.Ellipse", alias = "@ellipse", properties = new { MajorAxis = 20, MinorAxis = 10 } }
            }
        });
        assert(source.Current.Revision == initial.Revision + 1 && editor.State.UndoCount == 1 && changes == 1 && document.Entities.All.Count() == 3,
            "Rapid mixed entity creation did not publish exactly one source revision/history entry/notification.");
        var aliases = created.GetProperty("aliases");
        var polylineId = aliases.GetProperty("@polyline").GetString()!;
        var ellipseId = aliases.GetProperty("@ellipse").GetString()!;
        var polyline = document.Entities.Polylines2D.Single();
        var ellipse = document.Entities.Ellipses.Single();
        assert(polyline.Vertexes.Count == 3 && polyline.IsClosed && document.Entities.All.All(e => e.Layer.Name == "MCP drawing"),
            "Inline vertices or shared default layer references failed.");
        var originalVertex = polyline.Vertexes[0];
        var beforeSplice = source.Current.Revision;
        await Success("shadow_collection_edit", new
        {
            edits = new object[]
            {
                new { id = polylineId, path = "/Vertexes", index = 1, deleteCount = 1, items = new[] { Vertex(5, 5), Vertex(6, 6) } },
                new { id = polylineId, path = "/Vertexes", index = 4, items = new[] { Vertex(0, 10) } }
            }
        });
        assert(polyline.Vertexes.Count == 5 && polyline.Vertexes[1].Position == new Vector2(5, 5) && ReferenceEquals(polyline.Vertexes[0], originalVertex)
            && source.Current.Revision == beforeSplice + 1 && editor.State.UndoCount == 2, "Ordered collection slices lost members, identities or atomicity.");
        await Success("shadow_undo", new { });
        assert(polyline.Vertexes.Count == 3 && polyline.Vertexes[1].Position == new Vector2(10, 0), "Collection undo failed to restore owned vertices.");
        await Success("shadow_redo", new { });
        assert(polyline.Vertexes.Count == 5, "Collection redo failed to restore inserted owned vertices.");
        var beforeFailure = source.Current; var beforeHistory = editor.State.UndoCount;
        var invalidSplice = await Call("shadow_collection_edit", new
        {
            edits = new object[]
            {
                new { id = polylineId, path = "/Vertexes", index = 0, deleteCount = 1 },
                new { id = polylineId, path = "/Vertexes", index = 999, deleteCount = 0 }
            }
        });
        assert(invalidSplice.IsError && ReferenceEquals(source.Current, beforeFailure) && polyline.Vertexes.Count == 5 && editor.State.UndoCount == beforeHistory,
            "Late invalid collection slice partially committed earlier changes.");
        var invalidCreate = await Call("shadow_create_entities", new
        {
            entities = new object[] { new { type = "netDxf.Entities.Line", properties = new { } }, new { type = "netDxf.Entities.Circle", properties = new { Radius = -1 } } }
        });
        assert(invalidCreate.IsError && ReferenceEquals(source.Current, beforeFailure) && document.Entities.All.Count() == 3 && editor.State.UndoCount == beforeHistory,
            "Native validation of the last created entity failed to roll back the whole batch.");
        var unregistered = await Call("shadow_create_entities", new
        {
            resources = new[] { new { type = "Layer", alias = "@unused", properties = new { Name = "Unused" } } },
            entities = new[] { new { type = "netDxf.Entities.Line" } }
        });
        assert(unregistered.IsError && ReferenceEquals(source.Current, beforeFailure) && !document.Layers.Contains("Unused"),
            "An unattached resource was silently accepted or left partial source state.");
        var methods = await Success("dxf_methods", new { type = "netDxf.Entities.Ellipse", callableOnly = true }, false);
        var method = methods.GetProperty("methods").GetProperty("items").EnumerateArray().Single();
        assert(method.GetProperty("name").GetString() == "SetAxis" && method.GetProperty("parameters").GetArrayLength() == 2 && method.GetProperty("callable").GetBoolean(),
            "Audited public method was absent from discoverable signatures.");
        var unsupportedMethods = await Success("dxf_methods", new { type = "DxfDocument", search = "Save", limit = 1 }, false);
        assert(!unsupportedMethods.GetProperty("methods").GetProperty("items")[0].GetProperty("callable").GetBoolean()
            && unsupportedMethods.GetProperty("methods").GetProperty("nextOffset").ValueKind != JsonValueKind.Null, "Unsupported method overloads were hidden, callable, or unpaged.");
        await Success("dxf_method_call", new { calls = new[] { new { id = ellipseId, method = "SetAxis", arguments = new { axis1 = 7, axis2 = 30 } } } });
        assert(ellipse.MajorAxis == 30 && ellipse.MinorAxis == 7 && editor.State.UndoCount == beforeHistory + 1, "Audited SetAxis did not normalize or create one undoable change.");
        await Success("shadow_undo", new { });
        assert(ellipse.MajorAxis == 20 && ellipse.MinorAxis == 10, "Audited public method undo did not restore both axes.");
        beforeFailure = source.Current;
        var unsupported = await Call("dxf_method_call", new
        {
            calls = new[]
            {
                new { id = ellipseId, method = "SetAxis", arguments = new { axis1 = 50, axis2 = 20 } },
                new { id = ellipseId, method = "TransformBy", arguments = new { axis1 = 1, axis2 = 2 } }
            }
        });
        assert(unsupported.IsError && ReferenceEquals(source.Current, beforeFailure) && ellipse.MajorAxis == 20,
            "Unsupported method partially committed a preceding audited call.");
        var polymorphic = await Success("shadow_graph_edit", new
        {
            operations = new[] { new { op = "addEntity", type = "netDxf.Entities.Hatch", alias = "@hatch", properties = new
            {
                Pattern = new { Name = "SOLID" }, Associative = false, BoundaryPaths = new[] { new { Edges = new object[]
                {
                    new Dictionary<string, object> { ["$type"] = "netDxf.Entities.HatchBoundaryPath+Line", ["Start"] = new { X = 0, Y = 0 }, ["End"] = new { X = 1, Y = 0 } },
                    new Dictionary<string, object> { ["$type"] = "netDxf.Entities.HatchBoundaryPath+Line", ["Start"] = new { X = 1, Y = 0 }, ["End"] = new { X = 0, Y = 1 } },
                    new Dictionary<string, object> { ["$type"] = "netDxf.Entities.HatchBoundaryPath+Line", ["Start"] = new { X = 0, Y = 1 }, ["End"] = new { X = 0, Y = 0 } }
                } } }
            } } }
        });
        assert(document.Entities.Hatches.Single().BoundaryPaths.Single().Edges.Count == 3, "Inline abstract hatch edges were not constructed from assignable $type declarations.");
        beforeFailure = source.Current;
        var invalidSubtype = await Call("shadow_create_entities", new
        {
            entities = new[] { new { type = "netDxf.Entities.Line", properties = new { Layer = new Dictionary<string, object> { ["$type"] = "netDxf.Entities.Circle", ["Radius"] = 2 } } } }
        });
        assert(invalidSubtype.IsError && ReferenceEquals(source.Current, beforeFailure), "An incompatible inline subtype was accepted.");
        var countBeforeBulk = document.Entities.All.Count(); var revisionBeforeBulk = source.Current.Revision;
        await Success("shadow_create_entities", new
        {
            type = "netDxf.Entities.Circle", properties = new { Radius = 1 }, entities = Enumerable.Range(0, 300)
                .Select(i => new { properties = new { Center = new { X = i * 3, Y = 100, Z = 0 } } }).ToArray()
        });
        assert(document.Entities.Circles.Count() == 300 && source.Current.Revision == revisionBeforeBulk + 1,
            "Rapid creation above the old 256-operation ceiling was not one native transaction.");
        await Success("shadow_undo", new { });
        assert(document.Entities.All.Count() == countBeforeBulk, "Undo did not remove the entire bulk creation transaction.");
        await Success("shadow_redo", new { });
        using var stream = new MemoryStream();
        assert(document.Save(stream), "Created drawing could not be saved as DXF.");
        stream.Position = 0;
        var loaded = DxfDocument.Load(stream);
        assert(loaded is not null && loaded.Entities.Circles.Count() == 300 && loaded.Entities.Polylines2D.Single().Vertexes.Count == 5 && loaded.Entities.Hatches.Any(),
            "Rapidly constructed drawing failed DXF save/reload fidelity.");
        // DXF serialization may normalize document bookkeeping outside the editor.
        source = new ShadowSession(document);
        var branch = new EditorSession(new ShadowMemoryStore(source.Current));
        editor = branch;
        var sourceBeforeBranch = source.Current;
        await Success("shadow_create_entities", new { type = "netDxf.Entities.Circle", entities = new[] { new { properties = new { Radius = 2 } } } });
        assert(ReferenceEquals(source.Current, sourceBeforeBranch) && editor.Snapshot.EntityIds.Count == source.Current.EntityIds.Count + 1,
            "Rapid creation in an isolated editor branch changed the live source.");
        var stale = await registry.ExecuteAsync("shadow_create_entities", McpJson.Element(new { revision = sourceBeforeBranch.Revision, type = "netDxf.Entities.Circle", entities = new[] { new { } } }));
        assert(stale.IsError, "Rapid mutation accepted a stale revision.");
        var limited = await Call("shadow_create_entities", new { type = "netDxf.Entities.Line", entities = Enumerable.Range(0, 1025).Select(_ => new { }).ToArray() });
        assert(limited.IsError, "Rapid creation accepted more than its documented entity limit.");
    }
}
