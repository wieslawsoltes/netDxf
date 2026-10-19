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
            // Undo/redo can restore every binding from history without building an index.
            Dictionary<ObjectId, List<(ObjectId Owner, SourceAccessor Accessor)>>? ownedReferences = null;
            var constructing = new HashSet<ObjectId>();
            var implicitInserts = new List<(ObjectId Id, netDxf.Entities.Insert Insert)>();
            Resolver resolver = null!;
            resolver = new Resolver(Resolve, (value, type) => ConvertValue(value, type, Resolve));
            var writes = new List<(object Source, SourceAccessor Accessor, object? Before, object? After)>();
            var collections = new List<SourceCollectionPlan>();
            var methods = new List<SourceMethodPlan>();
            var collectionSources = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var assertions = new List<(ObjectId Id, string Property, ShadowValue Value)>();
            // Preparation allocates only new objects and detached constructor state.
            foreach (var id in additions.Keys) Resolve(id);
            var insertDefaults = implicitInserts.Select(entry => SourceImplicitInsertPlan.Prepare(entry.Id, entry.Insert, current, target, Resolve, resolver.Convert)).ToArray();
            var attributeIdentities = SourceAttributeIdentityPlan.Prepare(patch, target, Resolve, historical is not null ? patch.HistoricalTarget : null);
            foreach (var change in patch.Changes.Where(e => e.After is not null))
            {
                var source = Resolve(change.Id);
                var method = change.Before is { } oldNode
                    ? SourceMethodPlan.AttributeMembership(source, oldNode, change.After!, current, target, resolver.Convert)
                        ?? SourceMethodPlan.Create(source, oldNode, change.After!, source is netDxf.Tables.TextStyle && SourceMethodPlan.XDataGraphChanged(current, target, change.Id, patch))
                    : null;
                if (method is not null) methods.Add(method);
                foreach (var (name, value) in change.After!.Properties)
                {
                    if (change.Before is { } previous && ShadowValue.Equivalent(previous[name], value)) continue;
                    if (attributeIdentities.Handles(change.Id, name)) continue;
                    if (method?.Properties?.Contains(name) == true) continue;
                    // Type is read-only entity metadata on some objects, but a writable
                    // semantic enum on HatchPattern and other public data-model types.
                    // Let the accessor contract decide rather than dropping it by name.
                    if (name is "Handle" or "Owner" or "CodeName") continue;
                    if (name == OleRasterPreview.Property && source is netDxf.Entities.Ole2Frame ole)
                    {
                        if (!ShadowValue.Equivalent(OleRasterPreview.Read(ole), value))
                            throw new NotSupportedException("Stored OLE preview data is derived and read-only.");
                        continue;
                    }
                    // The source allocator is monotonic; undo restores semantic objects,
                    // never rewinds handles that may already have been assigned elsewhere.
                    if (historical is not null && name == "HandleSeed") continue;
                    var accessor = SourceAccessors.Find(source.GetType(), name);
                    if (name == "$items" || accessor is { CanWrite: false } && value is ShadowValue.Sequence)
                    {
                        if (value is not ShadowValue.Sequence sequence) throw new ArgumentException("Collection membership must be a sequence.");
                        var collectionSource = name == "$items" ? source : accessor!.Get(source);
                        var plan = SourceCollectionPlan.TryCreate(collectionSource, sequence, resolver.Convert, attributeIdentities.Tag);
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
            // Stable dependency phases: initialize owned payload, register definitions,
            // then attach referencing entities. Reverse this order when detaching.
            // Within a phase preserve captured membership order and native semantics.
            collections = collections.OrderBy(p => p.AttachmentOrder).ToList();
            // Resource names must exist before setters validate named references.
            writes = writes.OrderBy(w => w.Source is netDxf.Tables.TableObject && w.Accessor.Name == "Name" ? 0 : 1).ToList();
            var appliedWrites = new List<(object Source, SourceAccessor Accessor, object? Before)>();
            var attemptedMethods = 0;
            var touchedCollections = false;
            try
            {
                foreach (var method in methods) { attemptedMethods++; method.Apply(); }
                foreach (var write in writes)
                {
                    var actual = write.Accessor.Get(write.Source);
                    // Compound native setters can already satisfy a derived write.
                    // Record the actual intermediate value for reverse-order rollback.
                    if (ReferenceEquals(actual, write.After) || actual is not null &&
                        (actual.GetType().IsValueType || actual is string) && actual.Equals(write.After)) continue;
                    appliedWrites.Add((write.Source, write.Accessor, actual));
                    write.Accessor.Set(write.Source, write.After);
                }
                // A native resource rename may already have rekeyed saved layer
                // state dictionaries. Do not remove stale pre-rename keys again.
                // Rollback restores this post-write membership first; reversing
                // the scalar writes then reverses their native side effects.
                collections = collections.Select(p => p.AfterSourceWrites()).Where(p => !p.IsNoOp).ToList();
                // Moving entities across owners must release every old owner first.
                touchedCollections = collections.Count != 0;
                foreach (var plan in collections.AsEnumerable().Reverse()) plan.RemoveOld();
                attributeIdentities.Apply();
                foreach (var defaults in insertDefaults) defaults.Apply();
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
                    // Prepare from actual membership after a possibly partial source
                    // callback. Release every changed destination before restoring any
                    // original owner, without touching unchanged/reserved table entries.
                    var rollback = new List<SourceCollectionPlan>(collections.Count);
                    foreach (var plan in collections)
                        try { rollback.Add(plan.PrepareRollback()); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    foreach (var plan in rollback.AsEnumerable().Reverse())
                        try { plan.RemoveOld(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    foreach (var defaults in insertDefaults.Reverse())
                        try { defaults.Rollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    try { attributeIdentities.Rollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    foreach (var plan in rollback)
                        try { plan.AddNew(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                }
                else
                {
                    foreach (var defaults in insertDefaults.Reverse())
                        try { defaults.Rollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    try { attributeIdentities.Rollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                }
                for (var index = appliedWrites.Count - 1; index >= 0; index--)
                    try { appliedWrites[index].Accessor.Set(appliedWrites[index].Source, appliedWrites[index].Before); }
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
                if (!constructing.Add(id)) throw new ArgumentException($"Cyclic constructor dependency at {id} ({node.TypeName}).");
                try
                {
                    // Read-only owned collections must be bound to the new owner's actual
                    // collection instance, not constructed as an unrelated empty container.
                    // Entity/table/definition references are shared semantic dependencies,
                    // not constructor-owned objects (e.g. INSERT.Block and Entity.Owner).
                    if (node.Kind == ShadowObjectKind.Object &&
                        (ownedReferences ??= IndexOwnedReferences(additions)).TryGetValue(id, out var candidates))
                    {
                        foreach (var candidate in candidates)
                        {
                            // Owner is a semantic back-reference, not an owned container.
                            // Block.Owner is the public alias of its constructor-owned Record.
                            if (candidate.Accessor.Name == "Owner" && additions[candidate.Owner].TypeName != "netDxf.Blocks.Block") continue;
                            if (constructing.Contains(candidate.Owner)) continue;
                            var owned = candidate.Accessor.Get(Resolve(candidate.Owner));
                            if (owned is null || owned.GetType().FullName != node.TypeName) continue;
                            bindings[id] = owned; capture.Bind(id, owned); return owned;
                        }
                    }
                    source = ObjectFactories.Create(node, resolver!, out var initializeInsertAttributes);
                    if (initializeInsertAttributes) implicitInserts.Add((id, (netDxf.Entities.Insert)source));
                    bindings[id] = source; capture.Bind(id, source); return source;
                }
                finally { constructing.Remove(id); }
            }
        }
    }

    /// <summary>
    /// Builds a reverse index for newly created, read-only owned references. The old
    /// resolver scanned every added node for each addition: O(A * P) property visits,
    /// where P is the total number of properties across A additions. This preparation
    /// makes one O(A + P) pass and preserves the old candidate order within each ID.
    /// Only candidate discovery is linearized; constructors, capture and source
    /// collection operations retain their own independent costs.
    /// </summary>
    private static Dictionary<ObjectId, List<(ObjectId Owner, SourceAccessor Accessor)>> IndexOwnedReferences(
        IReadOnlyDictionary<ObjectId, ShadowObject> additions)
    {
        var result = new Dictionary<ObjectId, List<(ObjectId Owner, SourceAccessor Accessor)>>();
        foreach (var parent in additions.Values)
        {
            var type = typeof(DxfDocument).Assembly.GetType(parent.TypeName);
            if (type is null) continue;
            foreach (var (name, value) in parent.Properties)
            {
                if (value is not ShadowValue.Reference reference || !additions.ContainsKey(reference.Id)) continue;
                // ATTRIB.Definition is a shared semantic dependency, not an owned payload.
                if (parent.TypeName == "netDxf.Entities.Attribute" && name == "Definition") continue;
                var accessor = SourceAccessors.Find(type, name);
                if (accessor is not { CanWrite: false }) continue;
                if (!result.TryGetValue(reference.Id, out var candidates))
                {
                    candidates = new();
                    result.Add(reference.Id, candidates);
                }
                candidates.Add((parent.Id, accessor));
            }
        }
        return result;
    }

    private sealed class Resolver(Func<ObjectId, object> resolve, Func<ShadowValue, Type, object?> convert) : IShadowObjectResolver
    {
        public object Resolve(ObjectId id) => resolve(id);
        public object? Convert(ShadowValue value, Type targetType) => convert(value, targetType);
    }
}
