using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace netDxf.Shadow.NetDxf;

public sealed partial class ShadowSession
{
    // Only the source adapter owns these mutable bindings. Snapshots and history records
    // contain no source references. Weak keys release retired source objects when callers
    // release the corresponding immutable history snapshots.
    private readonly ConditionalWeakTable<ShadowSnapshot, Dictionary<ObjectId, object>> historyBindings = new();
    public ShadowObjectFactories ObjectFactories { get; } = new();

    public ShadowSnapshot Apply(ShadowGraphPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        lock (gate)
        using (EnterSourceOperation())
        {
            var target = patch.Apply(current);
            if (ReferenceEquals(target, current)) return current;
            // Structural adoption always validates the complete captured graph, including
            // sessions that opt into edited-property validation for scalar batches.
            EnsureSourceCurrent();
            var before = current;
            var bindings = capture.RetainBindings();
            historyBindings.GetValue(before, _ => bindings);
            Dictionary<ObjectId, object>? historical = null;
            if (patch.HistoricalTarget is { } old) historyBindings.TryGetValue(old, out historical);
            var additions = patch.Changes.Where(e => e.Before is null && e.After is not null).ToDictionary(e => e.Id, e => e.After!);
            var constructing = new HashSet<ObjectId>();
            Resolver resolver = null!;
            resolver = new Resolver(Resolve, (value, type) => ConvertValue(value, type, Resolve));
            var writes = new List<(object Source, SourceAccessor Accessor, object? Before, object? After)>();
            var collections = new List<SourceCollectionPlan>();
            var methods = new List<SourceMethodPlan>();
            var collectionSources = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var assertions = new List<(ObjectId Id, string Property, ShadowValue Value)>();
            // Preparation allocates only new objects and detached constructor state.
            foreach (var id in additions.Keys) Resolve(id);
            foreach (var change in patch.Changes.Where(e => e.After is not null))
            {
                var source = Resolve(change.Id);
                if (change.Before is { } oldNode && SourceMethodPlan.Create(source, oldNode, change.After!) is { } method) methods.Add(method);
                foreach (var (name, value) in change.After!.Properties)
                {
                    if (change.Before is { } previous && ShadowValue.Equivalent(previous[name], value)) continue;
                    if (name is "Handle" or "Owner" or "Type" or "CodeName") continue;
                    // The source allocator is monotonic; undo restores semantic objects,
                    // never rewinds handles that may already have been assigned elsewhere.
                    if (historical is not null && name == "HandleSeed") continue;
                    var accessor = SourceAccessors.For(source.GetType()).FirstOrDefault(a => a.Name == name);
                    if (name == "$items" || accessor is { CanWrite: false } && value is ShadowValue.Sequence)
                    {
                        if (value is not ShadowValue.Sequence sequence) throw new ArgumentException("Collection membership must be a sequence.");
                        var collectionSource = name == "$items" ? source : accessor!.Get(source);
                        var plan = SourceCollectionPlan.TryCreate(collectionSource, sequence, resolver.Convert);
                        if (plan is not null && !plan.IsNoOp && collectionSources.Add(collectionSource!)) collections.Add(plan);
                        else if (plan is null && change.Before is not null) assertions.Add((change.Id, name, value));
                        continue;
                    }
                    if (accessor is null) throw new NotSupportedException($"Unknown source property {change.After.Type}.{name}.");
                    if (!accessor.CanWrite)
                    {
                        if (change.Before is not null) assertions.Add((change.Id, name, value));
                        continue;
                    }
                    var oldValue = accessor.Get(source);
                    if (ShadowValue.Equivalent(capture.Preview(oldValue), value)) continue;
                    var newValue = resolver.Convert(value, accessor.ValueType);
                    writes.Add((source, accessor, oldValue, newValue));
                }
            }
            foreach (var group in writes.GroupBy(w => w.Source, ReferenceEqualityComparer.Instance))
            {
                if (group.Key is not ICloneable cloneable) continue;
                var clone = cloneable.Clone();
                foreach (var write in group) write.Accessor.Set(clone, write.After);
            }
            var attempted = 0;
            var attemptedMethods = 0;
            var touchedCollections = false;
            try
            {
                foreach (var method in methods) { attemptedMethods++; method.Apply(); }
                foreach (var write in writes)
                {
                    attempted++;
                    write.Accessor.Set(write.Source, write.After);
                }
                // Moving entities across owners must release every old owner first.
                touchedCollections = collections.Count != 0;
                foreach (var plan in collections) plan.RemoveOld();
                foreach (var plan in collections) plan.AddNew();
                var next = capture.Capture(document, current);
                foreach (var assertion in assertions)
                {
                    if (!next.Objects.TryGetValue(assertion.Id, out var node) || !ShadowValue.Equivalent(node[assertion.Property], assertion.Value))
                        throw new NotSupportedException($"Read-only source property {assertion.Id}.{assertion.Property} was not regenerated by this transaction.");
                }
                foreach (var change in patch.Changes)
                {
                    if (change.After is null && next.Objects.ContainsKey(change.Id))
                        throw new InvalidOperationException($"Source object {change.Id} is still referenced and cannot be removed.");
                    if (change.Before is null && change.After is not null && !next.Objects.ContainsKey(change.Id))
                        throw new InvalidOperationException($"New source object {change.Id} was not attached to the public document graph.");
                }
                historyBindings.GetValue(next, _ => capture.RetainBindings());
                return Publish(next);
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error.GetBaseException() };
                if (touchedCollections)
                {
                    // Clear every destination before restoring any source owner.
                    foreach (var plan in collections.AsEnumerable().Reverse())
                        try { plan.ClearForRollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    foreach (var plan in collections)
                        try { plan.Restore(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                }
                for (var index = attempted - 1; index >= 0; index--)
                    try { writes[index].Accessor.Set(writes[index].Source, writes[index].Before); }
                    catch (Exception e) { failures.Add(e.GetBaseException()); }
                for (var index = attemptedMethods - 1; index >= 0; index--)
                    try { methods[index].Rollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                try
                {
                    if (!ReferenceEquals(capture.Capture(document, before), before))
                        failures.Add(new InvalidOperationException("Structural rollback did not restore the complete public graph."));
                }
                catch (Exception e) { failures.Add(e.GetBaseException()); }
                if (failures.Count > 1)
                {
                    faulted = true;
                    throw new AggregateException("Structural rollback failed; this source session is fail-stopped.", failures);
                }
                throw new InvalidOperationException("The source rejected the structural transaction; changes were rolled back.", failures[0]);
            }

            object Resolve(ObjectId id)
            {
                if (bindings.TryGetValue(id, out var source)) return source;
                if (historical is not null && historical.TryGetValue(id, out source))
                {
                    bindings[id] = source; capture.Bind(id, source); return source;
                }
                if (!additions.TryGetValue(id, out var node)) throw new ShadowConflictException($"No source binding for {id}.");
                if (!constructing.Add(id)) throw new ArgumentException($"Cyclic constructor dependency at {id}.");
                try
                {
                    // Read-only owned collections must be bound to the new owner's actual
                    // collection instance, not constructed as an unrelated empty container.
                    foreach (var parent in additions.Values)
                    {
                        foreach (var property in parent.Properties.Where(p => p.Value is ShadowValue.Reference r && r.Id == id))
                        {
                            var parentType = typeof(DxfDocument).Assembly.GetType(parent.TypeName);
                            var getter = parentType is null ? null : SourceAccessors.For(parentType).FirstOrDefault(a => a.Name == property.Key && !a.CanWrite);
                            if (getter is null || constructing.Contains(parent.Id)) continue;
                            var owned = getter.Get(Resolve(parent.Id));
                            if (owned is null || owned.GetType().FullName != node.TypeName) continue;
                            bindings[id] = owned; capture.Bind(id, owned); return owned;
                        }
                    }
                    source = ObjectFactories.Create(node, resolver!);
                    bindings[id] = source; capture.Bind(id, source); return source;
                }
                finally { constructing.Remove(id); }
            }
        }
    }

    private sealed class Resolver(Func<ObjectId, object> resolve, Func<ShadowValue, Type, object?> convert) : IShadowObjectResolver
    {
        public object Resolve(ObjectId id) => resolve(id);
        public object? Convert(ShadowValue value, Type targetType) => convert(value, targetType);
    }
}
