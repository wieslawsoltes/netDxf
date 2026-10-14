using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Host-selected, revisioned resources for complex linetype decorations.
/// Return unit-size local geometry. No resource name implicitly authorizes file access.
/// Providers remain responsible for bounding their own parsing, glyph compilation and custom geometry.</summary>
public interface ILinetypeDecorationProvider
{
    long Revision { get; }
    bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic);
}

internal sealed record LinetypeExpansion(ImmutableArray<CadOccurrence> Occurrences, ImmutableArray<ShadowDiagnostic> Diagnostics,
    int PlannedPlacements, long ExpansionWork);

/// <summary>A per-compilation ledger, also charged on cache hits and rejected groups.</summary>
internal sealed class LinetypeCompilationBudget(LinetypePlanOptions options)
{
    private long placements = options.MaximumScenePlacements;
    private long work = options.MaximumSceneExpansionWork;
    internal bool Exhausted => placements <= 0 || work <= 0;
    internal void Placements(long count) => Charge(ref placements, count, "Scene linetype placement budget exceeded.");
    internal void Work(long count) => Charge(ref work, count, "Scene linetype expansion work budget exceeded.");
    private static void Charge(ref long remaining, long count, string message)
    {
        if (count < 0 || count > remaining) { remaining = 0; throw new InvalidOperationException(message); }
        remaining -= count;
    }
}

/// <summary>Expands presentation only. The original CadScene remains the topology input.</summary>
internal sealed class LinetypeRenderCompiler
{
    private sealed class Cache { internal readonly Dictionary<(SamplingOptions Sampling, long Revision), LinetypeExpansion> Values = new(); }
    private sealed record Prototype(CadGeometry? Geometry, long Work);
    private readonly ConditionalWeakTable<CadOccurrence, Cache> cache = new();
    private readonly ILinetypeDecorationProvider? provider;
    internal LinetypePlanOptions Options { get; }
    internal long Revision => provider?.Revision ?? 0;
    internal LinetypeRenderCompiler(ILinetypeDecorationProvider? provider, LinetypePlanOptions? options)
    { this.provider = provider; Options = options ?? new(); Options.Validate(); }

    internal LinetypeExpansion Expand(CadOccurrence source, RenderItem item, SamplingOptions sampling,
        LinetypeCompilationBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Style.Linetype is not { } pattern || item.Filled || item.Text is not null || item.Image is not null || item.Contours.IsDefaultOrEmpty)
            return new([], [], 0, 0);
        if (budget.Exhausted) throw new InvalidOperationException("Scene linetype budget exhausted.");
        var revision = Revision;
        var values = cache.GetValue(source, _ => new());
        lock (values)
        {
            var key = (sampling, revision);
            if (values.Values.TryGetValue(key, out var retained))
            {
                budget.Placements(retained.PlannedPlacements); budget.Work(retained.ExpansionWork);
                return retained;
            }
            var placements = LinetypePlanner.Plan(item.Contours, pattern, source.SourceToWorld, Options, token);
            budget.Placements(placements.Length);
            var diagnostics = ImmutableArray.CreateBuilder<ShadowDiagnostic>();
            var prototypes = new Dictionary<int, Prototype>();
            long work = 0;
            // Resolve once per element and preflight the full expansion BEFORE copying geometry or shaping repeated text.
            foreach (var placement in placements)
            {
                token.ThrowIfCancellationRequested();
                if (!prototypes.TryGetValue(placement.ElementIndex, out var prototype))
                {
                    var geometry = Resolve(placement.Decoration, source.Entity, diagnostics);
                    prototype = new(geometry, ExpansionCost(geometry));
                    prototypes.Add(placement.ElementIndex, prototype);
                }
                budget.Work(prototype.Work);
                if (prototype.Work > Options.MaximumExpansionWork - work)
                    throw new InvalidOperationException("Linetype expansion work budget exceeded.");
                work += prototype.Work;
            }
            var occurrences = ImmutableArray.CreateBuilder<CadOccurrence>(placements.Length);
            var plainStyle = source.Style with { Dashes = [], Linetype = null };
            foreach (var placement in placements)
            {
                token.ThrowIfCancellationRequested();
                if (prototypes[placement.ElementIndex].Geometry is not { } prototype) continue;
                var geometry = prototype.Transform(placement.Frame);
                occurrences.Add(new(source.Key + "/linetype[" + placement.ContourIndex + ":" + placement.ElementIndex + ":" + placement.Ordinal + "]",
                    source.Entity, source.InsertPath, plainStyle, geometry)
                    { SourceToWorld = source.SourceToWorld, IsPresentationOnly = true });
            }
            if (revision != Revision) throw new InvalidOperationException("Linetype resources changed while compiling; retry the current resource revision.");
            var result = new LinetypeExpansion(occurrences.ToImmutable(), diagnostics.ToImmutable(), placements.Length, work);
            if (values.Values.Count >= 8) values.Values.Clear();
            values.Values[key] = result;
            return result;
        }
    }

    private static long ExpansionCost(CadGeometry? geometry)
    {
        if (geometry is null) return 1; // Missing resources still consume attempt work and placement quotas.
        long count = 1 + (long)(geometry.Text?.Content.Length ?? 0);
        count += Contours(geometry.Contours) + Contours(geometry.DisplayContours);
        foreach (var stroke in geometry.Strokes) count += 1 + Contours(stroke.Contours);
        foreach (var region in geometry.Regions) count += 1 + Contours(region.Contours);
        if (geometry.Image is { } image) count += image.Corners.Length + (long)image.ClipPolygon.Length;
        return count;
        static long Contours(ImmutableArray<CadContour> contours)
        {
            if (contours.IsDefaultOrEmpty) return 0;
            long result = contours.Length;
            foreach (var contour in contours) result += contour.Curves.Length;
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
