using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;

internal static class CaptureTraversalVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("capture/repeated cyclic references are revisited exactly once on every full pass", FullTraversal));
        tests.Add(("capture/dirty roots remain independent of references and duplicate dirty IDs", DirtyTraversal));
        tests.Add(("capture/dirty reference replacement captures new identities and full refresh prunes old ones", ReplacedReference));
        tests.Add(("capture/repeated references cannot hide duplicate source identities", DuplicateIdentities));
        tests.Add(("capture/failed traversals release new source bindings and queued objects", FailedTraversal));
        tests.Add(("capture/unknown dirty roots do not bypass later rebinding validation", FailedDirtyPreparation));
        tests.Add(("capture/empty and exact-capacity sequences retain enumeration and detachment", SequenceEnumeration));
        tests.Add(("capture/sequence limits preserve enumeration order and disposal", SequenceBudgets));
        tests.Add(("capture/sequence exceptions retain identity and release their enumerator", SequenceFailures));
    }

    private static void FullTraversal()
    {
        var (document, first, second) = Drawing();
        first.LinkA = first; first.LinkB = second;
        second.LinkA = first; second.LinkB = second;
        var session = new ShadowSession(document);
        var before = session.Current;
        var secondId = Id(second);
        Require(first.Reads == 1 && second.Reads == 1);
        Require(ReferenceEquals(before, session.Refresh()));
        Require(first.Reads == 2 && second.Reads == 2, "A retained reference skipped a later full traversal.");
        second.Probe = 73;
        var changed = session.Refresh();
        Require(first.Reads == 3 && second.Reads == 3);
        Require(before.Read(secondId)["Probe"].Number() == 29);
        Require(changed.Read(secondId)["Probe"].Number() == 73);

        document.Entities.Remove(second);
        Require(session.Refresh().Objects.ContainsKey(secondId), "An object reachable only through another public reference was pruned.");
        first.LinkB = null;
        var removed = session.Refresh();
        Require(!removed.Objects.ContainsKey(secondId), "A cycle disconnected from the document was retained.");
        Require(ReferenceEquals(removed, session.Refresh()));
    }

    private static void DirtyTraversal()
    {
        var (document, first, second) = Drawing();
        first.LinkA = second; first.LinkB = second;
        second.LinkA = first; second.LinkB = first;
        var session = new ShadowSession(document);
        var before = session.Current;
        Require(ReferenceEquals(before, session.RefreshProperties([Id(first)])));
        Require(first.Reads == 2 && second.Reads == 1);
        second.Probe = 97;
        Require(ReferenceEquals(before, session.RefreshProperties([Id(first)])));
        Require(first.Reads == 3 && second.Reads == 1);
        var changed = session.RefreshProperties([Id(second), Id(first), Id(second)]);
        Require(first.Reads == 4 && second.Reads == 2, "A referenced dirty root was skipped or visited twice.");
        Require(changed.Read(Id(second))["Probe"].Number() == 97);
        Require(before.Read(Id(second))["Probe"].Number() == 29);
        Require(ReferenceEquals(changed, session.Refresh()));
        Require(first.Reads == 5 && second.Reads == 3);
    }

    private static void ReplacedReference()
    {
        var (document, first, second) = Drawing();
        var session = new ShadowSession(document);
        var before = session.Current;
        var oldColor = ((ShadowValue.Reference)before.Objects[Id(first)]["Color"]).Id;
        first.Color = second.Color = new AciColor(31, 47, 59);
        var changed = session.RefreshProperties([Id(first), Id(second)]);
        var firstReference = (ShadowValue.Reference)changed.Objects[Id(first)]["Color"];
        var secondReference = (ShadowValue.Reference)changed.Objects[Id(second)]["Color"];
        Require(ReferenceEquals(firstReference, secondReference));
        Require(changed.Objects.ContainsKey(firstReference.Id));
        Require(changed.Read(firstReference.Id)["R"].Number() == 31);
        Require(changed.Objects.ContainsKey(oldColor), "A dirty-only pass unexpectedly changed graph reachability.");
        var complete = session.Refresh();
        Require(!complete.Objects.ContainsKey(oldColor));
        Require(complete.Objects.ContainsKey(firstReference.Id));
        Require(ReferenceEquals(complete, session.Refresh()));
    }

    private static void DuplicateIdentities()
    {
        var (document, first, _) = Drawing();
        var session = new ShadowSession(document, new(FailOnUnreadableProperty: true));
        var before = session.Current;
        var duplicate = new ProbeLine();
        typeof(DxfObject).GetProperty(nameof(DxfObject.Handle))!.SetValue(duplicate, first.Handle);
        first.LinkA = duplicate; first.LinkB = duplicate;
        var failure = Expect<InvalidOperationException>(() => session.Refresh());
        Require(failure.Message.Contains("Duplicate source identity", StringComparison.Ordinal));
        Require(ReferenceEquals(before, session.Current));
        first.LinkA = null; first.LinkB = null;
        Require(ReferenceEquals(before, session.Refresh()), "Failed capture stamps affected the next pass.");

        first.LinkA = duplicate; first.LinkB = duplicate;
        var diagnosed = new ShadowSession(document).Current;
        Require(diagnosed.Diagnostics.Count(issue => issue.Code == "capture.property" && issue.Object == Id(first)) == 2,
            "One failed reference suppressed a later duplicate-identity diagnostic.");
        Require(diagnosed.Objects[Id(first)]["LinkA"] is ShadowValue.Fault);
        Require(diagnosed.Objects[Id(first)]["LinkB"] is ShadowValue.Fault);
    }

    private static void FailedTraversal()
    {
        var (document, first, _) = Drawing();
        var expectedCount = new ShadowSession(document).Current.Objects.Count;
        var session = new ShadowSession(document, new(MaximumObjects: expectedCount));
        var before = session.Current;
        var bindingCount = Bindings(session).Count;
        var weak = ExceedBudget(session, first);
        Require(Bindings(session).Count == bindingCount, "A failed dirty capture retained newly registered sources.");
        Require(ReferenceEquals(before, session.Current));
        for (var attempt = 0; attempt < 3 && weak.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Require(!weak.IsAlive, "A failed traversal retained a queued source after its public reference was removed.");
        Require(ReferenceEquals(before, session.RefreshProperties([Id(first)])));
        Require(ReferenceEquals(before, session.Refresh()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExceedBudget(ShadowSession session, ProbeLine first)
    {
        var detached = new ProbeLine();
        detached.LinkA = detached; detached.LinkB = first;
        first.LinkA = detached; first.LinkB = detached;
        var weak = new WeakReference(detached);
        try
        {
            var failure = Expect<InvalidOperationException>(() => session.RefreshProperties([Id(first)]));
            Require(failure.Message == "Shadow object budget exceeded.");
        }
        finally { first.LinkA = null; first.LinkB = null; }
        return weak;
    }

    private static void FailedDirtyPreparation()
    {
        var (document, first, _) = Drawing();
        var session = new ShadowSession(document);
        var before = session.Current;
        Expect<ShadowConflictException>(() => session.RefreshProperties([Id(first), new("absent") ]));
        var capture = Capture(session);
        // Structural replay can replace the live object bound to an existing ID.
        // A preview after either a successful or failed pass must check that binding.
        var replacement = new ProbeLine();
        Invoke(capture, "Bind", Id(first), replacement);
        var failure = Expect<InvalidOperationException>(() => Invoke(capture, "Preview", first));
        Require(failure.Message.Contains("Duplicate source identity", StringComparison.Ordinal));
        Invoke(capture, "Bind", Id(first), first);
        Require(ReferenceEquals(before, session.Refresh()));
    }

    private static void SequenceEnumeration()
    {
        var session = new ShadowSession(new DxfDocument());
        foreach (var length in new[] { 0, 1, 8, 9, 16 })
        {
            var source = new TrackingSequence(Enumerable.Range(0, length).Select(value => (object)value).ToArray());
            var value = (ShadowValue.Sequence)Invoke(Capture(session), "Preview", source)!;
            Require(value.Items.Length == length);
            for (var index = 0; index < length; index++) Require(value.Items[index] is ShadowValue.Atom atom && atom.Text == index.ToString());
            Require(source.Events.SequenceEqual(ExpectedEvents(length)));
            Require(source.Disposals == 1);
            Array.Fill(source.Values, 999);
            if (length != 0) Require(value.Items[0] is ShadowValue.Atom { Text: "0" }, "Sequence storage retained the mutable source array.");
        }
        var first = Invoke(Capture(session), "Preview", new TrackingSequence([]));
        var second = Invoke(Capture(session), "Preview", new TrackingSequence([]));
        Require(ReferenceEquals(first, second), "Empty detached sequences were repeatedly allocated.");
    }

    private static void SequenceBudgets()
    {
        var capture = NewCapture(new(MaximumCollectionItems: 2, MaximumValueDepth: 1));
        var source = new TrackingSequence([1, 2, 3]);
        var failure = Expect<InvalidOperationException>(() => Invoke(capture, "Preview", source));
        // A root external sequence reaches depth two for each non-null member.
        Require(failure.Message == "Shadow value nesting budget exceeded.");
        Require(source.Events.SequenceEqual(new[] { "enumerate", "move:0", "current:0", "dispose" }));
        Require(source.Disposals == 1);
        Require(Invoke(capture, "Preview", new TrackingSequence([])) is ShadowValue.Sequence { Items.IsEmpty: true });

        capture = NewCapture(new(MaximumCollectionItems: 2));
        source = new TrackingSequence([1, 2, 3]);
        failure = Expect<InvalidOperationException>(() => Invoke(capture, "Preview", source));
        Require(failure.Message == "Shadow collection budget exceeded.");
        Require(source.Events.SequenceEqual(new[]
        {
            "enumerate", "move:0", "current:0", "move:1", "current:1", "move:2", "current:2", "dispose"
        }));
        Require(source.Disposals == 1);
        Require(Invoke(capture, "Preview", new TrackingSequence([1, 2])) is ShadowValue.Sequence { Items.Length: 2 });
    }

    private static void SequenceFailures()
    {
        var capture = NewCapture(new());
        foreach (var at in new[] { 0, 1 })
        {
            var sentinel = new InvalidOperationException("enumeration sentinel");
            var source = new TrackingSequence([1, 2]) { ThrowAtMove = at, Failure = sentinel };
            Require(ReferenceEquals(sentinel, Expect<InvalidOperationException>(() => Invoke(capture, "Preview", source))));
            Require(source.Disposals == 1);
            Require(source.Events[^1] == "dispose");
        }
    }

    private static IEnumerable<string> ExpectedEvents(int length)
    {
        yield return "enumerate";
        for (var index = 0; index < length; index++) { yield return "move:" + index; yield return "current:" + index; }
        yield return "move:" + length;
        yield return "dispose";
    }

    private static (DxfDocument Document, ProbeLine First, ProbeLine Second) Drawing()
    {
        var document = new DxfDocument();
        var first = new ProbeLine { Probe = 17 };
        var second = new ProbeLine { Probe = 29 };
        document.Entities.Add(first); document.Entities.Add(second);
        return (document, first, second);
    }

    private static readonly Type CaptureType = typeof(ShadowSession).Assembly.GetType("netDxf.Shadow.NetDxf.ShadowCapture", true)!;
    private static object NewCapture(ShadowCaptureOptions options) => Activator.CreateInstance(CaptureType, options)!;
    private static object Capture(ShadowSession session) => typeof(ShadowSession)
        .GetField("capture", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
    private static Dictionary<ObjectId, object> Bindings(ShadowSession session) =>
        (Dictionary<ObjectId, object>)Invoke(Capture(session), "RetainBindings")!;
    private static object? Invoke(object target, string method, params object?[] arguments)
    {
        try { return target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments); }
        catch (TargetInvocationException failure) when (failure.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static T Expect<T>(Action run) where T : Exception
    {
        try { run(); }
        catch (T failure) { return failure; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Require(bool value, string message = "Capture traversal regression failed.")
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class ProbeLine : Line
    {
        private double probe;
        internal int Reads;
        public EntityObject? LinkA { get; set; }
        public EntityObject? LinkB { get; set; }
        public double Probe { get { Reads++; return probe; } set => probe = value; }
    }

    // Count deliberately throws: capture must enumerate even an apparently empty
    // external collection instead of trusting a separate, observable count getter.
    private sealed class TrackingSequence(object[] values) : ICollection
    {
        internal object[] Values { get; } = values;
        internal List<string> Events { get; } = [];
        internal int Disposals;
        internal int ThrowAtMove = -1;
        internal Exception? Failure;
        public int Count => throw new InvalidOperationException("Do not inspect Count.");
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public IEnumerator GetEnumerator() { Events.Add("enumerate"); return new Cursor(this); }

        private sealed class Cursor(TrackingSequence source) : IEnumerator, IDisposable
        {
            private int index = -1;
            public object Current { get { source.Events.Add("current:" + index); return source.Values[index]; } }
            public bool MoveNext()
            {
                index++; source.Events.Add("move:" + index);
                if (index == source.ThrowAtMove) throw source.Failure!;
                return index < source.Values.Length;
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() { source.Disposals++; source.Events.Add("dispose"); }
        }
    }
}
