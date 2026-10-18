using System.Text.Json;

namespace netDxf.Shadow.Mcp;

/// <summary>Validates the structural JSON Schema vocabulary used by registry contracts, without code generation.</summary>
internal static class McpSchemaValidator
{
    public static void Validate(JsonElement schema, JsonElement value) => Validate(schema, value, "$", 0);

    private static void Validate(JsonElement schema, JsonElement value, string path, int depth)
    {
        if (depth > 48) throw new ArgumentException($"{path}: maximum argument nesting exceeded.");
        if (schema.ValueKind == JsonValueKind.True) return;
        if (schema.ValueKind == JsonValueKind.False) throw new ArgumentException($"{path}: this value is not allowed.");
        if (schema.ValueKind != JsonValueKind.Object) throw new ArgumentException("Invalid tool schema.");
        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(combinator, out var alternatives)) continue;
            var matches = 0;
            foreach (var alternative in alternatives.EnumerateArray())
            {
                try { Validate(alternative, value, path, depth + 1); matches++; }
                catch (ArgumentException) { }
            }
            if (combinator == "allOf" && matches != alternatives.GetArrayLength() ||
                combinator == "anyOf" && matches == 0 || combinator == "oneOf" && matches != 1)
                throw new ArgumentException($"{path}: value does not satisfy {combinator}.");
        }
        if (schema.TryGetProperty("type", out var declared))
        {
            var valid = declared.ValueKind == JsonValueKind.Array
                ? declared.EnumerateArray().Any(t => Matches(t.GetString(), value)) : Matches(declared.GetString(), value);
            if (!valid) throw new ArgumentException($"{path}: expected {declared.GetRawText()}, received {value.ValueKind}.");
        }
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(choice => Equal(choice, value)))
            throw new ArgumentException($"{path}: value is not in the allowed enum {choices.GetRawText()}.");
        if (schema.TryGetProperty("const", out var constant) && !Equal(constant, value))
            throw new ArgumentException($"{path}: value differs from the required constant.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray())
                    if (!value.TryGetProperty(name.GetString()!, out _)) throw new ArgumentException($"{path}.{name.GetString()}: required property is missing.");
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException($"{path}.{property.Name}: duplicate JSON properties are not allowed.");
                if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
                    Validate(propertySchema, property.Value, path + "." + property.Name, depth + 1);
                else if (hasAdditional) Validate(additional, property.Value, path + "." + property.Name, depth + 1);
                else ValidateTree(property.Value, path + "." + property.Name, depth + 1);
            }
            Limit(schema, "minProperties", names.Count, path, minimum: true);
            Limit(schema, "maxProperties", names.Count, path, minimum: false);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            Limit(schema, "minItems", value.GetArrayLength(), path, minimum: true);
            Limit(schema, "maxItems", value.GetArrayLength(), path, minimum: false);
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (schema.TryGetProperty("items", out var itemSchema)) Validate(itemSchema, item, $"{path}[{index}]", depth + 1);
                else ValidateTree(item, $"{path}[{index}]", depth + 1);
                index++;
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            Limit(schema, "minLength", value.GetString()!.Length, path, minimum: true);
            Limit(schema, "maxLength", value.GetString()!.Length, path, minimum: false);
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) throw new ArgumentException($"{path}: numbers must be finite.");
            Limit(schema, "minimum", number, path, minimum: true);
            Limit(schema, "maximum", number, path, minimum: false);
        }
    }

    private static void ValidateTree(JsonElement value, string path, int depth) => Validate(McpJson.EmptyObject, value, path, depth);
    private static void Limit(JsonElement schema, string keyword, double count, string path, bool minimum)
    {
        if (schema.TryGetProperty(keyword, out var limit) && (minimum ? count < limit.GetDouble() : count > limit.GetDouble()))
            throw new ArgumentException($"{path}: violates {keyword} {limit.GetRawText()}.");
    }
    private static bool Matches(string? type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && decimal.Truncate(number) == number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => throw new ArgumentException($"Unsupported JSON Schema type '{type}'.")
    };
    private static bool Equal(JsonElement left, JsonElement right) => left.ValueKind == right.ValueKind &&
        (left.ValueKind == JsonValueKind.String ? left.GetString() == right.GetString() : left.GetRawText() == right.GetRawText());
}
