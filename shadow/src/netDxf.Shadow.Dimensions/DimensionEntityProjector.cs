using System.Collections.Immutable;
using System.Globalization;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;

namespace netDxf.Shadow.Dimensions;

public sealed record DimensionProjectionOptions(int MaximumSourceObjects = 4096, int MaximumSourceValues = 131_072,
    int MaximumGeneratedOccurrences = 16_384, int MaximumDepth = 32, int MaximumTextLength = 262_144, string CultureName = "");

/// <summary>Regenerates all eight netDxf dimension families from detached public values. The
/// mutable generation graph is private and temporary; only immutable components escape.</summary>
public sealed class DimensionEntityProjector : ICacheableEntityProjector
{
    private readonly DimensionProjectionOptions options;
    private readonly ImmutableArray<ICacheableEntityProjector> resources;
    private readonly CultureInfo culture;
    public long Revision => resources.Aggregate(1L, (value, resource) => unchecked(value * 31 + resource.Revision));

    public DimensionEntityProjector(DimensionProjectionOptions? options = null, IEnumerable<ICacheableEntityProjector>? resources = null)
    {
        this.options = options ?? new();
        if (this.options.MaximumSourceObjects < 1 || this.options.MaximumSourceValues < 1 || this.options.MaximumGeneratedOccurrences < 1 ||
            this.options.MaximumDepth < 1 || this.options.MaximumDepth > 128 || this.options.MaximumTextLength < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        culture = CultureInfo.GetCultureInfo(this.options.CultureName);
        this.resources = (resources ?? []).ToImmutableArray();
        if (this.resources.Any(p => p is DimensionEntityProjector)) throw new ArgumentException("Dimension projectors cannot recursively invoke themselves.");
    }

    public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
    {
        geometry = CadGeometry.Empty; diagnostic = null;
        if (entity.Type is not ("AlignedDimension" or "LinearDimension" or "Angular2LineDimension" or "Angular3PointDimension" or
            "RadialDimension" or "DiametricDimension" or "OrdinateDimension" or "ArcLengthDimension")) return false;
        var version = Revision;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            var dimension = new DimensionSnapshotReader(options).Read(entity);
            var generated = new DxfDocument { BuildDimensionBlocks = false };
            generated.DrawingVariables.LtScale = entity.Resolve(entity.Snapshot!.Root)["DrawingVariables"]["LtScale"].Number(1);
            // Negative DIMLFAC depends on the definition owner's space, not the displayed viewport.
            if (entity["Owner"]["Record"]["Layout"]["IsPaperSpace"].Boolean())
            {
                var context = new Layout("Dimension context"); generated.Layouts.Add(context); context.AssociatedBlock.Entities.Add(dimension);
            }
            else
            {
                var context = new Block("Dimension context"); context.Entities.Add(dimension); generated.Blocks.Add(context);
            }
            var block = DimensionBlock.Build(dimension, "Dimension presentation") ?? throw new NotSupportedException("The dimension builder returned no geometry.");
            var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
            var origin = basis.Point(new(0, 0, entity["Elevation"].Number()));
            generated.Entities.Add(new Insert(block, new Vector3(origin.X, origin.Y, origin.Z)) { Normal = dimension.Normal });
            var detached = new ShadowSession(generated).Current;
            var scene = new CadSceneBuilder(resources).Build(detached,
                new SceneOptions(MaximumDepth: options.MaximumDepth, MaximumOccurrences: options.MaximumGeneratedOccurrences, IncludeInvisible: true));
            if (scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException("Generated dimension contains invalid geometry: " + string.Join("; ", scene.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
            var parts = ImmutableArray.CreateBuilder<CadComponent>(scene.Occurrences.Length);
            var index = 0;
            foreach (var occurrence in scene.Occurrences)
            {
                // Remove semantic analysis curves, preserving their exact presentation/filled contours.
                var geometryPart = occurrence.Geometry with
                {
                    Contours = [], DisplayContours = occurrence.Geometry.DisplayContours.IsDefault ? occurrence.Geometry.Contours : occurrence.Geometry.DisplayContours
                };
                parts.Add(new((index++).ToString(CultureInfo.InvariantCulture), geometryPart, BindStyle(detached, occurrence)));
            }
            if (Revision != version) throw new InvalidOperationException("Dimension resources changed during regeneration. Retry with current resources.");
            geometry = new([]) { Components = parts.ToImmutable() };
            var warnings = scene.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Error).Select(d => d.Message).Distinct().Take(8).ToArray();
            diagnostic = warnings.Length == 0 ? null : string.Join("; ", warnings);
            return true;
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static CadComponentStyle BindStyle(ShadowSnapshot snapshot, CadOccurrence occurrence)
    {
        var color = CadStyleSource.Parent; var alpha = CadStyleSource.Parent; var weight = CadStyleSource.Parent; var dashes = CadStyleSource.Parent;
        var dashScale = 1.0; var layerZero = true;
        // The first path entry is our synthetic container and must not overwrite dimension inheritance.
        foreach (var id in occurrence.InsertPath.Skip(1).Append(occurrence.Entity))
        {
            var entity = snapshot.Read(id);
            if (entity["Layer"]["Name"].Text != "0") layerZero = false;
            var c = entity["Color"];
            if (!c["IsByBlock"].Boolean()) color = c["IsByLayer"].Boolean() && layerZero ? CadStyleSource.Layer : CadStyleSource.Explicit;
            var a = entity["Transparency"];
            if (!a["IsByBlock"].Boolean()) alpha = a["IsByLayer"].Boolean() && layerZero ? CadStyleSource.Layer : CadStyleSource.Explicit;
            var w = entity["Lineweight"].Text;
            if (w != "ByBlock") weight = w == "ByLayer" && layerZero ? CadStyleSource.Layer : CadStyleSource.Explicit;
            var l = entity["Linetype"]["Name"].Text;
            if (string.Equals(l, "ByBlock", StringComparison.OrdinalIgnoreCase)) dashScale *= entity["LinetypeScale"].Number(1);
            else
            {
                dashes = string.Equals(l, "ByLayer", StringComparison.OrdinalIgnoreCase) && layerZero ? CadStyleSource.Layer : CadStyleSource.Explicit;
                dashScale = entity["LinetypeScale"].Number(1);
            }
        }
        return new(occurrence.Style, color, alpha, weight, dashes, layerZero, dashes == CadStyleSource.Explicit ? 1 : dashScale);
    }
}
