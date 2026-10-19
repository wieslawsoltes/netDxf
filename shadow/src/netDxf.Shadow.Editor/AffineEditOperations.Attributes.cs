using System.Collections.Immutable;
using System.Globalization;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Editor;

public static partial class AffineEditOperations
{
    private sealed partial class Planner
    {
        private void EmbeddedAttributeText(ShadowReader entity, Affine3 transform)
        {
            var source = CadTextProjection.Read(entity); var text = source.Text;
            var x = transform.Direction(text.XAxis); var y = transform.Direction(text.YAxis);
            var sx = x.Length; var sy = y.Length;
            if (!(sx > 0 && sy > 0) || !double.IsFinite(sx + sy) ||
                Math.Abs(sx - sy) > options.RelativeTolerance * Math.Max(sx, sy) ||
                Math.Abs(Vec3.Dot(x / sx, y / sy)) > options.RelativeTolerance)
                throw new NotSupportedException("Embedded multiline text requires a uniform, orthogonal frame.");
            if (text.Content.Contains("\\H", StringComparison.Ordinal) && Math.Abs(sx - 1) > options.RelativeTolerance)
                throw new NotSupportedException("Embedded text with inline heights needs an explicit rich-text scale operation.");
            var position = transform.Point(text.Origin); var normal = Vec3.Cross(x / sx, y / sy).Normalized;
            if (!position.IsFinite) throw new ArithmeticException("Embedded attribute position is not representable.");
            var basis = Affine3.ObjectCoordinates(normal);
            var rotation = Math.Atan2(Vec3.Dot(x, basis.Y), Vec3.Dot(x, basis.X)) * 180 / Math.PI;
            var packet = (ShadowValue.Structure)entity["TextData"].Value;
            var content = (ShadowValue.Structure)packet.Fields["Content"];
            var fields = ((ShadowValue.Sequence)content.Fields["Fields"]).Items.ToList();
            void SetTag(int code, double value)
            {
                if (!double.IsFinite(value)) throw new ArithmeticException("Embedded attribute layout is not representable.");
                if (++coordinates > options.MaximumCoordinates) throw new InvalidOperationException("Affine coordinate budget exceeded.");
                var index = fields.FindIndex(value => value is ShadowValue.Structure field && field.Fields["Code"] is ShadowValue.Atom c && c.Text == code.ToString(CultureInfo.InvariantCulture));
                var tag = new ShadowValue.Structure("netDxf.IO.DxfTag", ImmutableDictionary<string, ShadowValue>.Empty
                    .Add("Code", new ShadowValue.Atom("System.Int16", code.ToString(CultureInfo.InvariantCulture)))
                    .Add("Value", ShadowValue.From(value)));
                if (index < 0) fields.Add(tag); else fields[index] = tag;
            }
            void VectorTag(int code, Vec3 value) { SetTag(code, value.X); SetTag(code + 10, value.Y); SetTag(code + 20, value.Z); }
            VectorTag(10, position); VectorTag(11, x / sx); VectorTag(210, normal);
            SetTag(50, rotation); SetTag(40, text.Height * sx); SetTag(41, text.Width * sx);
            foreach (var field in entity["TextData"]["Content"]["Fields"].Items)
                if (field["Code"].Number() is 42 or 43 or 46) SetTag((int)field["Code"].Number(), field["Value"].Number() * sx);
            content = content with { Fields = content.Fields.SetItem("Fields", new ShadowValue.Sequence(fields.ToImmutableArray())) };
            packet = packet with { Fields = packet.Fields.SetItem("Content", content) };
            if (entity["TextData"]["SecondaryAlignment"].Value is ShadowValue.Structure)
                packet = packet with { Fields = packet.Fields.SetItem("SecondaryAlignment", Coordinate(entity["TextData"]["SecondaryAlignment"], transform.Point(entity["TextData"]["SecondaryAlignment"].Vector()))) };
            Set(entity, "TextData", packet);
        }
    }
}
