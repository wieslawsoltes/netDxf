namespace netDxf.Shadow.Editor;

public static partial class AffineEditOperations
{
    private sealed partial class Planner
    {
        private void Conic(ShadowReader entity, Affine3 transform)
        {
            var plane = Plane(entity, transform); RequireSimilarity(plane);
            Point(entity, "Center", transform); Number(entity, "Radius", entity["Radius"].Number() * plane.ScaleX);
            Vector(entity, "Normal", plane.Normal); Thickness(entity, transform, plane.Normal);
            if (entity.Type != "Arc") return;
            foreach (var name in new[] { "StartAngle", "EndAngle" })
            {
                var angle = entity[name].Number() * Math.PI / 180;
                Number(entity, name, Degrees(plane.Local.Direction(new(Math.Cos(angle), Math.Sin(angle)))));
            }
        }
        private void Ellipse(ShadowReader entity, Affine3 transform)
        {
            var plane = Plane(entity, transform);
            var rotation = Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180);
            var u = plane.Local.Direction(rotation.X) * (entity["MajorAxis"].Number() / 2);
            var v = plane.Local.Direction(rotation.Y) * (entity["MinorAxis"].Number() / 2);
            // A scaled 2x2 SVD recovers principal axes without squaring large coordinates.
            var scale = Math.Max(u.Length, v.Length);
            if (!(scale > 0) || !double.IsFinite(scale)) throw new ArithmeticException("Invalid transformed ellipse.");
            var a = u / scale; var b = v / scale;
            var xx = a.X * a.X + b.X * b.X; var yy = a.Y * a.Y + b.Y * b.Y;
            var xy = a.X * a.Y + b.X * b.Y;
            var angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
            var x = new Vec3(Math.Cos(angle), Math.Sin(angle)); var y = new Vec3(-x.Y, x.X);
            var major = Math.Sqrt(Math.Max(0, (xx + yy + Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy)) / 2));
            var minor = Math.Abs(a.X * b.Y - a.Y * b.X) / major;
            if (!(minor > 0)) throw new NotSupportedException("The ellipse collapses under this transform.");
            Point(entity, "Center", transform); Vector(entity, "Normal", plane.Normal);
            Number(entity, "MajorAxis", 2 * scale * major); Number(entity, "MinorAxis", 2 * scale * minor);
            Number(entity, "Rotation", angle * 180 / Math.PI); Thickness(entity, transform, plane.Normal);
            if (entity["IsFullEllipse"].Boolean()) { Number(entity, "StartAngle", 0); Number(entity, "EndAngle", 0); return; }
            foreach (var name in new[] { "StartAngle", "EndAngle" })
            {
                var polar = entity[name].Number() * Math.PI / 180;
                var parameter = Math.Atan2(entity["MajorAxis"].Number() * Math.Sin(polar), entity["MinorAxis"].Number() * Math.Cos(polar));
                var point = u * Math.Cos(parameter) + v * Math.Sin(parameter);
                Number(entity, name, Math.Atan2(Vec3.Dot(point, y), Vec3.Dot(point, x)) * 180 / Math.PI);
            }
        }
        private void Polyline(ShadowReader entity, Affine3 transform)
        {
            var plane = Plane(entity, transform);
            var vertices = entity["Vertexes"].Items.ToArray();
            if (vertices.Any(v => v["Bulge"].Number() != 0 || v["StartWidth"].Number() != 0 || v["EndWidth"].Number() != 0)) RequireSimilarity(plane);
            foreach (var vertex in vertices)
            {
                var point = vertex["Position"].Vector() with { Z = entity["Elevation"].Number() };
                Vector(vertex, "Position", plane.Local.Point(point));
                foreach (var name in new[] { "StartWidth", "EndWidth" })
                    if (vertex[name].Number() != 0) Number(vertex, name, vertex[name].Number() * plane.ScaleX);
            }
            SetPlane(entity, plane); Thickness(entity, transform, plane.Normal);
        }
        private void Image(ShadowReader entity, Affine3 transform)
        {
            var plane = Plane(entity, transform);
            var u = plane.Local.Direction(entity["Uvector"].Vector(Vec3.UnitX));
            var v = plane.Local.Direction(entity["Vvector"].Vector(Vec3.UnitY));
            Point(entity, "Position", transform); Vector(entity, "Normal", plane.Normal);
            Vector(entity, "Uvector", u / u.Length); Vector(entity, "Vvector", v / v.Length);
            Number(entity, "Width", entity["Width"].Number() * u.Length); Number(entity, "Height", entity["Height"].Number() * v.Length);
            // Pixel clipping coordinates remain unchanged; only the image frame moves.
        }
        private void Text(ShadowReader entity, Affine3 transform)
        {
            if (entity["HasMText"].Boolean()) throw new NotSupportedException("Embedded multiline attributes need a coordinated text-frame planner.");
            var plane = Plane(entity, transform);
            var rotation = Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180);
            var u = plane.Local.Direction(rotation.X);
            var v = plane.Local.Direction(rotation.Y + rotation.X * Math.Tan(entity["ObliqueAngle"].Number() * Math.PI / 180));
            var x = u / u.Length; var perpendicular = new Vec3(-x.Y, x.X);
            var heightScale = Vec3.Dot(v, perpendicular);
            if (!(heightScale > 0)) throw new NotSupportedException("Degenerate text frame.");
            Point(entity, "Position", transform); Vector(entity, "Normal", plane.Normal);
            Number(entity, "Rotation", Degrees(x)); Number(entity, "Height", entity["Height"].Number() * heightScale);
            Number(entity, "WidthFactor", entity["WidthFactor"].Number(1) * u.Length / heightScale);
            Number(entity, "ObliqueAngle", Math.Atan2(Vec3.Dot(v, x), heightScale) * 180 / Math.PI);
            Thickness(entity, transform, plane.Normal);
        }
        private void MultilineText(ShadowReader entity, Affine3 transform)
        {
            var plane = Plane(entity, transform); RequireSimilarity(plane);
            Point(entity, "Position", transform); Vector(entity, "Normal", plane.Normal);
            var direction = Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180).X;
            Number(entity, "Rotation", Degrees(plane.Local.Direction(direction)));
            Number(entity, "Height", entity["Height"].Number() * plane.ScaleX);
            if (entity["RectangleWidth"].Exists) Number(entity, "RectangleWidth", entity["RectangleWidth"].Number() * plane.ScaleX);
            // Relative rich-text controls scale with the base height; absolute controls do not.
            if (entity["Value"].Text.Contains("\\H", StringComparison.Ordinal) && Math.Abs(plane.ScaleX - 1) > options.RelativeTolerance)
                throw new NotSupportedException("MTEXT with inline absolute heights needs an explicit rich-text scale operation.");
        }
        private void Insert(ShadowReader entity, Affine3 transform)
        {
            if (entity["RowCount"].Number(1) > 1 || entity["ColumnCount"].Number(1) > 1)
                throw new NotSupportedException("Array inserts need coordinated row/column spacing edits.");
            var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ)) * Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180);
            var scale = entity["Scale"].Vector(new(1, 1, 1));
            var u = transform.Direction(basis.X * scale.X); var v = transform.Direction(basis.Y * scale.Y); var z = transform.Direction(basis.Z * scale.Z);
            var sx = u.Length; var sy = v.Length;
            if (!(sx > 0 && sy > 0) || !double.IsFinite(sx + sy) || Math.Abs(Vec3.Dot(u / sx, v / sy)) > options.RelativeTolerance)
                throw new NotSupportedException("An INSERT cannot store a sheared frame.");
            var normal = Vec3.Cross(u / sx, v / sy).Normalized; var sz = Vec3.Dot(z, normal);
            if (sz == 0 || !Near(z, normal * sz)) throw new NotSupportedException("An INSERT cannot store an oblique Z axis.");
            var target = Affine3.ObjectCoordinates(normal);
            Point(entity, "Position", transform); Vector(entity, "Normal", normal); Vector(entity, "Scale", new(sx, sy, sz));
            Number(entity, "Rotation", Math.Atan2(Vec3.Dot(u, target.Y), Vec3.Dot(u, target.X)) * 180 / Math.PI);
            foreach (var attribute in entity["Attributes"].Items) Visit(attribute, transform);
        }
    }
}
