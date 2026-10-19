using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

/// <summary>Shared, bounded discovery contracts for native and browser Studio agents.</summary>
public static class StudioAgentSchema
{
    public sealed record Route(string Method, string Pattern);
    private static readonly Lazy<Type[]> RequestTypes = new(() => typeof(Studio).Assembly.GetTypes()
        .Where(t => t.IsPublic && t.Namespace == typeof(Studio).Namespace &&
            (t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name == "StudioRequestIdentity"))
        .Concat([typeof(DrawingReferenceCaptureRequest), typeof(DrawingReferenceValidationRequest)])
        .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray());
    private static readonly Lazy<Dictionary<string, Type>> Types = new(BuildCatalog);
    private static readonly NullabilityInfoContext Nullability = new();
    private static readonly object MetadataGate = new();

    public static void Register(McpToolRegistry registry)
    {
        registry.Register(new("studio_request_schema",
            "Discover exact JSON request shapes for all Studio features: drafting, blocks, hatches, layers, selection, transforms, branches and presentation. Omit type to page/search request names; supply a full or short type name to read its complete nested schema with defaults and numeric enum values. ObjectId is {value:...}, Vec3 is {x,y,z}; these are Studio route bodies, not shadow_edit values.",
            McpJson.Parse("""{"type":"object","properties":{"type":{"type":"string","maxLength":256},"search":{"type":"string","maxLength":256},"offset":{"type":"integer","minimum":0,"maximum":1000000},"limit":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}"""), true), (args, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (Text(args, "type") is { } name)
            {
                var candidates = Types.Value.Values.Where(t => t.FullName == name || t.Name == name).Distinct().ToArray();
                if (candidates.Length != 1) throw new ArgumentException("Unknown or ambiguous request type. Discover a full name with studio_request_schema.");
                lock (MetadataGate)
                {
                    var definitions = new JsonObject();
                    var schema = Describe(candidates[0], definitions, ct);
                    schema["$defs"] = definitions;
                    return ValueTask.FromResult(McpToolResult.FromObject(new { type = candidates[0].FullName, schema,
                        note = "Required fields have no constructor default. Supply current revision and identity headers for mutations. Semantic constraints are checked by Studio; this schema describes the JSON wire shape." }));
                }
            }
            var search = Text(args, "search"); var offset = Number(args, "offset", 0); var limit = Number(args, "limit", 40);
            var types = Types.Value.Values.Where(t => search is null || t.FullName!.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
            return ValueTask.FromResult(McpToolResult.FromObject(new { total = types.Length, offset,
                items = types.Skip(offset).Take(limit).Select(t => new { name = t.Name, fullName = t.FullName, request = RequestTypes.Value.Contains(t), isEnum = t.IsEnum }),
                nextOffset = offset + limit < types.Length ? (int?)(offset + limit) : null }));
        });
    }

    public static void RegisterRoutes(McpToolRegistry registry, string document, Func<IEnumerable<Route>> routes)
    {
        registry.Register(new("studio_routes",
            "Search and page all Studio API routes. Filter by method or path text, then use studio_request_schema to discover nested request bodies. Use studio_read for GET and studio_request for POST; drawing writes require current identity headers and body revision. No route is executed by discovery.",
            McpJson.Parse("""{"type":"object","properties":{"search":{"type":"string","maxLength":256},"method":{"type":"string","enum":["GET","POST"]},"offset":{"type":"integer","minimum":0,"maximum":1000000},"limit":{"type":"integer","minimum":1,"maximum":100},"includeRequestTypes":{"type":"boolean"}},"additionalProperties":false}"""), true), (args, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var search = Text(args, "search"); var method = Text(args, "method");
            var offset = Number(args, "offset", 0); var limit = Number(args, "limit", 40);
            var matches = routes().Where(r => StudioAgentRouteTools.IsStudioPath(r.Pattern) &&
                (method is null || r.Method.Equals(method, StringComparison.Ordinal)) &&
                (search is null || r.Pattern.Contains(search, StringComparison.OrdinalIgnoreCase))).Distinct()
                .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal).ToArray();
            return ValueTask.FromResult(McpToolResult.FromObject(new { document, total = matches.Length, offset,
                routes = matches.Skip(offset).Take(limit), nextOffset = offset + limit < matches.Length ? (int?)(offset + limit) : null,
                requestTypes = args.TryGetProperty("includeRequestTypes", out var include) && include.GetBoolean() ? DescribeRequests() : null,
                requestSchemaTool = "studio_request_schema", identityHeaders = new[] { "X-Shadow-Document", "X-Shadow-Workspace", "X-Shadow-View" },
                example = new { method = "GET", path = $"/api/cad/{Uri.EscapeDataString(document)}/state" } }));
        });
    }

