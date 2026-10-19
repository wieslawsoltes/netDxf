using System.Collections;
using System.Reflection;

namespace netDxf.Shadow.NetDxf;

/// <summary>Prepared collection delta. All removals can precede additions across owners.</summary>
internal sealed class SourceCollectionPlan
{
    private readonly object source;
    private readonly Type elementType;
    // Register APPIDs before XDATA; native adoption otherwise clones an unregistered
    // registry and breaks the explicit graph identity. Constructor-owned arrays/vertices
    // precede other registered definitions; entities are
    // attached last so references never force a table to retain a removed definition.
    internal int AttachmentOrder { get; }
    private readonly MethodInfo? insert;
    private readonly MethodInfo? removeAt;
    private readonly MethodInfo? add;
    private readonly MethodInfo? remove;
    private readonly IList? list;
    private readonly Array? array;
    private readonly int prefix;
    private readonly int suffix;
    public object?[] Before { get; }
    public object?[] After { get; }

    private SourceCollectionPlan(object source, Type elementType, object?[] after)
    {
        this.source = source;
        this.elementType = elementType;
        AttachmentOrder = source switch
        {
            netDxf.Collections.ApplicationRegistries => -1,
            netDxf.Collections.EntityCollection => 7,
            netDxf.Collections.DimensionStyles or netDxf.Collections.MLineStyles => 6,
            netDxf.Collections.BlockRecords => 5,
            netDxf.Collections.Layers => 4,
            netDxf.Collections.Linetypes => 3,
            _ => IsTable(source.GetType()) ? 2 : 0
        };
        array = source as Array;
        Before = ((IEnumerable)source).Cast<object?>().ToArray();
        After = after;
        list = source as IList;
        var genericList = typeof(IList<>).MakeGenericType(elementType);
        var collection = typeof(ICollection<>).MakeGenericType(elementType);
        if (genericList.IsInstanceOfType(source))
        {
            insert = genericList.GetMethod("Insert"); removeAt = genericList.GetMethod("RemoveAt");
            add = collection.GetMethod("Add"); remove = collection.GetMethod("Remove");
        }
        else if (collection.IsInstanceOfType(source))
        {
            add = collection.GetMethod("Add"); remove = collection.GetMethod("Remove");
        }
        else
        {
            add = source.GetType().GetMethod("Add", [elementType]);
            remove = source.GetType().GetMethod("Remove", [elementType]);
            insert = source.GetType().GetMethod("Insert", [typeof(int), elementType]);
            removeAt = source.GetType().GetMethod("RemoveAt", [typeof(int)]);
        }
        while (prefix < Math.Min(Before.Length, After.Length) && Same(Before[prefix], After[prefix])) prefix++;
        if (list is not null || insert is not null && removeAt is not null)
            while (suffix < Math.Min(Before.Length, After.Length) - prefix && Same(Before[^(suffix + 1)], After[^(suffix + 1)])) suffix++;
    }

