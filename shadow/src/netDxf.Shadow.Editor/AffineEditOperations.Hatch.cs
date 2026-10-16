using System.Collections.Immutable;

namespace netDxf.Shadow.Editor;

public static partial class AffineEditOperations
{
    private sealed partial class Planner
    {
        private bool pruneHatchResources;
        private int hatchObjects;
        private ImmutableDictionary<ObjectId, ImmutableHashSet<ObjectId>>? hatchDependents;

        // Translation preserves every analytic boundary representation. Other affine
        // maps need coordinated conic/pattern reparameterization, not an XY shortcut.
        private void Hatch(ShadowReader entity, Affine3 transform)
        {
            if (transform.X != Vec3.UnitX || transform.Y != Vec3.UnitY || transform.Z != Vec3.UnitZ)
                throw new NotSupportedException("HATCH currently admits exact translations, not rotation, scale or shear.");
            if (entity["Associative"].Boolean())
                throw new NotSupportedException("Translate associative hatches through a coordinated boundary-entity planner.");
            var pattern = entity["Pattern"];
            if (pattern.Type != "HatchPattern")
                throw new NotSupportedException("This HATCH translation planner admits solid and line-family patterns, not gradients.");
            var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
            var movement = transform.Translation;
            var local = new Vec3(Vec3.Dot(movement, basis.X), Vec3.Dot(movement, basis.Y), Vec3.Dot(movement, basis.Z));
            if (!local.IsFinite) throw new ArithmeticException("Hatch translation is not representable.");

            // Bound all traversal, including empty paths. Native paths/edges are
            // normally owned; reject externally shared mutable boundaries explicitly.
            var owned = new HashSet<ObjectId> { entity.Id!.Value };
            var edges = new List<ShadowReader>();
            void Own(ShadowReader item)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++hatchObjects > options.MaximumCoordinates) throw new InvalidOperationException("Hatch object budget exceeded.");
                if (item.Id is { } id) owned.Add(id);
            }
            Own(entity["BoundaryPaths"]);
            foreach (var path in entity["BoundaryPaths"].Items)
            {
                Own(path); Own(path["Edges"]);
                if (path["Entities"].Items.Any())
                    throw new NotSupportedException("Hatch paths with linked boundary entities need coordinated translation.");
                foreach (var edge in path["Edges"].Items) { Own(edge); edges.Add(edge); }
            }
            hatchDependents ??= snapshot.BuildDependents();
            foreach (var id in owned)
            {
                if (id == entity.Id) continue;
                cancellation.ThrowIfCancellationRequested();
                if (hatchDependents.TryGetValue(id, out var parents) && parents.Any(p => !owned.Contains(p)))
                    throw new NotSupportedException("Externally shared hatch boundary data cannot be moved independently.");
            }
            var seen = new HashSet<ObjectId>();
            foreach (var edge in edges)
            {
                if (edge.Id is not { } id) throw new NotSupportedException("Hatch edges require detached object identities.");
                if (!seen.Add(id)) continue;
                switch (edge.Type)
                {
                    case "HatchBoundaryPath+Line": MoveXY(edge, "Start"); MoveXY(edge, "End"); break;
                    case "HatchBoundaryPath+Arc": case "HatchBoundaryPath+Ellipse": MoveXY(edge, "Center"); break;
                    case "HatchBoundaryPath+Polyline": MoveXYSequence(edge, "Vertexes"); break;
                    case "HatchBoundaryPath+Spline":
                        MoveXYSequence(edge, "ControlPoints");
                        if (edge["FitPoints"].Exists) MoveXYSequence(edge, "FitPoints");
                        // Knot values, weights and tangent vectors are not positions.
                        break;
                    default: throw new NotSupportedException($"Unsupported analytic hatch edge {edge.Type}.");
                }
            }
            if (entity["SeedPoints"].Exists) MoveXYSequence(entity, "SeedPoints");
            Number(entity, "Elevation", entity["Elevation"].Number() + local.Z);

            // Patterns are shareable between hatches. Detach the pattern and line
            // definitions before shifting its OCS origin; never shift each family a
            // second time or mutate an unselected hatch's pattern.
            if (local.X != 0 || local.Y != 0)
            {
                var lines = ImmutableArray.CreateBuilder<ShadowValue>();
                foreach (var line in pattern["LineDefinitions"].Items)
                {
                    Own(line);
                    if (line.Object is not { Type: "HatchPatternLineDefinition" } lineNode)
                        throw new NotSupportedException("Unknown hatch line-family representation.");
                    var dashes = ImmutableArray.CreateBuilder<ShadowValue>();
                    foreach (var dash in line["DashPattern"].Items) { Own(dash); dashes.Add(dash.Value); }
                    var copy = edit.Create(lineNode.TypeName, lineNode.Kind,
                        lineNode.Properties.SetItem("DashPattern", new ShadowValue.Sequence(dashes.ToImmutable())));
                    lines.Add(new ShadowValue.Reference(copy));
                }
                var node = pattern.Object!;
                var properties = node.Properties
                    .SetItem("Origin", ShiftXY(pattern["Origin"]))
                    .SetItem("LineDefinitions", new ShadowValue.Sequence(lines.ToImmutable()));
                var copiedPattern = edit.Create(node.TypeName, node.Kind, properties);
                Set(entity, "Pattern", new ShadowValue.Reference(copiedPattern));
                pruneHatchResources = true;
            }

            ShadowValue ShiftXY(ShadowReader value)
            {
                var before = value.Vector();
                // Z on polyline vertices is a bulge; on spline controls it is a
                // rational weight. Neither is a spatial Z coordinate.
                return Coordinate(value, new(before.X + local.X, before.Y + local.Y, before.Z));
            }
            void MoveXY(ShadowReader item, string name) => Set(item, name, ShiftXY(item[name]));
            void MoveXYSequence(ShadowReader item, string name)
            {
                var values = ImmutableArray.CreateBuilder<ShadowValue>();
                foreach (var value in item[name].Items) values.Add(ShiftXY(value));
                edit.ReplaceSequence(item.Id!.Value, name, values);
            }
        }
    }
}
