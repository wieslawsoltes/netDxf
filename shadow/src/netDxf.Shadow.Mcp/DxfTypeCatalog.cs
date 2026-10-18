using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using netDxf.Entities;

namespace netDxf.Shadow.Mcp;

/// <summary>Public linked data-model metadata. Inspection never invokes arbitrary methods or loads assemblies.</summary>
internal static class DxfTypeCatalog
{
    internal sealed record DataMember(string Name, Type ValueType, bool CanWrite, MemberInfo Member)
    {
        public object? GetValue(object instance) => Member is PropertyInfo property ? property.GetValue(instance) : ((FieldInfo)Member).GetValue(instance);
    }

    private static readonly Lazy<Type[]> KnownTypes = new(() => typeof(DxfDocument).Assembly.GetExportedTypes()
        .Where(t => AllowedNamespace(t.Namespace)).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray());
    private static readonly ConcurrentDictionary<Type, DataMember[]> Properties = new();

    public static IReadOnlyList<Type> Types => KnownTypes.Value;
    public static DataMember[] PublicProperties(Type type) => Properties.GetOrAdd(type, t => t.GetMembers(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p is FieldInfo || p is PropertyInfo property && property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
        // netDxf exposes typed Owner properties hiding base DxfObject.Owner. Use the
        // most-derived public property, matching the native shadow accessor contract.
        .GroupBy(p => p.Name, StringComparer.Ordinal).Select(group => group.OrderBy(p => InheritanceDistance(t, p.DeclaringType)).First())
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .Select(p => p is PropertyInfo property
            ? new DataMember(p.Name, property.PropertyType, property.SetMethod?.IsPublic == true, p)
            : new DataMember(p.Name, ((FieldInfo)p).FieldType, !((FieldInfo)p).IsInitOnly && !((FieldInfo)p).IsLiteral, p)).ToArray());

    private static int InheritanceDistance(Type type, Type? declaringType)
    {
        var distance = 0;
        for (Type? current = type; current is not null; current = current.BaseType, distance++)
            if (current == declaringType) return distance;
        return int.MaxValue;
    }

    public static Type Resolve(string name)
    {
        var full = KnownTypes.Value.FirstOrDefault(t => t.FullName == name);
        if (full is not null) return full;
        var shortNames = KnownTypes.Value.Where(t => t.Name == name).Take(2).ToArray();
        return shortNames.Length == 1 ? shortNames[0] : throw new ArgumentException($"Unknown or ambiguous public netDxf type '{name}'. Use dxf_types to find its full name.");
    }

    public static object Describe(Type type, int offset = 0, int limit = 100) => new
    {
        name = type.FullName, type.IsAbstract, type.IsEnum, valueType = type.IsValueType,
        sourceConstructible = IsSourceConstructible(type),
        enumValues = type.IsEnum ? Enum.GetNames(type) : [],
        offset, totalProperties = PublicProperties(type).Length,
        nextOffset = offset + limit < PublicProperties(type).Length ? (int?)(offset + limit) : null,
        properties = PublicProperties(type).Skip(offset).Take(limit).Select(p => new
        {
            name = p.Name, path = "/" + ShadowJson.Escape(p.Name), type = p.ValueType.FullName,
            writable = p.CanWrite,
            enumValues = (Nullable.GetUnderlyingType(p.ValueType) ?? p.ValueType).IsEnum
                ? Enum.GetNames(Nullable.GetUnderlyingType(p.ValueType) ?? p.ValueType) : []
        }).ToArray(),
        constructors = type.GetConstructors().Select(c => c.GetParameters().Select(p => new
        {
            name = p.Name, type = p.ParameterType.FullName, optional = p.HasDefaultValue
        }).ToArray()).ToArray()
    };

    public static ShadowObjectKind Kind(Type type) => typeof(EntityObject).IsAssignableFrom(type) ? ShadowObjectKind.Entity :
        type == typeof(netDxf.Blocks.Block) ? ShadowObjectKind.Block :
        type == typeof(netDxf.Objects.Layout) ? ShadowObjectKind.Layout :
        type.Namespace == "netDxf.Tables" ? ShadowObjectKind.Table : ShadowObjectKind.Object;

    public static bool IsSourceConstructible(Type type) => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters && type != typeof(DxfDocument) &&
        type.Namespace is "netDxf" or "netDxf.Entities" or "netDxf.Blocks" or "netDxf.Tables" or "netDxf.Objects" or "netDxf.Header" or "netDxf.Collections";

    public static ImmutableDictionary<string, ShadowValue> ReadProperties(Type type, JsonElement properties, Func<string, ObjectId> aliases)
    {
        if (properties.ValueKind != JsonValueKind.Object) throw new ArgumentException("properties must be a JSON object.");
        var known = PublicProperties(type).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var result = ImmutableDictionary.CreateBuilder<string, ShadowValue>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Name == "$items") { result.Add(property.Name, ShadowJson.FromInput(property.Value, null, aliases)); continue; }
            if (!known.TryGetValue(property.Name, out var declared)) throw new ArgumentException($"Unknown property {type.FullName}.{property.Name}.");
            result.Add(declared.Name, ReadTyped(property.Value, declared.ValueType, aliases));
        }
        return result.ToImmutable();
    }

    private static ShadowValue ReadTyped(JsonElement input, Type target, Func<string, ObjectId> aliases)
    {
        target = Nullable.GetUnderlyingType(target) ?? target;
        if (input.ValueKind == JsonValueKind.Null || input.ValueKind == JsonValueKind.Object && (input.TryGetProperty("kind", out _) || input.TryGetProperty("$ref", out _)))
            return ShadowJson.FromInput(input, null, aliases);
        if (target.IsEnum || target.IsPrimitive || target == typeof(string) || target == typeof(decimal) || target == typeof(DateTime) || target == typeof(Guid))
            return new ShadowValue.Atom(target.FullName!, ShadowJson.AtomText(input));
        if (target.IsValueType && input.ValueKind == JsonValueKind.Object)
        {
            var fields = ImmutableDictionary.CreateBuilder<string, ShadowValue>(StringComparer.Ordinal);
            var declared = PublicProperties(target).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var field in input.EnumerateObject())
            {
                if (!declared.TryGetValue(field.Name, out var property)) throw new ArgumentException($"Unknown {target.FullName}.{field.Name} field.");
                fields.Add(property.Name, ReadTyped(field.Value, property.ValueType, aliases));
            }
            return new ShadowValue.Structure(target.FullName!, fields.ToImmutable());
        }
        if (input.ValueKind == JsonValueKind.Array)
        {
            var element = target.IsArray ? target.GetElementType() : target.GetInterfaces().Append(target)
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (element is not null) return new ShadowValue.Sequence(input.EnumerateArray().Select(v => ReadTyped(v, element, aliases)).ToImmutableArray());
        }
        return ShadowJson.FromInput(input, null, aliases);
    }

    private static bool AllowedNamespace(string? name) => name is "netDxf" or "netDxf.Entities" or "netDxf.Blocks" or "netDxf.Tables" or "netDxf.Objects" or "netDxf.Header" or "netDxf.Collections" or "netDxf.IO" or "netDxf.Units" or "netDxf.GTE";
}
