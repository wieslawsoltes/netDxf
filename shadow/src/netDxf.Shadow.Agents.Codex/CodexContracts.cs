using System.Text.Json;

namespace netDxf.Shadow.Agents.Codex;

/// <summary>Host-owned process settings. Never accept executable paths or arguments from an untrusted browser request.</summary>
public sealed record CodexProcessOptions
{
    public string Executable { get; init; } = "codex";
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    /// <summary>Explicit child-only environment supplied by the trusted host, e.g. a transient MCP token. Values are never logged.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    /// <summary>Optional isolated Codex configuration location. The library never reads login files.</summary>
    public string? CodexHome { get; init; }
    public int MaximumMessageBytes { get; init; } = 4 * 1024 * 1024;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public override string ToString() => $"CodexProcessOptions {{ Executable={Executable}, WorkingDirectory={WorkingDirectory}, Arguments=[redacted], Environment=[redacted] }}";
}

public sealed record CodexNotification(string Method, JsonElement Parameters);
public sealed record CodexServerRequest(JsonElement Id, string Method, JsonElement Parameters);

/// <summary>A remote JSON-RPC error. Data is retained without interpreting experimental extensions.</summary>
public sealed class CodexRpcException(int code, string message, JsonElement? data = null) : Exception(message)
{
    public int Code { get; } = code;
    public JsonElement? ErrorData { get; } = data;
}
