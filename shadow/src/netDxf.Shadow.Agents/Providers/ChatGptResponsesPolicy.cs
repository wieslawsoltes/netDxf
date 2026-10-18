using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace netDxf.Shadow.Agents;

/// <summary>Enforces the documented Sign in with ChatGPT Responses request contract.</summary>
/// <remarks>https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations</remarks>
internal sealed class ChatGptResponsesPolicy : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (IsResponseRequest(message))
        {
            using var buffer = new MemoryStream();
            message.Request.Content!.WriteTo(buffer, message.CancellationToken);
            Rewrite(message, buffer);
        }
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (IsResponseRequest(message))
        {
            using var buffer = new MemoryStream();
            await message.Request.Content!.WriteToAsync(buffer, message.CancellationToken).ConfigureAwait(false);
            Rewrite(message, buffer);
        }
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static bool IsResponseRequest(PipelineMessage message) => message.Request.Method == "POST"
        && message.Request.Uri?.AbsolutePath.TrimEnd('/').EndsWith("/responses", StringComparison.Ordinal) == true
        && message.Request.Content is not null;

    private static void Rewrite(PipelineMessage message, MemoryStream buffer)
    {
        var root = JsonNode.Parse(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)))?.AsObject()
            ?? throw new InvalidOperationException("The Responses request has no JSON object.");
        ChatGptResponsesContract.Normalize(root);
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(root.ToJsonString()));
    }
}

/// <summary>Normalizes a private request JSON object to OpenAI's documented ChatGPT plan usage contract.</summary>
/// <remarks>The supplied object is updated in place. The same contract is applied in the browser SDK and the local companion.</remarks>
public static class ChatGptResponsesContract
{
    private const string LocalNamespace = "shadow";
    private static readonly string[] UnsupportedFields =
    [
        "background", "conversation", "max_output_tokens", "max_tool_calls", "metadata", "moderation",
        "multi_agent", "prompt", "prompt_cache_retention", "safety_identifier", "temperature",
        "top_logprobs", "top_p", "truncation", "user", "previous_response_id",
    ];

    public static JsonObject Normalize(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var field in UnsupportedFields) root.Remove(field);
        root["store"] = false;
        root["stream"] = true;
        if (root["input"] is JsonValue text && text.TryGetValue<string>(out var prompt))
            root["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt });
        if (root["input"] is JsonArray inputs)
        {
            foreach (var item in inputs.OfType<JsonObject>())
                if (item["role"]?.GetValue<string>() == "system") item["role"] = "developer";
        }
        else
        {
            root["input"] = new JsonArray();
        }
        if (root["tools"] is JsonArray tools)
        {
            foreach (var entry in tools)
            {
                if (entry is not JsonObject tool) throw new ArgumentException("Tool definitions must be JSON objects.");
                var kind = tool["type"]?.GetValue<string>();
                if (kind is not ("function" or "custom" or "namespace" or "web_search" or "web_search_preview"))
                    throw new ArgumentException($"Tool type '{kind}' is unavailable for ChatGPT plan usage. Execute drawing and MCP tools locally through function tools.");
                if (kind == "namespace") ValidateNamespace(tool);
            }
            var functions = tools.OfType<JsonObject>().Where(tool => tool["type"]?.GetValue<string>() is "function" or "custom").ToArray();
            if (functions.Length != 0)
            {
                var existing = tools.OfType<JsonObject>().FirstOrDefault(tool => tool["type"]?.GetValue<string>() == "namespace"
                    && tool["name"]?.GetValue<string>() == LocalNamespace);
                var members = existing?["tools"]?.AsArray() ?? new JsonArray();
                foreach (var function in functions)
                {
                    tools.Remove(function);
                    members.Add(function);
                }
                if (existing is null) tools.Add(new JsonObject
                {
                    ["type"] = "namespace", ["name"] = LocalNamespace,
                    ["description"] = "netDxf drawing, shadow model, and studio tools", ["tools"] = members,
                });
            }
        }
        // The current official .NET SDK predates the wire-level namespace property. Portable history
        // therefore needs it restored when re-encoding calls to our grouped local function tools.
        // https://developers.openai.com/api/reference/resources/responses/methods/create
        if (root["input"] is JsonArray history)
        {
            var namespaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (root["tools"] is JsonArray definitions)
                foreach (var group in definitions.OfType<JsonObject>().Where(tool => tool["type"]?.GetValue<string>() == "namespace"))
                    foreach (var member in group["tools"]!.AsArray().OfType<JsonObject>())
                    {
                        var name = member["name"]?.GetValue<string>() ?? throw new ArgumentException("A namespace member requires a tool name.");
                        if (!namespaces.TryGetValue(name, out var values)) namespaces[name] = values = new(StringComparer.Ordinal);
                        values.Add(group["name"]!.GetValue<string>());
                    }
            foreach (var item in history.OfType<JsonObject>())
            {
                if (item["type"]?.GetValue<string>() is not ("function_call" or "custom_tool_call")
                    || item["namespace"]?.GetValue<string>() is { Length: > 0 }) continue;
                var name = item["name"]?.GetValue<string>() ?? throw new ArgumentException("A function call requires a tool name.");
                if (namespaces.TryGetValue(name, out var values) && values.Count > 1)
                    throw new ArgumentException("A tool call with an ambiguous name must preserve its explicit namespace.");
                item["namespace"] = values?.Single() ?? LocalNamespace;
            }
        }
        return root;
    }

    private static void ValidateNamespace(JsonObject tool)
    {
        if (tool["name"]?.GetValue<string>() is not { Length: > 0 } || tool["tools"] is not JsonArray members
            || members.Any(member => member is not JsonObject definition
                || definition["type"]?.GetValue<string>() is not ("function" or "custom")))
            throw new ArgumentException("A namespace must name a group containing only local function or custom tools.");
    }
}
