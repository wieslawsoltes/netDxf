using System.Collections.Immutable;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("netDxf.Shadow.NetDxf")]

namespace netDxf.Shadow;

/// <summary>Acceleration metadata lives outside the immutable model and never retains its parent snapshot.</summary>
internal static class ShadowChangeJournal
{
    private sealed record Entry(WeakReference<ShadowSnapshot> Parent, ShadowDelta Delta);
    private static readonly ConditionalWeakTable<ShadowSnapshot, Entry> Entries = new();
    internal static void Record(ShadowSnapshot before, ShadowSnapshot after, IEnumerable<ObjectId> added,
        IEnumerable<ObjectId> changed, IEnumerable<ObjectId> removed)
    {
        if (ReferenceEquals(before, after)) return;
        var delta = new ShadowDelta(before.Revision, after.Revision, added.ToImmutableHashSet(), changed.ToImmutableHashSet(), removed.ToImmutableHashSet());
        Entries.Add(after, new(new(before), delta));
    }
    internal static bool TryGet(ShadowSnapshot before, ShadowSnapshot after, out ShadowDelta delta)
    {
        if (Entries.TryGetValue(after, out var entry) && entry.Parent.TryGetTarget(out var parent) && ReferenceEquals(before, parent))
        { delta = entry.Delta; return true; }
        delta = null!; return false;
    }
}
