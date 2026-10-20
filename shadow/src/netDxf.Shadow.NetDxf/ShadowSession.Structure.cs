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
            // Count is a derived membership assertion, never an independent edit.
            // Reject contradictions before native adoption can allocate handles.
            foreach (var change in patch.Changes)
                if (change.After is { } after && after["$items"] is ShadowValue.Sequence items && after["Count"] is ShadowValue.Atom count &&
                    (!int.TryParse(count.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var declared) || declared != items.Items.Length))
                    throw new ArgumentException($"Collection {change.Id} Count does not match its proposed membership.");
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
                    if (name == "Entities" && source is netDxf.Entities.HatchBoundaryPath)
                    {
                        // The containing hatch updates sources and reactor links as
                        // one native operation; writing the backing list bypasses it.
                        assertions.Add((change.Id, name, value));
                        continue;
                    }
                    if (name == "Reactors" && source is netDxf.Entities.EntityObject)
                    {
                        // Native association callbacks own this list. Mutating its
                        // backing List would duplicate links as hatches are attached.
                        assertions.Add((change.Id, name, value));
                        continue;
                    }
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
            // Deleting a definition also deletes its owned collection nodes, so there
            // is no After node from which to prepare their membership delta. Release
            // those entity references before removing the block records/resources.
            // Otherwise deleting [parent, child] tries to remove a still-referenced
            // child first, and undo of a library import can fail after partial removal.
            foreach (var change in patch.Changes.Where(e => e.After is null && e.Before?.Kind == ShadowObjectKind.Block))
            {
                var block = (netDxf.Blocks.Block)Resolve(change.Id);
                if (collectionSources.Add(block.Entities))
                {
                    var plan = SourceCollectionPlan.TryCreate(block.Entities, new ShadowValue.Sequence([]), resolver.Convert);
                    if (plan is not null && !plan.IsNoOp) collections.Add(plan);
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
            var affectedHatches = patch.Changes.Where(change => (change.After ?? change.Before)?.TypeName == "netDxf.Entities.Hatch")
                .Select(change => change.Id).ToHashSet();
            var changedPaths = patch.Changes.Where(change => (change.After ?? change.Before)?.TypeName == "netDxf.Entities.HatchBoundaryPath")
                .Select(change => change.Id).ToHashSet();
            if (changedPaths.Count != 0)
                foreach (var node in target.Objects.Values.Where(node => node.TypeName == "netDxf.Entities.Hatch"))
                    if (target.Read(node.Id)["BoundaryPaths"].Items.Any(path => path.Id is { } id && changedPaths.Contains(id))) affectedHatches.Add(node.Id);
            var hatchesInCollections = collections.SelectMany(plan => plan.Before.Concat(plan.After)).OfType<netDxf.Entities.Hatch>()
                .ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var pair in bindings)
                if (pair.Value is netDxf.Entities.Hatch hatch && hatchesInCollections.Contains(hatch) && target.Objects.ContainsKey(pair.Key))
                    affectedHatches.Add(pair.Key);
            var hatchSources = affectedHatches.Select(id => new SourceHatchSourcesPlan((netDxf.Entities.Hatch)Resolve(id), target.Read(id), Resolve)).ToArray();
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
                foreach (var plan in collections.Where(plan => plan.AttachmentOrder < 7)) plan.AddNew();
                foreach (var plan in hatchSources) plan.Apply();
                foreach (var plan in collections.Where(plan => plan.AttachmentOrder >= 7)) plan.AddNew();
                var next = capture.Capture(document, current);
                foreach (var assertion in assertions)
                {
                    if (!next.Objects.TryGetValue(assertion.Id, out var node) || !EquivalentDerived(assertion.Property, node[assertion.Property], assertion.Value))
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
                    foreach (var plan in rollback.Where(plan => plan.AttachmentOrder < 7))
                        try { plan.AddNew(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    foreach (var plan in hatchSources)
                        try { plan.Rollback(); } catch (Exception e) { failures.Add(e.GetBaseException()); }
                    foreach (var plan in rollback.Where(plan => plan.AttachmentOrder >= 7))
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
                    var restored = capture.Capture(document, before);
                    if (failures.Count == 1 && !ReferenceEquals(restored, before))
                    {
                        var differences = ShadowGraphPatch.Between(before, restored).Changes;
                        // Failed table adoption can consume handles before callbacks
                        // throw. Restore its public header value only after proving
                        // every semantic object/property was restored. The private
                        // allocator remains monotonic; later adoption/save refreshes
                        // the header and never reuses the abandoned handles.
                        if (differences.Length == 1 && differences[0].Before is { } oldHeader &&
                            differences[0].After is { } newHeader && oldHeader.TypeName == "netDxf.Header.HeaderVariables" &&
                            oldHeader.Properties.Keys.Union(newHeader.Properties.Keys)
                                .Where(name => !ShadowValue.Equivalent(oldHeader[name], newHeader[name])).SequenceEqual(["HandleSeed"]) &&
                            oldHeader["HandleSeed"] is ShadowValue.Atom oldSeed && newHeader["HandleSeed"] is ShadowValue.Atom newSeed &&
                            long.TryParse(oldSeed.Text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var oldNumber) &&
                            long.TryParse(newSeed.Text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var newNumber) &&
                            newNumber > oldNumber)
                        {
                            document.DrawingVariables.KnownValues().Single(value => value.Name == "$HANDSEED").Value = oldSeed.Text;
                            restored = capture.Capture(document, before);
                        }
                    }
                    if (!ReferenceEquals(restored, before))
                    {
                        var differences = ShadowGraphPatch.Between(before, restored).Changes.Take(8).Select(change =>
                            $"{change.Id} ({(change.After ?? change.Before)!.Type}): " + (change.Before is null ? "added" : change.After is null ? "removed" :
                                string.Join(", ", change.Before.Properties.Keys.Union(change.After.Properties.Keys)
                                    .Where(name => !ShadowValue.Equivalent(change.Before[name], change.After[name])))));
                        failures.Add(new InvalidOperationException("Structural rollback did not restore the complete public graph. " + string.Join("; ", differences)));
                    }
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
                    // Captured INSERTs expose an owned AttributeCollection reference;
                    // authored INSERTs may instead supply the sequence inline. Constructor
                    // arguments must use the proposed members before binding read-only
                    // owned records such as SEQEND. Resolving the container itself here
                    // would recurse through its not-yet-constructed insertion owner.
                    var constructorNode = node;
                    if (node.TypeName == "netDxf.Entities.Insert" && node["Attributes"] is ShadowValue.Reference attributes
                        && target.Objects[attributes.Id]["$items"] is ShadowValue.Sequence members)
                        constructorNode = node with { Properties = node.Properties.SetItem("Attributes", members) };
                    source = ObjectFactories.Create(constructorNode, resolver!, out var initializeInsertAttributes);
                    if (initializeInsertAttributes) implicitInserts.Add((id, (netDxf.Entities.Insert)source));
                    bindings[id] = source; capture.Bind(id, source); return source;
                }
                finally { constructing.Remove(id); }
            }
        }
    }

    private static bool EquivalentDerived(string property, ShadowValue actual, ShadowValue expected)
    {
        // Managed reactor order follows callback execution, not drawing order.
        // Preserve every relationship and its multiplicity; persistent stored
        // reactors continue to use the ordinary exact sequence contract.
        if (property == "Reactors" && actual is ShadowValue.Sequence a && expected is ShadowValue.Sequence b)
            return a.Items.Length == b.Items.Length && a.Items.All(v => v is ShadowValue.Reference) && b.Items.All(v => v is ShadowValue.Reference) &&
                a.Items.Cast<ShadowValue.Reference>().Select(v => v.Id.Value).Order(StringComparer.Ordinal)
                    .SequenceEqual(b.Items.Cast<ShadowValue.Reference>().Select(v => v.Id.Value).Order(StringComparer.Ordinal));
        return ShadowValue.Equivalent(actual, expected);
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
