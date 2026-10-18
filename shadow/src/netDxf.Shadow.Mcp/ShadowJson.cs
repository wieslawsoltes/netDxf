using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace netDxf.Shadow.Mcp;

/// <summary>Lossless typed values, bounded previews, and JSON Pointer navigation over the immutable graph.</summary>
public static class ShadowJson
{
    private sealed class PreviewBudget { public int Nodes = 512; public int Characters = 32768; }

    public static object? Describe(ShadowValue value, int depth = 3, int limit = 32, int offset = 0, int textOffset = 0, int textLimit = 4096)
    {
        if (depth is < 0 or > 16 || limit is < 1 or > 1000 || offset < 0 || textOffset < 0 || textLimit is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(depth), "Invalid shadow preview bounds.");
        return DescribeCore(value, depth, limit, offset, textOffset, textLimit, new());
    }

    private static object? DescribeCore(ShadowValue value, int depth, int limit, int offset, int textOffset, int textLimit, PreviewBudget budget)
    {
        if (--budget.Nodes < 0 || budget.Characters <= 0) return new { kind = value.GetType().Name, collapsed = true, reason = "preview budget; inspect a narrower path" };
        var characterLimit = Math.Min(textLimit, budget.Characters);
        if (value is ShadowValue.Atom textValue) budget.Characters -= Math.Min(characterLimit, Math.Max(0, textValue.Text.Length - textOffset));
        return value switch
        {
            ShadowValue.NullValue => new { kind = "null" },
            ShadowValue.Atom atom => new
            {
                kind = "atom", type = atom.TypeName,
                value = atom.Text.Substring(Math.Min(textOffset, atom.Text.Length), Math.Min(characterLimit, Math.Max(0, atom.Text.Length - textOffset))),
                length = atom.Text.Length, textOffset,
                nextTextOffset = (long)textOffset + characterLimit < atom.Text.Length ? (int?)(textOffset + characterLimit) : null
            },
            ShadowValue.Reference reference => new { kind = "reference", id = reference.Id.Value },
            ShadowValue.Fault fault => new { kind = "fault", error = fault.Message[..Math.Min(fault.Message.Length, textLimit)] },
            ShadowValue.Sequence sequence => new
            {
                kind = "sequence", count = sequence.Items.Length, offset,
                items = depth > 0 ? sequence.Items.Skip(offset).Take(limit).Select(v => DescribeCore(v, depth - 1, Math.Min(limit, 16), 0, 0, textLimit, budget)).ToArray() : [],
                nextOffset = depth > 0 && (long)offset + limit < sequence.Items.Length ? (int?)(offset + limit) : null,
                collapsed = depth == 0
            },
            ShadowValue.Structure structure => new
            {
                kind = "structure", type = structure.TypeName, count = structure.Fields.Count, offset,
                fields = depth > 0 ? structure.Fields.OrderBy(p => p.Key, StringComparer.Ordinal).Skip(offset).Take(limit)
                    .ToDictionary(p => p.Key, p => DescribeCore(p.Value, depth - 1, Math.Min(limit, 16), 0, 0, textLimit, budget)) : [],
                nextOffset = depth > 0 && (long)offset + limit < structure.Fields.Count ? (int?)(offset + limit) : null,
                collapsed = depth == 0
            },
            _ => throw new ArgumentException("Unknown shadow value.", nameof(value))
        };
    }

