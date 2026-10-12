using System.Runtime.CompilerServices;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;

internal static class SourceBoundaryVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var mode in new[] { ShadowSourceValidationMode.FullGraph, ShadowSourceValidationMode.EditedProperties })
        {
            foreach (var operation in new[] { "property", "structure", "refresh", "dirty-refresh", "mutation", "empty-batch" })
                tests.Add(($"boundary/{mode}/read callback rejects {operation}", () => RejectWriteInsideRead(mode, operation)));
            tests.Add(($"boundary/{mode}/nested read scopes keep the outer read protected", () => NestedReads(mode)));
            tests.Add(($"boundary/{mode}/escaped read exception releases the scope", () => ReadException(mode)));
            tests.Add(($"boundary/{mode}/mutation callback can read in-progress source without publishing", () => ReadDuringMutation(mode)));
            tests.Add(($"boundary/{mode}/callback failure preserves exception identity and throw site", () => CallbackException(mode, true)));
            tests.Add(($"boundary/{mode}/unchanged throwing callback retains snapshot identity", () => CallbackException(mode, false)));
            tests.Add(($"boundary/{mode}/mutation and reconciliation errors are both retained", () => DualFailure(mode)));
            tests.Add(($"boundary/{mode}/reconciliation-only failure fail-stops without an aggregate", () => ReconciliationFailure(mode)));
        }
    }

    private static void RejectWriteInsideRead(ShadowSourceValidationMode mode, string operation)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var batch = Batch(f);
        var graph = new ShadowGraphEdit(before);
        graph.Set(Id(f.Line), "StartPoint", ShadowValue.From(new Vec3(3, 0)));
        var patch = graph.Build();
        var mutationRan = false;
        var dirtyEnumerationRan = false;
        var callbackResult = f.Session.ReadSource(document =>
        {
            Require(ReferenceEquals(document, f.Document));
            var error = Expect<InvalidOperationException>(() =>
            {
                switch (operation)
                {
                    case "property": f.Session.Apply(batch); break;
                    case "structure": f.Session.Apply(patch); break;
                    case "refresh": f.Session.Refresh(); break;
                    case "dirty-refresh": f.Session.RefreshProperties(DirtyObjects()); break;
                    case "mutation": f.Session.MutateSource(_ => mutationRan = true); break;
                    case "empty-batch": f.Session.Apply(ShadowEditBatch.Create(before)); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
            });
            Require(error.Message.Contains("Reentrant", StringComparison.Ordinal));
            Require(ReferenceEquals(before, f.Session.Current));
            Require(f.Line.StartPoint.X == 0);
            return 42;
        });
        Require(callbackResult == 42);
        Require(!mutationRan && !dirtyEnumerationRan, "Rejected callbacks/enumerators must not run.");
        Require(ReferenceEquals(before, f.Session.Current));
        f.Session.Apply(batch);
        Require(f.Line.StartPoint.X == 2, "The outer read must release its guard.");

        IEnumerable<ObjectId> DirtyObjects()
        {
            dirtyEnumerationRan = true;
            yield return Id(f.Line);
        }
    }

    private static void NestedReads(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var expected = new ApplicationException("nested read sentinel");
        f.Session.ReadSource(outer =>
        {
            var count = f.Session.ReadSource(inner =>
            {
                Require(ReferenceEquals(outer, inner));
                return f.Session.ReadSource(deepest => deepest.Entities.All.Count());
            });
            Require(count == 1);
            Require(ReferenceEquals(expected, Expect<ApplicationException>(() =>
                f.Session.ReadSource<int>(_ => throw expected))));
            // Disposing an inner scope must not release the outer read scope.
            Expect<InvalidOperationException>(() => f.Session.Apply(Batch(f)));
            Expect<InvalidOperationException>(() => f.Session.Refresh());
            Require(ReferenceEquals(before, f.Session.Current));
            return count;
        });
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void ReadException(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var expected = new ApplicationException("read sentinel");
        var before = f.Session.Current;
        var actual = Expect<ApplicationException>(() => f.Session.ReadSource<int>(_ => throw expected));
        Require(ReferenceEquals(expected, actual));
        Require(ReferenceEquals(before, f.Session.Current));
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void ReadDuringMutation(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        f.Session.MutateSource(_ =>
        {
            f.Line.EndPoint = new Vector3(20, 0, 0);
            var endX = f.Session.ReadSource(document =>
            {
                Require(document.Entities.Lines.Single().EndPoint.X == 20);
                Require(before.Read(Id(f.Line))["EndPoint"]["X"].Number() == 10);
                Require(ReferenceEquals(before, f.Session.Current));
                Expect<InvalidOperationException>(() => f.Session.Apply(Batch(f)));
                return f.Session.ReadSource(inner => inner.Entities.Lines.Single().EndPoint.X);
            });
            Require(endX == 20);
        });
        Require(f.Session.Current.Revision == before.Revision + 1);
        Require(f.Session.Current.Read(Id(f.Line))["EndPoint"]["X"].Number() == 20);
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void CallbackException(ShadowSourceValidationMode mode, bool change)
    {
        var f = Create(mode);
        var before = f.Session.Current;
        var expected = new ApplicationException("mutation sentinel");
        var actual = Expect<ApplicationException>(() => f.Session.MutateSource(_ =>
        {
            if (change) f.Line.EndPoint = new Vector3(30, 0, 0);
            ThrowSentinel(expected);
        }));
        Require(ReferenceEquals(expected, actual));
        Require(actual.StackTrace?.Contains(nameof(ThrowSentinel), StringComparison.Ordinal) == true);
        Require(before.Read(Id(f.Line))["EndPoint"]["X"].Number() == 10);
        if (change)
        {
            Require(f.Session.Current.Revision == before.Revision + 1);
            Require(f.Session.Current.Read(Id(f.Line))["EndPoint"]["X"].Number() == 30);
        }
        else Require(ReferenceEquals(before, f.Session.Current));
        f.Session.Apply(Batch(f));
        Require(f.Line.StartPoint.X == 2);
    }

    private static void DualFailure(ShadowSourceValidationMode mode)
    {
        var f = CreateBounded(mode);
        var before = f.Session.Current;
        var expected = new ApplicationException("mutation before failed capture");
        var actual = Expect<AggregateException>(() => f.Session.MutateSource(document =>
        {
            document.Entities.Add(new Circle(new Vector2(20, 0), 5));
            ThrowSentinel(expected);
        }));
        Require(actual.InnerExceptions.Count == 2);
        Require(ReferenceEquals(actual.InnerExceptions[0], expected));
        Require(expected.StackTrace?.Contains(nameof(ThrowSentinel), StringComparison.Ordinal) == true);
        Require(actual.InnerExceptions[1] is InvalidOperationException);
        Require(actual.InnerExceptions[1].Message.Contains("budget", StringComparison.OrdinalIgnoreCase));
        AssertFaulted(f, before);
    }

    private static void ReconciliationFailure(ShadowSourceValidationMode mode)
    {
        var f = CreateBounded(mode);
        var before = f.Session.Current;
        var actual = Expect<InvalidOperationException>(() => f.Session.MutateSource(document =>
            document.Entities.Add(new Circle(new Vector2(20, 0), 5))));
        Require(actual.Message.Contains("budget", StringComparison.OrdinalIgnoreCase));
        AssertFaulted(f, before);
    }

    private static void AssertFaulted(Fixture f, ShadowSnapshot before)
    {
        Require(ReferenceEquals(before, f.Session.Current), "Failed reconciliation must not publish partial state.");
        Require(before.DrawingEntities().Count() == 1);
        Require(f.Document.Entities.All.Count() == 2, "MutateSource is deliberately not a transaction.");
        var mutationRan = false;
        Expect<InvalidOperationException>(() => f.Session.Refresh());
        Expect<InvalidOperationException>(() => f.Session.RefreshProperties([Id(f.Line)]));
        Expect<InvalidOperationException>(() => f.Session.ReadSource(document => document.Entities.All.Count()));
        Expect<InvalidOperationException>(() => f.Session.Apply(Batch(f)));
        Expect<InvalidOperationException>(() => f.Session.MutateSource(_ => mutationRan = true));
        Require(!mutationRan);
        var reattached = new ShadowSession(f.Document);
        Require(reattached.Current.DrawingEntities().Count() == 2);
        Require(ReferenceEquals(before, f.Session.Current));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowSentinel(Exception error) => throw error;

    private static Fixture Create(ShadowSourceValidationMode mode)
    {
        var document = new DxfDocument();
        var line = new Line(Vector2.Zero, new Vector2(10, 0));
        document.Entities.Add(line);
        return new(document, new ShadowSession(document, null, mode), line);
    }

    private static Fixture CreateBounded(ShadowSourceValidationMode mode)
    {
        var f = Create(mode);
        var options = new ShadowCaptureOptions(MaximumObjects: f.Session.Current.Objects.Count);
        return f with { Session = new ShadowSession(f.Document, options, mode) };
    }

    private static ShadowEditBatch Batch(Fixture f)
    {
        var before = f.Session.Current;
        var id = Id(f.Line);
        return ShadowEditBatch.Create(before, new ShadowPropertyEdit(id, "StartPoint",
            before.Read(id)["StartPoint"].Value, ShadowValue.From(new Vec3(2, 0))));
    }

    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
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
    private sealed record Fixture(DxfDocument Document, ShadowSession Session, Line Line);
}
