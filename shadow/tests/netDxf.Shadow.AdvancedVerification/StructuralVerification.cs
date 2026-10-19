using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Studio;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

internal static class StructuralVerification
{
    private static void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch { return; } throw new InvalidOperationException("Expected rejection"); }
    private static ObjectId Id(DxfObject entity) => new("H:" + entity.Handle.ToUpperInvariant());
    private static Dictionary<string, ShadowValue> LineValues(double x) => new()
    {
        ["StartPoint"] = ShadowValue.From(new Vec3(x, 0)), ["EndPoint"] = ShadowValue.From(new Vec3(x + 10, 0))
    };
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("structure/native compound rekey is regenerated once during undo and redo", () =>
        {
            var document = new DxfDocument(); var layer = document.Layers.Add(new netDxf.Tables.Layer("Process"));
            document.DrawingVariables.CLayer = layer.Name;
            var state = new netDxf.Objects.LayerState("Stored", document.Layers) { CurrentLayer = layer.Name }; document.Layers.StateManager.Add(state);
            var property = state.Properties["Process"];
            var source = new ShadowSession(document); var editor = new EditorSession(source);
            var edit = new ShadowGraphEdit(source.Current); edit.Set(Id(layer), "Name", ShadowValue.From("Renamed"));
            editor.Execute("Rename", edit.Build());
            for (var i = 0; i < 3; i++)
            {
                Require(ReferenceEquals(state.Properties["Renamed"], property) && state.CurrentLayer == "Renamed");
                editor.Undo(); Require(ReferenceEquals(state.Properties["Process"], property) && state.CurrentLayer == "Process");
                editor.Redo();
            }
        }));
        tests.Add(("structure/failed membership after compound rename rolls back keys and source identity", () =>
        {
            var document = new DxfDocument(); var layer = document.Layers.Add(new netDxf.Tables.Layer("Process"));
            document.DrawingVariables.CLayer = layer.Name;
            var state = new netDxf.Objects.LayerState("Stored", document.Layers) { CurrentLayer = layer.Name }; document.Layers.StateManager.Add(state);
            var property = state.Properties["Process"];
            var source = new ShadowSession(document); var editor = new EditorSession(source); var before = source.Current;
            var edit = new ShadowGraphEdit(before); edit.Set(Id(layer), "Name", ShadowValue.From("Renamed"));
            var entries = before.Read(Id(state))["Properties"].Items.ToArray();
            var node = entries.Single(p => p["Key"].Text == "Process")["Value"].Object!;
            var invalid = edit.Create(node.TypeName, node.Kind, node.Properties.SetItem("Name", ShadowValue.From("Renamed"))
                .SetItem("LinetypeName", ShadowValue.From("Unavailable")));
            edit.ReplaceSequence(Id(state), "Properties", entries.Where(p => p["Key"].Text != "Process").Select(p => p.Value).Append(
                new ShadowValue.Structure("System.Collections.Generic.KeyValuePair`2", new Dictionary<string, ShadowValue>
                { ["Key"] = ShadowValue.From("Renamed"), ["Value"] = new ShadowValue.Reference(invalid) }.ToImmutableDictionary())));
            Reject(() => editor.Execute("Rejected rename and replacement", edit.Build()));
            Require(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0);
            Require(layer.Name == "Process" && state.CurrentLayer == "Process" && ReferenceEquals(state.Properties["Process"], property));
            // The session must remain usable; a partial rollback would fail-stop it.
            edit = new ShadowGraphEdit(source.Current); edit.Set(Id(layer), "Name", ShadowValue.From("Recovered"));
            editor.Execute("Rename after rejection", edit.Build()); editor.Undo(); Require(layer.Name == "Process");
        }));
        tests.Add(("structure/studio add-delete-undo-redo without history reset", () =>
        {
            using var studio = new Studio();
            JsonElement State(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var state = State(studio.State()); var count = state.GetProperty("occurrences").GetInt32();
            state = State(studio.Add(new(state.GetProperty("revision").GetInt64(), "circle", 210, 0)));
            Require(state.GetProperty("occurrences").GetInt32() == count + 1);
            Require(state.GetProperty("undoCount").GetInt32() == 1);
            state = State(studio.Delete(state.GetProperty("revision").GetInt64()));
            Require(state.GetProperty("occurrences").GetInt32() == count);
            Require(state.GetProperty("undoCount").GetInt32() == 2);
            state = State(studio.History("undo", state.GetProperty("revision").GetInt64()));
            Require(state.GetProperty("occurrences").GetInt32() == count + 1);
            state = State(studio.History("undo", state.GetProperty("revision").GetInt64()));
            Require(state.GetProperty("occurrences").GetInt32() == count);
            state = State(studio.History("redo", state.GetProperty("revision").GetInt64()));
            Require(state.GetProperty("occurrences").GetInt32() == count + 1);
        }));
        tests.Add(("structure/memory create, delete and immutable history", () =>
        {
            var source = new ShadowSession(new DxfDocument()); var initial = source.Current;
            var memory = new ShadowMemoryStore(initial); var editor = new EditorSession(memory);
            var id = editor.CreateEntity("netDxf.Entities.Line", LineValues(10));
            Require(memory.Current.DrawingEntities().Single().Id == id);
            Require(initial.DrawingEntities().Count() == 0);
            editor.DeleteEntities([id]); Require(!memory.Current.Objects.ContainsKey(id));
            editor.Undo(); Require(memory.Current.DrawingEntities().Single().Id == id);
            editor.Undo(); Require(!memory.Current.DrawingEntities().Any());
            editor.Redo(); editor.Redo(); Require(!memory.Current.DrawingEntities().Any());
        }));
        tests.Add(("structure/source create and mixed property-delete history", () =>
        {
            var document = new DxfDocument(); var source = new ShadowSession(document); var editor = new EditorSession(source);
            var original = source.Current;
            var id = editor.CreateEntity("netDxf.Entities.Line", LineValues(1));
            Require(source.Current.DrawingEntities().Single().Id == id);
            var line = document.Entities.Lines.Single();
            editor.SetProperty(id, "StartPoint", ShadowValue.From(new Vec3(4, 0)));
            editor.DeleteEntities([id]); Require(!document.Entities.All.Any());
            editor.Undo(); Require(ReferenceEquals(document.Entities.Lines.Single(), line)); Require(line.StartPoint.X == 4);
            editor.Undo(); Require(line.StartPoint.X == 1);
            editor.Undo(); Require(!document.Entities.All.Any());
            editor.Redo(); editor.Redo(); editor.Redo(); Require(!document.Entities.All.Any());
            Require(original.DrawingEntities().Count() == 0);
        }));
        tests.Add(("structure/source delete existing entities and repeated replay", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0)); document.Entities.Add(line);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var id = Id(line);
            var original = source.Current;
            editor.DeleteEntities([id]); Require(!source.Current.Objects.ContainsKey(id));
            for (var i = 0; i < 5; i++)
            {
                editor.Undo(); Require(source.Current.DrawingEntities().Single().Id == id); Require(ReferenceEquals(document.Entities.Lines.Single(), line));
                editor.Redo(); Require(!document.Entities.All.Any());
            }
            Require(original.DrawingEntities().Count() == 1);
        }));
        tests.Add(("structure/source vertex collection splice and history identity", () =>
        {
            var document = new DxfDocument();
            var polyline = new Polyline2D([new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0)]); document.Entities.Add(polyline);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var id = Id(polyline);
            var vertices = source.Current.Read(id)["Vertexes"].Items.ToArray(); var vertexId = vertices[1].Id!.Value;
            editor.SetProperty(vertexId, "Bulge", ShadowValue.From(0.5));
            editor.ReplaceCollection(id, "Vertexes", [vertices[0].Value, vertices[2].Value]);
            Require(polyline.Vertexes.Count == 2);
            editor.Undo(); Require(polyline.Vertexes.Count == 3); Require(source.Current.Read(id)["Vertexes"].Items.ElementAt(1).Id == vertexId);
            editor.Undo(); Require(polyline.Vertexes[1].Bulge == 0);
            editor.Redo(); editor.Redo(); Require(polyline.Vertexes.Count == 2);
        }));
        tests.Add(("structure/source move entity between block owners", () =>
        {
            var document = new DxfDocument(); var a = new Block("A"); var b = new Block("B");
            var line = new Line(Vector2.Zero, new Vector2(10, 0)); a.Entities.Add(line); document.Blocks.Add(a); document.Blocks.Add(b);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var snapshot = source.Current;
            var change = new ShadowGraphEdit(snapshot); change.ReplaceSequence(Id(a), "Entities", []);
            change.ReplaceSequence(Id(b), "Entities", [new ShadowValue.Reference(Id(line))]);
            editor.Execute("Move owner", change.Build()); Require(a.Entities.Count == 0 && b.Entities.Count == 1);
            editor.Undo(); Require(a.Entities.Count == 1 && b.Entities.Count == 0);
            editor.Redo(); Require(a.Entities.Count == 0 && b.Entities.Count == 1);
        }));
        tests.Add(("structure/invalid graph and duplicate membership are atomic", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0)); document.Entities.Add(line);
            var source = new ShadowSession(document); var before = source.Current;
            var change = new ShadowGraphEdit(before); change.Set(Id(line), "Layer", new ShadowValue.Reference(new ObjectId("missing")));
            Reject(() => source.Apply(change.Build())); Require(ReferenceEquals(before, source.Current));
            change = new ShadowGraphEdit(before); var owner = before.Read(Id(line))["Owner"].Id!.Value;
            change.ReplaceSequence(owner, "Entities", [new ShadowValue.Reference(Id(line)), new ShadowValue.Reference(Id(line))]);
            Reject(() => source.Apply(change.Build())); Require(document.Entities.All.Count() == 1); Require(ReferenceEquals(before, source.Current));
        }));
        tests.Add(("structure/external edit rejects stale history", () =>
        {
            var document = new DxfDocument(); var source = new ShadowSession(document); var editor = new EditorSession(source);
            editor.CreateEntity("netDxf.Entities.Circle", new Dictionary<string, ShadowValue> { ["Radius"] = ShadowValue.From(5) });
            document.Entities.Circles.Single().Radius = 8;
            Reject(() => editor.Undo()); Require(document.Entities.Circles.Single().Radius == 8);
            source.Refresh(); Reject(() => editor.Undo());
        }));
    }
}
