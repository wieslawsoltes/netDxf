using System.Text.Json;

namespace netDxf.Shadow.Agents.Acp;

/// <summary>Host-configured executable and arguments. No shell parsing or credential discovery is performed.</summary>
public sealed record AcpProcessOptions(string Executable, string WorkingDirectory,
    IReadOnlyList<string>? Arguments = null, IReadOnlyDictionary<string, string>? Environment = null);

public sealed record AcpConnectionOptions
{
    public int MaximumMessageBytes { get; init; } = 4 * 1024 * 1024;
    public int MaximumPendingRequests { get; init; } = 64;
    public int MaximumIncomingRequests { get; init; } = 32;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan PermissionTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan CancellationGrace { get; init; } = TimeSpan.FromSeconds(10);
    internal void Validate()
    {
        if (MaximumMessageBytes is < 1024 or > 64 * 1024 * 1024 || MaximumPendingRequests is < 1 or > 1024 || MaximumIncomingRequests is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
        if (RequestTimeout <= TimeSpan.Zero || PermissionTimeout <= TimeSpan.Zero || CancellationGrace <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
    }
}

/// <summary>Permission IDs/options remain opaque. Returning null declines/cancels; only an offered option can be selected.</summary>
public sealed record AcpPermissionRequest(string RequestId, string SessionId, JsonElement ToolCall, JsonElement Options, JsonElement Raw);

/// <summary>Optional client services. Their presence determines advertised capabilities. The host enforces workspace and process policy.</summary>
public sealed record AcpClientServices
{
    public Action<string, JsonElement>? Notification { get; init; }
    public Func<AcpPermissionRequest, CancellationToken, Task<string?>>? RequestPermissionAsync { get; init; }
    public Func<JsonElement, CancellationToken, Task<JsonElement>>? ReadTextFileAsync { get; init; }
    public Func<JsonElement, CancellationToken, Task<JsonElement>>? WriteTextFileAsync { get; init; }
    /// <summary>Implements all five terminal methods: create, output, wait_for_exit, kill, release.</summary>
    public Func<string, JsonElement, CancellationToken, Task<JsonElement>>? TerminalRequestAsync { get; init; }
    /// <summary>Host renders and validates the requested form schema; returns ACP action/content. Never collect secrets in form mode.</summary>
    public Func<JsonElement, CancellationToken, Task<JsonElement>>? ElicitFormAsync { get; init; }
    /// <summary>Host presents the agent-provided URL for explicit user interaction; never opens it automatically.</summary>
    public Func<JsonElement, CancellationToken, Task<JsonElement>>? ElicitUrlAsync { get; init; }
    /// <summary>Optional stderr observer. Logs are untrusted and may contain secrets; omitted by default.</summary>
    public Action<string>? StandardError { get; init; }
}

public sealed class AcpException(int code, string message, JsonElement? errorData = null) : Exception(message)
{
    public int Code { get; } = code;
    public JsonElement? ErrorData { get; } = errorData;
}

internal static class AcpJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    internal static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    internal static readonly JsonElement Empty = Element(new { });
    internal static string RequiredString(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            && item.GetString() is { Length: > 0 and <= 16384 } text ? text
            : throw new AcpException(-32602, "Expected a nonempty string: " + name);
    internal static bool Has(JsonElement value, params string[] path)
    {
        foreach (var part in path) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value)) return false;
        return value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False);
    }
}