    public static object[] DescribeRequests() => RequestTypes.Value.Select(t => (object)new { name = t.Name, fullName = t.FullName }).ToArray();
    private static string? Text(JsonElement args, string name) => args.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static int Number(JsonElement args, string name, int fallback) => args.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;

    private static Dictionary<string, Type> BuildCatalog()
    {
        var found = new Dictionary<string, Type>(StringComparer.Ordinal);
        var queue = new Queue<Type>(RequestTypes.Value);
        while (queue.TryDequeue(out var type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (Element(type) is { } element) { queue.Enqueue(element); continue; }
            if (type.Namespace?.StartsWith("netDxf.Shadow", StringComparison.Ordinal) != true || !found.TryAdd(type.FullName!, type)) continue;
            if (!type.IsEnum) foreach (var property in Inputs(type)) queue.Enqueue(property.PropertyType);
        }
        return found;
    }

    private static Type? Element(Type type) => type.IsArray ? type.GetElementType() :
        type.GetInterfaces().Append(type).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    private static PropertyInfo[] Inputs(Type type) => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.GetIndexParameters().Length == 0 && p.SetMethod is { IsPublic: true } &&
            p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always).OrderBy(p => p.MetadataToken).ToArray();
    private static string Name(PropertyInfo property) => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    private static JsonObject NullableSchema(JsonObject schema) => new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };

    private static JsonObject Describe(Type type, JsonObject definitions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Nullable.GetUnderlyingType(type) is { } underlying) return NullableSchema(Describe(underlying, definitions, token));
        if (type == typeof(JsonElement) || type == typeof(object)) return new();
        if (type == typeof(string) || type == typeof(char)) return new() { ["type"] = "string" };
        if (type == typeof(Guid)) return new() { ["type"] = "string", ["format"] = "uuid" };
        if (type == typeof(bool)) return new() { ["type"] = "boolean" };
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().Select(v => Convert.ToInt64(v)).Distinct().ToArray();
            var result = new JsonObject { ["type"] = "integer", ["enumNames"] = JsonSerializer.SerializeToNode(Enum.GetNames(type).Select(n => new { name = n, value = Convert.ToInt64(Enum.Parse(type, n)) })) };
            if (type.GetCustomAttribute<FlagsAttribute>() is null) result["enum"] = JsonSerializer.SerializeToNode(values);
            else result["description"] = "Bit flags: combine numeric values with bitwise OR.";
            return result;
        }
        if (type.IsPrimitive || type == typeof(decimal)) return new() { ["type"] = type == typeof(double) || type == typeof(float) || type == typeof(decimal) ? "number" : "integer" };
        if (Element(type) is { } element) return new() { ["type"] = "array", ["items"] = Describe(element, definitions, token) };
        var key = type.FullName!;
        var reference = new JsonObject { ["$ref"] = "#/$defs/" + key.Replace("~", "~0").Replace("/", "~1") };
        if (definitions.ContainsKey(key)) return reference;
        if (definitions.Count >= 64) throw new NotSupportedException("Request schema exceeds the 64-type expansion budget.");
        var definition = new JsonObject { ["type"] = "object", ["additionalProperties"] = false };
        definitions[key] = definition;
        var properties = new JsonObject(); var required = new JsonArray();
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        foreach (var property in Inputs(type))
        {
            var name = Name(property); var propertySchema = Describe(property.PropertyType, definitions, token);
            if (!property.PropertyType.IsValueType && Nullability.Create(property).WriteState == NullabilityState.Nullable) propertySchema = NullableSchema(propertySchema);
            var parameter = constructor?.GetParameters().FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (parameter is { HasDefaultValue: false } || property.GetCustomAttribute<JsonRequiredAttribute>() is not null) required.Add(name);
            if (parameter is { HasDefaultValue: true } && parameter.DefaultValue is not null && parameter.DefaultValue != DBNull.Value && parameter.DefaultValue != Missing.Value)
                propertySchema["default"] = JsonSerializer.SerializeToNode(parameter.DefaultValue, McpJson.Options);
            properties[name] = propertySchema;
        }
        definition["properties"] = properties; definition["required"] = required;
        return reference;
    }
}
