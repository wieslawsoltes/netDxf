using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class MergeVerification
{
    private static readonly ObjectId Root = new("root");
    private static readonly ObjectId Entity = new("entity");

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        Add("identity no-op shares snapshot and empty patch", () =>
        {
            var b = Basic(); var plan = ShadowMerge.Plan(b, b, b);
            Require(plan.CanApply && plan.Patch!.Changes.IsEmpty && ReferenceEquals(plan.Preview, b));
            var store = new ShadowMemoryStore(b); Require(ReferenceEquals(plan.ApplyTo(store), b));
        });
        Add("incoming-only edit", () =>
        {
            var b = Basic(); var i = Edit(b, "A", 5); var p = ShadowMerge.Plan(b, b, i);
            Require(p.CanApply && Number(p.Preview!, "A") == 5 && Number(b, "A") == 1);
            Require(p.Preview!.Revision == b.Revision + 1 && ReferenceEquals(p.Preview.Objects[Root], b.Objects[Root]));
        });
        Add("target-only edit remains no-op", () =>
        {
            var b = Basic(); var t = Edit(b, "A", 5); var p = ShadowMerge.Plan(b, t, b);
            Require(p.CanApply && p.Patch!.Changes.IsEmpty && ReferenceEquals(p.Preview, t));
        });
        Add("equal independently allocated edits converge", () =>
        {
            var b = Basic(); var t = Edit(b, "A", 5); var i = Edit(b, "A", 5);
            var p = ShadowMerge.Plan(b, t, i); Require(p.CanApply && ReferenceEquals(p.Preview, t));
        });
        Add("disjoint property edits on the same object", () =>
        {
            var b = Basic(); var t = Edit(b, "A", 5); var i = Edit(b, "B", 7);
            var p = ShadowMerge.Plan(b, t, i);
            Require(p.CanApply && Number(p.Preview!, "A") == 5 && Number(p.Preview!, "B") == 7);
            Require(p.Patch!.ExpectedRevision == t.Revision && p.Patch.Changes.Length == 1);
            Require(ReferenceEquals(p.Patch.Changes[0].Before, t.Objects[Entity]));
        });
        foreach (var choice in Enum.GetValues<ShadowMergeChoice>())
        {
            Add("divergent scalar / " + choice, () =>
            {
                var b = Basic(); var t = Edit(b, "A", 5); var i = Edit(b, "A", 7);
                var p = ShadowMerge.Plan(b, t, i, new(ConflictChoice: choice));
                var c = p.Conflicts.Single();
                Require(c.Kind == ShadowMergeConflictKind.Value && c.Path.SequenceEqual(new[] { "A" }) && c.Resolution == choice);
                Require(c.BaseValue is ShadowValue.Atom { Text: "1" } && c.TargetValue is ShadowValue.Atom { Text: "5" } && c.IncomingValue is ShadowValue.Atom { Text: "7" });
                if (choice == ShadowMergeChoice.Unresolved) Require(!p.CanApply && p.Patch is null && p.Preview is null);
                else Require(p.CanApply && Number(p.Preview!, "A") == (choice == ShadowMergeChoice.Target ? 5 : 7));
            });
            Add("delete/modify / " + choice, () =>
            {
                var b = Basic(); var t = Remove(b); var i = Edit(b, "A", 7);
                var p = ShadowMerge.Plan(b, t, i, new(ConflictChoice: choice));
                Require(p.Conflicts.Single().Kind == ShadowMergeConflictKind.DeleteModify);
                Require(p.CanApply == (choice != ShadowMergeChoice.Unresolved));
                if (p.CanApply) Require(p.Preview!.Objects.ContainsKey(Entity) == (choice == ShadowMergeChoice.Incoming));
            });
            Add("modify/delete / " + choice, () =>
            {
                var b = Basic(); var t = Edit(b, "A", 7); var i = Remove(b);
                var p = ShadowMerge.Plan(b, t, i, new(ConflictChoice: choice));
                Require(p.Conflicts.Single().Kind == ShadowMergeConflictKind.DeleteModify);
                if (p.CanApply) Require(p.Preview!.Objects.ContainsKey(Entity) == (choice == ShadowMergeChoice.Target));
            });
            Add("concurrent identity addition / " + choice, () =>
            {
                var b = Remove(Basic()); var t = AddObject(b, "new", 1); var i = AddObject(b, "new", 2);
                var p = ShadowMerge.Plan(b, t, i, new(ConflictChoice: choice));
                Require(p.Conflicts.Single().Kind == ShadowMergeConflictKind.ConcurrentAddition);
                Require(p.CanApply == (choice != ShadowMergeChoice.Unresolved));
                if (p.CanApply) Require(p.Preview!.Read(new("new"))["A"].Number() == (choice == ShadowMergeChoice.Target ? 1 : 2));
            });
        }
        Add("nested independent structure fields merge", () =>
        {
            var b = Basic(); var t = Set(b, "Point", ShadowValue.From(new Vec3(10, 2, 3)));
            var i = Set(b, "Point", ShadowValue.From(new Vec3(1, 20, 3)));
            var p = ShadowMerge.Plan(b, t, i); Require(p.CanApply && p.Preview!.Read(Entity)["Point"].Vector() == new Vec3(10, 20, 3));
        });
        Add("nested conflict path remains segmented", () =>
        {
            var b = Basic(); var t = Set(b, "Point", ShadowValue.From(new Vec3(10, 2, 3)));
            var i = Set(b, "Point", ShadowValue.From(new Vec3(20, 2, 3)));
            var p = ShadowMerge.Plan(b, t, i); Require(p.Conflicts.Single().Path.SequenceEqual(new[] { "Point", "X" }));
        });
        Add("type-incompatible structures remain atomic", () =>
        {
            var b = Basic(); var t = Set(b, "Point", ShadowValue.From(new Vec3(10, 2, 3)));
            var i = Set(b, "Point", ShadowValue.From(new Vec3(1, 20), true));
            var p = ShadowMerge.Plan(b, t, i); Require(!p.CanApply && p.Conflicts.Single().Path.Length == 1);
        });
        Add("concurrent sequence edits conflict instead of guessing order", () =>
        {
            var b = Basic(); var t = Set(b, "Items", Seq(1, 2, 3)); var i = Set(b, "Items", Seq(1, 2, 4));
            var p = ShadowMerge.Plan(b, t, i); Require(!p.CanApply && p.Conflicts.Single().Path.Single() == "Items");
        });
        Add("equal concurrent sequence edits converge", () =>
        {
            var b = Basic(); var t = Set(b, "Items", Seq(1, 2, 3)); var i = Set(b, "Items", Seq(1, 2, 3));
            Require(ReferenceEquals(ShadowMerge.Plan(b, t, i).Preview, t));
        });
        Add("absent property is distinct from explicit null", () =>
        {
            var b = Basic(); var t = ReplaceNode(b, n => n with { Properties = n.Properties.Remove("A") });
            var i = Set(b, "A", ShadowValue.Null); var p = ShadowMerge.Plan(b, t, i);
            Require(!p.CanApply && p.Conflicts.Single().TargetValue is null && p.Conflicts[0].IncomingValue is ShadowValue.NullValue);
        });
        Add("disjoint new properties merge", () =>
        {
            var b = Basic(); var t = Set(b, "one", ShadowValue.Null); var i = Set(b, "two", ShadowValue.From("x"));
            var p = ShadowMerge.Plan(b, t, i);
            Require(p.CanApply && p.Preview!.Objects[Entity].Properties.ContainsKey("one") && p.Preview.Objects[Entity].Properties.ContainsKey("two"));
        });
        Add("same field removed on both branches", () =>
        {
            var b = Basic(); var t = ReplaceNode(b, n => n with { Properties = n.Properties.Remove("A") });
            var i = ReplaceNode(b, n => n with { Properties = n.Properties.Remove("A") });
            Require(ReferenceEquals(ShadowMerge.Plan(b, t, i).Preview, t));
        });
        Add("equal deletion converges", () =>
        {
            var b = Basic(); var t = Remove(b); Require(ReferenceEquals(ShadowMerge.Plan(b, t, Remove(b)).Preview, t));
        });
        Add("disjoint identity additions merge", () =>
        {
            var b = Basic(); var t = AddObject(b, "one", 1); var i = AddObject(b, "two", 2);
            var p = ShadowMerge.Plan(b, t, i); Require(p.CanApply && p.Preview!.Objects.Count == 4 && p.Preview.EntityIds.Count == 3);
        });
        Add("identical concurrent addition converges", () =>
        {
            var b = Basic(); var t = AddObject(b, "one", 1); var i = AddObject(b, "one", 1);
            Require(ReferenceEquals(ShadowMerge.Plan(b, t, i).Preview, t));
        });
        Add("metadata and scalar change merge independently", () =>
        {
            var b = Basic(); var t = ReplaceNode(b, n => n with { WritableProperties = n.WritableProperties.Remove("A") });
            var i = Edit(b, "B", 20); var p = ShadowMerge.Plan(b, t, i);
            Require(p.CanApply && !p.Preview!.Objects[Entity].WritableProperties.Contains("A") && Number(p.Preview, "B") == 20);
        });
        Add("competing metadata changes conflict", () =>
        {
            var b = Basic(); var t = ReplaceNode(b, n => n with { WritableProperties = n.WritableProperties.Remove("A") });
            var i = ReplaceNode(b, n => n with { WritableProperties = n.WritableProperties.Remove("B") });
            var p = ShadowMerge.Plan(b, t, i); Require(!p.CanApply && p.Conflicts.Single().Kind == ShadowMergeConflictKind.Metadata);
        });
        Add("entity kind index is recomputed", () =>
        {
            var b = Basic(); var i = ReplaceNode(b, n => n with { Kind = ShadowObjectKind.Object });
            var p = ShadowMerge.Plan(b, b, i); Require(p.CanApply && !p.Preview!.EntityIds.Contains(Entity));
        });
        foreach (var choice in Enum.GetValues<ShadowMergeChoice>())
            Add("cross-branch dangling reference cannot be forced / " + choice, () =>
            {
                var b = AddObject(Basic(), "resource", 1);
                var t = new ShadowGraphPatch(b.DocumentId, b.Revision, [new(new("resource"), b.Objects[new("resource")], null)]).Apply(b);
                var i = Set(b, "Ref", new ShadowValue.Reference(new("resource")));
                var p = ShadowMerge.Plan(b, t, i, new(ConflictChoice: choice));
                Require(!p.CanApply && p.Patch is null && p.Preview is null);
                Require(p.Conflicts.Single().Kind == ShadowMergeConflictKind.DanglingReference && p.Conflicts[0].ReferencedObject == new ObjectId("resource"));
            });
        Add("references in nested sequences are validated", () =>
        {
            var b = AddObject(Basic(), "resource", 1);
            var t = new ShadowGraphPatch(b.DocumentId, b.Revision, [new(new("resource"), b.Objects[new("resource")], null)]).Apply(b);
            var i = Set(b, "Items", new ShadowValue.Sequence([new ShadowValue.Reference(new("resource"))]));
            var p = ShadowMerge.Plan(b, t, i); Require(p.Conflicts.Single().Path.SequenceEqual(new[] { "Items", "0" }));
        });
        Add("reference cycles are identities not recursive object expansion", () =>
        {
            var b = Set(Basic(), "Ref", new ShadowValue.Reference(Entity));
            var p = ShadowMerge.Plan(b, Edit(b, "A", 10), Edit(b, "B", 20));
            Require(p.CanApply && p.Preview!.Read(Entity)["Ref"].Id == Entity);
        });
        Add("conflicts are deterministic ordinal object and field order", () =>
        {
            var b = Basic(); var t = Edit(Edit(b, "B", 10), "A", 20); var i = Edit(Edit(b, "A", 40), "B", 30);
            var p = ShadowMerge.Plan(b, t, i); Require(p.Conflicts.Select(c => c.Path[0]).SequenceEqual(new[] { "A", "B" }));
        });
        Add("stale target plan rejected", () =>
        {
            var b = Basic(); var store = new ShadowMemoryStore(b); var p = ShadowMerge.Plan(b, b, Edit(b, "A", 5));
            store.Apply(ShadowEditBatch.Create(b, new ShadowPropertyEdit(Entity, "B", b.Objects[Entity]["B"], ShadowValue.From(8.0))));
            Throws<ShadowConflictException>(() => p.ApplyTo(store)); Require(Number(store.Current, "A") == 1);
        });
        Add("equal revision on another branch is not the target", () =>
        {
            var b = Basic(); var target = Edit(b, "A", 5); var other = Edit(b, "B", 7);
            var p = ShadowMerge.Plan(b, target, Edit(b, "B", 8));
            Throws<ShadowConflictException>(() => p.ApplyTo(new ShadowMemoryStore(other)));
        });
        Add("failed plan cannot apply or change editor history", () =>
        {
            var b = Basic(); var t = Edit(b, "A", 5); var store = new ShadowMemoryStore(t); var editor = new EditorSession(store);
            var p = editor.PreviewMerge(b, Edit(b, "A", 7));
            Throws<ShadowConflictException>(() => editor.ApplyMerge(p));
            Require(ReferenceEquals(editor.Snapshot, t) && editor.State.UndoCount == 0);
        });
        Add("merge is one undo-redo transaction", () =>
        {
            var b = Basic(); var t = Edit(b, "A", 5); var editor = new EditorSession(new ShadowMemoryStore(t));
            var p = editor.PreviewMerge(b, Edit(b, "B", 7));
            editor.ApplyMerge(p); Require(editor.State.UndoCount == 1 && Number(editor.Snapshot, "B") == 7);
            editor.Undo(); Require(Number(editor.Snapshot, "A") == 5 && Number(editor.Snapshot, "B") == 2);
            editor.Redo(); Require(Number(editor.Snapshot, "B") == 7);
        });
        Add("no-op merge leaves history unchanged", () =>
        {
            var b = Basic(); var editor = new EditorSession(new ShadowMemoryStore(b));
            Require(ReferenceEquals(editor.ApplyMerge(editor.PreviewMerge(b, b)), b) && editor.State.UndoCount == 0);
        });
        Add("memory-store publication records a precise delta", () =>
        {
            var b = Basic(); var store = new ShadowMemoryStore(b); var p = ShadowMerge.Plan(b, b, Edit(b, "A", 10));
            var after = p.ApplyTo(store); var delta = after.Diff(b);
            Require(delta.Changed.SetEquals(new[] { Entity }) && delta.Added.IsEmpty && delta.Removed.IsEmpty);
        });
        Add("unrelated documents rejected", () => { var b = Basic(); Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, b with { DocumentId = Guid.NewGuid() })); });
        Add("different roots rejected", () => { var b = Basic(); Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, b with { Root = Entity })); });
        Add("backward revision rejected", () => { var b = Basic() with { Revision = 5 }; Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, b with { Revision = 4 })); });
        Add("runtime identity type replacement rejected", () =>
        {
            var b = Basic(); var node = b.Objects[Entity] with { TypeName = "OtherType" };
            var i = b with { Objects = b.Objects.SetItem(Entity, node) }; Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, i));
        });
        Add("inconsistent entity index rejected", () => { var b = Basic(); Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, b with { EntityIds = [] })); });
        Add("malformed incoming references rejected", () =>
        {
            var b = Basic(); var node = b.Objects[Entity] with { Properties = b.Objects[Entity].Properties.SetItem("Ref", new ShadowValue.Reference(new("missing"))) };
            Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, b with { Objects = b.Objects.SetItem(Entity, node) }));
        });
        Add("default sequence rejected", () => { var b = Basic(); var node = b.Objects[Entity] with { Properties = b.Objects[Entity].Properties.SetItem("Items", new ShadowValue.Sequence(default)) }; b = b with { Objects = b.Objects.SetItem(Entity, node) }; Throws<ArgumentException>(() => ShadowMerge.Plan(b, b, b)); });
        Add("case-insensitive maps rejected", () =>
        {
            var b = Basic(); var node = b.Objects[Entity] with { Properties = b.Objects[Entity].Properties.WithComparers(StringComparer.OrdinalIgnoreCase) };
            var bad = b with { Objects = b.Objects.SetItem(Entity, node) }; Throws<ArgumentException>(() => ShadowMerge.Plan(bad, bad, bad));
        });
        Add("object budget", () => { var b = Basic(); Throws<InvalidOperationException>(() => ShadowMerge.Plan(b, b, b, new(MaximumObjects: 1))); });
        Add("union budget", () =>
        {
            var b = Basic(); var t = AddObject(b, "one", 1); var i = AddObject(b, "two", 2);
            Throws<InvalidOperationException>(() => ShadowMerge.Plan(b, t, i, new(MaximumObjects: 3)));
        });
        Add("work budget", () => { var b = Basic(); Throws<InvalidOperationException>(() => ShadowMerge.Plan(b, b, b, new(MaximumWork: 1))); });
        Add("conflict budget", () =>
        {
            var b = Basic(); var t = Edit(Edit(b, "A", 10), "B", 20); var i = Edit(Edit(b, "A", 30), "B", 40);
            Throws<InvalidOperationException>(() => ShadowMerge.Plan(b, t, i, new(MaximumConflicts: 1)));
        });
        Add("deep values are rejected before recursive comparison", () =>
        {
            var b = Basic(); ShadowValue value = ShadowValue.From(1.0);
            for (var i = 0; i < 1000; i++) value = new ShadowValue.Sequence([value]);
            var node = b.Objects[Entity] with { Properties = b.Objects[Entity].Properties.SetItem("Items", value) };
            var bad = b with { Objects = b.Objects.SetItem(Entity, node) };
            Throws<InvalidOperationException>(() => ShadowMerge.Plan(bad, bad, bad));
        });
        Add("cancellation", () => { var b = Basic(); Throws<OperationCanceledException>(() => ShadowMerge.Plan(b, b, b, cancellationToken: new(true))); });
        Add("invalid limits", () => { var b = Basic(); Throws<ArgumentOutOfRangeException>(() => ShadowMerge.Plan(b, b, b, new(MaximumValueDepth: 999))); });
        Add("invalid conflict preference", () => { var b = Basic(); Throws<ArgumentOutOfRangeException>(() => ShadowMerge.Plan(b, b, b, new(ConflictChoice: (ShadowMergeChoice)99))); });
        Add("ten-thousand-object sharing", () =>
        {
            var b = Basic(); var nodes = b.Objects.ToBuilder(); var ids = b.EntityIds.ToBuilder();
            for (var i = 0; i < 10000; i++) { var id = new ObjectId("N:" + i); nodes.Add(id, b.Objects[Entity] with { Id = id }); ids.Add(id); }
            b = b with { Objects = nodes.ToImmutable(), EntityIds = ids.ToImmutable() };
            var t = Edit(b, "A", 10); var p = ShadowMerge.Plan(b, t, Edit(b, "B", 20));
            Require(p.CanApply && p.Patch!.Changes.Length == 1);
            Require(p.Preview!.Objects.Count(pair => ReferenceEquals(pair.Value, t.Objects[pair.Key])) == 10001);
            Console.WriteLine($"Merge sharing: {p.Work} bounded work units; 10001 unchanged objects shared.");
        });
        Add("real netDxf source merge render graph save undo", SourceIntegration);
        Add("real netDxf source refuses invalid incoming value atomically", SourceRejection);
        Add("branch structural creation adopts into source and undoes", SourceCreation);

        void Add(string name, Action action) => tests.Add(("merge/" + name, action));
    }

    private static void SourceIntegration()
    {
        var document = new DxfDocument(); var line = new Line(new Vector3(0, 0, 0), new Vector3(10, 0, 0));
        var untouched = new Circle(new Vector2(30, 0), 5); document.Entities.Add(line); document.Entities.Add(untouched);
        var source = new ShadowSession(document); var live = new EditorSession(source); var b = source.Current;
        var branch = new EditorSession(new ShadowMemoryStore(b)); var id = new ObjectId("H:" + line.Handle);
        branch.SetProperty(id, "EndPoint", ShadowValue.From(new Vec3(10, 5, 0)));
        live.SetProperty(id, "StartPoint", ShadowValue.From(new Vec3(-5, 0, 0)));
        var compiler = new RenderCompiler(); var before = compiler.Compile(source.Current);
        var plan = live.PreviewMerge(b, branch.Snapshot); Require(plan.CanApply);
        live.ApplyMerge(plan); Require(line.StartPoint.X == -5 && line.EndPoint.Y == 5);
        var after = compiler.Compile(source.Current);
        var circleId = new ObjectId("H:" + untouched.Handle);
        Require(ReferenceEquals(before.Items.Single(i => i.Entity == circleId), after.Items.Single(i => i.Entity == circleId)));
        Require(new TopologyBuilder().Build(source.Current).Edges.Length > 0);
        Require(b.Read(id)["StartPoint"].Vector().X == 0 && b.Read(id)["EndPoint"].Vector().Y == 0);
        live.Undo(); Require(line.StartPoint.X == -5 && line.EndPoint.Y == 0);
        live.Redo(); Require(line.EndPoint.Y == 5);
        using var stream = new MemoryStream(); Require(document.Save(stream)); stream.Position = 0;
        var reloaded = DxfDocument.Load(stream)!; Require(reloaded.Entities.Lines.Single().EndPoint.Y == 5);
    }
    private static void SourceRejection()
    {
        var document = new DxfDocument(); var circle = new Circle(new Vector2(0, 0), 5); document.Entities.Add(circle);
        var source = new ShadowSession(document); var live = new EditorSession(source); var b = source.Current;
        var id = new ObjectId("H:" + circle.Handle); var branch = new EditorSession(new ShadowMemoryStore(b));
        branch.SetProperty(id, "Radius", ShadowValue.From(-1.0));
        var plan = live.PreviewMerge(b, branch.Snapshot); Require(plan.CanApply);
        Throws<Exception>(() => live.ApplyMerge(plan));
        Require(ReferenceEquals(source.Current, b) && circle.Radius == 5 && live.State.UndoCount == 0);
    }
    private static void SourceCreation()
    {
        var source = new ShadowSession(new DxfDocument()); var live = new EditorSession(source); var b = source.Current;
        var branch = new EditorSession(new ShadowMemoryStore(b));
        branch.CreateEntity("netDxf.Entities.Line", new Dictionary<string, ShadowValue>
        { ["StartPoint"] = ShadowValue.From(new Vec3(0, 0)), ["EndPoint"] = ShadowValue.From(new Vec3(5, 0)) });
        var p = live.PreviewMerge(b, branch.Snapshot); Require(p.CanApply); live.ApplyMerge(p);
        Require(source.Current.EntityIds.Count == b.EntityIds.Count + 1);
        live.Undo(); Require(source.Current.EntityIds.Count == b.EntityIds.Count);
        live.Redo(); Require(source.Current.EntityIds.Count == b.EntityIds.Count + 1);
    }

    private static ShadowSnapshot Basic()
    {
        var root = new ShadowObject(Root, "Document", ShadowObjectKind.Document,
            ImmutableDictionary<string, ShadowValue>.Empty, ImmutableHashSet<string>.Empty);
        var fields = ImmutableDictionary<string, ShadowValue>.Empty.Add("A", ShadowValue.From(1.0)).Add("B", ShadowValue.From(2.0))
            .Add("Point", ShadowValue.From(new Vec3(1, 2, 3))).Add("Items", Seq(1, 2));
        var entity = new ShadowObject(Entity, "Entity", ShadowObjectKind.Entity, fields, fields.Keys.ToImmutableHashSet());
        return new(Guid.NewGuid(), 0, Root, ImmutableDictionary<ObjectId, ShadowObject>.Empty.Add(Root, root).Add(Entity, entity), [Entity], []);
    }
    private static ShadowSnapshot Edit(ShadowSnapshot b, string name, double value) => Set(b, name, ShadowValue.From(value));
    private static ShadowSnapshot Set(ShadowSnapshot b, string name, ShadowValue value)
        => ReplaceNode(b, n => n with { Properties = n.Properties.SetItem(name, value) });
    private static ShadowSnapshot ReplaceNode(ShadowSnapshot b, Func<ShadowObject, ShadowObject> replace)
        => new ShadowGraphPatch(b.DocumentId, b.Revision, [new(Entity, b.Objects[Entity], replace(b.Objects[Entity]))]).Apply(b);
    private static ShadowSnapshot Remove(ShadowSnapshot b)
        => new ShadowGraphPatch(b.DocumentId, b.Revision, [new(Entity, b.Objects[Entity], null)]).Apply(b);
    private static ShadowSnapshot AddObject(ShadowSnapshot b, string id, double value)
    {
        var fields = ImmutableDictionary<string, ShadowValue>.Empty.Add("A", ShadowValue.From(value));
        var node = new ShadowObject(new(id), "Entity", ShadowObjectKind.Entity, fields, ["A"]);
        return new ShadowGraphPatch(b.DocumentId, b.Revision, [new(node.Id, null, node)]).Apply(b);
    }
    private static ShadowValue.Sequence Seq(params double[] values) => new(values.Select(ShadowValue.From).ToImmutableArray());
    private static double Number(ShadowSnapshot s, string name) => s.Read(Entity)[name].Number();
    private static void Require(bool condition, string message = "Merge contract failed.") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
