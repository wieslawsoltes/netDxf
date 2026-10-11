using System.Collections.Immutable;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

public sealed record EditHistoryEntry(string Description, ImmutableArray<ShadowPropertyEdit> Changes,
    ShadowSnapshot? Before = null, ShadowSnapshot? After = null);
public sealed record EditorState(long Revision, ImmutableHashSet<string> Selection, int UndoCount, int RedoCount);

/// <summary>Revision-checked editing over a source or an isolated immutable branch.</summary>
public sealed partial class EditorSession
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

    public ShadowSnapshot Execute(string description, ShadowGraphPatch patch)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(patch);
        lock (gate)
        {
            EnsureCurrent();
            if (store is not IShadowGraphStore structural) throw new NotSupportedException("This store does not support structural transactions.");
            var before = store.Current;
            var after = structural.Apply(patch);
            expectedRevision = after.Revision;
            if (ReferenceEquals(before, after)) return after;
            undo = undo.Add(new(description, [], before, after));
            if (undo.Count > historyLimit) undo = undo.RemoveAt(0);
            redo = ImmutableList<EditHistoryEntry>.Empty;
            return after;
        }
    }

    public ObjectId CreateEntity(string typeName, IEnumerable<KeyValuePair<string, ShadowValue>> properties, string layout = "Model")
    {
        lock (gate)
        {
            EnsureCurrent();
            var snapshot = store.Current;
            var block = snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown layout.", nameof(layout));
            var edit = new ShadowGraphEdit(snapshot);
            var id = edit.AddEntity(block, typeName, properties);
            Execute("Create " + typeName[(typeName.LastIndexOf('.') + 1)..], edit.Build());
            return id;
        }
    }

    public ShadowSnapshot DeleteEntities(IEnumerable<ObjectId> entities)
    {
        lock (gate)
        {
            EnsureCurrent();
            var edit = new ShadowGraphEdit(store.Current);
            edit.RemoveEntities(entities);
            var after = Execute("Delete entities", edit.Build());
            selection = ImmutableHashSet<string>.Empty;
            return after;
        }
    }

    public ShadowSnapshot ReplaceCollection(ObjectId id, string property, IEnumerable<ShadowValue> values)
    {
        lock (gate)
        {
            EnsureCurrent();
            var edit = new ShadowGraphEdit(store.Current);
            edit.ReplaceSequence(id, property, values);
            return Execute("Edit " + property, edit.Build());
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
            return Execute("Translate selection", SelectionTranslation.Plan(snapshot, scene, selection, delta, editBlockDefinition));
        }
    }

}
