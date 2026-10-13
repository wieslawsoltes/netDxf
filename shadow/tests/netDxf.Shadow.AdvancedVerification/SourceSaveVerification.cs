using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class SourceSaveVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        Add("no-op synchronization retains snapshot and history", () =>
        {
            var (source, editor, id) = Create();
            editor.SetProperty(id, "Radius", ShadowValue.From(9.0));
            var before = source.Current; var history = editor.UndoDescriptions;
            Require(ReferenceEquals(editor.SynchronizeSource(() => source.Current), before));
            Require(editor.UndoDescriptions.SequenceEqual(history) && editor.State.RedoCount == 0);
        });
        Add("real writer normalization is captured before the next edit", () =>
        {
            var (source, editor, id) = Create(); var retained = source.Current;
            var bytes = Save(source, editor);
            Require(bytes.Length > 1000 && source.Current.Revision > retained.Revision);
            Require(editor.State.Revision == source.Current.Revision && editor.State.UndoCount == 0);
            editor.SetProperty(id, "Radius", ShadowValue.From(9.0));
            Require(source.Current.Read(id)["Radius"].Number() == 9 && retained.Read(id)["Radius"].Number() == 5);
        });
        Add("property undo and redo survive repeated saves", () =>
        {
            var (source, editor, id) = Create();
            editor.SetProperty(id, "Radius", ShadowValue.From(9.0));
            Save(source, editor); Save(source, editor);
            Require(editor.State.UndoCount == 1 && editor.State.RedoCount == 0);
            editor.Undo(); Require(source.Current.Read(id)["Radius"].Number() == 5);
            Save(source, editor); Require(editor.State.RedoCount == 1);
            editor.Redo(); Require(source.Current.Read(id)["Radius"].Number() == 9);
            var data = Save(source, editor);
            using var stream = new MemoryStream(data);
            Require(DxfDocument.Load(stream)!.Entities.Circles.Single().Radius == 9);
        });
        Add("structural creation undo and redo survive saves", () =>
        {
            var (source, editor, _) = Create();
            editor.CreateEntity("netDxf.Entities.Line", new Dictionary<string, ShadowValue>
            { ["StartPoint"] = ShadowValue.From(new Vec3(0, 0)), ["EndPoint"] = ShadowValue.From(new Vec3(10, 0)) });
            Save(source, editor); Require(source.ReadSource(d => d.Entities.Lines.Count()) == 1);
            Require(source.Current.Objects.Values.Count(n => n.Type == "Line") == 1);
            editor.Undo(); Require(source.ReadSource(d => d.Entities.Lines.Count()) == 0);
            Save(source, editor); editor.Redo(); Require(source.ReadSource(d => d.Entities.Lines.Count()) == 1);
            using var stream = new MemoryStream(Save(source, editor));
            Require(DxfDocument.Load(stream)!.Entities.Lines.Count() == 1);
        });
        Add("structural deletion undo and redo survive saves", () =>
        {
            var (source, editor, id) = Create();
            editor.DeleteEntities([id]); Save(source, editor);
            Require(!source.Current.Objects.ContainsKey(id) && source.ReadSource(d => d.Entities.Circles.Count()) == 0);
            editor.Undo(); Require(source.Current.Read(id)["Radius"].Number() == 5);
            Save(source, editor); editor.Redo(); Require(!source.Current.Objects.ContainsKey(id));
        });
        Add("writer normalization after a branch merge preserves one merge command", () =>
        {
            var (source, editor, id) = Create(); var ancestor = source.Current;
            var branch = new EditorSession(new ShadowMemoryStore(ancestor));
            branch.SetProperty(id, "Radius", ShadowValue.From(11.0));
            editor.ApplyMerge(editor.PreviewMerge(ancestor, branch.Snapshot));
            Save(source, editor); Require(editor.State.UndoCount == 1);
            editor.Undo(); Require(source.Current.Read(id)["Radius"].Number() == 5);
            Save(source, editor); editor.Redo(); Require(source.Current.Read(id)["Radius"].Number() == 11);
        });
        Add("failed writer callback reconciles and rethrows the original exception", () =>
        {
            var (source, editor, id) = Create();
            editor.SetProperty(id, "Radius", ShadowValue.From(8.0)); var before = source.Current;
            var failure = new IOException("synthetic post-save failure");
            var seen = Throws<IOException>(() => editor.SynchronizeSource(() => source.MutateSource(document =>
            {
                using var stream = new MemoryStream(); Require(document.Save(stream)); throw failure;
            })));
            Require(ReferenceEquals(seen, failure) && source.Current.Revision > before.Revision);
            Require(editor.State.Revision == source.Current.Revision && editor.State.UndoCount == 1);
            editor.Undo(); Require(source.Current.Read(id)["Radius"].Number() == 5);
        });
        Add("failed callback without mutation leaves history and revision unchanged", () =>
        {
            var (source, editor, id) = Create(); editor.SetProperty(id, "Radius", ShadowValue.From(8.0));
            var before = source.Current;
            Throws<IOException>(() => editor.SynchronizeSource(() => throw new IOException("fixture")));
            Require(ReferenceEquals(source.Current, before) && editor.State.UndoCount == 1);
            editor.Undo(); Require(source.Current.Read(id)["Radius"].Number() == 5);
        });
        Add("arbitrary prior source changes are not silently accepted", () =>
        {
            var (source, editor, _) = Create(); source.MutateSource(d => d.Entities.Circles.Single().Radius = 12);
            var called = false;
            Throws<ShadowConflictException>(() => editor.SynchronizeSource(() => { called = true; return source.Current; }));
            Require(!called);
        });
        Add("same-valued copied snapshot cannot be the callback receipt", () =>
        {
            var (source, editor, id) = Create();
            Throws<InvalidOperationException>(() => editor.SynchronizeSource(() => source.Current with { }));
            Require(editor.State.Revision == source.Current.Revision);
            editor.SetProperty(id, "Radius", ShadowValue.From(7.0)); Require(editor.State.UndoCount == 1);
        });
        foreach (var operation in new[] { "property", "undo", "redo", "reset", "recursive", "merge" })
            Add("reentrant transaction rejected: " + operation, () =>
            {
                var (source, editor, id) = Create(); var before = source.Current;
                Throws<InvalidOperationException>(() => editor.SynchronizeSource(() =>
                {
                    switch (operation)
                    {
                        case "property": editor.SetProperty(id, "Radius", ShadowValue.From(1.0)); break;
                        case "undo": editor.Undo(); break;
                        case "redo": editor.Redo(); break;
                        case "reset": editor.ResetHistory(); break;
                        case "recursive": editor.SynchronizeSource(() => source.Current); break;
                        case "merge": editor.PreviewMerge(before, before); break;
                    }
                    return source.Current;
                }));
                Require(ReferenceEquals(source.Current, before) && editor.State.UndoCount == 0);
                editor.SetProperty(id, "Radius", ShadowValue.From(6.0));
            });
        Add("source replacement clears stale history", () =>
        {
            var (source, _, _) = Create(); var store = new ReplacingStore(source.Current); var editor = new EditorSession(store);
            editor.Select(["selected"]);
            Throws<ShadowConflictException>(() => editor.SynchronizeSource(() => store.Current = store.Current with { DocumentId = Guid.NewGuid() }));
            Require(editor.State.Selection.IsEmpty && editor.State.UndoCount == 0);
        });
        Add("Studio save publishes canonical invalidation and preserves undo", () =>
        {
            using var studio = new Studio(); using var events = studio.Subscribe(); events.Reader.TryRead(out _);
            studio.Add(new(0, "line", 200, 200)); events.Reader.TryRead(out _);
            var before = State(studio); var bytes = studio.Save(before.GetProperty("revision").GetInt64()); var after = State(studio);
            Require(bytes.Length > 1000 && after.GetProperty("undoCount").GetInt32() == 1);
            Require(events.Reader.TryRead(out var notice) && notice!.Revision == after.GetProperty("revision").GetInt64());
            studio.History("undo", after.GetProperty("revision").GetInt64());
            Require(State(studio).GetProperty("entities").GetInt32() == before.GetProperty("entities").GetInt32() - 1);
            Throws<ShadowConflictException>(() => studio.Save(0));
        });
        Add("Studio save expires a preview but leaves its branch available", () =>
        {
            using var studio = new Studio(); studio.Fork(new(0, "Save boundary"));
            var branch = State(studio).GetProperty("workspaceId").GetString()!; studio.Add(new(0, "line", 200, 200));
            var doc = State(studio).GetProperty("documentId").GetGuid();
            var plan = JsonSerializer.SerializeToElement(studio.PreviewMerge(new(doc, branch, 0, 1)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            studio.SwitchWorkspace(new(1, "live")); studio.Save();
            Throws<ShadowConflictException>(() => studio.ApplyMerge(new(doc, plan.GetProperty("planId").GetString()!, 0, 1)));
            var current = State(studio).GetProperty("revision").GetInt64();
            var fresh = JsonSerializer.SerializeToElement(studio.PreviewMerge(new(doc, branch, current, 1)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Require(fresh.GetProperty("canApply").GetBoolean());
            studio.ApplyMerge(new(doc, fresh.GetProperty("planId").GetString()!, current, 1));
            Require(State(studio).GetProperty("undoCount").GetInt32() == 1);
        });
        void Add(string name, Action action) => tests.Add(("source-save/" + name, action));
    }

    private static (ShadowSession Source, EditorSession Editor, ObjectId Circle) Create()
    {
        var document = new DxfDocument(); var circle = new Circle(new Vector2(0, 0), 5); document.Entities.Add(circle);
        var source = new ShadowSession(document); return (source, new(source), new("H:" + circle.Handle.ToUpperInvariant()));
    }
    private static byte[] Save(ShadowSession source, EditorSession editor)
    {
        using var stream = new MemoryStream(); editor.SynchronizeSource(() => source.MutateSource(d => Require(d.Save(stream)))); return stream.ToArray();
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private sealed class ReplacingStore(ShadowSnapshot snapshot) : IShadowStore
    {
        public ShadowSnapshot Current { get; set; } = snapshot;
        public ShadowSnapshot Apply(ShadowEditBatch batch) => throw new NotSupportedException();
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Source save contract failed."); }
    private static T Throws<T>(Action action) where T : Exception { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
