using System.Text.Json;

namespace netDxf.Shadow.Mcp;

/// <summary>Identity of one drawing workspace at the instant context was captured.</summary>
public sealed record DrawingReferenceIdentity(Guid DocumentId, string WorkspaceId, long ViewVersion, long Revision);
/// <summary>A stable source object plus the exact displayed occurrence. Handles are descriptive, never a fallback identity.</summary>
public sealed record DrawingObjectReference(Guid DocumentId, string WorkspaceId, long ViewVersion, long Revision,
    string ObjectId, string? Handle, string Type, string Layout, string? OccurrenceKey = null,
    IReadOnlyList<string>? InsertPath = null, string? DefinitionObjectId = null);
public sealed record DrawingReferenceCaptureRequest(Guid DocumentId, string WorkspaceId, long ViewVersion, long Revision, int Maximum = 32);
public sealed record DrawingReferenceSet(DrawingReferenceIdentity Identity, IReadOnlyList<DrawingObjectReference> References);
public sealed record DrawingReferenceValidationRequest(IReadOnlyList<DrawingObjectReference> References, bool Refresh = false);
public sealed record DrawingReferenceResolution(DrawingObjectReference Reference, string Status, DrawingObjectReference? Current, string Message);
public sealed record DrawingReferenceValidation(DrawingReferenceIdentity Identity, bool CanSend, IReadOnlyList<DrawingReferenceResolution> Items);

/// <summary>Shared bounded wire contract used by native, browser and external agent hosts.</summary>
public static class DrawingReferences
{
    public const string ValidationTool = "studio_references_validate";
    public const int MaximumCount = 32;
    public static DrawingObjectReference[] ValidateShape(IReadOnlyList<DrawingObjectReference>? references)
    {
        if (references is null) return [];
        if (references.Count > MaximumCount) throw new ArgumentException("Attach at most 32 drawing references.");
        var keys = new HashSet<(Guid, string, string, string?)>();
        static bool Text(string? value, int maximum = 512) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);
        foreach (var r in references)
        {
            if (r is null || r.DocumentId == Guid.Empty || !Text(r.WorkspaceId) || r.ViewVersion < 0 || r.Revision < 0 || !Text(r.ObjectId)
                || !Text(r.Type) || !Text(r.Layout) || r.Handle is not null && !Text(r.Handle)
                || r.OccurrenceKey is not null && !Text(r.OccurrenceKey, 4096) || r.DefinitionObjectId is not null && !Text(r.DefinitionObjectId)
                || r.InsertPath?.Count > 64 || r.InsertPath?.Any(id => !Text(id)) == true)
                throw new ArgumentException("Drawing references require bounded object, occurrence and workspace identities.");
            if (!keys.Add((r.DocumentId, r.WorkspaceId, r.ObjectId, r.OccurrenceKey))) throw new ArgumentException("Duplicate drawing reference.");
        }
        return references.Select(r => r with { InsertPath = r.InsertPath?.ToArray() }).ToArray();
    }
    /// <summary>Revalidates the actual host drawing; never accepts browser assertions of freshness.</summary>
    public static async Task<DrawingObjectReference[]> ValidateCurrentAsync(IMcpToolExecutor executor,
        IReadOnlyList<DrawingObjectReference>? references, CancellationToken cancellationToken = default)
    {
        var requested = ValidateShape(references); if (requested.Length == 0) return [];
        var result = await executor.ExecuteAsync(ValidationTool, McpJson.Element(new DrawingReferenceValidationRequest(requested)), cancellationToken).ConfigureAwait(false);
        if (result.IsError) throw new InvalidOperationException("Drawing references could not be validated against the active drawing. Refresh them before sending.");
        var validation = JsonSerializer.Deserialize<DrawingReferenceValidation>(result.Text, McpJson.Options);
        if (validation?.CanSend != true || validation.Items is null || validation.Items.Count != requested.Length
            || validation.Items.Any(i => i.Status != "current" || i.Current is null))
            throw new InvalidOperationException("Attached drawing objects changed, were deleted, or belong to another workspace. Refresh or remove the affected references before sending.");
        var canonical = ValidateShape(validation.Items.Select(i => i.Current!).ToArray());
        for (var index = 0; index < canonical.Length; index++)
            if (McpJson.Element(canonical[index]).GetRawText() != McpJson.Element(requested[index]).GetRawText()
                || canonical[index].DocumentId != validation.Identity.DocumentId || canonical[index].WorkspaceId != validation.Identity.WorkspaceId
                || canonical[index].Revision != validation.Identity.Revision || canonical[index].ViewVersion != validation.Identity.ViewVersion)
                throw new InvalidOperationException("Drawing reference validation changed the captured identity. Capture the selection again.");
        return canonical;
    }
}
