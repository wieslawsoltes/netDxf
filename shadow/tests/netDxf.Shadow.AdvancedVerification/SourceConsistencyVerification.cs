using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;

internal static class SourceConsistencyVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("source/default full-graph policy and retained snapshot sharing", ValidCommit));
        tests.Add(("source/reject unrelated geometry changed before batch creation", UnrelatedGeometry));
        tests.Add(("source/reject another property on the edited entity", OtherProperty));
        tests.Add(("source/reject changed layer resource", LayerResource));
        tests.Add(("source/reject unrefreshed entity addition", EntityAddition));
        tests.Add(("source/reject unrefreshed entity removal", EntityRemoval));
        tests.Add(("source/conflict rejects the entire property batch before writes", AtomicConflict));
        tests.Add(("source/explicit refresh recovers without mutating retained snapshots", RefreshRecovery));
        tests.Add(("source/empty batch does not implicitly refresh or advance revision", EmptyBatch));
        tests.Add(("source/reject invalid validation mode", InvalidPolicy));
        tests.Add(("source/failed setter validation releases the operation scope", SetterFailure));
        tests.Add(("source/uncaught nested operation reconciles outer callback changes", UncaughtReentrancy));
        tests.Add(("source/throwing mutation callback reconciles and releases the gate", CallbackFailure));

        foreach (var mode in new[] { ShadowSourceValidationMode.FullGraph, ShadowSourceValidationMode.EditedProperties })
        {
            tests.Add(($"source/{mode}/edited property still uses optimistic source comparison", () => EditedProperty(mode)));
            tests.Add(($"source/{mode}/structural patch always validates the full graph", () => StructuralConflict(mode)));
            tests.Add(($"source/{mode}/stale revision does not write", () => StaleRevision(mode)));
            tests.Add(($"source/{mode}/competing batches have exactly one winner", () => CompetingBatches(mode)));
            foreach (var operation in new[] { "property", "structure", "refresh", "dirty-refresh", "mutation" })
                tests.Add(($"source/{mode}/reject reentrant {operation}", () => ReentrantOperation(mode, operation)));
        }
    }

    private static void ValidCommit()
    {
        var f = Create();
        Require(f.Session.SourceValidation == ShadowSourceValidationMode.FullGraph);
        var before = f.Session.Current;
        var next = f.Session.Apply(Batch(f, 2));
        Require(f.Line.StartPoint.X == 2);
        Require(next.Revision == before.Revision + 1);
        Require(next.Read(Id(f.Line))["StartPoint"]["X"].Number() == 2);
        Require(before.Read(Id(f.Line))["StartPoint"]["X"].Number() == 0);
        Require(ReferenceEquals(before.Objects[Id(f.Circle)], next.Objects[Id(f.Circle)]));
        Require(ReferenceEquals(next, f.Session.Refresh()));
    }

    private static void UnrelatedGeometry()
    {
        var f = Create();
        var before = f.Session.Current;
        f.Circle.Radius = 9;
        // Creating a batch after the external write must not bless the stale snapshot.
        Expect<ShadowConflictException>(() => f.Session.Apply(Batch(f)));
        AssertUnchangedLine(f, before);
        Require(f.Circle.Radius == 9);
        Require(before.Read(Id(f.Circle))["Radius"].Number() == 5);
    }

    private static void OtherProperty()
    {
        var f = Create();
        var before = f.Session.Current;
        var batch = Batch(f);
        f.Line.EndPoint = new Vector3(25, 0, 0);
        Expect<ShadowConflictException>(() => f.Session.Apply(batch));
        AssertUnchangedLine(f, before);
        Require(f.Line.EndPoint.X == 25);
    }

    private static void LayerResource()
    {
        var f = Create();
        var before = f.Session.Current;
        f.Layer.Color = AciColor.Blue;
        Expect<ShadowConflictException>(() => f.Session.Apply(Batch(f)));
        AssertUnchangedLine(f, before);
        Require(f.Layer.Color.Index == AciColor.Blue.Index);
    }

    private static void EntityAddition()
    {
        var f = Create();
        var before = f.Session.Current;
        var added = new Line(new Vector2(40, 0), new Vector2(50, 0));
        f.Document.Entities.Add(added);
        Expect<ShadowConflictException>(() => f.Session.Apply(Batch(f)));
        AssertUnchangedLine(f, before);
        Require(f.Document.Entities.All.Count() == 3);
        Require(!before.Objects.ContainsKey(Id(added)));
        var refreshed = f.Session.Refresh();
        Require(refreshed.Objects.ContainsKey(Id(added)));
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void EntityRemoval()
    {
        var f = Create();
        var before = f.Session.Current;
        var removedId = Id(f.Circle);
        f.Document.Entities.Remove(f.Circle);
        Expect<ShadowConflictException>(() => f.Session.Apply(Batch(f)));
        AssertUnchangedLine(f, before);
        Require(f.Document.Entities.All.Count() == 1);
        Require(before.Objects.ContainsKey(removedId));
        var refreshed = f.Session.Refresh();
        Require(!refreshed.Objects.ContainsKey(removedId));
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void AtomicConflict()
    {
        var f = Create();
        var before = f.Session.Current;
        var batch = ShadowEditBatch.Create(before,
            Edit(before, Id(f.Line), "StartPoint", ShadowValue.From(new Vec3(2, 0))),
            Edit(before, Id(f.Circle), "Radius", ShadowValue.From(8.0)));
        f.Layer.Color = AciColor.Blue;
        Expect<ShadowConflictException>(() => f.Session.Apply(batch));
        AssertUnchangedLine(f, before);
        Require(f.Circle.Radius == 5);
    }

    private static void RefreshRecovery()
    {
        var f = Create();
        var retained = f.Session.Current;
        var stale = Batch(f);
        f.Circle.Radius = 9;
        Expect<ShadowConflictException>(() => f.Session.Apply(stale));
        Require(ReferenceEquals(retained, f.Session.Current), "Conflict must not silently rebase Current.");
        var refreshed = f.Session.Refresh();
        Require(refreshed.Revision == retained.Revision + 1);
        Require(refreshed.Read(Id(f.Circle))["Radius"].Number() == 9);
        Expect<ShadowConflictException>(() => f.Session.Apply(stale));
        var committed = f.Session.Apply(Batch(f));
        Require(committed.Revision == refreshed.Revision + 1);
        Require(committed.Read(Id(f.Circle))["Radius"].Number() == 9);
        Require(retained.Read(Id(f.Circle))["Radius"].Number() == 5);
    }

    private static void EmptyBatch()
    {
        var f = Create();
        var before = f.Session.Current;
        f.Circle.Radius = 9;
        Require(ReferenceEquals(before, f.Session.Apply(ShadowEditBatch.Create(before))));
        Require(before.Revision == f.Session.Current.Revision);
        Expect<ShadowConflictException>(() => f.Session.Apply(Batch(f)));
        f.Session.Refresh();
        Expect<ShadowConflictException>(() => f.Session.Apply(ShadowEditBatch.Create(before)));
    }

    private static void InvalidPolicy()
    {
        var error = Expect<ArgumentOutOfRangeException>(() =>
            new ShadowSession(new DxfDocument(), null, (ShadowSourceValidationMode)42));
        Require(error.ParamName == "sourceValidation");
    }

    private static void SetterFailure()
    {
        var f = Create();
        var before = f.Session.Current;
        var invalid = ShadowEditBatch.Create(before,
            Edit(before, Id(f.Line), "StartPoint", ShadowValue.From(new Vec3(3, 0))),
            Edit(before, Id(f.Circle), "Radius", ShadowValue.From(-1.0)));
        Expect<Exception>(() => f.Session.Apply(invalid));
        AssertUnchangedLine(f, before);
        Require(f.Circle.Radius == 5);
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2, "Failure must release the source-operation scope.");
    }

    private static void EditedProperty(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var batch = Batch(f);
        f.Line.StartPoint = new Vector3(7, 0, 0);
        Expect<ShadowConflictException>(() => f.Session.Apply(batch));
        Require(f.Line.StartPoint.X == 7);
        Require(ReferenceEquals(before, f.Session.Current));
    }

    private static void StructuralConflict(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var graph = new ShadowGraphEdit(before);
        graph.Set(Id(f.Line), "StartPoint", ShadowValue.From(new Vec3(3, 0)));
        var patch = graph.Build();
        f.Circle.Radius = 9;
        Expect<ShadowConflictException>(() => f.Session.Apply(patch));
        AssertUnchangedLine(f, before);
        Require(f.Circle.Radius == 9);
    }

    private static void StaleRevision(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var stale = Batch(f, 3);
        var committed = f.Session.Apply(Batch(f, 2));
        Expect<ShadowConflictException>(() => f.Session.Apply(stale));
        Require(ReferenceEquals(committed, f.Session.Current));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void CompetingBatches(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var first = Batch(f, 2);
        var second = Batch(f, 3);
        var committed = 0;
        var conflicts = 0;
        void Apply(ShadowEditBatch batch)
        {
            try { f.Session.Apply(batch); Interlocked.Increment(ref committed); }
            catch (ShadowConflictException) { Interlocked.Increment(ref conflicts); }
        }
        Parallel.Invoke(() => Apply(first), () => Apply(second));
        Require(committed == 1 && conflicts == 1);
        Require(f.Session.Current.Revision == before.Revision + 1);
        Require(f.Line.StartPoint.X is 2 or 3);
        Require(before.Read(Id(f.Line))["StartPoint"]["X"].Number() == 0);
    }

    private static void ReentrantOperation(ShadowSourceValidationMode mode, string operation)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var batch = Batch(f);
        var graph = new ShadowGraphEdit(before);
        graph.Set(Id(f.Line), "StartPoint", ShadowValue.From(new Vec3(3, 0)));
        var patch = graph.Build();
        var nestedMutationRan = false;
        f.Session.MutateSource(_ =>
        {
            f.Circle.Radius = 7;
            var error = Expect<InvalidOperationException>(() =>
            {
                switch (operation)
                {
                    case "property": f.Session.Apply(batch); break;
                    case "structure": f.Session.Apply(patch); break;
                    case "refresh": f.Session.Refresh(); break;
                    case "dirty-refresh": f.Session.RefreshProperties([Id(f.Circle)]); break;
                    case "mutation": f.Session.MutateSource(nestedDocument => nestedMutationRan = true); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
            });
            Require(error.Message.Contains("Reentrant", StringComparison.Ordinal));
            Require(ReferenceEquals(before, f.Session.Current), "Nested callbacks must not publish.");
            Require(f.Session.Current.Read(Id(f.Circle))["Radius"].Number() == 5);
            Require(f.Session.ReadSource(d => d.Entities.Circles.Single().Radius) == 7);
        });
        Require(!nestedMutationRan);
        Require(f.Line.StartPoint.X == 0);
        Require(f.Session.Current.Revision == before.Revision + 1);
        Require(f.Session.Current.Read(Id(f.Circle))["Radius"].Number() == 7);
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void UncaughtReentrancy()
    {
        var f = Create();
        var before = f.Session.Current;
        var error = Expect<InvalidOperationException>(() => f.Session.MutateSource(_ =>
        {
            f.Circle.Radius = 7;
            f.Session.Refresh();
        }));
        Require(error.Message.Contains("Reentrant", StringComparison.Ordinal));
        Require(f.Session.Current.Revision == before.Revision + 1);
        Require(f.Session.Current.Read(Id(f.Circle))["Radius"].Number() == 7);
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void CallbackFailure()
    {
        var f = Create();
        var expected = new ApplicationException("callback sentinel");
        var actual = Expect<ApplicationException>(() => f.Session.MutateSource(_ =>
        {
            f.Circle.Radius = 7;
            throw expected;
        }));
        Require(ReferenceEquals(expected, actual));
        Require(f.Session.Current.Read(Id(f.Circle))["Radius"].Number() == 7);
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static Fixture Create(ShadowSourceValidationMode? mode = null)
    {
        var document = new DxfDocument();
        var layer = new Layer("SourceConsistency") { Color = AciColor.Red };
        var line = new Line(Vector2.Zero, new Vector2(10, 0));
        var circle = new Circle(new Vector2(20, 0), 5) { Layer = layer };
        document.Entities.Add(line);
        document.Entities.Add(circle);
        var session = mode is { } policy ? new ShadowSession(document, null, policy) : new ShadowSession(document);
        return new(document, session, line, circle, layer);
    }

    private static ShadowEditBatch Batch(Fixture f, double x = 2)
    {
        var snapshot = f.Session.Current;
        return ShadowEditBatch.Create(snapshot,
            Edit(snapshot, Id(f.Line), "StartPoint", ShadowValue.From(new Vec3(x, 0))));
    }

    private static ShadowPropertyEdit Edit(ShadowSnapshot snapshot, ObjectId id, string property, ShadowValue after)
        => new(id, property, snapshot.Read(id)[property].Value, after);

    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());

    private static void AssertUnchangedLine(Fixture f, ShadowSnapshot before)
    {
        Require(f.Line.StartPoint.X == 0, "Source write occurred before conflict detection.");
        Require(ReferenceEquals(before, f.Session.Current), "Rejected operation published a snapshot.");
    }

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Require(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(DxfDocument Document, ShadowSession Session, Line Line, Circle Circle, Layer Layer);
}
