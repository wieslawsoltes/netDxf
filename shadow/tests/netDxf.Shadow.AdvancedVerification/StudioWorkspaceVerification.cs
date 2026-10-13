using System.Text.Json;
using netDxf.Shadow;
using netDxf.Shadow.Studio;

internal static class StudioWorkspaceVerification
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        Add("fork isolates source and shares initial revision", studio =>
        {
            var before = State(studio); var id = Line(before); var x = Coordinate(studio, id, "EndPoint", "X");
            var branch = Json(studio.Fork(new(Revision(before), "Experiment")));
            Require(Revision(branch) == Revision(before) && Text(branch, "workspaceId") != "live");
            Set(studio, id, "EndPoint", new { X = x + 15 });
            Require(Coordinate(studio, id, "EndPoint", "X") == x + 15);
            studio.SwitchWorkspace(new(Revision(State(studio)), "live"));
            Require(Coordinate(studio, id, "EndPoint", "X") == x && Revision(State(studio)) == Revision(before));
        });
        Add("branch uses common graph renderer and undo", studio =>
        {
            studio.Fork(new(0, "Geometry")); var state = State(studio); var count = state.GetProperty("entities").GetInt32();
            studio.Add(new(Revision(state), "circle", 220, 100));
            Require(State(studio).GetProperty("entities").GetInt32() == count + 1);
            Require(Json(studio.Graph(default)).GetProperty("edges").GetArrayLength() > 0);
            Require(studio.Render(640, 480, null, null, null, "skia", null, default).Bytes.Length > 1000);
            Require(studio.Render(640, 480, null, null, null, "svg", null, default).Bytes.Length > 1000);
            studio.History("undo", Revision(State(studio))); Require(State(studio).GetProperty("entities").GetInt32() == count);
            studio.History("redo", Revision(State(studio))); Require(State(studio).GetProperty("entities").GetInt32() == count + 1);
        });
        Add("layouts and selections belong to their workspace", studio =>
        {
            var state = State(studio); var key = state.GetProperty("items")[0].GetProperty("key").GetString()!;
            studio.Select(new(0, key)); studio.Fork(new(0, "Layout")); studio.SetLayout(new(0, "Hatch islands"));
            var id = Text(State(studio), "workspaceId");
            studio.SwitchWorkspace(new(0, "live")); state = State(studio);
            Require(Text(state, "layout") == "Model" && state.GetProperty("selection")[0].GetString() == key);
            studio.SwitchWorkspace(new(0, id)); Require(Text(State(studio), "layout") == "Hatch islands");
        });
        Add("branch history cannot undo the source editor", studio =>
        {
            studio.Add(new(0, "line", 200, 100)); var liveRevision = Revision(State(studio));
            studio.Fork(new(liveRevision, "History"));
            Require(State(studio).GetProperty("undoCount").GetInt32() == 0);
            studio.History("undo", liveRevision); Require(Revision(State(studio)) == liveRevision);
            studio.SwitchWorkspace(new(liveRevision, "live")); Require(State(studio).GetProperty("undoCount").GetInt32() == 1);
        });
        Add("retained snapshots are scoped to their workspace", studio =>
        {
            studio.Retain(0); studio.Fork(new(0, "Retained"));
            Require(State(studio).GetProperty("snapshots").GetArrayLength() == 0);
            Throws<ShadowConflictException>(() => studio.Render(320, 240, null, null, null, "skia", 0, default));
            studio.Retain(0); var image = studio.Render(320, 240, null, null, null, "skia", 0, default);
            studio.Add(new(0, "line", 200, 200));
            Require(studio.Render(320, 240, null, null, null, "skia", 0, default).Bytes.SequenceEqual(image.Bytes));
            studio.SwitchWorkspace(new(1, "live")); Require(State(studio).GetProperty("snapshots").GetArrayLength() == 1);
        });
        Add("merge independently edited coordinates into one canonical command", studio =>
        {
            var initial = State(studio); var line = Line(initial); var x = Coordinate(studio, line, "StartPoint", "X"); var y = Coordinate(studio, line, "EndPoint", "Y");
            studio.Fork(new(0, "Merge")); var branchId = Text(State(studio), "workspaceId");
            Set(studio, line, "EndPoint", new { Y = y + 20 }); var incoming = Revision(State(studio));
            studio.SwitchWorkspace(new(incoming, "live")); Set(studio, line, "StartPoint", new { X = x - 20 });
            var plan = Preview(studio, branchId, incoming);
            Require(plan.GetProperty("canApply").GetBoolean() && plan.GetProperty("changed").GetInt32() == 1);
            var preview = studio.Render(640, 480, null, null, null, "skia", null, default, Text(plan, "planId"));
            Require(preview.Bytes.Length > 1000);
            Apply(studio, plan); var live = State(studio);
            Require(Text(live, "workspaceId") == "live" && live.GetProperty("undoCount").GetInt32() == 2);
            Require(Coordinate(studio, line, "StartPoint", "X") == x - 20 && Coordinate(studio, line, "EndPoint", "Y") == y + 20);
            studio.History("undo", Revision(live)); Require(Coordinate(studio, line, "EndPoint", "Y") == y);
            studio.History("redo", Revision(State(studio))); Require(Coordinate(studio, line, "EndPoint", "Y") == y + 20);
            using var file = new MemoryStream(studio.Save()); var reloaded = new Studio();
            try { reloaded.Load(file); Require(Coordinate(reloaded, line, "EndPoint", "Y") == y + 20); } finally { reloaded.Dispose(); }
        });
        Add("merged branch rebases onto canonical source state", studio =>
        {
            studio.Fork(new(0, "Adoption")); var id = Text(State(studio), "workspaceId"); studio.Add(new(0, "line", 220, 100));
            Apply(studio, Preview(studio, id, 1)); var source = State(studio); var canonical = Revision(source);
            studio.SwitchWorkspace(new(canonical, id)); var branch = State(studio);
            Require(Revision(branch) == canonical && branch.GetProperty("ancestorRevision").GetInt64() == canonical);
            Require(branch.GetProperty("undoCount").GetInt32() == 0 && branch.GetProperty("entities").GetInt32() == source.GetProperty("entities").GetInt32());
            var noOp = Preview(studio, id, canonical);
            Require(noOp.GetProperty("canApply").GetBoolean() && noOp.GetProperty("changed").GetInt32() == 0 && noOp.GetProperty("added").GetInt32() == 0);
        });
        foreach (var choice in new[] { "Unresolved", "Target", "Incoming" })
            Add("explicit conflict policy " + choice, studio =>
            {
                var line = Line(State(studio)); studio.Fork(new(0, "Conflict")); var id = Text(State(studio), "workspaceId");
                Set(studio, line, "EndPoint", new { Y = 15 }); studio.SwitchWorkspace(new(1, "live")); Set(studio, line, "EndPoint", new { Y = 30 });
                var plan = Preview(studio, id, 1, choice);
                Require(plan.GetProperty("conflictCount").GetInt32() == 1);
                Require(plan.GetProperty("conflicts")[0].GetProperty("path")[1].GetString() == "Y");
                Require(plan.GetProperty("canApply").GetBoolean() == (choice != "Unresolved"));
                if (choice == "Unresolved") Require(plan.GetProperty("planId").ValueKind == JsonValueKind.Null);
                else { Apply(studio, plan); Require(Coordinate(studio, line, "EndPoint", "Y") == (choice == "Incoming" ? 15 : 30)); }
            });
        Add("branch change invalidates preview rendering and apply", studio =>
        {
            studio.Fork(new(0, "Stale")); var id = Text(State(studio), "workspaceId"); studio.Add(new(0, "line", 220, 0));
            var plan = Preview(studio, id, 1); studio.Add(new(1, "circle", 220, 20));
            Throws<ShadowConflictException>(() => Apply(studio, plan));
            Throws<ShadowConflictException>(() => studio.Render(320, 240, null, null, null, "skia", null, default, Text(plan, "planId")));
            Require(State(studio).GetProperty("sourceRevision").GetInt64() == 0);
        });
        Add("live change invalidates exact target even after undo", studio =>
        {
            studio.Fork(new(0, "Target")); var id = Text(State(studio), "workspaceId"); studio.Add(new(0, "line", 220, 0));
            var plan = Preview(studio, id, 1); studio.SwitchWorkspace(new(1, "live")); studio.Add(new(0, "circle", 220, 20)); studio.History("undo", 1);
            Throws<ShadowConflictException>(() => Apply(studio, plan));
        });
        Add("new preview expires the previous token", studio =>
        {
            studio.Fork(new(0, "Token")); var id = Text(State(studio), "workspaceId");
            var old = Preview(studio, id, 0); var next = Preview(studio, id, 0);
            Throws<ShadowConflictException>(() => Apply(studio, old)); Apply(studio, next);
        });
        Add("source validation rejects an invalid branch without losing it", studio =>
        {
            var circle = State(studio).GetProperty("items").EnumerateArray().First(i => Text(i, "type") == "Circle").GetProperty("id").GetString()!;
            studio.Fork(new(0, "Invalid radius")); var id = Text(State(studio), "workspaceId"); Set(studio, circle, "Radius", "-1");
            var plan = Preview(studio, id, 1); Require(plan.GetProperty("canApply").GetBoolean());
            Throws<Exception>(() => Apply(studio, plan));
            Require(Revision(State(studio)) == 1 && Text(State(studio), "workspaceId") == id);
            studio.SwitchWorkspace(new(1, "live")); Require(Revision(State(studio)) == 0 && State(studio).GetProperty("undoCount").GetInt32() == 0);
        });
        Add("document replacement expires branches plans and request identities", studio =>
        {
            var saved = studio.Save(); var initial = State(studio); var identity = Identity(initial);
            studio.Fork(new(0, "Replace")); var id = Text(State(studio), "workspaceId"); var plan = Preview(studio, id, 0);
            studio.Load(new MemoryStream(saved));
            Require(State(studio).GetProperty("branches").GetArrayLength() == 0 && Guid.Parse(Text(State(studio), "documentId")) != identity.DocumentId);
            Throws<ShadowConflictException>(() => Apply(studio, plan));
            Throws<ShadowConflictException>(() => studio.ExecuteRequest(identity, () => studio.Add(new(0, "line", 0, 0))));
        });
        Add("equal-revision different workspace cannot accept stale requests", studio =>
        {
            var live = Identity(State(studio)); studio.Fork(new(0, "Identity"));
            Throws<ShadowConflictException>(() => studio.ExecuteRequest(live, () => studio.Add(new(0, "line", 0, 0))));
        });
        Add("switch away and back invalidates the old workspace epoch", studio =>
        {
            studio.Fork(new(0, "Epoch")); var identity = Identity(State(studio));
            studio.SwitchWorkspace(new(0, "live")); studio.SwitchWorkspace(new(0, identity.WorkspaceId));
            Throws<ShadowConflictException>(() => studio.ExecuteRequest(identity, () => studio.Add(new(0, "line", 0, 0))));
        });
        Add("valid request identity executes under the source gate", studio =>
        {
            studio.ExecuteRequest(Identity(State(studio)), () => studio.Add(new(0, "line", 0, 0)));
            Require(Revision(State(studio)) == 1);
        });
        Add("branch source mutation and DXF save are explicit refusals", studio =>
        {
            studio.Fork(new(0, "No source writes"));
            Throws<InvalidOperationException>(() => studio.Save()); Throws<InvalidOperationException>(() => studio.SourceDemo(0));
        });
        Add("branch discard never mutates the source", studio =>
        {
            studio.Fork(new(0, "Discard")); var id = Text(State(studio), "workspaceId"); studio.Add(new(0, "line", 0, 0));
            studio.DiscardBranch(new(1, id, 1)); var state = State(studio);
            Require(Text(state, "workspaceId") == "live" && Revision(state) == 0 && state.GetProperty("branches").GetArrayLength() == 0);
        });
        Add("stale branch deletion rejected", studio =>
        {
            studio.Fork(new(0, "Delete revision")); var id = Text(State(studio), "workspaceId"); studio.Add(new(0, "line", 0, 0));
            Throws<ShadowConflictException>(() => studio.DiscardBranch(new(1, id, 0)));
        });
        foreach (var name in new[] { "", " ", "unsafe\nname", new string('x', 81) })
            Add("invalid name length " + name.Length, studio => Throws<ArgumentException>(() => studio.Fork(new(0, name))));
        Add("duplicate names and fork-from-branch are refused", studio =>
        {
            studio.Fork(new(0, "Same")); Throws<InvalidOperationException>(() => studio.Fork(new(0, "Nested")));
            studio.SwitchWorkspace(new(0, "live")); Throws<ArgumentException>(() => studio.Fork(new(0, "same")));
        });
        Add("branch budget", studio =>
        {
            for (var i = 0; i < 8; i++) { studio.Fork(new(0, "Branch " + i)); studio.SwitchWorkspace(new(0, "live")); }
            Throws<InvalidOperationException>(() => studio.Fork(new(0, "Overflow")));
        });
        Add("merge policy must be recognized", studio =>
        {
            studio.Fork(new(0, "Policy")); var id = Text(State(studio), "workspaceId");
            Throws<ArgumentException>(() => Preview(studio, id, 0, "999"));
        });
        Add("retained snapshot and merge preview cannot be combined", studio =>
        {
            studio.Fork(new(0, "Views")); var id = Text(State(studio), "workspaceId"); studio.Retain(0); var plan = Preview(studio, id, 0);
            Throws<ArgumentException>(() => studio.Render(320, 240, null, null, null, "svg", 0, default, Text(plan, "planId")));
        });
        Add("feed starts with coherent current identities", studio =>
        {
            using var sub = studio.Subscribe(); Require(sub.Reader.TryRead(out var first)); var state = State(studio);
            Require(first!.DocumentId == Guid.Parse(Text(state, "documentId")) && first.WorkspaceId == "live" && first.Reset);
            studio.Fork(new(0, "Event")); Require(sub.Reader.TryRead(out var next));
            Require(next!.ChangeVersion > first.ChangeVersion && next.WorkspaceId == Text(State(studio), "workspaceId"));
        });
        Add("feed coalesces slow readers without queuing snapshots", studio =>
        {
            using var sub = studio.Subscribe();
            for (var i = 0; i < 20; i++) studio.Retain(0);
            Require(sub.Reader.TryRead(out var latest) && latest!.ChangeVersion == State(studio).GetProperty("changeVersion").GetInt64());
            Require(!sub.Reader.TryRead(out _));
        });
        Add("feed reconnect distinguishes exact and stale cursors", studio =>
        {
            using var first = studio.Subscribe(); Require(first.Reader.TryRead(out var notice));
            using var same = studio.Subscribe(notice!.EventId); Require(same.Reader.TryRead(out var current) && !current!.Reset);
            studio.Add(new(0, "line", 0, 0));
            using var stale = studio.Subscribe(notice.EventId); Require(stale.Reader.TryRead(out var reset) && reset!.Reset && reset.Revision == 1);
        });
        Add("feed reports document replacement even when revisions coincide", studio =>
        {
            var bytes = studio.Save(); using var sub = studio.Subscribe(); Require(sub.Reader.TryRead(out var first));
            studio.Load(new MemoryStream(bytes)); Require(sub.Reader.TryRead(out var next));
            Require(next!.Revision == first!.Revision && next.DocumentId != first.DocumentId && next.ChangeVersion > first.ChangeVersion);
        });
        Add("subscription disposal unregisters and completes", studio =>
        {
            var sub = studio.Subscribe(); Require(studio.ActiveSubscriptions == 1); sub.Reader.TryRead(out _); sub.Dispose(); sub.Dispose();
            Require(studio.ActiveSubscriptions == 0 && sub.Reader.Completion.IsCompleted);
        });
        Add("subscription budget does not leak failed registrations", studio =>
        {
            var subscriptions = Enumerable.Range(0, 32).Select(_ => studio.Subscribe()).ToArray();
            try { Throws<InvalidOperationException>(() => studio.Subscribe()); Require(studio.ActiveSubscriptions == 32); }
            finally { foreach (var item in subscriptions) item.Dispose(); }
            Require(studio.ActiveSubscriptions == 0);
        });
        Add("disposed host completes subscriptions and rejects new ones", studio =>
        {
            using var sub = studio.Subscribe(); sub.Reader.TryRead(out _); studio.Dispose();
            Require(sub.Reader.Completion.IsCompleted && studio.ActiveSubscriptions == 0);
            Throws<ObjectDisposedException>(() => studio.Subscribe());
        });
        void Add(string name, Action<Studio> action) => tests.Add(("workspace/" + name, () => { using var studio = new Studio(); action(studio); }));
    }
    private static JsonElement State(Studio studio) => Json(studio.State());
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Web);
    private static long Revision(JsonElement state) => state.GetProperty("revision").GetInt64();
    private static string Text(JsonElement state, string name) => state.GetProperty(name).GetString()!;
    private static string Line(JsonElement state) => state.GetProperty("items").EnumerateArray().First(i => Text(i, "type") == "Line" && !i.GetProperty("inserted").GetBoolean()).GetProperty("id").GetString()!;
    private static double Coordinate(Studio studio, string id, string property, string axis)
        => double.Parse(Json(studio.Inspect(new(id))).GetProperty("properties").EnumerateArray().Single(p => Text(p, "name") == property).GetProperty("value").GetProperty(axis).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static void Set(Studio studio, string id, string property, object value)
        => studio.SetProperty(new(Revision(State(studio)), id, property, JsonSerializer.SerializeToElement(value)));
    private static JsonElement Preview(Studio studio, string branchId, long revision, string choice = "Unresolved")
    {
        var s = State(studio); return Json(studio.PreviewMerge(new(Guid.Parse(Text(s, "documentId")), branchId, s.GetProperty("sourceRevision").GetInt64(), revision, choice)));
    }
    private static void Apply(Studio studio, JsonElement plan) => studio.ApplyMerge(new(Guid.Parse(Text(plan, "documentId")), Text(plan, "planId"), plan.GetProperty("targetRevision").GetInt64(), plan.GetProperty("incomingRevision").GetInt64()));
    private static StudioRequestIdentity Identity(JsonElement state) => new(Guid.Parse(Text(state, "documentId")), Text(state, "workspaceId"), state.GetProperty("viewVersion").GetInt64());
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Studio workspace contract failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
