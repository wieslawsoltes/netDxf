using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace netDxf.Shadow.AgentHost;

public sealed partial class ExternalAgentHost
{
    private JsonElement CodexParameters(ExternalAgentCall call)
    {
        var p = call.Parameters; var args = new JsonObject();
        switch (call.Method)
        {
            case "account/read": args["refreshToken"] = false; break;
            case "account/login/start":
                var type = RequiredString(p, "type");
                if (type is not ("chatgpt" or "chatgptDeviceCode")) throw new ArgumentException("Use Codex's browser or device login flow.");
                args["type"] = type; break;
            case "account/login/cancel": args["loginId"] = RequiredString(p, "loginId"); break;
            case "account/logout": break;
            case "model/list":
                args["limit"] = PageLimit(p); args["includeHidden"] = false; OptionalString(p, args, "cursor", 4096); break;
            case "thread/start":
            case "thread/resume":
                args["cwd"] = options.Workspace; args["approvalPolicy"] = "unlessTrusted"; args["sandbox"] = "workspaceWrite";
                args["model"] = RequiredString(p, "model", 256);
                if (call.Method == "thread/resume") { args["threadId"] = RequiredString(p, "threadId"); args["excludeTurns"] = true; }
                break;
            case "thread/list":
                args["cwd"] = options.Workspace; args["limit"] = PageLimit(p); OptionalString(p, args, "cursor", 4096); break;
            case "thread/read": args["threadId"] = RequiredString(p, "threadId"); args["includeTurns"] = false; break;
            case "thread/turns/list":
            case "thread/items/list":
                args["threadId"] = RequiredString(p, "threadId"); args["limit"] = PageLimit(p);
                OptionalEnum(p, args, "sortDirection", ["asc", "desc"]);
                if (call.Method == "thread/turns/list")
                {
                    OptionalString(p, args, "cursor", 4096); OptionalEnum(p, args, "itemsView", ["notLoaded", "summary", "full"]);
                }
                else
                {
                    OptionalString(p, args, "turnId");
                    if (p.TryGetProperty("cursor", out var cursor) && cursor.ValueKind != JsonValueKind.Null)
                    {
                        if (cursor.ValueKind == JsonValueKind.String) args["cursor"] = RequiredString(p, "cursor", 4096);
                        else if (cursor.ValueKind == JsonValueKind.Object && RequiredString(cursor, "type") == "item")
                        {
                            _ = RequiredString(p, "turnId");
                            args["cursor"] = new JsonObject { ["type"] = "item", ["itemId"] = RequiredString(cursor, "itemId") };
                        }
                        else throw new ArgumentException("Supply a history cursor or item anchor.");
                    }
                }
                break;
            case "turn/start":
                args["threadId"] = RequiredString(p, "threadId"); args["input"] = TextContent(p, "input"); break;
            case "turn/steer":
                args["threadId"] = RequiredString(p, "threadId"); args["expectedTurnId"] = RequiredString(p, "expectedTurnId");
                args["input"] = TextContent(p, "input"); break;
            case "thread/compact/start": args["threadId"] = RequiredString(p, "threadId"); break;
            case "turn/interrupt": args["threadId"] = RequiredString(p, "threadId"); args["turnId"] = RequiredString(p, "turnId"); break;
            default: throw new ArgumentException("This Codex method is not exposed by the Studio host.");
        }
        return JsonSerializer.SerializeToElement(args, Json);
    }
    private JsonElement AcpParameters(State state, ExternalAgentCall call)
    {
        var p = call.Parameters; var args = new JsonObject();
        switch (call.Method)
        {
            case "authenticate": args["methodId"] = RequiredString(p, "methodId"); break;
            case "logout": break;
            case "session/new":
            case "session/load":
            case "session/resume":
                if (call.Method != "session/new") args["sessionId"] = RequiredString(p, "sessionId");
                args["cwd"] = options.Workspace;
                var http = state.Acp!.Initialization.TryGetProperty("agentCapabilities", out var caps)
                    && caps.TryGetProperty("mcpCapabilities", out var mcp) && mcp.TryGetProperty("http", out var supported) && supported.ValueKind == JsonValueKind.True;
                args["mcpServers"] = http ? new JsonArray(new JsonObject { ["type"] = "http", ["name"] = "shadow-studio",
                    ["url"] = $"http://127.0.0.1:{options.Port}/mcp",
                    ["headers"] = new JsonArray(new JsonObject { ["name"] = "Authorization", ["value"] = "Bearer " + bridgeToken }) }) : new JsonArray();
                break;
            case "session/list": args["cwd"] = options.Workspace; OptionalString(p, args, "cursor", 4096); break;
            case "session/close":
            case "session/delete": args["sessionId"] = RequiredString(p, "sessionId"); break;
            case "session/prompt": args["sessionId"] = RequiredString(p, "sessionId"); args["prompt"] = TextContent(p, "prompt"); break;
            case "session/set_mode": args["sessionId"] = RequiredString(p, "sessionId"); args["modeId"] = RequiredString(p, "modeId"); break;
            case "session/set_config_option":
                args["sessionId"] = RequiredString(p, "sessionId"); args["configId"] = RequiredString(p, "configId");
                if (!p.TryGetProperty("value", out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("Choose an advertised string or boolean configuration value.");
                args["value"] = value.ValueKind == JsonValueKind.String ? JsonValue.Create(RequiredString(p, "value", 4096)) : JsonValue.Create(value.GetBoolean());
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) args["type"] = "boolean";
                break;
            default: throw new ArgumentException("This ACP method is not exposed by the Studio host.");
        }
        return JsonSerializer.SerializeToElement(args, Json);
    }
    private static JsonArray TextContent(JsonElement parameters, string property)
    {
        if (!parameters.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 16)
            throw new ArgumentException("Supply a bounded nonempty text prompt array.");
        var result = new JsonArray(); var bytes = 0;
        foreach (var part in values.EnumerateArray())
        {
            if (RequiredString(part, "type") != "text") throw new ArgumentException("This Studio interface accepts text prompts only.");
            var text = RequiredString(part, "text", 64 * 1024, multiline: true); bytes += Encoding.UTF8.GetByteCount(text);
            if (bytes > 64 * 1024) throw new ArgumentException("The text prompt exceeds 64 KiB.");
            result.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        }
        return result;
    }
    private static int PageLimit(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("limit", out var limit) || limit.ValueKind == JsonValueKind.Null) return 50;
        if (limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out var value) || value is < 1 or > 100)
            throw new ArgumentException("Page limit must be between 1 and 100.");
        return value;
    }
    private static void OptionalEnum(JsonElement parameters, JsonObject target, string property, string[] choices)
    {
        if (!parameters.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return;
        var text = RequiredString(parameters, property);
        if (!choices.Contains(text, StringComparer.Ordinal)) throw new ArgumentException("Unsupported " + property + ".");
        target[property] = text;
    }
    private static void OptionalString(JsonElement parameters, JsonObject target, string property, int maximum = 512)
    {
        if (parameters.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null) target[property] = RequiredString(parameters, property, maximum);
    }
    private static string RequiredString(JsonElement parameters, string property, int maximum = 512, bool multiline = false)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } text || text.Length > maximum || string.IsNullOrWhiteSpace(text)
            || text.Contains('\0') || !multiline && text.Any(char.IsControl))
            throw new ArgumentException("Supply a bounded " + property + " string.");
        return text;
    }
    private static void RequireObject(JsonElement value, int maximumBytes)
    {
        if (value.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(value.GetRawText()) > maximumBytes)
            throw new ArgumentException("Agent parameters must be a bounded JSON object.");
        void Unique(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in node.EnumerateObject())
                { if (!keys.Add(property.Name)) throw new ArgumentException("Duplicate JSON properties are not supported."); Unique(property.Value); }
            }
            else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) Unique(child);
        }
        Unique(value);
    }
}
