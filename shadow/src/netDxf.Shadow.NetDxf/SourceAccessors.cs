using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace netDxf.Shadow.NetDxf;

internal sealed record SourceAccessor(PropertyInfo Property, Func<object, object?> Get)
{
    public string Name => Property.Name;
    public bool CanWrite => Property.SetMethod?.IsPublic == true;
}

internal static class SourceAccessors
{
    private static readonly ConcurrentDictionary<Type, SourceAccessor[]> Cache = new();
    public static SourceAccessor[] For(Type type) => Cache.GetOrAdd(type, static t =>
        t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)
            // DrawingEntities exposes many filtered aliases of the same layout collection.
            .Where(p => t.FullName != "netDxf.Collections.DrawingEntities" || p.Name is "ActiveLayout" or "All")
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(p => Distance(t, p.DeclaringType!)).First())
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p =>
            {
                var source = Expression.Parameter(typeof(object), "source");
                var body = Expression.Convert(Expression.Property(Expression.Convert(source, p.DeclaringType!), p), typeof(object));
                return new SourceAccessor(p, Expression.Lambda<Func<object, object?>>(body, source).Compile());
            }).ToArray());
    private static int Distance(Type type, Type declaring)
    {
        var distance = 0;
        for (Type? current = type; current is not null; current = current.BaseType, distance++)
            if (current == declaring) return distance;
        return int.MaxValue;
    }
}
