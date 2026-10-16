using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterDrawingCompatibilityTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"spline/periodic-explicit/clamped/{v}/{b}", () => ExplicitPeriodicClamped(v, b));
            Run($"olemetadata/extensions/{v}/{b}", () => OleNumericMetadataExtensions(v, b));
            Run($"image/dictionary-name/{v}/{b}", () => ImageDictionaryName(v, b));
            if (version >= DxfVersion.AutoCad2004)
                Run($"containers/sortents/attributes/{v}/{b}", () => SortentsAttributeDefinitions(v, b));
        }
        Run("spline/periodic-explicit/clone-reverse-transform", ExplicitPeriodicMutations);
        Run("containers/sortents/attribute-boundaries", SortentsAttributeBoundaries);
    }

    private static void ImageDictionaryName(DxfVersion version, bool binary)
    {
        const string name = "Raster,revision=2";
        Check(!TableObject.IsValidName(name), "Raster key must exercise dictionary-only punctuation.");
        var definition = new ImageDefinition(name, "relative-image.png", 8, 96, 6, 96, netDxf.Units.ImageResolutionUnits.Inches);
        var document = new DxfDocument(version);
        document.Entities.Add(new Image(definition, Vector3.Zero, 8, 6));
        document.Entities.Add(new Image(definition, new Vector3(10, 0, 0), 8, 6));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream();
            Check(document.Save(output, binary ^ (cycle % 2 != 0)), "Raster dictionary-name save.");
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Raster dictionary-name import.");
            Equal(2, document.Entities.Images.Count(), "Raster entities dropped.");
            Equal(1, document.ImageDefinitions.Count, "Raster definition duplicated.");
            var retained = document.ImageDefinitions[name];
            Check(retained != null && retained.Name == name && retained.File == "relative-image.png", "Raster dictionary key or path changed.");
            foreach (var image in document.Entities.Images) Check(ReferenceEquals(retained, image.Definition), "Raster definition reference lost.");
        }
    }

    private static void OleNumericMetadataExtensions(DxfVersion version, bool binary)
    {
        DxfTag[] extensions =
        {
            new(73, (short)3), new(74, (short)7), new(44, 0.875), new(290, true), new(160, 1234567890123L)
        };
        var tags = OleTags(version, 255, 2, false);
        tags.InsertRange(tags.FindIndex(tag => tag.Code == 90), extensions);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Numeric OLE metadata import failed.");
        var frame = document.Entities.Ole2Frames.Single();
        document.Entities.Add((Ole2Frame)frame.Clone());
        document.Entities.Add(frame.WithMetadataFields(Ole2FrameMetadataFields.None));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            foreach (var current in document.Entities.Ole2Frames)
            {
                Equal(extensions.Length, current.AdditionalMetadata.Count, "OLE extension count.");
                for (int i = 0; i < extensions.Length; i++)
                {
                    Equal(extensions[i].Code, current.AdditionalMetadata[i].Code, "OLE extension group order.");
                    Equal(extensions[i].Value, current.AdditionalMetadata[i].Value, "OLE extension value/type.");
                }
                Check(OlePayload(255).SequenceEqual(current.GetBinaryData()), "Numeric metadata changed inert OLE payload.");
            }
            using var output = new MemoryStream();
            Check(document.Save(output, binary ^ (cycle % 2 != 0)), "OLE metadata extension save.");
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("OLE metadata extension reload.");
        }
        tags.Insert(tags.FindIndex(tag => tag.Code == 90), new DxfTag(73, (short)4));
        using var duplicate = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(duplicate));
#else
        Check(DxfDocument.Load(duplicate) == null, "Duplicate numeric OLE metadata accepted.");
