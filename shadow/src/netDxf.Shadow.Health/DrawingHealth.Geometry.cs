using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Health;

public static partial class DrawingHealth
{
    private sealed partial class Analyzer
    {
        private void CheckGeometry()
        {
            foreach (var id in snapshot.EntityIds.OrderBy(i => i.Value, StringComparer.Ordinal))
            {
                Tick(); var e = snapshot.Read(id); if (!e.Exists) continue;
                void Invalid(string property, string message) => Add("geometry.invalid", DiagnosticSeverity.Error, id, property, message);
                if (e["Normal"].Exists && e["Normal"].Vector().Length == 0) Invalid("Normal", "The extrusion normal has zero length.");
                if (e.Type is "Circle" or "Arc" && e["Radius"].Number() <= 0) Invalid("Radius", "Radius must be positive.");
                if (e.Type == "Ellipse" && (e["MajorAxis"].Number() <= 0 || e["MinorAxis"].Number() <= 0 || e["MinorAxis"].Number() > e["MajorAxis"].Number())) Invalid("MajorAxis", "Ellipse axes must be positive and the major axis at least as long as the minor axis.");
                if (e.Type == "Insert")
                {
                    var s = e["Scale"].Vector(new(1, 1, 1));
                    if (s.X == 0 || s.Y == 0 || s.Z == 0) Invalid("Scale", "INSERT scale components must be nonzero.");
                    if (!e["Block"].Exists) Invalid("Block", "INSERT has no block definition.");
                }
                if (e.Type is "Text" or "MText" or "Attribute" or "AttributeDefinition" && e["Height"].Number() <= 0) Invalid("Height", "Text height must be positive.");
                if (e.Type is "Polyline2D" or "Polyline3D" && e["Vertexes"].Items.Take(2).Count() < 2) Invalid("Vertexes", "Polyline has fewer than two vertices.");
                var owner = owners.GetValueOrDefault(id, snapshot.Root); string? key = null;
                var normal = e["Normal"].Vector(Vec3.UnitZ);
                switch (e.Type)
                {
                    case "Line":
                        var a = e["StartPoint"].Vector(); var b = e["EndPoint"].Vector();
                        if (Finite(a) && Finite(b))
                        {
                            if (Vec3.Distance(a, b) <= options.TinyLength) Add("geometry.tiny", DiagnosticSeverity.Warning, id, "", "LINE length is at or below the tiny-length threshold.", position: a);
                            Endpoint(a); Endpoint(b); var av = V(a); var bv = V(b);
                            key = string.CompareOrdinal(av, bv) <= 0 ? av + ";" + bv : bv + ";" + av;
                        }
                        break;
                    case "Point": key = V(e["Position"].Vector()); break;
                    case "Circle": key = V(e["Center"].Vector()) + ";" + N(e["Radius"].Number()) + ";" + V(normal); break;
                    case "Arc":
                        key = V(e["Center"].Vector()) + ";" + N(e["Radius"].Number()) + ";" + V(normal) + ";" + N(e["StartAngle"].Number()) + ";" + N(e["EndAngle"].Number());
                        if (Finite(normal) && normal.Length > 0)
                        {
                            var basis = Affine3.ObjectCoordinates(normal); var center = e["Center"].Vector(); var radius = e["Radius"].Number();
                            foreach (var angle in new[] { e["StartAngle"].Number(), e["EndAngle"].Number() })
                            { var r = angle * Math.PI / 180; Endpoint(center + basis.X * (radius * Math.Cos(r)) + basis.Y * (radius * Math.Sin(r))); }
                        }
                        break;
                    case "Ellipse": key = string.Join(";", new[] { "Center", "Normal", "MajorAxis", "MinorAxis", "Rotation", "StartAngle", "EndAngle" }.Select(p => e[p].Value is ShadowValue.Structure ? V(e[p].Vector()) : N(e[p].Number()))); break;
                    case "Polyline2D": case "Polyline3D":
                        if (e["IsClosed"].Boolean()) break;
                        ShadowReader first = default, last = default;
                        foreach (var vertex in e["Vertexes"].Items) { Tick(); if (!first.Exists) first = vertex; last = vertex; }
                        if (!first.Exists) break;
                        if (e.Type == "Polyline2D" && (!Finite(normal) || normal.Length == 0)) break;
                        var frame = e.Type == "Polyline2D" ? Affine3.ObjectCoordinates(normal) : Affine3.Identity;
                        foreach (var vertex in new[] { first, last })
                        { var point = vertex["Position"].Exists ? vertex["Position"].Vector() : vertex.Vector(); Endpoint(frame.Point(e.Type == "Polyline2D" ? point with { Z = e["Elevation"].Number() } : point)); }
                        break;
                }
                if (options.FindDuplicates && key is not null)
                {
                    key = owner.Value + ":" + e.Type + ":" + key;
                    if (duplicates.TryGetValue(key, out var previous)) Add("geometry.duplicate", DiagnosticSeverity.Warning, id, "", "Exact geometry duplicates another entity in the same owner. Appearance may differ.", [previous]);
                    else duplicates[key] = id;
                }
                void Endpoint(Vec3 point) { if (Finite(point)) endpoints.Add((id, owner, point)); }
            }
        }
        private void CheckGaps()
        {
            var cells = new Dictionary<(ObjectId, long, long, long), List<int>>();
            var emitted = new HashSet<(ObjectId, ObjectId)>();
            for (var i = 0; i < endpoints.Count; i++)
            {
                Tick(); var endpoint = endpoints[i]; var p = endpoint.Point;
                var xx = Math.Floor(p.X / options.GapTolerance); var yy = Math.Floor(p.Y / options.GapTolerance); var zz = Math.Floor(p.Z / options.GapTolerance);
                // Grid arithmetic must not overflow even for adversarial finite coordinates.
                if (Math.Abs(xx) >= 9e18 || Math.Abs(yy) >= 9e18 || Math.Abs(zz) >= 9e18) { complete = false; continue; }
                var x = (long)xx; var y = (long)yy; var z = (long)zz;
                for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
                {
                    Tick(); if (!cells.TryGetValue((endpoint.Owner, x + dx, y + dy, z + dz), out var candidates)) continue;
                    foreach (var j in candidates)
                    {
                        Tick(); var other = endpoints[j]; if (endpoint.Entity == other.Entity) continue;
                        var distance = Vec3.Distance(p, other.Point);
                        if (distance <= options.TinyLength || distance > options.GapTolerance) continue;
                        var pair = string.CompareOrdinal(endpoint.Entity.Value, other.Entity.Value) < 0 ? (endpoint.Entity, other.Entity) : (other.Entity, endpoint.Entity);
                        if (emitted.Add(pair)) Add("geometry.tiny-gap", DiagnosticSeverity.Warning, pair.Item1, "", "Endpoints are separated by " + N(distance) + " drawing units.", [pair.Item2], (p + other.Point) * .5);
                    }
                }
                var cell = (endpoint.Owner, x, y, z); if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = []; list.Add(i);
            }
        }
    }
}
