using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public sealed partial class CadSceneBuilder
{
    private (ShadowValue Entities, ShadowValue Definitions, ShadowValue Membership)? rootMembership;
    private CadScene BuildCore(ShadowSnapshot snapshot, SceneOptions options, CancellationToken cancellationToken)
    {
        if (options.MaximumDepth < 1 || options.MaximumOccurrences < 1 || !(options.LinetypeScale > 0) || !double.IsFinite(options.LinetypeScale)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.RootBlock is { } root && snapshot.Read(root).Object is not { Kind: ShadowObjectKind.Block })
            throw new ArgumentException("The scene root is not a source block.", nameof(options));
        if (options.RootBlock is null && !snapshot.Layout(options.Layout).Exists) throw new ArgumentException($"Unknown layout '{options.Layout}'.", nameof(options));
        if (!Enum.IsDefined(options.LinetypeAlignment)) throw new ArgumentOutOfRangeException(nameof(options));
        var output = ImmutableArray.CreateBuilder<CadOccurrence>();
        var diagnostics = snapshot.Diagnostics.ToBuilder();
        var activeBlocks = new HashSet<ObjectId>();
        var visits = 0;
        if (options.RootBlock is { } activeRoot) activeBlocks.Add(activeRoot);
        var owner = options.RootBlock is { } blockRoot ? snapshot.Read(blockRoot) : snapshot.Layout(options.Layout)["AssociatedBlock"];
        var entities = owner["Entities"];
        var definitions = owner["AttributeDefinitions"];
        var entityMembership = entities.Value is ShadowValue.Sequence ? entities.Value : entities["$items"].Value;
        var definitionMembership = definitions.Value is ShadowValue.Sequence ? definitions.Value : definitions["$items"].Value;
        var membership = rootMembership is { } previous && ReferenceEquals(previous.Entities, entityMembership) && ReferenceEquals(previous.Definitions, definitionMembership)
            ? previous.Membership : new ShadowValue.Sequence([entityMembership, definitionMembership]);
        rootMembership = (entityMembership, definitionMembership, membership);
        var projectionVersion = ProjectionVersion;
        var rootTransaction = rootCache.Begin(snapshot, options, projectionVersion, membership, IsSnapshotCacheable);
        ShadowReadSet? rootReads = null;
        foreach (var entity in entities.Items.Concat(definitions.Items.Select(entry => entry["Value"])))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entity.Id is not { } rootId) continue;
            if (rootTransaction.TryGet(rootId, out var cached))
            {
                visits = checked(visits + cached.Visits);
                if (visits > options.MaximumOccurrences) throw new InvalidOperationException("Scene occurrence budget exceeded.");
                output.AddRange(cached.Occurrences); diagnostics.AddRange(cached.Diagnostics);
                continue;
            }
            rootReads = new();
            var start = output.Count; var issueStart = diagnostics.Count; var startVisits = visits;
            Visit(entity.Observe(rootReads), Affine3.Identity, [], string.Empty, null, default, 0);
            rootTransaction.Add(rootId, new(output.Skip(start).ToImmutableArray(), diagnostics.Skip(issueStart).ToImmutableArray(), rootReads.Freeze(), visits - startVisits));
        }
        var scene = new CadScene(snapshot.DocumentId, snapshot.Revision, options.Layout, output.ToImmutable(), diagnostics.ToImmutable());
        if (ProjectionVersion != projectionVersion)
            throw new InvalidOperationException("Projection resources changed during the build. Retry with the current resource revision.");
        rootTransaction.Commit();
        return scene;

        void Visit(ShadowReader entity, Affine3 matrix, ImmutableArray<ObjectId> insertPath, string prefix, CadStyle? inherited, ShadowReader inheritedLayer, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visits > options.MaximumOccurrences) throw new InvalidOperationException("Scene occurrence budget exceeded.");
            if (depth > options.MaximumDepth) { diagnostics.Add(new("scene.depth", "Block nesting limit reached.", entity.Id)); return; }
            if (entity.Id is not { } id) return;
            var layer = entity["Layer"];
            if (layer["Name"].Text == "0" && inheritedLayer.Exists) layer = inheritedLayer;
            var globalScale = snapshot.Document.Observe(rootReads!)["DrawingVariables"]["LtScale"].Number(1) * options.LinetypeScale;
            if (!(globalScale > 0) || !double.IsFinite(globalScale))
                throw new ArgumentException("Effective linetype scale exceeds the numeric range.", nameof(options));
            var style = ResolveStyle(entity, layer, inherited, globalScale);
            if (style.LinetypeAlignment != options.LinetypeAlignment) style = style with { LinetypeAlignment = options.LinetypeAlignment };
            if (!options.IncludeInvisible && !style.Visible) return;
            var key = prefix + id.Value;
            if (entity.Type == "Insert")
            {
                var block = entity["Block"];
                if (block.Id is not { } blockId) { diagnostics.Add(new("insert.block", "Insert has no block.", id)); return; }
                if (!activeBlocks.Add(blockId)) { diagnostics.Add(new("insert.cycle", "Cyclic block reference was not expanded.", id)); return; }
                try
                {
                    foreach (var cell in InsertCells(entity, block, matrix, key, options.MaximumOccurrences))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (var child in block["Entities"].Items)
                            Visit(child, cell.Transform, insertPath.Add(id), cell.Key, style, layer, depth + 1);
                        foreach (var attribute in entity["Attributes"].Items)
                            Visit(attribute, cell.AttributeTransform, insertPath.Add(id), cell.Key + "attribute/", style, layer, depth + 1);
                    }
                }
                finally { activeBlocks.Remove(blockId); }
                return;
            }
            foreach (var projector in projectors)
            {
                try
                {
                    if (!cache.TryProject(projector, entity, out var geometry, out var diagnostic, rootReads)) continue;
                    if (diagnostic is not null) diagnostics.Add(new("geometry.approximation", diagnostic, id));
                    if (geometry.Components.IsDefaultOrEmpty)
                        output.Add(cache.Occurrence(snapshot.DocumentId, key, id, insertPath, style, geometry, matrix));
                    else
                    {
                        var components = new List<CadOccurrence>();
                        Emit(geometry, key, style, 0, components);
                        output.AddRange(components);
                    }
                    return;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    diagnostics.Add(new("geometry.invalid", error.Message, id, DiagnosticSeverity.Error));
                    return;
                }
            }
            if (entity.Type.EndsWith("Dimension", StringComparison.Ordinal) && entity["Block"].Id is { } dimensionBlock)
            {
                if (!activeBlocks.Add(dimensionBlock)) { diagnostics.Add(new("dimension.cycle", "Cyclic dimension block.", id)); return; }
                try { foreach (var child in entity["Block"]["Entities"].Items) Visit(child, matrix, insertPath, key + "/", style, layer, depth + 1); }
                finally { activeBlocks.Remove(dimensionBlock); }
                return;
            }
            diagnostics.Add(new("geometry.unsupported", $"No semantic projector for {entity.Type}; all public source data remains in the snapshot.", id));

            void Emit(CadGeometry geometry, string componentKey, CadStyle componentStyle, int componentDepth, List<CadOccurrence> components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (componentDepth > options.MaximumDepth) throw new InvalidOperationException("Component nesting budget exceeded.");
                if (geometry.Components.IsDefaultOrEmpty)
                {
                    if (componentStyle.LinetypeAlignment != options.LinetypeAlignment)
                        componentStyle = componentStyle with { LinetypeAlignment = options.LinetypeAlignment };
                    components.Add(cache.Occurrence(snapshot.DocumentId, componentKey, id, insertPath, componentStyle, geometry, matrix));
                    return;
                }
                if (!geometry.Contours.IsDefaultOrEmpty || geometry.Text is not null || geometry.Image is not null || !geometry.Regions.IsDefaultOrEmpty || !geometry.Strokes.IsDefaultOrEmpty || !geometry.DisplayContours.IsDefaultOrEmpty)
                    throw new ArgumentException("Composite containers cannot mix component and primitive geometry.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var component in geometry.Components)
                {
                    if (++visits > options.MaximumOccurrences) throw new InvalidOperationException("Scene occurrence budget exceeded.");
                    if (string.IsNullOrEmpty(component.Key) || !seen.Add(component.Key)) throw new ArgumentException("Component keys must be nonempty and unique.");
                    Emit(component.Geometry, componentKey + "/part[" + component.Key.Length + "]:" + component.Key,
                        component.Style?.Resolve(componentStyle, ResolveLayerStyle(layer, globalScale), options.LinetypeScale) ?? componentStyle, componentDepth + 1, components);
                }
            }
        }
    }

}
