using System.Text;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

public sealed partial class AgentSession
{
    private readonly ConditionalWeakTable<McpToolDefinition, AgentFunction> functionDeclarations = new();
    private AgentFunction FunctionFor(McpToolDefinition tool) => functionDeclarations.GetValue(tool, definition => new(definition));
    private IEnumerable<McpToolDefinition> AvailableTools => ToolDefinitions
        .Where(tool => Options.PermissionMode != AgentPermissionMode.ReadOnly && Options.Mode != AgentRunMode.Plan || tool.ReadOnly)
        .OrderBy(tool => tool.Name, StringComparer.Ordinal);

    // Keep the harness useful with unrelated registries. Only describe workflows that
    // the current host actually exposes, and rebuild when a dynamic host changes tools.
    private string ToolGuidance()
    {
        var names = AvailableTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var text = new StringBuilder();
        if (names.Contains("shadow_state"))
            text.AppendLine("CAD workflow: begin with shadow_state for current document/workspace/revision receipts. Reuse unchanged receipts for reads, and use the receipt returned by each successful mutation for the next edit. A conflict requires fresh state and inspection; do not guess the next revision.");
        if (names.Contains("dxf_types"))
            text.AppendLine("Discover only the specific needed types with dxf_types and inspect their properties/constructors. Avoid enumerating the entire type catalog or reading the whole drawing when a filtered object query or narrow property path answers the question.");
        if (names.Contains("shadow_create_entities"))
            text.AppendLine("For new drawings and repeated geometry, prefer shadow_create_entities: supply many entities and any shared resources in one atomic batch, use aliases for shared layers/resources, and retain the returned entity IDs. Compute regular coordinates from the user's dimensions. Follow the advertised batch limit.");
        if (names.Contains("shadow_edit"))
            text.AppendLine("For existing geometry, collect current IDs with filtered shadow_objects/read queries, then put related property/path changes in one shadow_edit transaction. Follow pagination to cover the requested set. Verify the changed paths and diagnostics after the batch.");
        if (names.Contains("shadow_graph_edit"))
            text.AppendLine("Use shadow_graph_edit for general construction, attachment, references, collection replacement and deletion across the captured graph. New @aliases can be referenced by later operations in the same transaction. A preview validates the proposal but does not apply it.");
        if (names.Contains("graph_summary"))
            text.AppendLine("For geometric network analysis start with graph_summary, then request graph_components/features/nodes/edges as needed; use graph_nearest to resolve coordinates to node IDs before graph_path/traverse. These tools analyze geometric connectivity; shadow_references/traverse inspect object ownership and resource references. Preserve graph options across related queries.");
        if (text.Length > 0)
            text.AppendLine("Batch independent reads in one model turn. Mutations execute in order, but a later mutation cannot know an earlier result's new IDs or revision: group compatible writes into one transaction or wait for its receipt. Finish only after tools confirm the requested changes; report unresolved errors and unverified results accurately.");
        return text.ToString();
    }
}
