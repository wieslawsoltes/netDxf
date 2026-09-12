using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMinimalDocumentTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version;
                bool b = binary;
                Run($"minimal/header-only/{v}/{b}", () => CheckMinimalDocument(v, b, false, false));
                Run($"minimal/empty-sections/{v}/{b}", () => CheckMinimalDocument(v, b, false, true));
                Run($"minimal/implicit-tables/{v}/{b}", () => CheckMinimalDocument(v, b, true, false));
                Run($"minimal/implicit-tables-empty-objects/{v}/{b}", () => CheckMinimalDocument(v, b, true, true));
                Run($"minimal/omitted-objects/{v}/{b}", () => CheckOmittedObjects(v, b, false));
                Run($"minimal/empty-objects/{v}/{b}", () => CheckOmittedObjects(v, b, true));
                Run($"minimal/blocks-without-tables/{v}/{b}", () => CheckMinimalBlock(v, b));
            }
        }
    }

    private static void CheckMinimalDocument(DxfVersion version, bool binary, bool entity, bool emptySections)
    {
        using var input = CreateMinimalDocument(version, binary, entity, emptySections);
        DxfDocument loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Minimal DXF failed to load.");
        Check(input.CanRead, "Load closed its caller-owned stream.");
        CheckInitializedDocument(loaded);
        Equal(version, loaded.DrawingVariables.AcadVer, "minimal version");
        Equal(entity ? 1 : 0, loaded.Entities.Lines.Count(), "minimal LINE count");
        if (entity)
        {
            Line line = loaded.Entities.Lines.Single();
            Equal(new Vector3(1, 2, 3), line.StartPoint, "authored start");
            Equal(new Vector3(4, 5, 6), line.EndPoint, "authored end");
            Equal("200", line.Handle, "authored entity handle");
            Equal("ImplicitLayer", line.Layer.Name, "implicit layer");
            Equal((short)7, line.Layer.Color.Index, "implicit layer color");
            Equal(Linetype.DefaultName, line.Layer.Linetype.Name, "implicit linetype");
            Equal("authored payload", (string)line.XData["MINIMAL_DXF"].XDataRecord[0].Value, "implicit application registry");
            Check(ReferenceEquals(loaded.GetObjectByHandle("200"), line), "Entity handle did not resolve.");
            Check(ReferenceEquals(line.Owner, loaded.Layouts[Layout.ModelSpaceName].AssociatedBlock), "Entity was not assigned to model space.");
        }
        using var output = new MemoryStream();
        Check(loaded.Save(output, binary), "Recovered minimal document failed to save.");
        output.Position = 0;
        DxfDocument reloaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Recovered minimal document failed to reload.");
        CheckInitializedDocument(reloaded);
        Equal(entity ? 1 : 0, reloaded.Entities.Lines.Count(), "minimal second round trip");
    }

    private static void CheckInitializedDocument(DxfDocument document)
    {
        Check(document.ApplicationRegistries != null && document.Blocks != null && document.DimensionStyles != null &&
              document.Layers != null && document.Linetypes != null && document.TextStyles != null &&
              document.ShapeStyles != null && document.UCSs != null && document.Views != null && document.VPorts != null,
              "A symbol-table collection was not initialized.");
        Check(document.Groups != null && document.Layouts != null && document.MlineStyles != null &&
              document.ImageDefinitions != null && document.UnderlayDgnDefinitions != null &&
              document.UnderlayDwfDefinitions != null && document.UnderlayPdfDefinitions != null && document.RasterVariables != null,
              "An object collection was not initialized.");
        Layout model = document.Layouts![Layout.ModelSpaceName];
        Check(model != null && model.AssociatedBlock != null, "Default model-space graph is missing.");
        Check(ReferenceEquals(model!.AssociatedBlock!.Record.Layout, model), "Model-space ownership is inconsistent.");
        Check(document.Layers!.Contains(Layer.Default.Name), "Default layer is missing.");
        Check(document.Linetypes!.Contains(Linetype.Continuous.Name), "Continuous linetype is missing.");
        Equal(Layout.ModelSpaceName, document.Entities.ActiveLayout, "active layout");
    }

    private static MemoryStream CreateMinimalDocument(DxfVersion version, bool binary, bool entity, bool emptySections, bool block = false)
    {
        var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, binary);
        void Tag(short code, object value) => Invoke(writer, "Write", code, value);
        string acadver = version switch
        {
            DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018",
            DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024",
            DxfVersion.AutoCad2013 => "AC1027", DxfVersion.AutoCad2018 => "AC1032",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        Tag(0, "SECTION"); Tag(2, "HEADER"); Tag(9, "$ACADVER"); Tag(1, acadver);
        Tag(9, "$DWGCODEPAGE"); Tag(3, "ANSI_1252"); Tag(9, "$HANDSEED"); Tag(5, "1000"); Tag(0, "ENDSEC");
        if (emptySections)
        {
            Tag(0, "SECTION"); Tag(2, "TABLES"); Tag(0, "ENDSEC");
            Tag(0, "SECTION"); Tag(2, "BLOCKS"); Tag(0, "ENDSEC");
        }
        if (block)
        {
            Tag(0, "SECTION"); Tag(2, "BLOCKS");
            Tag(0, "BLOCK"); Tag(5, "203"); Tag(100, "AcDbEntity"); Tag(8, "BlockLayer");
            Tag(100, "AcDbBlockBegin"); Tag(2, "Component"); Tag(70, (short)0);
            Tag(10, 0.0); Tag(20, 0.0); Tag(30, 0.0); Tag(3, "Component"); Tag(1, "");
            Tag(0, "LINE"); Tag(5, "201"); Tag(100, "AcDbEntity"); Tag(8, "BlockLayer");
            Tag(100, "AcDbLine"); Tag(10, 0.0); Tag(20, 0.0); Tag(30, 0.0);
            Tag(11, 1.0); Tag(21, 2.0); Tag(31, 3.0);
            Tag(0, "ENDBLK"); Tag(5, "204"); Tag(100, "AcDbEntity"); Tag(8, "BlockLayer");
            Tag(100, "AcDbBlockEnd"); Tag(0, "ENDSEC");
        }
        if (entity || emptySections)
        {
            Tag(0, "SECTION"); Tag(2, "ENTITIES");
            if (block)
            {
                Tag(0, "INSERT"); Tag(5, "200"); Tag(100, "AcDbEntity"); Tag(8, "ImplicitLayer");
                Tag(100, "AcDbBlockReference"); Tag(2, "Component"); Tag(10, -1.0); Tag(20, 2.0); Tag(30, 3.0);
            }
            else if (entity)
            {
                Tag(0, "LINE"); Tag(5, "200"); Tag(100, "AcDbEntity"); Tag(8, "ImplicitLayer");
                Tag(100, "AcDbLine"); Tag(10, 1.0); Tag(20, 2.0); Tag(30, 3.0);
                Tag(11, 4.0); Tag(21, 5.0); Tag(31, 6.0);
                Tag(1001, "MINIMAL_DXF"); Tag(1000, "authored payload");
            }
            Tag(0, "ENDSEC");
        }
        if (emptySections) { Tag(0, "SECTION"); Tag(2, "OBJECTS"); Tag(0, "ENDSEC"); }
        Tag(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0;
        return stream;
    }

    private static void CheckMinimalBlock(DxfVersion version, bool binary)
    {
        using var input = CreateMinimalDocument(version, binary, true, false, true);
        DxfDocument loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Block-only tables fixture failed.");
        CheckInitializedDocument(loaded);
        Insert insert = loaded.Entities.Inserts.Single();
        Equal(new Vector3(-1, 2, 3), insert.Position, "minimal insert position");
        Equal("Component", insert.Block.Name, "minimal block name");
        Line child = insert.Block.Entities.OfType<Line>().Single();
        Equal(new Vector3(1, 2, 3), child.EndPoint, "minimal block geometry");
        Check(ReferenceEquals(child.Owner, insert.Block), "Nested entity has no block owner.");
        Check(ReferenceEquals(loaded.GetObjectByHandle("201"), child), "Nested entity handle changed.");
        using var output = new MemoryStream();
        Check(loaded.Save(output, binary), "Block fixture could not save.");
        output.Position = 0;
        DxfDocument roundTrip = DxfDocument.Load(output) ?? throw new InvalidOperationException("Block fixture could not reload.");
        Equal("Component", roundTrip.Entities.Inserts.Single().Block.Name, "reloaded block reference");
    }

    private static void CheckOmittedObjects(DxfVersion version, bool binary, bool emptySection)
    {
        var original = new DxfDocument(version);
        original.Entities.Add(new Line(Vector3.Zero, new Vector3(1, 2, 3)));
        original.Views.Add(new View("KeepView") { Width = 32, Height = 24 });
        original.UCSs.Add(new UCS("KeepUcs") { Elevation = 4.5 });
        original.Layouts.Add(new Layout("Review"));
        original.Entities.ActiveLayout = "Review";
        original.Entities.Add(new Circle(new Vector3(7, 8, 9), 2.5));
        original.Entities.ActiveLayout = Layout.ModelSpaceName;
        string lineHandle = original.Entities.Lines.Single().Handle;
        string viewHandle = original.Views["KeepView"].Handle;
        using var generated = new MemoryStream();
        Check(original.Save(generated, binary), "Source fixture save failed.");
        generated.Position = 0;
        object reader = NewCodeReader(generated, binary);
        var tags = new List<(short Code, object Value)>();
        do
        {
            Invoke(reader, "Next");
            tags.Add((TagCode(reader), reader.GetType().GetProperty("Value")!.GetValue(reader)!));
        } while (!(tags[^1].Code == 0 && Equals(tags[^1].Value, "EOF")));
        int start = tags.FindIndex(tag => tag.Code == 2 && Equals(tag.Value, "OBJECTS")) - 1;
        Check(start >= 0, "Fixture has no OBJECTS section.");
        int end = tags.FindIndex(start + 2, tag => tag.Code == 0 && Equals(tag.Value, "ENDSEC"));
        Check(end > start, "Fixture has no OBJECTS terminator.");
        if (emptySection) tags.RemoveRange(start + 2, end - start - 2);
        else tags.RemoveRange(start, end - start + 1);
        using var modified = new MemoryStream();
        object writer = NewCodeWriter(modified, binary);
        foreach (var tag in tags) Invoke(writer, "Write", tag.Code, tag.Value);
        Invoke(writer, "Flush"); modified.Position = 0;
        DxfDocument loaded = DxfDocument.Load(modified) ?? throw new InvalidOperationException("Omitted OBJECTS failed to load.");
        CheckInitializedDocument(loaded);
        Equal(2, loaded.Layouts.Count, "reconstructed layout count");
        Check(loaded.Entities.Lines.Single().Handle == lineHandle, "Existing LINE handle changed.");
        Check(loaded.Views["KeepView"].Handle == viewHandle, "Existing VIEW handle changed.");
        Near(4.5, loaded.UCSs["KeepUcs"].Elevation, "existing UCS metadata");
        Layout paper = loaded.Layouts.Single(layout => layout.IsPaperSpace);
        Circle circle = paper.AssociatedBlock.Entities.OfType<Circle>().Single();
        Near(2.5, circle.Radius, "recovered paper-space geometry");
        Check(ReferenceEquals(circle.Owner, paper.AssociatedBlock), "Paper-space owner changed.");
        Check(ReferenceEquals(loaded.GetObjectByHandle(circle.Handle), circle), "Paper-space handle no longer resolves.");
        using var output = new MemoryStream();
        Check(loaded.Save(output, binary), "Recovered drawing could not save.");
        output.Position = 0;
        Check(DxfDocument.Load(output) != null, "Recovered drawing could not reload.");
    }
}
