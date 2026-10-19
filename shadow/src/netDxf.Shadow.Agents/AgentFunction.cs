using System.Text.Json;
using System.Text;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

internal sealed class AgentFunction(McpToolDefinition definition) : AIFunctionDeclaration
{
    internal int ContextBytes { get; } = Encoding.UTF8.GetByteCount(definition.InputSchema.GetRawText()) + Encoding.UTF8.GetByteCount(definition.Description) + 128;
    // MCP schemas preserve optional fields, open property bags and arbitrary typed CAD
    // values. Responses otherwise attempts strict normalization, which can change their
    // semantics (for example forcing every optional edit field to be present).
    public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; } =
        new Dictionary<string, object?> { ["strict"] = false };
    public override string Name => definition.Name;
    public override string Description => definition.Description;
    public override JsonElement JsonSchema => definition.InputSchema;
}
