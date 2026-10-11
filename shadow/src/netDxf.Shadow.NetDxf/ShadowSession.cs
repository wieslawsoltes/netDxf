using System.Collections;
using System.Globalization;
using System.Reflection;

namespace netDxf.Shadow.NetDxf;

/// <summary>
/// Single-writer adapter. Current is lock-free and detached. All live-source access must
/// use this gate, or be externally serialized before Refresh. Dispose is not required.
/// </summary>
public sealed class ShadowSession : IShadowStore
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
                writes.Add((source, accessor, before, ConvertValue(edit.After, accessor.Property.PropertyType)));
            }
            // Exercise normal entity/table validation on detached clones when available.
            foreach (var group in writes.GroupBy(w => w.Source, ReferenceEqualityComparer.Instance))
            {
                if (group.Key is not ICloneable cloneable) continue;
                var clone = cloneable.Clone();
                foreach (var write in group) write.Accessor.Property.SetValue(clone, write.After);
            }
            var attempted = 0;
            try
            {
                foreach (var write in writes)
                {
                    attempted++;
                    write.Accessor.Property.SetValue(write.Source, write.After);
                }
                // Only explicitly audited scalar/coordinate setters may use the fast path.
                var dirty = batch.Edits.All(IsLocalProperty) ? batch.Edits.Select(e => e.Object).Distinct().ToArray() : null;
                return Publish(capture.Capture(document, current, dirty));
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error.GetBaseException() };
                for (var i = attempted - 1; i >= 0; i--)
                    try { writes[i].Accessor.Property.SetValue(writes[i].Source, writes[i].Before); }
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

    private object? ConvertValue(ShadowValue value, Type target)
    {
        var nullable = Nullable.GetUnderlyingType(target);
        if (value is ShadowValue.NullValue)
        {
            if (target.IsValueType && nullable is null) throw new ArgumentException($"{target.Name} cannot be null.");
            return null;
        }
        target = nullable ?? target;
        if (value is ShadowValue.Reference reference)
        {
            var source = capture.Source(reference.Id);
            return target.IsInstanceOfType(source) ? source : throw new ArgumentException("Incompatible source reference.");
        }
        if (value is ShadowValue.Atom atom)
        {
            if (target.IsEnum) return Enum.Parse(target, atom.Text, false);
            if (target == typeof(Guid)) return Guid.Parse(atom.Text);
            if (target == typeof(DateTime)) return DateTime.Parse(atom.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(atom.Text, CultureInfo.InvariantCulture);
            if (target == typeof(TimeSpan)) return TimeSpan.Parse(atom.Text, CultureInfo.InvariantCulture);
            var result = Convert.ChangeType(atom.Text, target, CultureInfo.InvariantCulture);
            if (result is double d && !double.IsFinite(d) || result is float f && !float.IsFinite(f))
                throw new ArgumentException("Edits must contain finite numbers.");
            return result;
        }
        if (value is ShadowValue.Structure structure && target.IsValueType)
        {
            double Number(string name) => structure.Fields.TryGetValue(name, out var field) ? Convert.ToDouble(ConvertValue(field, typeof(double)), CultureInfo.InvariantCulture) : 0;
            if (target == typeof(Vector2)) return new Vector2(Number("X"), Number("Y"));
            if (target == typeof(Vector3)) return new Vector3(Number("X"), Number("Y"), Number("Z"));
            var result = Activator.CreateInstance(target)!;
            foreach (var (name, field) in structure.Fields)
            {
                var property = target.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (property?.SetMethod?.IsPublic != true) throw new NotSupportedException($"Cannot write {target.Name}.{name}.");
                property.SetValue(result, ConvertValue(field, property.PropertyType));
            }
            return result;
        }
        if (value is ShadowValue.Sequence sequence)
        {
            var element = target.IsArray ? target.GetElementType() : target.GetInterfaces().Append(target)
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (element is null) throw new NotSupportedException($"Cannot construct collection {target.Name}.");
            var array = Array.CreateInstance(element, sequence.Items.Length);
            for (var i = 0; i < array.Length; i++) array.SetValue(ConvertValue(sequence.Items[i], element), i);
            if (target.IsArray) return array;
            var listType = typeof(List<>).MakeGenericType(element);
            if (!target.IsAssignableFrom(listType)) throw new NotSupportedException($"Collection {target.Name} needs a custom editor.");
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in array) list.Add(item);
            return list;
        }
        throw new NotSupportedException($"Cannot convert {value.GetType().Name} to {target.Name}.");
    }

    private ShadowSnapshot Publish(ShadowSnapshot snapshot) { Volatile.Write(ref current, snapshot); return snapshot; }
    private void EnsureHealthy()
    {
        if (faulted) throw new InvalidOperationException("The source session is faulted. Reattach after inspecting the mutable document.");
    }
}
