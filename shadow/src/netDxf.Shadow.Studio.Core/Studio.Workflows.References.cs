using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private ShadowSnapshot? workflowReferenceSnapshot;
    private Dictionary<ObjectId, string> workflowReferences = [];
    private Dictionary<string, ObjectId> workflowReferenceIds = new(StringComparer.Ordinal);
    private void BuildWorkflowReferences(ShadowSnapshot source)
    {
        if (ReferenceEquals(workflowReferenceSnapshot, source)) return;
        if (source.Objects.Count > 250_000) throw new InvalidOperationException("Workflow selector object budget exceeded.");
        var names = new Dictionary<ObjectId, string>();
        foreach (var table in new[] { "Blocks", "Layers", "Linetypes", "TextStyles", "ShapeStyles" })
            foreach (var item in source.Document[table].Items)
                if (item.Id is { } id) names[id] = "$resource/" + table + "/" + Uri.EscapeDataString(item["Name"].Text);
        foreach (var state in source.Document["Layers"]["StateManager"].Items)
            if (state.Id is { } id) names[id] = "$layer-state/" + Uri.EscapeDataString(state["Name"].Text);
        foreach (var block in source.Document["Blocks"].Items)
        {
            var index = 0; foreach (var entity in block["Entities"].Items)
            { if (entity.Id is { } id) names[id] = "$entity/" + Uri.EscapeDataString(block["Name"].Text) + "/" + index; index++; }
        }
        workflowReferenceIds = names.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
        workflowReferences = names; workflowReferenceSnapshot = source;
    }
    // Portable references describe current semantic ownership/order, not random N:/O: capture identities.
    private string WorkflowReference(ObjectId id) => WorkflowReference(Current, id);
    private string WorkflowReference(ShadowSnapshot source, ObjectId id)
    {
        BuildWorkflowReferences(source);
        return workflowReferences.TryGetValue(id, out var reference) ? reference : throw new NotSupportedException(
            "Workflow references support named resources and direct block entities; this nested source object has no portable selector.");
    }
    private ObjectId ResolveWorkflowReference(string reference)
    {
        BuildWorkflowReferences(Current);
        return workflowReferenceIds.TryGetValue(reference, out var id) ? id : throw new ArgumentException("Workflow selector cannot be resolved in the current drawing: " + reference);
    }
    private JsonElement EncodeWorkflowArguments(JsonElement value) => TransformWorkflowArguments(value, false);
    private JsonElement DecodeWorkflowArguments(JsonElement value) => TransformWorkflowArguments(value, true);
    private JsonElement TransformWorkflowArguments(JsonElement value, bool decode)
    {
        JsonNode? Visit(JsonNode? node, string? key, int depth)
        {
            var source = workflowLastSnapshot ?? Current;
            if (depth > 32) throw new ArgumentException("Workflow argument depth exceeded.");
            if (node is JsonObject map)
            {
                if (map.Count == 1 && map["value"] is JsonValue idValue && idValue.TryGetValue<string>(out var id))
                {
                    if (decode && id.StartsWith('$')) return new JsonObject { ["value"] = ResolveWorkflowReference(id).Value };
                    if (!decode && source.Objects.ContainsKey(new(id))) return new JsonObject { ["value"] = WorkflowReference(source, new(id)) };
                }
                var result = new JsonObject(); foreach (var pair in map) result[pair.Key] = Visit(pair.Value, pair.Key, depth + 1); return result;
            }
            if (node is JsonArray array) { var result = new JsonArray(); foreach (var item in array) result.Add(Visit(item, key, depth + 1)); return result; }
            if (key is "entity" or "id" or "block" && node is JsonValue text && text.TryGetValue<string>(out var scalar))
            {
                if (decode && scalar.StartsWith('$')) return JsonValue.Create(ResolveWorkflowReference(scalar).Value);
                if (!decode && source.Objects.ContainsKey(new(scalar))) return JsonValue.Create(WorkflowReference(source, new(scalar)));
            }
            return node?.DeepClone();
        }
        return JsonSerializer.SerializeToElement(Visit(JsonNode.Parse(value.GetRawText()), null, 0), StudioJson.CreateOptions());
    }
}
