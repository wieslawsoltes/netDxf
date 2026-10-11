using System.Collections.Immutable;

namespace netDxf.Shadow.Editor;

public sealed partial class EditorSession
{
    public ShadowSnapshot Undo()
    {
        lock (gate)
        {
            EnsureCurrent();
            if (undo.Count == 0) return store.Current;
            var entry = undo[undo.Count - 1];
            if (entry.Before is not null)
            {
                var restored = ((IShadowGraphStore)store).Apply(ShadowGraphPatch.Between(store.Current, entry.Before));
                expectedRevision = restored.Revision;
                undo = undo.RemoveAt(undo.Count - 1);
                redo = redo.Add(entry);
                return restored;
            }
            var changes = ImmutableArray.CreateBuilder<ShadowPropertyEdit>(entry.Changes.Length);
            for (var index = entry.Changes.Length - 1; index >= 0; index--)
            {
                var edit = entry.Changes[index];
                changes.Add(new ShadowPropertyEdit(edit.Object, edit.Property, edit.After, edit.Before));
            }
            var after = store.Apply(new ShadowEditBatch(store.Current.DocumentId, expectedRevision, changes.MoveToImmutable()));
            expectedRevision = after.Revision;
            undo = undo.RemoveAt(undo.Count - 1);
            redo = redo.Add(entry);
            return after;
        }
    }

    public ShadowSnapshot Redo()
    {
        lock (gate)
        {
            EnsureCurrent();
            if (redo.Count == 0) return store.Current;
            var entry = redo[redo.Count - 1];
            var after = entry.After is not null
                ? ((IShadowGraphStore)store).Apply(ShadowGraphPatch.Between(store.Current, entry.After))
                : store.Apply(new ShadowEditBatch(store.Current.DocumentId, expectedRevision, entry.Changes));
            expectedRevision = after.Revision;
            redo = redo.RemoveAt(redo.Count - 1);
            undo = undo.Add(entry);
            return after;
        }
    }

    public void ResetHistory(bool clearSelection = false)
    {
        lock (gate)
        {
            undo = ImmutableList<EditHistoryEntry>.Empty;
            redo = ImmutableList<EditHistoryEntry>.Empty;
            expectedRevision = store.Current.Revision;
            if (clearSelection) selection = ImmutableHashSet<string>.Empty;
        }
    }

    private void EnsureCurrent()
    {
        if (store.Current.Revision != expectedRevision)
            throw new ShadowConflictException("The source changed outside this editor. Refresh the view and explicitly reset history before editing.");
    }
}
