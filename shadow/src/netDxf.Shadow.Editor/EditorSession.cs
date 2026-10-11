using System.Collections.Immutable;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

public sealed record EditHistoryEntry(string Description, ImmutableArray<ShadowPropertyEdit> Changes);
public sealed record EditorState(long Revision, ImmutableHashSet<string> Selection, int UndoCount, int RedoCount);

/// <summary>Revision-checked editing over a source or an isolated immutable branch.</summary>
public sealed class EditorSession
{
    private readonly object gate = new();
    private readonly IShadowStore store;
    private readonly int historyLimit;
    private ImmutableList<EditHistoryEntry> undo = ImmutableList<EditHistoryEntry>.Empty;
    private ImmutableList<EditHistoryEntry> redo = ImmutableList<EditHistoryEntry>.Empty;
    private ImmutableHashSet<string> selection = ImmutableHashSet<string>.Empty;
    private long expectedRevision;

    public EditorSession(IShadowStore store, int historyLimit = 100)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        if (historyLimit < 1) throw new ArgumentOutOfRangeException(nameof(historyLimit));
        this.historyLimit = historyLimit;
        expectedRevision = store.Current.Revision;
    }

    public ShadowSnapshot Snapshot => store.Current;
    public EditorState State
    {
        get { lock (gate) return new EditorState(expectedRevision, selection, undo.Count, redo.Count); }
    }
    public ImmutableArray<string> UndoDescriptions
    {
        get
        {
            lock (gate)
            {
                var result = ImmutableArray.CreateBuilder<string>(undo.Count);
                foreach (var entry in undo) result.Add(entry.Description);
                return result.MoveToImmutable();
            }
        }
    }

    public void Select(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        lock (gate) selection = keys.ToImmutableHashSet(StringComparer.Ordinal);
    }

    public void ToggleSelection(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (gate) selection = selection.Contains(key) ? selection.Remove(key) : selection.Add(key);
    }

    public ShadowSnapshot Execute(string description, ShadowEditBatch batch)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(batch);
        lock (gate)
        {
            EnsureCurrent();
            var before = store.Current;
            var after = store.Apply(batch);
            expectedRevision = after.Revision;
            if (ReferenceEquals(before, after)) return after;

            // Record the source's canonical values after validation/normalization.
            var canonical = ImmutableArray.CreateBuilder<ShadowPropertyEdit>();
            foreach (var edit in batch.Edits)
            {
                var oldValue = before.Objects[edit.Object][edit.Property];
                var newValue = after.Objects[edit.Object][edit.Property];
                if (!ShadowValue.Equivalent(oldValue, newValue))
                    canonical.Add(new ShadowPropertyEdit(edit.Object, edit.Property, oldValue, newValue));
            }
            if (canonical.Count == 0) return after;
            undo = undo.Add(new EditHistoryEntry(description, canonical.ToImmutable()));
            if (undo.Count > historyLimit) undo = undo.RemoveAt(0);
            redo = ImmutableList<EditHistoryEntry>.Empty;
            return after;
        }
    }

    public ShadowSnapshot SetProperty(ObjectId id, string property, ShadowValue value)
    {
        lock (gate)
        {
            EnsureCurrent();
            var snapshot = store.Current;
            if (!snapshot.Objects.TryGetValue(id, out var node)) throw new ArgumentException("Unknown object.", nameof(id));
            return Execute($"Set {node.Type}.{property}", ShadowEditBatch.Create(snapshot,
                new ShadowPropertyEdit(id, property, node[property], value)));
        }
    }

    public ShadowSnapshot Translate(RenderScene scene, Vec3 delta, bool editBlockDefinition = false)
    {
        lock (gate)
        {
            EnsureCurrent();
            var snapshot = store.Current;
            if (scene.DocumentId != snapshot.DocumentId || scene.Revision != snapshot.Revision)
                throw new ShadowConflictException("Selection belongs to an obsolete rendered snapshot.");
            var targets = new HashSet<ObjectId>();
            foreach (var item in scene.Items)
            {
                if (!selection.Contains(item.Key)) continue;
                targets.Add(!editBlockDefinition && !item.InsertPath.IsEmpty ? item.InsertPath[0] : item.Entity);
            }
            return Execute("Translate selection", EditOperations.Translate(snapshot, targets, delta));
        }
    }

    public ShadowSnapshot Undo()
    {
        lock (gate)
        {
            EnsureCurrent();
            if (undo.Count == 0) return store.Current;
            var entry = undo[undo.Count - 1];
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
            var after = store.Apply(new ShadowEditBatch(store.Current.DocumentId, expectedRevision, entry.Changes));
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