    /// <summary>Accepts typed value envelopes, JSON primitives, references, arrays and typed structure patches.</summary>
    public static ShadowValue FromInput(JsonElement input, ShadowValue? exemplar = null, Func<string, ObjectId>? resolveAlias = null)
    {
        if (input.ValueKind == JsonValueKind.Null) return ShadowValue.Null;
        if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("$ref", out var reference))
            return new ShadowValue.Reference(Resolve(reference.GetString()!));
        if (input.ValueKind == JsonValueKind.Object && input.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
        {
            switch (kind.GetString())
            {
                case "null": return ShadowValue.Null;
                case "atom":
                    EnsureCompletePreview(input, "length", "value", "textOffset", "nextTextOffset");
                    return new ShadowValue.Atom(input.GetProperty("type").GetString()!, AtomText(input.GetProperty("value")));
                case "reference": return new ShadowValue.Reference(Resolve(input.GetProperty("id").GetString()!));
                case "structure":
                    EnsureCompletePreview(input, "count", "fields", "offset", "nextOffset");
                    return new ShadowValue.Structure(input.GetProperty("type").GetString()!, input.GetProperty("fields").EnumerateObject()
                        .ToImmutableDictionary(p => p.Name, p => FromInput(p.Value, null, resolveAlias), StringComparer.Ordinal));
                case "sequence":
                    EnsureCompletePreview(input, "count", "items", "offset", "nextOffset");
                    return new ShadowValue.Sequence(input.GetProperty("items").EnumerateArray().Select(v => FromInput(v, null, resolveAlias)).ToImmutableArray());
                case "fault": throw new ArgumentException("Capture faults are diagnostic values and cannot be authored.");
                default: throw new ArgumentException("Unknown typed shadow value kind.");
            }
        }
        if (exemplar is ShadowValue.Atom atom && input.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            return atom with { Text = AtomText(input) };
        if (exemplar is ShadowValue.Structure structure && input.ValueKind == JsonValueKind.Object)
        {
            var fields = structure.Fields.ToBuilder();
            foreach (var property in input.EnumerateObject())
            {
                if (!fields.TryGetValue(property.Name, out var before)) throw new ArgumentException($"Unknown {structure.TypeName} field '{property.Name}'.");
                fields[property.Name] = FromInput(property.Value, before, resolveAlias);
            }
            return structure with { Fields = fields.ToImmutable() };
        }
        if (input.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            return new ShadowValue.Sequence(input.EnumerateArray().Select(item =>
            {
                var before = exemplar is ShadowValue.Sequence sequence && index < sequence.Items.Length ? sequence.Items[index] : null;
                index++;
                return FromInput(item, before, resolveAlias);
            }).ToImmutableArray());
        }
        return input.ValueKind switch
        {
            JsonValueKind.String => ShadowValue.From(input.GetString()!),
            JsonValueKind.Number => new ShadowValue.Atom("System.Double", input.GetRawText()),
            JsonValueKind.True => ShadowValue.From(true),
            JsonValueKind.False => ShadowValue.From(false),
            _ => throw new ArgumentException("Use a typed {kind:'structure',type,fields} or {$ref:id} envelope for a new compound value.")
        };

        ObjectId Resolve(string id) => resolveAlias?.Invoke(id) ?? new(id);
    }

    private static void EnsureCompletePreview(JsonElement input, string totalName, string payloadName, string offsetName, string nextName)
    {
        if (input.TryGetProperty("collapsed", out var collapsed) && collapsed.ValueKind == JsonValueKind.True ||
            input.TryGetProperty(offsetName, out var offset) && offset.GetInt64() != 0 ||
            input.TryGetProperty(nextName, out var next) && next.ValueKind != JsonValueKind.Null)
            throw new ArgumentException("A partial value preview cannot replace a complete property. Retrieve the missing pages or author a complete typed value explicitly.");
        if (!input.TryGetProperty(totalName, out var total) || !input.TryGetProperty(payloadName, out var payload)) return;
        var count = payload.ValueKind switch
        {
            JsonValueKind.String => payload.GetString()!.Length,
            JsonValueKind.Array => payload.GetArrayLength(),
            JsonValueKind.Object => payload.EnumerateObject().Count(),
            _ => -1
        };
        if (total.GetInt64() != count) throw new ArgumentException("A partial value preview cannot replace a complete property. Retrieve the missing pages or author a complete typed value explicitly.");
    }

    internal static string AtomText(JsonElement input) => input.ValueKind switch
    {
        JsonValueKind.String => input.GetString()!,
        JsonValueKind.Number => input.GetRawText(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => throw new ArgumentException("An atom requires a string, number or Boolean value.")
    };

    public sealed record Location(ObjectId Object, string Property, ImmutableArray<string> Nested, ShadowValue Value);

    /// <summary>References are followed at path boundaries; numeric segments index a sequence or a collection's $items.</summary>
    public static Location Locate(ShadowSnapshot snapshot, ObjectId id, string path)
    {
        var segments = Segments(path);
        if (segments.Length == 0) throw new ArgumentException("A property path is required.", nameof(path));
        ShadowValue value = new ShadowValue.Reference(id);
        var owner = id;
        string? propertyName = null;
        var nested = ImmutableArray.CreateBuilder<string>();
        foreach (var segment in segments)
        {
            if (value is ShadowValue.Reference reference)
            {
                var node = snapshot.Objects.TryGetValue(reference.Id, out var found) ? found : throw new ArgumentException($"Unknown object {reference.Id}.");
                owner = node.Id;
                nested.Clear();
                if (Index(segment, out _) && node["$items"] is ShadowValue.Sequence items)
                {
                    propertyName = "$items";
                    value = items;
                }
                else
                {
                    if (!node.Properties.TryGetValue(segment, out value!)) throw new ArgumentException($"Unknown property {node.Id}/{Escape(segment)}.");
                    propertyName = segment;
                    continue;
                }
            }
            switch (value)
            {
                case ShadowValue.Structure structure when structure.Fields.TryGetValue(segment, out var field): value = field; break;
                case ShadowValue.Sequence sequence when Index(segment, out var index) && index < sequence.Items.Length: value = sequence.Items[index]; break;
                default: throw new ArgumentException($"Path segment '{segment}' does not exist. Inspect the parent value to discover valid fields or indices.");
            }
            nested.Add(segment);
        }
        return new(owner, propertyName!, nested.ToImmutable(), value);
    }

    public static ShadowValue Replace(ShadowValue root, ImmutableArray<string> nested, ShadowValue replacement, int index = 0)
    {
        if (index == nested.Length) return replacement;
        var segment = nested[index];
        return root switch
        {
            ShadowValue.Structure structure => structure with { Fields = structure.Fields.SetItem(segment, Replace(structure.Fields[segment], nested, replacement, index + 1)) },
            ShadowValue.Sequence sequence when Index(segment, out var item) => sequence with { Items = sequence.Items.SetItem(item, Replace(sequence.Items[item], nested, replacement, index + 1)) },
            _ => throw new ArgumentException("The property path changed while planning the edit.")
        };
    }

    public static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    internal static string[] Segments(string path)
    {
        if (string.IsNullOrEmpty(path)) return [];
        var result = (path.StartsWith('/') ? path[1..] : path).Split('/');
        if (result.Length > 48) throw new ArgumentException("Property paths are limited to 48 segments.");
        return result.Select(s => s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
    }
    internal static bool Index(string segment, out int index) => int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
}
