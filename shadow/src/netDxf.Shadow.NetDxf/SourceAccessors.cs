using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;

namespace netDxf.Shadow.NetDxf;

internal sealed record SourceAccessor(string Name, Type ValueType, bool CanWrite,
    Func<object, object?> Get, Action<object, object?> Set);

internal static class SourceAccessors
{
    // GetOrAdd may invoke its factory concurrently. Lazy prevents duplicate reflection
    // walks and expression compilation; losing dictionary factories allocate only Lazy.
    private static readonly ConcurrentDictionary<Type, Lazy<AccessorTable>> Cache = new();

    public static SourceAccessor[] For(Type type) => GetTable(type).Ordered;

    public static SourceAccessor? Find(Type type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return GetTable(type).ByName.TryGetValue(name, out var accessor) ? accessor : null;
    }

    private static AccessorTable GetTable(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Cache.GetOrAdd(type, static t => new Lazy<AccessorTable>(
            () => CreateTable(t), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static AccessorTable CreateTable(Type t) => new(CustomHeaderAccessors.Extend(t,
        t.GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m is FieldInfo || m is PropertyInfo p && p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)
            // DrawingEntities exposes filtered aliases of the same layout collection.
            .Where(m => t.FullName != "netDxf.Collections.DrawingEntities" || m.Name is "ActiveLayout" or "All")
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(m => Distance(t, m.DeclaringType!)).First())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m =>
            {
                var source = Expression.Parameter(typeof(object), "source");
                var instance = Expression.Convert(source, m.DeclaringType!);
                var member = m is PropertyInfo p ? Expression.Property(instance, p) : Expression.Field(instance, (FieldInfo)m);
                var getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(member, typeof(object)), source).Compile();
                var writable = m is PropertyInfo property ? property.SetMethod?.IsPublic == true : !((FieldInfo)m).IsInitOnly && !((FieldInfo)m).IsLiteral;
                var valueType = m is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)m).FieldType;
                void Set(object target, object? value)
                {
                    if (!writable) throw new InvalidOperationException($"{m.DeclaringType!.Name}.{m.Name} is read-only.");
                    // Reflection writes through a boxed value type correctly; a compiled
                    // conversion followed by assignment would otherwise mutate a copy.
                    if (m is PropertyInfo property) property.SetValue(target, value);
                    else ((FieldInfo)m).SetValue(target, value);
                }
                return new SourceAccessor(m.Name, valueType, writable, getter, Set);
            }).ToArray()));

    private sealed class AccessorTable(SourceAccessor[] ordered)
    {
        // Capture still walks the same nearest-declaration, ordinal-sorted array.
        public SourceAccessor[] Ordered { get; } = ordered;
        public FrozenDictionary<string, SourceAccessor> ByName { get; } =
            ordered.ToFrozenDictionary(static accessor => accessor.Name, StringComparer.Ordinal);
    }

    private static int Distance(Type type, Type declaring)
    {
        var distance = 0;
        for (Type? current = type; current is not null; current = current.BaseType, distance++)
            if (current == declaring) return distance;
        return int.MaxValue;
    }
}
