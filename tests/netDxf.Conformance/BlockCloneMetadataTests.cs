using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterBlockCloneMetadataTests()
    {
        foreach (bool renamed in new[] { false, true })
        foreach (bool binary in new[] { false, true }) Run("block-clone/record-settings-and-transport/" + renamed + "/" + binary, () =>
        {
            var source = new Block("SOURCE") { Origin = new(1, 2, 0) };
            source.Record.Units = DrawingUnits.Inches; source.Record.AllowExploding = false; source.Record.ScaleUniformly = true;
            source.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
            var clone = renamed ? (Block)source.Clone("RENAMED") : (Block)source.Clone();
            Check(clone.Record.Units == DrawingUnits.Inches && !clone.Record.AllowExploding && clone.Record.ScaleUniformly, "Block clone lost native insertion settings");
            Check(clone.Record.Name == clone.Name && clone.Record.Owner == null && clone.Record.Layout == null && clone.Record.Handle == null, "Block clone copied document registration or mismatched its name");
            var doc = new DxfDocument(); doc.DrawingVariables.InsUnits = DrawingUnits.Millimeters; doc.Entities.Add(new Insert(clone));
            using var data = new MemoryStream(); Check(doc.Save(data, binary), "Save failed"); data.Position = 0;
            var restored = DxfDocument.Load(data)!.Blocks[clone.Name];
            Check(restored.Record.Units == DrawingUnits.Inches && !restored.Record.AllowExploding && restored.Record.ScaleUniformly, "DXF transport lost cloned block settings");
            clone.Record.Units = DrawingUnits.Meters; clone.Record.AllowExploding = true; clone.Record.ScaleUniformly = false;
            Check(source.Record.Units == DrawingUnits.Inches && !source.Record.AllowExploding && source.Record.ScaleUniformly, "Cloned settings share mutable source state");
        });
        Run("block-clone/insert-clone-retains-unit-transform", () =>
        {
            var block = new Block("UNITS"); block.Record.Units = DrawingUnits.Inches;
            block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Position = new(2, 3, 0) });
            var original = new Insert(block) { Position = new(10, 20, 0), Scale = new(2, 3, 1), Rotation = 25 };
            var cloned = (Insert)original.Clone();
            Check(original.GetTransformation(DrawingUnits.Millimeters) == cloned.GetTransformation(DrawingUnits.Millimeters), "INSERT clone changed insertion unit conversion");
        });
        foreach (bool overlay in new[] { false, true })
        foreach (bool binary in new[] { false, true }) Run("block-clone/xref-path-and-mode/" + overlay + "/" + binary, () =>
        {
            const string path = "../references/equipment.dwg"; var source = new Block("REFERENCE", path, overlay);
            source.Record.Units = DrawingUnits.Inches; var clone = (Block)source.Clone("COPY_REFERENCE");
            Check(clone.IsXRef && clone.XrefFile == path && clone.Flags == source.Flags && clone.Record.Units == DrawingUnits.Inches, "Clone lost external reference metadata");
            var doc = new DxfDocument(); doc.Entities.Add(new Insert(clone)); using var data = new MemoryStream(); Check(doc.Save(data, binary), "Save failed"); data.Position = 0;
            var restored = DxfDocument.Load(data)!.Blocks[clone.Name];
            Check(restored.IsXRef && restored.XrefFile == path && restored.Flags == clone.Flags, "DXF transport lost cloned external reference path or mode");
        });
    }
}
