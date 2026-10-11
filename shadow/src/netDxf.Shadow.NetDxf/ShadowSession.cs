namespace netDxf.Shadow.NetDxf;

/// <summary>
/// Single-writer adapter. Current is lock-free and detached. All live-source access must
/// use this gate, or be externally serialized before Refresh. Dispose is not required.
/// </summary>
public sealed partial class ShadowSession : IShadowGraphStore
{
    private readonly object gate = new();
    private readonly DxfDocument document;
    private readonly ShadowCapture capture;
    private ShadowSnapshot current;
    private bool faulted;

    public ShadowSession(DxfDocument document, ShadowCaptureOptions? options = null)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        capture = new(options ?? new());
        current = capture.Capture(document, null);
    }

    public ShadowSnapshot Current => Volatile.Read(ref current);

    public ShadowSnapshot Refresh()
    {
        lock (gate)
        {
            EnsureHealthy();
            return Publish(capture.Capture(document, current));
        }
    }

    /// <summary>Refresh known dirty public objects without scanning unrelated nodes. Only valid when ownership/collections have not changed.</summary>
    public ShadowSnapshot RefreshProperties(IEnumerable<ObjectId> dirtyObjects)
    {
        ArgumentNullException.ThrowIfNull(dirtyObjects);
        lock (gate)
        {
            EnsureHealthy();
            return Publish(capture.Capture(document, current, dirtyObjects.Distinct().ToArray()));
        }
    }

    public T ReadSource<T>(Func<DxfDocument, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (gate) { EnsureHealthy(); return read(document); }
    }

    /// <summary>
    /// Explicit source escape hatch for topology/add/remove operations. Unlike Apply,
    /// arbitrary callbacks are not transactions. Even a failing callback is reconciled.
    /// Never retain mutable source references from this callback.
    /// </summary>
    public ShadowSnapshot MutateSource(Action<DxfDocument> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        lock (gate)
        {
            EnsureHealthy();
            try { mutation(document); }
            finally
            {
                try { Publish(capture.Capture(document, current)); }
                catch { faulted = true; throw; }
            }
            return current;
        }
    }

    public ShadowSnapshot Apply(ShadowEditBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (gate)
        {
            EnsureHealthy();
            _ = current.Apply(batch); // Validate the complete immutable transaction before touching source.
            if (batch.Edits.IsDefaultOrEmpty) return current;
            var writes = new List<(object Source, SourceAccessor Accessor, object? Before, object? After)>();
            foreach (var edit in batch.Edits)
            {
                var source = capture.Source(edit.Object);
                var accessor = SourceAccessors.For(source.GetType()).SingleOrDefault(a => a.Name == edit.Property && a.CanWrite)
                    ?? throw new InvalidOperationException($"{edit.Object}.{edit.Property} is not writable.");
                var before = accessor.Get(source);
                if (!ShadowValue.Equivalent(capture.Preview(before), edit.Before))
                    throw new ShadowConflictException($"Source {edit.Object}.{edit.Property} changed outside the session. Refresh first.");
                writes.Add((source, accessor, before, ConvertValue(edit.After, accessor.ValueType)));
            }
            // Exercise normal entity/table validation on detached clones when available.
            foreach (var group in writes.GroupBy(w => w.Source, ReferenceEqualityComparer.Instance))
            {
                if (group.Key is not ICloneable cloneable) continue;
                var clone = cloneable.Clone();
                foreach (var write in group) write.Accessor.Set(clone, write.After);
            }
            var attempted = 0;
            try
            {
                foreach (var write in writes)
                {
                    attempted++;
                    write.Accessor.Set(write.Source, write.After);
                }
                // Only explicitly audited scalar/coordinate setters may use the fast path.
                var dirty = batch.Edits.All(IsLocalProperty) ? batch.Edits.Select(e => e.Object).Distinct().ToArray() : null;
                return Publish(capture.Capture(document, current, dirty));
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error.GetBaseException() };
                for (var i = attempted - 1; i >= 0; i--)
                    try { writes[i].Accessor.Set(writes[i].Source, writes[i].Before); }
                    catch (Exception rollback) { failures.Add(rollback.GetBaseException()); }
                try
                {
                    var restored = capture.Capture(document, current);
                    if (!ReferenceEquals(restored, current))
                    {
                        // Do not claim a successful rollback when source-side events changed other data.
                        failures.Add(new InvalidOperationException("Rollback did not restore the full public source graph."));
                    }
                }
                catch (Exception reconcile) { failures.Add(reconcile.GetBaseException()); }
                if (failures.Count > 1)
                {
                    faulted = true;
                    throw new AggregateException("Source rollback failed. This session is fail-stopped; inspect the document and attach a new session.", failures);
                }
                throw new InvalidOperationException("The source rejected the edit; changes were rolled back.", failures[0]);
            }
        }
    }

    private bool IsLocalProperty(ShadowPropertyEdit edit)
    {
        var type = current.Objects[edit.Object].Type;
        return (type, edit.Property) switch
        {
            ("Line", "StartPoint" or "EndPoint" or "Thickness" or "IsVisible") => true,
            ("Circle", "Center" or "Radius" or "Thickness" or "IsVisible") => true,
            ("Arc", "Center" or "Radius" or "StartAngle" or "EndAngle" or "IsVisible") => true,
            ("Polyline2DVertex", "Position" or "Bulge" or "StartWidth" or "EndWidth") => true,
            ("Text", "Value" or "Position" or "Height" or "Rotation" or "WidthFactor" or "ObliqueAngle" or "IsVisible") => true,
            _ => false
        };
    }

    private ShadowSnapshot Publish(ShadowSnapshot snapshot) { Volatile.Write(ref current, snapshot); return snapshot; }
    private void EnsureHealthy()
    {
        if (faulted) throw new InvalidOperationException("The source session is faulted. Reattach after inspecting the mutable document.");
    }
}
