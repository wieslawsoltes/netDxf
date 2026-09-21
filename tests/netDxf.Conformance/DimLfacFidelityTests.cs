// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string DimLfacStyleName = "DIMLFAC_FIDELITY";
    private static readonly double[] DimLfacValues = {
        double.Epsilon, -double.Epsilon, 1e-300, -1e-300, 1e-13, -1e-13,
        1e-6, -1e-6, 1, -1, 1e300, -1e300, double.MaxValue, -double.MaxValue
    };

    private static void RegisterDimLfacFidelityTests()
    {
        foreach (double epsilon in new[] { double.Epsilon, 1e-12, 1e-3, 100.0 })
        {
            foreach (double value in DimLfacValues)
                Run($"dimlfac-fidelity/api/{ParameterBits(epsilon)}/{ParameterBits(value)}", () =>
                {
                    double old = MathHelper.Epsilon;
                    try
                    {
                        MathHelper.Epsilon = epsilon;
                        var style = new DimensionStyle(DimLfacStyleName) { DimScaleLinear = value };
                        SameDoubleBits(value, style.DimScaleLinear, "Assigned linear scale");
                        var clone = (DimensionStyle)style.Clone();
                        SameDoubleBits(value, clone.DimScaleLinear, "Cloned linear scale");
                        clone.DimScaleLinear = -value;
                        SameDoubleBits(value, style.DimScaleLinear, "Clone changed source");
                        var item = new DimensionStyleOverride(DimensionStyleOverrideType.DimScaleLinear, value);
                        SameDoubleBits(value, (double)item.Value, "Override linear scale");
                        SameDoubleBits(epsilon, MathHelper.Epsilon, "API changed global tolerance");
                    }
                    finally { MathHelper.Epsilon = old; }
                });
            foreach (double invalid in new[] { 0.0, BitConverter.Int64BitsToDouble(long.MinValue), double.NaN,
                double.PositiveInfinity, double.NegativeInfinity })
                Run($"dimlfac-fidelity/reject/{ParameterBits(epsilon)}/{ParameterBits(invalid)}", () =>
                {
                    var style = new DimensionStyle(DimLfacStyleName) { DimScaleLinear = -2.5 };
                    double old = MathHelper.Epsilon;
                    try
                    {
                        MathHelper.Epsilon = epsilon;
                        Throws<ArgumentOutOfRangeException>(() => style.DimScaleLinear = invalid);
                        SameDoubleBits(-2.5, style.DimScaleLinear, "Rejected assignment mutated style");
                        Throws<ArgumentOutOfRangeException>(() => new DimensionStyleOverride(
                            DimensionStyleOverrideType.DimScaleLinear, invalid));
                    }
                    finally { MathHelper.Epsilon = old; }
                });
        }
        foreach (object wrongType in new object[] { 1, 1f, 1m, "1" })
            Run("dimlfac-fidelity/override-type/" + wrongType.GetType().Name, () =>
                Throws<ArgumentException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.DimScaleLinear, wrongType)));

        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            foreach (bool block in new[] { false, true }) for (int index = 0; index < DimLfacValues.Length; index++)
            {
                int k = index;
                Run($"dimlfac-fidelity/wire/{version}/{binary}/{block}/{k}", () =>
                {
                    double value = DimLfacValues[k];
                    byte[] source = DimLfacSource(version, binary, block, value, -value);
                    string stem = $"dimlfac-fidelity-{version}-{binary}-{block}-{k}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source);
                    using var input = new MemoryStream(source);
                    var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("DIMLFAC source load failed");
                    CheckDimLfacDocument(doc, value, -value, block);
                    string handle = DimLfacDimension(doc, block).Handle;
                    foreach (bool output in new[] { false, true })
                    {
                        using var stream = new MemoryStream(); Check(doc.Save(stream, output), "DIMLFAC save");
                        byte[] bytes = stream.ToArray();
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), bytes);
                        CheckDimLfacPacket(LoadRaw(bytes), value, -value);
                        stream.Position = 0;
                        var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("DIMLFAC reload failed");
                        CheckDimLfacDocument(second, value, -value, block);
                        Equal(handle, DimLfacDimension(second, block).Handle, "Dimension handle changed");
                    }
                    Check(input.CanRead, "Input ownership changed");
                });
            }
            foreach (int kind in new[] { 0, 1, 2 })
                Run($"dimlfac-fidelity/default/{version}/{binary}/{kind}", () =>
                {
                    double value = kind == 1 ? BitConverter.Int64BitsToDouble(long.MinValue) : 0.0;
                    var raw = LoadRaw(DimLfacSource(version, binary, false, value, 2));
                    if (kind == 2)
                    {
                        var record = DimLfacRecord(raw);
                        raw = raw.WithRecord(record, record.Tags.Where(t => t.Code != 144));
                    }
                    using var input = new MemoryStream(SaveRaw(raw, binary));
                    var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Default DIMLFAC load failed");
                    CheckDimLfacDocument(doc, 1, 2, false);
                });
            foreach (bool table in new[] { false, true }) foreach (double invalid in new[] {
                double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Run($"dimlfac-fidelity/nonfinite-wire/{version}/{binary}/{table}/{ParameterBits(invalid)}", () =>
                {
                    const double marker = 123456789.25;
                    byte[] bytes = DimLfacSource(version, binary, false, table ? marker : 1, table ? 2 : marker);
                    bytes = DimLfacInjectNonfinite(bytes, binary, marker, invalid);
                    using var input = new MemoryStream(bytes);
                    bool rejected;
                    try { rejected = DxfDocument.Load(input) == null; }
                    catch (ArgumentException) { rejected = true; }
                    catch (FormatException) { rejected = true; }
                    catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "Nonfinite stored DIMLFAC accepted");
                    Check(input.CanRead, "Rejected load closed source stream");
                });
            Run($"dimlfac-fidelity/zero-override/{version}/{binary}", () =>
            {
                using var input = new MemoryStream(DimLfacSource(version, binary, false, 1, 0));
                bool rejected;
                try { rejected = DxfDocument.Load(input) == null; }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Zero override admission changed");
            });
            // Restore the previously blocked whole-document coverage without weakening the old codec tests.
            foreach (int kind in new[] { 1, 2, 3, 7, 8 })
                Run($"dimlfac-fidelity/ellipse-epsilon100/{version}/{binary}/{kind}", () =>
                {
                    byte[] source = EllipseIoSource(version, binary, false, kind);
                    double old = MathHelper.Epsilon;
                    try
                    {
                        MathHelper.Epsilon = 100;
                        using var input = new MemoryStream(source);
                        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Extreme-epsilon document load failed");
                        Check(!doc.Entities.Ellipses.Single().IsFullEllipse, "Extreme epsilon closed ellipse arc");
                        SameDoubleBits(90, doc.Entities.Ellipses.Single().Rotation, "Extreme epsilon changed orientation");
                        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Extreme-epsilon document save");
                        EllipseIoCheckPacket(output.ToArray(), kind);
                        output.Position = 0;
                        var second = DxfDocument.Load(output) ?? throw new InvalidOperationException("Extreme-epsilon reload failed");
                        Check(!second.Entities.Ellipses.Single().IsFullEllipse, "Extreme epsilon closed reloaded arc");
                        SameDoubleBits(100, MathHelper.Epsilon, "IO changed global tolerance");
                    }
                    finally { MathHelper.Epsilon = old; }
                });
        }
    }

    private static DxfRawRecord DimLfacRecord(DxfRawDocument raw) => raw.Sections.SelectMany(s => s.Records)
        .Single(r => r.Name == "DIMSTYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, DimLfacStyleName)));
    private static Dimension DimLfacDimension(DxfDocument doc, bool block) => block
        ? doc.Blocks["DIMLFAC_HOLDER"].Entities.OfType<Dimension>().Single() : doc.Entities.Dimensions.Single();

    private static byte[] DimLfacSource(DxfVersion version, bool binary, bool block, double scale, double overrideScale)
    {
        var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
        var style = new DimensionStyle(DimLfacStyleName); doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
        var dimension = new LinearDimension(Vector2.Zero, new Vector2(10, 0), 3, 0, style) { UserText = "FIXED" };
        dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.DimScaleLinear, 2.0));
        if (block) doc.Entities.Add(new Insert(new Block("DIMLFAC_HOLDER", new EntityObject[] { dimension })));
        else doc.Entities.Add(dimension);
        doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "DIMLFAC seed save");
        var raw = LoadRaw(stream.ToArray()); var record = DimLfacRecord(raw);
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 144 ? new DxfTag(144, scale) : t));
        record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMENSION");
        Equal(1, record.Tags.Count(t => t.Code == 1040), "One seed real override");
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 1040 ? new DxfTag(1040, overrideScale) : t));
        CheckDimLfacPacket(raw, scale, overrideScale);
        return SaveRaw(raw, binary);
    }

    private static void CheckDimLfacPacket(DxfRawDocument raw, double scale, double overrideScale)
    {
        SameDoubleBits(scale, (double)DimLfacRecord(raw).Tags.Single(t => t.Code == 144).Value, "Stored DIMSTYLE group144");
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMENSION");
        var tags = record.Tags;
        int at = Enumerable.Range(0, tags.Count - 1).Single(i => tags[i].Code == 1070 && Equals(tags[i].Value, (short)144));
        Equal((short)1040, tags[at + 1].Code, "Override real group");
        SameDoubleBits(overrideScale, (double)tags[at + 1].Value, "Stored DIMLFAC override");
    }

    private static void CheckDimLfacDocument(DxfDocument doc, double scale, double overrideScale, bool block)
    {
        SameDoubleBits(scale, doc.DimensionStyles[DimLfacStyleName].DimScaleLinear, "Typed DIMLFAC retained");
        var dimension = DimLfacDimension(doc, block);
        SameDoubleBits(overrideScale, (double)dimension.StyleOverrides[DimensionStyleOverrideType.DimScaleLinear].Value, "Typed override retained");
        var clone = (Dimension)dimension.Clone();
        SameDoubleBits(scale, clone.Style.DimScaleLinear, "Cloned dimension style");
        SameDoubleBits(overrideScale, (double)clone.StyleOverrides[DimensionStyleOverrideType.DimScaleLinear].Value, "Cloned dimension override");
        Check(!ReferenceEquals(clone.Style, dimension.Style), "Clone style identity");
        Equal(0, doc.Objects.Validate().Count, "DIMLFAC graph validation");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }

    private static byte[] DimLfacInjectNonfinite(byte[] source, bool binary, double marker, double value)
    {
        if (!binary)
        {
            string text = Encoding.UTF8.GetString(source), needle = marker.ToString("G17", CultureInfo.InvariantCulture);
            Equal(1, text.Split(needle, StringSplitOptions.None).Length - 1, "One text sentinel");
            return Encoding.UTF8.GetBytes(text.Replace(needle, value.ToString("G17", CultureInfo.InvariantCulture)));
        }
        byte[] bytes = (byte[])source.Clone(), pattern = BitConverter.GetBytes(marker), replacement = BitConverter.GetBytes(value);
        int matches = 0;
        for (int i = 0; i <= bytes.Length - pattern.Length; i++)
            if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern)) { replacement.CopyTo(bytes, i); matches++; i += pattern.Length - 1; }
        Equal(1, matches, "One binary sentinel"); return bytes;
    }
}