    public static SourceCollectionPlan? TryCreate(object? source, ShadowValue.Sequence value, Func<ShadowValue, Type, object?> convert,
        Func<netDxf.Entities.AttributeDefinition, string>? definitionTag = null)
    {
        if (source is not IEnumerable || source is string) return null;
        if (source is Array array)
        {
            if (array.Rank != 1 || array.GetLowerBound(0) != 0 || array.Length != value.Items.Length)
                throw new NotSupportedException("Fixed coordinate arrays require identical cardinality.");
            var elementType = array.GetType().GetElementType()!;
            return new(source, elementType, value.Items.Select(item => convert(item, elementType)).ToArray());
        }
        var type = source.GetType();
        var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        var element = enumerable?.GetGenericArguments()[0];
        if (element is null) return null;
        var collection = typeof(ICollection<>).MakeGenericType(element);
        if (collection.IsInstanceOfType(source) && (bool)collection.GetProperty("IsReadOnly")!.GetValue(source)!) return null;
        if (source is IList list && (list.IsReadOnly || list.IsFixedSize)) return null;
        if (!collection.IsInstanceOfType(source) && (type.GetMethod("Add", [element]) is null || type.GetMethod("Remove", [element]) is null)) return null;
        var after = new object?[value.Items.Length];
        for (var i = 0; i < after.Length; i++) after[i] = convert(value.Items[i], element);
        if (source is netDxf.Collections.EntityCollection)
        {
            var unique = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var item in after)
                if (item is null || !unique.Add(item)) throw new ArgumentException("A block cannot contain null or duplicate entity instances.");
        }
        if (source is netDxf.Collections.AttributeDefinitionDictionary)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in DefinitionItems(after))
                if (item.Value is null || string.IsNullOrEmpty(item.Key) || !tags.Add(item.Key) ||
                    !string.Equals(item.Key, definitionTag?.Invoke(item.Value) ?? item.Value.Tag, StringComparison.Ordinal))
                    throw new ArgumentException("Definition membership requires unique tags matching their definition objects.");
        }
        return new(source, element, after);
    }

    public bool IsNoOp => prefix == Before.Length && prefix == After.Length;
    public void RemoveOld()
    {
        // Other native removals can already unlink owned lists. Keep Before for
        // rollback, but calculate indices from the collection's current contents.
        new SourceCollectionPlan(source, elementType, After).RemovePrepared();
    }
    private void RemovePrepared()
    {
        if (array is not null) return;
        if (source is netDxf.Collections.AttributeDefinitionDictionary definitions)
        {
            var retained = DefinitionItems(After).Select(item => item.Value).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var item in DefinitionItems(Before).Reverse())
                if (!retained.Contains(item.Value)) definitions.Remove(item.Key);
            return;
        }
        if (source is netDxf.Collections.EntityCollection entities)
        {
            // Detach referencing hatches before their source geometry. Entity order
            // in a DXF need not put the hatch after its boundary, and several hatches
            // can retain the same source. Remove by identity as callbacks change indices.
            var pending = Before.Skip(prefix).Take(Before.Length - prefix - suffix)
                .Cast<netDxf.Entities.EntityObject>().Reverse().ToArray();
            var membership = pending.ToHashSet();
            var dependencies = new Dictionary<netDxf.Entities.EntityObject, int>();
            var dependents = new Dictionary<netDxf.Entities.EntityObject, List<netDxf.Entities.EntityObject>>();
            foreach (var entity in pending)
            {
                var reactors = entity.Reactors.OfType<netDxf.Entities.EntityObject>().Where(membership.Contains).Distinct().ToArray();
                dependencies[entity] = reactors.Length;
                foreach (var reactor in reactors)
                {
                    if (!dependents.TryGetValue(reactor, out var linked)) dependents[reactor] = linked = [];
                    linked.Add(entity);
                }
            }
            var ready = new Queue<netDxf.Entities.EntityObject>(pending.Where(entity => dependencies[entity] == 0));
            var ordered = new List<netDxf.Entities.EntityObject>(pending.Length);
            while (ready.TryDequeue(out var next))
            {
                ordered.Add(next);
                if (dependents.TryGetValue(next, out var linked))
                    foreach (var entity in linked) if (--dependencies[entity] == 0) ready.Enqueue(entity);
            }
            if (ordered.Count != pending.Length) throw new NotSupportedException("Cyclic entity reactor dependencies cannot be detached.");
            foreach (var entity in ordered)
            {
                if (entities.Contains(entity) && !entities.Remove(entity)) throw new InvalidOperationException("The source refused entity removal.");
            }
            return;
        }
        for (var i = Before.Length - suffix - 1; i >= prefix; i--) Remove(i, Before[i]);
    }
    public void AddNew()
    {
        // Boundary adoption and compound callbacks can satisfy part of another
        // delta. Never insert a second copy using stale pre-callback indices.
        var remaining = new SourceCollectionPlan(source, elementType, After);
        remaining.RemovePrepared();
        remaining.AddPrepared();
    }
    private void AddPrepared()
    {
        if (array is not null) { AssignArray(After); return; }
        if (source is netDxf.Collections.AttributeDefinitionDictionary definitions)
        {
            var items = DefinitionItems(After).ToArray();
            var existing = definitions.Values.ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var item in items)
            {
                if (!string.Equals(item.Key, item.Value.Tag, StringComparison.Ordinal))
                    throw new ArgumentException("Definition dictionary keys must match their attribute tags.");
                if (!existing.Contains(item.Value)) definitions.Add(item.Value);
            }
            definitions.Reorder(items.Select(item => item.Value));
        }
        else for (var i = prefix; i < After.Length - suffix; i++) Insert(i, After[i]);
        if (!Matches(After)) throw new InvalidOperationException($"Source collection {source.GetType().FullName} rejected or reordered a requested change.");
    }
    private static IEnumerable<KeyValuePair<string, netDxf.Entities.AttributeDefinition>> DefinitionItems(object?[] items) =>
        items.Cast<KeyValuePair<string, netDxf.Entities.AttributeDefinition>>();
    /// <summary>Recover only changed membership. Clearing an entire table would also
    /// remove untouched definitions, reallocating their handles and breaking rollback.</summary>
    internal SourceCollectionPlan PrepareRollback() => new(source, elementType, Before);

    /// <summary>Native compound setters can rekey owned collections. Derive the
    /// remaining membership delta from their actual post-write state, while the
    /// already converted destination values remain unchanged.</summary>
    internal SourceCollectionPlan AfterSourceWrites() => new(source, elementType, After);

    private static bool IsTable(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(netDxf.Collections.TableObjects<>)) return true;
        return false;
    }
    private void AssignArray(object?[] values)
    {
        for (var i = 0; i < values.Length; i++) array!.SetValue(values[i], i);
        if (!Matches(values)) throw new InvalidOperationException("The source array rejected a requested value.");
    }
    private void Remove(int index, object? value)
    {
        if (list is not null) list.RemoveAt(index);
        else if (removeAt is not null) removeAt.Invoke(source, [index]);
        else if (remove is not null) remove.Invoke(source, [value]);
        else throw new NotSupportedException("Collection removal is unavailable.");
    }
    private void Insert(int index, object? value)
    {
        if (source is netDxf.Collections.EntityCollection entities && index == entities.Count) entities.Add((netDxf.Entities.EntityObject)value!);
        else if (list is not null) list.Insert(index, value);
        else if (insert is not null) insert.Invoke(source, [index, value]);
        else if (add is not null) add.Invoke(source, [value]);
        else throw new NotSupportedException("Collection insertion is unavailable.");
    }
    private bool Matches(object?[] expected) => ((IEnumerable)source).Cast<object?>().SequenceEqual(expected, ItemComparer.Instance);
    private static bool Same(object? left, object? right) => ReferenceEquals(left, right) || left is not null && (left.GetType().IsValueType || left is string) && left.Equals(right);
    private sealed class ItemComparer : IEqualityComparer<object?>
    {
        internal static readonly ItemComparer Instance = new();
        public new bool Equals(object? x, object? y) => Same(x, y);
        public int GetHashCode(object? obj) => obj?.GetHashCode() ?? 0;
    }
}
