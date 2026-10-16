using System.Collections;
using System.Reflection;

namespace netDxf.Shadow.NetDxf;

/// <summary>Prepared collection delta. All removals can precede additions across owners.</summary>
internal sealed class SourceCollectionPlan
{
    private readonly object source;
    private readonly Type elementType;
    // Constructor-owned arrays/vertices precede registered definitions; entities are
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
        AttachmentOrder = source is netDxf.Collections.EntityCollection ? 2 : IsTable(source.GetType()) ? 1 : 0;
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

    public static SourceCollectionPlan? TryCreate(object? source, ShadowValue.Sequence value, Func<ShadowValue, Type, object?> convert)
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
        return new(source, element, after);
    }

    public bool IsNoOp => prefix == Before.Length && prefix == After.Length;
    public void RemoveOld()
    {
        if (array is not null) return;
        for (var i = Before.Length - suffix - 1; i >= prefix; i--) Remove(i, Before[i]);
    }
    public void AddNew()
    {
        if (array is not null) { AssignArray(After); return; }
        for (var i = prefix; i < After.Length - suffix; i++) Insert(i, After[i]);
        if (!Matches(After)) throw new InvalidOperationException($"Source collection {source.GetType().FullName} rejected or reordered a requested change.");
    }
    /// <summary>Recover only changed membership. Clearing an entire table would also
    /// remove untouched definitions, reallocating their handles and breaking rollback.</summary>
    internal SourceCollectionPlan PrepareRollback() => new(source, elementType, Before);

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
