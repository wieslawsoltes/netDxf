using System.Collections.Immutable;
using System.Text.Json;

namespace netDxf.Shadow.Mcp;

/// <summary>A transport-independent MCP tool contract, also consumed directly by agent runtimes.</summary>
public sealed record McpToolDefinition(string Name, string Description, JsonElement InputSchema, bool ReadOnly = false);

/// <summary>Detached tool output. Tool failures are returned to the model; cancellation remains cancellation.</summary>
public sealed record McpToolResult(string Text, bool IsError = false)
{
    public static McpToolResult FromObject(object? value) => new(JsonSerializer.Serialize(value, McpJson.Options));
    public static McpToolResult Error(string code, string message, bool retryable = false) =>
        new(JsonSerializer.Serialize(new { error = new { code, message, retryable } }, McpJson.Options), true);
}

public interface IMcpToolExecutor
{
    IReadOnlyList<McpToolDefinition> Tools { get; }
    ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>Shared JSON conventions; values never retain a caller-owned JsonDocument.</summary>
public static class McpJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
        WriteIndented = false
    };

    public static JsonElement Element(object? value) => JsonSerializer.SerializeToElement(value, Options);
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.Clone();
    }
    public static JsonElement EmptyObject { get; } = Parse("{}");
}

/// <summary>
/// Copy-on-write registry with central input validation and bounded detached results.
/// Hosts extend the same registry with application, document and execution tools.
/// </summary>
public sealed class McpToolRegistry : IMcpToolExecutor
{
    private sealed record Registration(McpToolDefinition Definition, Func<JsonElement, CancellationToken, ValueTask<McpToolResult>> Handler);
    private sealed record RegistryState(ImmutableDictionary<string, Registration> Entries, ImmutableArray<McpToolDefinition> Tools);
    private readonly object gate = new();
    private RegistryState state = new(ImmutableDictionary<string, Registration>.Empty.WithComparers(StringComparer.Ordinal), []);

    public McpToolRegistry(int maximumArgumentCharacters = 1_048_576, int maximumResultCharacters = 131_072)
    {
        if (maximumArgumentCharacters < 1024) throw new ArgumentOutOfRangeException(nameof(maximumArgumentCharacters));
        if (maximumResultCharacters < 1024) throw new ArgumentOutOfRangeException(nameof(maximumResultCharacters));
        MaximumArgumentCharacters = maximumArgumentCharacters;
        MaximumResultCharacters = maximumResultCharacters;
    }

    public int MaximumArgumentCharacters { get; }
    public int MaximumResultCharacters { get; }
    public IReadOnlyList<McpToolDefinition> Tools => Volatile.Read(ref state).Tools;

    public void Register(McpToolDefinition definition, Func<JsonElement, CancellationToken, ValueTask<McpToolResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 128 ||
            definition.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.'))
            throw new ArgumentException("Tool names require 1–128 ASCII letters, digits, dots, underscores or hyphens.", nameof(definition));
        if (definition.InputSchema.ValueKind != JsonValueKind.Object ||
            !definition.InputSchema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object")
            throw new ArgumentException("A tool input schema must declare an object root.", nameof(definition));
        definition = definition with { InputSchema = definition.InputSchema.Clone() };
        lock (gate)
        {
            var before = state;
            if (before.Entries.ContainsKey(definition.Name)) throw new ArgumentException($"Tool '{definition.Name}' is already registered.", nameof(definition));
            Volatile.Write(ref state, new(before.Entries.Add(definition.Name, new(definition, handler)), before.Tools.Add(definition)));
        }
    }

    public async ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (name is null || !Volatile.Read(ref state).Entries.TryGetValue(name, out var registration))
            return McpToolResult.Error("unknown_tool", $"Unknown tool '{name}'. List tools to discover the current catalog.");
        if (arguments.ValueKind is JsonValueKind.Undefined) arguments = McpJson.EmptyObject;
        if (arguments.ValueKind != JsonValueKind.Object) return McpToolResult.Error("invalid_arguments", "Tool arguments must be a JSON object.");
        if (arguments.GetRawText().Length > MaximumArgumentCharacters)
            return McpToolResult.Error("arguments_too_large", $"Tool arguments exceed {MaximumArgumentCharacters} characters. Split the transaction into bounded batches.");
        try
        {
            McpSchemaValidator.Validate(registration.Definition.InputSchema, arguments);
            var result = await registration.Handler(arguments, cancellationToken).ConfigureAwait(false);
            if (result.Text.Length <= MaximumResultCharacters) return result;
            // Preserve success: marking a completed mutation as failed could cause a model to repeat it.
            return new(JsonSerializer.Serialize(new
            {
                truncated = true,
                originalCharacters = result.Text.Length,
                message = "The result exceeded the response budget. Use a smaller page, depth or property path to retrieve the complete data.",
                preview = result.Text[..Math.Min(result.Text.Length, MaximumResultCharacters / 2)]
            }, McpJson.Options), result.IsError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ShadowConflictException error) { return McpToolResult.Error("revision_conflict", error.Message + " Read shadow_state and inspect the changed objects before rebuilding the edit.", true); }
        catch (ArgumentException error) { return McpToolResult.Error("invalid_arguments", error.Message); }
        catch (JsonException error) { return McpToolResult.Error("invalid_json", error.Message); }
        catch (NotSupportedException error) { return McpToolResult.Error("unsupported_operation", error.Message); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            return McpToolResult.Error("execution_failed", error.GetBaseException().Message);
        }
    }
}
