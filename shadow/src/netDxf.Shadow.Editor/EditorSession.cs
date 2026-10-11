using System.Collections.Immutable;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

public sealed record EditHistoryEntry(string Description, ImmutableArray<ShadowPropertyEdit> Changes);
public sealed record EditorState(long Revision, ImmutableHashSet<string> Selection, int UndoCount, int RedoCount);

/// <summary>
/// Backend- and source-independent editing over IShadowStore. History contains canonical
/// property values, not live source objects. An external source change invalidates history
/// until the host explicitly resets it; undo never overwrites an unrelated writer.
/// </summary>
public sealed class EditorSession
{
    private readonly object gate = new();
    private readonly IShadowStore store;
    private readonly int historyLimit;
    private ImmutableList<EditHistoryEntry> undo = [];
    private ImmutableList<EditHistoryEntry> redo = [];
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
    public EditorState State { get { lock (gate) return new(expectedRevision, selection, undo.Count, redo.Count); } }
    public ImmutableArray<string> UndoDescriptions { get { lock (gate) return undo.Select(e => e.Description).ToImmutableArray(); } }

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
        lock (gate)
        {
            EnsureCurrent();
            var before = store.Current;
            var after = store.Apply(batch);
            expectedRevision = after.Revision;
            if (ReferenceEquals(before, after)) return after;
            var canonical = batch.Edits.Select(edit => edit with
            {
                Before = before.Objects[edit.Object][edit.Property],
                After = after.Objects[edit.Object][edit.Property]
            }).Where(edit => !ShadowValue.Equivalent(edit.Before, edit.After)).ToImmutableArray();
            if (canonical.IsEmpty) return after;
            undo = undo.Add(new(description, canonical));
            if (undo.Count > historyLimit) undo = undo.RemoveAt(0);
            redo = [];
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
            return Execute($"Set {node.Type}.{property}", ShadowEditBatch.Create(snapshot, new(id, property, node[property], value)));
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
            var ids = scene.Items.Where(item => selection.Contains(item.Key)).Select(item =>
                !editBlockDefinition && !item.InsertPath.IsEmpty ? item.InsertPath[0] : item.Entity).Distinct();
            return Execute("Translate selection", EditOperations.Translate(snapshot, ids, delta));
        }
    }

    public ShadowSnapshot Undo()
    {
        lock (gate)
        {
            EnsureCurrent();
            if (undo.IsEmpty) return store.Current;
            var entry = undo[^1];
            var current = store.Current;
            var changes = entry.Changes.Reverse().Select(e => new ShadowPropertyEdit(e.Object, e.Property, e.After, e.Before)).ToImmutableArray();
            var after = store.Apply(new(current.DocumentId, expectedRevision, changes));
            expectedRevision = after.Revision;
            undo = undo.RemoveAt(undo.Count - 1); redo = redo.Add(entry);
            return after;
        }
    }
    public ShadowSnapshot Redo()
    {
        lock (gate)
        {
            EnsureCurrent();
            if (redo.IsEmpty) return store.Current;
            var entry = redo[^1];
            var after = store.Apply(new(store.Current.DocumentId, expectedRevision, entry.Changes));
            expectedRevision = after.Revision;
            redo = redo.RemoveAt(redo.Count - 1); undo = undo.Add(entry);
            return after;
        }
    }
    public void ResetHistory(bool clearSelection = false)
    {
        lock (gate)
        {
            undo = []; redo = []; expectedRevision = store.Current.Revision;
            if (clearSelection) selection = ImmutableHashSet<string>.Empty;
        }
    }
    private void EnsureCurrent()
    {
        if (store.Current.Revision != expectedRevision)
            throw new ShadowConflictException("The source changed outside this editor. Refresh the view and explicitly reset history before editing.");
    }
}
