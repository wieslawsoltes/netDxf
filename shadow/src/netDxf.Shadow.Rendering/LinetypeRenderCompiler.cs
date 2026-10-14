using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Host-selected, revisioned resources for complex linetype decorations.
/// Return unit-size local geometry. No resource name implicitly authorizes file access.</summary>
public interface ILinetypeDecorationProvider
{
    long Revision { get; }
    bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic);
}

internal sealed record LinetypeExpansion(ImmutableArray<CadOccurrence> Occurrences, ImmutableArray<ShadowDiagnostic> Diagnostics, int PlannedPlacements);

/// <summary>Expands presentation only. The original CadScene remains the topology input.</summary>
internal sealed class LinetypeRenderCompiler
{
    private sealed class Cache { internal readonly Dictionary<(SamplingOptions Sampling, long Revision), LinetypeExpansion> Values = new(); }
    private readonly ConditionalWeakTable<CadOccurrence, Cache> cache = new();
    private readonly ILinetypeDecorationProvider? provider;
    internal LinetypePlanOptions Options { get; }
    internal long Revision => provider?.Revision ?? 0;
    internal LinetypeRenderCompiler(ILinetypeDecorationProvider? provider, LinetypePlanOptions? options)
    { this.provider = provider; Options = options ?? new(); Options.Validate(); }

    internal LinetypeExpansion Expand(CadOccurrence source, RenderItem item, SamplingOptions sampling, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Style.Linetype is not { } pattern || item.Filled || item.Text is not null || item.Image is not null || item.Contours.IsDefaultOrEmpty)
            return new([], [], 0);
        var revision = Revision;
        var values = cache.GetValue(source, _ => new());
        lock (values)
        {
            var key = (sampling, revision);
            if (values.Values.TryGetValue(key, out var retained)) return retained;
            var placements = LinetypePlanner.Plan(item.Contours, pattern, source.SourceToWorld, Options, token);
            var occurrences = ImmutableArray.CreateBuilder<CadOccurrence>(placements.Length);
            var diagnostics = ImmutableArray.CreateBuilder<ShadowDiagnostic>();
            var prototypes = new Dictionary<int, CadGeometry?>();
            var plainStyle = source.Style with { Dashes = [], Linetype = null };
            foreach (var placement in placements)
            {
                token.ThrowIfCancellationRequested();
                if (!prototypes.TryGetValue(placement.ElementIndex, out var prototype))
                {
                    prototype = Resolve(placement.Decoration, source.Entity, diagnostics);
                    prototypes.Add(placement.ElementIndex, prototype);
                }
                if (prototype is null) continue;
                var geometry = prototype.Transform(placement.Frame);
                occurrences.Add(new(source.Key + "/linetype[" + placement.ContourIndex + ":" + placement.ElementIndex + ":" + placement.Ordinal + "]",
                    source.Entity, source.InsertPath, plainStyle, geometry) { SourceToWorld = source.SourceToWorld });
            }
            if (revision != Revision) throw new InvalidOperationException("Linetype resources changed while compiling; retry the current resource revision.");
            var result = new LinetypeExpansion(occurrences.ToImmutable(), diagnostics.ToImmutable(), placements.Length);
            if (values.Values.Count >= 8) values.Values.Clear();
            values.Values[key] = result;
            return result;
        }
    }

    private CadGeometry? Resolve(CadLinetypeDecoration decoration, ObjectId entity, ImmutableArray<ShadowDiagnostic>.Builder diagnostics)
    {
        try
        {
            if (provider is not null && provider.TryProject(decoration, out var supplied, out var issue))
            {
                if (issue is not null) diagnostics.Add(new("linetype.resource", issue, entity));
                if (!supplied.Components.IsDefaultOrEmpty) throw new NotSupportedException("Linetype providers must return primitive local geometry, not component trees.");
                return supplied;
            }
            if (decoration.IsVertical || decoration.BigFont.Length != 0)
                throw new NotSupportedException("Vertical/BIGFONT linetype text requires a specialized layout provider.");
            if (decoration.Kind == CadLinetypeDecorationKind.Shape || decoration.Resource.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Linetype resource '{decoration.Resource}' is not available from the selected host provider.");
            return new([], Text: decoration.CreateText());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or FormatException or ArithmeticException)
        {
            diagnostics.Add(new("linetype.resource", error.Message, entity));
            return null;
        }
    }
}
