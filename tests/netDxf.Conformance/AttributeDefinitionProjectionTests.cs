using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using Att = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeDefinitionProjectionTests()
    {
        Run("attribute-projection/detached-values-layout-and-metadata", () =>
        {
            var definition = new AttributeDefinition("PROJECT") { Value = "fallback", Position = new(2, 3, 0), Height = 4,
                Alignment = TextAlignment.TopRight, Flags = AttributeFlags.Verify };
            definition.SetMText(new AttributeMText { Value = "embedded\\Ptext", Position = new(5, 6, 0), Height = 3, RectangleWidth = 40 });
            definition.IsPositionLocked = true; definition.ProxyGraphics = new byte[] { 1, 2 };
            var projected = Att.CreateTransformed(definition, Matrix3.Identity, new(10, 20, 0), false);
            Check(projected.Owner == null && projected.Handle == null && projected.Definition == definition, "Projection adopted source ownership");
            Check(projected.Value == "fallback" && projected.GetMText().Value == "embedded\\Ptext" && projected.IsPositionLocked == true
                && projected.Flags == AttributeFlags.Verify && projected.Alignment == TextAlignment.TopRight, "Definition defaults changed during projection");
            R12Vector(new(12, 23, 0), projected.Position); R12Vector(new(15, 26, 0), projected.GetMText().Position);
            Check(projected.ProxyGraphics == null && definition.ProxyGraphics!.SequenceEqual(new byte[] { 1, 2 }), "Projection reused or cleared source proxy");
            var packet = projected.GetTextData(); packet.Content.Value = "changed"; projected.SetTextData(packet);
            Check(definition.GetMText().Value == "embedded\\Ptext", "Returned text shares definition state");
        });
        foreach (bool mirrorText in new[] { false, true }) Run("attribute-projection/explicit-mirror-policy/" + mirrorText, () =>
        {
            var doc = new DxfDocument(); doc.DrawingVariables.MirrText = mirrorText;
            var block = new Block("FRAME"); var definition = new AttributeDefinition("A") { Position = new(3, 4, 0), Alignment = TextAlignment.TopLeft };
            block.AttributeDefinitions.Add(definition); var root = new Insert(block) { Scale = new(-2, 3, 1), Rotation = 30, Normal = new(1, 2, 3) };
            doc.Entities.Add(root); root.TransformAttributes(); var matrix = root.GetTransformation(); var global = Text.DefaultMirrText;
            var projected = Att.CreateTransformed(definition, matrix, root.Position - matrix * block.Origin, mirrorText);
            var expected = root.Attributes[0]; R12Vector(expected.Position, projected.Position); R12Vector(expected.Normal, projected.Normal);
            Check(expected.Alignment == projected.Alignment && expected.IsBackward == projected.IsBackward && Math.Abs(expected.Height - projected.Height) < 1e-12,
                "Explicit policy differs from owning drawing policy");
            Check(Text.DefaultMirrText == global && definition.Owner == block && root.Attributes[0] == expected, "Projection changed source or global state");
        });
        Run("attribute-projection/invalid-frame-and-unrepresentable-content-do-not-mutate-definition", () =>
        {
            var definition = new AttributeDefinition("SAFE") { Value = "original" };
            Throws<ArgumentNullException>(() => Att.CreateTransformed(null!, Matrix3.Identity, Vector3.Zero, false));
            Throws<ArgumentException>(() => Att.CreateTransformed(definition, Matrix3.Scale(new Vector3(double.NaN, 1, 1)), Vector3.Zero, false));
            Throws<ArgumentException>(() => Att.CreateTransformed(definition, Matrix3.Identity, new(double.PositiveInfinity, 0, 0), false));
            definition.SetMText(new AttributeMText { Value = "original embedded" });
            Throws<NotSupportedException>(() => Att.CreateTransformed(definition, Matrix3.Scale(new Vector3(2, 3, 4)), Vector3.Zero, true));
            Check(definition.Value == "original" && definition.GetMText().Value == "original embedded" && definition.Owner == null, "Failed projection changed definition");
        });
    }
}
