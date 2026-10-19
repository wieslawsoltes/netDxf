namespace netDxf.Shadow.Agents;

/// <summary>Ephemeral credentials for a read-only model catalogue request; never checkpoint this value.</summary>
public sealed record AgentModelDiscoveryRequest
{
    public string Provider { get; init; } = "openai";
    public string ApiKey { get; init; } = "";
    public string? Endpoint { get; init; }
    public override string ToString() => "Model discovery; ApiKey=[redacted]";
}

/// <summary>Limits applied across all pages of one discovery operation; no inference is performed.</summary>
public sealed record AgentModelDiscoveryLimits
{
    public int MaximumPages { get; init; } = 10;
    public int MaximumModels { get; init; } = 1000;
    public int MaximumResponseBytes { get; init; } = 1_048_576;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>Provider-reported availability and explicitly sourced capacity metadata. Null means unknown.</summary>
/// <param name="Id">Exact provider model identifier accepted by generation requests.</param>
/// <param name="DisplayName">Provider display name, falling back to the identifier.</param>
/// <param name="ContextWindowTokens">Shared input/output context capacity, only when explicitly documented.</param>
/// <param name="InputTokenLimit">Maximum input tokens; not an inferred combined context capacity.</param>
/// <param name="OutputTokenLimit">Maximum output tokens; not necessarily the remaining capacity of a particular request.</param>
/// <param name="MetadataSource">provider-api, official-exact-id-catalog, or unavailable.</param>
/// <param name="MetadataUrl">Primary documentation for the metadata interpretation, when known.</param>
public sealed record AgentDiscoveredModel(string Id, string DisplayName, int? ContextWindowTokens,
    int? InputTokenLimit, int? OutputTokenLimit, string MetadataSource, string? MetadataUrl);

/// <summary>A bounded catalogue, independent of the active conversation. Availability does not guarantee inference or tool support.</summary>
public sealed record AgentModelDiscoveryResult(string Provider, string Status,
    IReadOnlyList<AgentDiscoveredModel> Models, string Notice, bool Truncated);
