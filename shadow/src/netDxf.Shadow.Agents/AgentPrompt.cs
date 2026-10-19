using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

/// <summary>One user message with immutable drawing context. References are data, not instructions or a mutation grant.</summary>
public sealed record AgentPrompt(string Text, IReadOnlyList<DrawingObjectReference>? References = null)
{
    public const string ReferenceStart = "<shadow-drawing-references-v1>";
    public const string ReferenceEnd = "</shadow-drawing-references-v1>";
    public AgentPrompt Validate()
    {
        if (Text is null || Text.Length > 256000) throw new ArgumentException("Prompt exceeds 256,000 characters or is missing.");
        return this with { References = DrawingReferences.ValidateShape(References) };
    }
    /// <summary>A portable context declaration retained in external agents' ordinary text history.</summary>
    public string ToContextText() => References is { Count: > 0 }
        ? Text + "\n\n" + ReferenceStart + "\n" + JsonSerializer.Serialize(References, McpJson.Options) + "\n" + ReferenceEnd : Text;
}

public sealed partial class AgentSession
{
    private sealed record PromptMetadata(string Id, AgentPrompt Prompt);
    private static readonly ConditionalWeakTable<ChatMessage, PromptMetadata> promptMetadata = new();
    private readonly List<AgentMessageRecord> archivedReferences = [];
    public Task RunAsync(AgentPrompt prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return RunCoreAsync(prompt.Validate(), cancellationToken, validateReferences: true);
    }
}