#endif
    }

    private static Spline ExplicitPeriodicSpline()
    {
        Vector3[] controls =
        {
            new(0, 0, 0), new(3, 5, 1), new(8, 5, 2), new(9, 1, 3),
            new(6, -2, 2), new(1, -3, 1), new(0, 0, 0)
        };
        double[] weights = { 1, 2, 1, 3, 1, 2, 1 };
        double[] knots = { 0, 0, 0, 0, 1, 2, 3, 4, 4, 4, 4 };
        return new Spline(controls, weights, knots, 3, true);
    }

    private static void CheckExplicitPeriodicBasis(Spline actual, Spline reference)
    {
        Check(actual.IsClosedPeriodic && actual.HasExplicitPeriodicControlPoints, "Explicit periodic representation lost.");
        Equal(reference.ControlPoints.Length, actual.ControlPoints.Length, "Explicit periodic control count.");
        Check(reference.ControlPoints.SequenceEqual(actual.ControlPoints), "Explicit periodic control coordinates.");
        Check(reference.Weights.SequenceEqual(actual.Weights) && reference.Knots.SequenceEqual(actual.Knots), "Explicit weights or knots changed.");
        for (int i = 0; i <= 16; i++)
        {
            double parameter = i * 0.25;
            Check(reference.EvaluateDerivatives(parameter, 3).SequenceEqual(actual.EvaluateDerivatives(parameter, 3)), "Explicit periodic derivatives changed.");
        }
        var samples = Spline.NurbsEvaluator(reference.ControlPoints, reference.Weights, reference.Knots, reference.Degree, true, false, 33);
        Check(samples.SequenceEqual(actual.PolygonalVertexes(33)), "Explicit periodic samples changed.");
    }

    private static void ExplicitPeriodicClamped(DxfVersion version, bool binary)
    {
        var source = ExplicitPeriodicSpline();
        var reference = new Spline(source.ControlPoints, source.Weights, source.Knots, source.Degree, false);
        var document = new DxfDocument(version);
        document.Entities.Add(source);
        document.Entities.Add(new Line(Vector3.UnitX, Vector3.UnitY));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            CheckExplicitPeriodicBasis(document.Entities.Splines.Single(), reference);
            document.SplineCountPolicy = (DxfSplineCountPolicy)cycle;
            using var output = new MemoryStream();
            Check(document.Save(output, binary ^ (cycle % 2 != 0)), "Explicit periodic save.");
            output.Position = 0;
            var record = DxfRawDocument.Load(output).Sections.SelectMany(section => section.Records).Single(record => record.Name == "SPLINE");
            Equal(7, record.Tags.Count(tag => tag.Code == 10), "Writer added a periodic prefix to explicit controls.");
            Equal(11, record.Tags.Count(tag => tag.Code == 40), "Writer replaced clamped knots.");
            Equal(cycle == 0 ? 0 : 1, record.Tags.Count(tag => tag.Code == 73), "Explicit count-policy presence.");
            if (cycle != 0)
            {
                Equal((short)7, (short)record.Tags.Single(tag => tag.Code == 73).Value, "Explicit declared control count.");
                Equal((short)11, (short)record.Tags.Single(tag => tag.Code == 72).Value, "Explicit declared knot count.");
            }
            Check(((short)record.Tags.Single(tag => tag.Code == 70).Value & 2) != 0, "Writer lost periodic marker.");
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Clamped periodic import failed.");
            Equal(Vector3.UnitX, document.Entities.Lines.Single().StartPoint, "Following entity framing.");
        }
    }

    private static void ExplicitPeriodicMutations()
    {
        var source = ExplicitPeriodicSpline();
        var clone = (Spline)source.Clone();
        Check(!ReferenceEquals(source.ControlPoints, clone.ControlPoints), "Explicit clone shared controls.");
        CheckExplicitPeriodicBasis(clone, new Spline(source.ControlPoints, source.Weights, source.Knots, source.Degree, false));
        double[] knots = clone.Knots;
        clone.IsClosedPeriodic = true;
        Check(ReferenceEquals(knots, clone.Knots), "Unchanged periodic assignment rebuilt explicit knots.");
        clone.Reverse();
        for (int i = 0; i <= 16; i++) Equal(source.PointAt(4 - i * 0.25), clone.PointAt(i * 0.25), "Explicit reversal rotated controls.");
        clone.Reverse();
        Check(source.ControlPoints.SequenceEqual(clone.ControlPoints) && source.Knots.SequenceEqual(clone.Knots), "Double reversal changed explicit basis.");
        var transform = Matrix3.Scale(2, 3, 4);
        var shift = new Vector3(4, -2, 1);
        clone.TransformBy(transform, shift);
        var transformed = new Spline(source.ControlPoints.Select(point => transform * point + shift), source.Weights, source.Knots, source.Degree, false);
        CheckExplicitPeriodicBasis(clone, transformed);
        clone.IsClosedPeriodic = false;
        Check(!clone.HasExplicitPeriodicControlPoints, "Periodic toggle retained explicit marker.");
        Throws<ArgumentException>(() => new Spline(source.ControlPoints, source.Weights, source.Knots.Take(10), source.Degree, true));
    }

    private static DxfDocument SortentsAttributeDocument(DxfVersion version)
    {
        var document = new DxfDocument(version);
        var block = new Block("SortAttributes");
        block.AttributeDefinitions.Add(new AttributeDefinition("FIRST") { Position = Vector3.UnitX, Value = "one" });
        block.AttributeDefinitions.Add(new AttributeDefinition("SECOND") { Position = Vector3.UnitY, Value = "two" });
        var line = new Line(Vector3.Zero, new Vector3(4, 5, 0));
        block.Entities.Add(line);
        document.Entities.Add(new Insert(block));
        document.Objects.CreateSortentsTable(block.Record, new[]
        {
            new DxfSortOrderEntry(block.AttributeDefinitions["SECOND"], "AB"),
            new DxfSortOrderEntry(line, "0"),
            new DxfSortOrderEntry(block.AttributeDefinitions["FIRST"], "AB")
        });
        return document;
    }

    private static void CheckSortentsAttributes(DxfDocument document)
    {
        var block = document.Blocks["SortAttributes"];
        var table = document.Objects.Items.OfType<DxfSortentsTable>().Single();
        Equal(3, table.Entries.Count, "Attribute redraw count.");
        Check(ReferenceEquals(block.Record, table.BlockRecord), "Attribute redraw block identity.");
        Check(ReferenceEquals(block.AttributeDefinitions["SECOND"], table.Entries[0].Target), "First redraw target is not retained ATTDEF.");
        Check(ReferenceEquals(block.Entities.Single(), table.Entries[1].Entity), "Entity accessor compatibility.");
        Check(ReferenceEquals(block.AttributeDefinitions["FIRST"], table.Entries[2].Target), "Last redraw target is not retained ATTDEF.");
        Check(table.Entries[0].Entity == null && table.Entries[2].Entity == null, "Attribute definitions projected as unrelated entities.");
        Check(table.Entries.Select(entry => entry.SortHandle).SequenceEqual(new[] { "AB", "0", "AB" }), "Attribute redraw keys/order changed.");
        Equal(0, document.Objects.Validate().Count, "Attribute redraw graph validation.");
    }

    private static void SortentsAttributeDefinitions(DxfVersion version, bool binary)
    {
        var document = SortentsAttributeDocument(version);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            CheckSortentsAttributes(document);
            using var output = new MemoryStream();
            Check(document.Save(output, binary ^ (cycle % 2 != 0)), "Attribute redraw save.");
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Attribute redraw load.");
        }
        var sourceBlock = document.Blocks["SortAttributes"];
        var target = new DxfDocument(version);
        var targetBlock = (Block)sourceBlock.Clone();
        target.Entities.Add(new Insert(targetBlock));
        var mapping = new Dictionary<DxfObject, DxfObject>();
        foreach (var definition in sourceBlock.AttributeDefinitions.Values) mapping.Add(definition, targetBlock.AttributeDefinitions[definition.Tag]);
        mapping.Add(sourceBlock.Entities.Single(), targetBlock.Entities.Single());
        target.Objects.CloneExtensionDictionary(sourceBlock.Record, targetBlock.Record, mapping);
        CheckSortentsAttributes(target);
    }

    private static void SortentsAttributeBoundaries()
    {
        var document = SortentsAttributeDocument(DxfVersion.AutoCad2018);
        var block = document.Blocks["SortAttributes"];
        var table = document.Objects.Items.OfType<DxfSortentsTable>().Single();
        Throws<ArgumentException>(() => new DxfSortOrderEntry(Layer.Default, "1"));
        Throws<ArgumentException>(() => table.Entries.Add(new DxfSortOrderEntry(block.AttributeDefinitions["FIRST"], "1")));
        var foreign = new Block("ForeignAttributes");
        foreign.AttributeDefinitions.Add(new AttributeDefinition("FOREIGN"));
        document.Blocks.Add(foreign);
        Throws<ArgumentException>(() => table.Entries.Add(new DxfSortOrderEntry(foreign.AttributeDefinitions["FOREIGN"], "1")));
        int count = table.Entries.Count;
        Throws<ArgumentException>(() => table.Entries[1] = new DxfSortOrderEntry(block.AttributeDefinitions["SECOND"], "1"));
        Equal(count, table.Entries.Count, "Failed redraw replacement mutated count.");
        table.Entries.RemoveAt(0);
        table.Entries.Add(new DxfSortOrderEntry(block.AttributeDefinitions["SECOND"], "1"));
        Equal(3, table.Entries.Count, "Attribute redraw removal did not clear membership.");
    }
}
