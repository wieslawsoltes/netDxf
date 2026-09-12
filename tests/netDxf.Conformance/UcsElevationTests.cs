using netDxf;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterUcsElevationTests()
    {
        Run("ucs/elevation/defaults", () =>
        {
            Near(0, new UCS("Default").Elevation, "default elevation");
            Near(0, new UCS("Axes", Vector3.Zero, Vector3.UnitX, Vector3.UnitY).Elevation, "axis constructor elevation");
            Near(0, UCS.FromNormal("Normal", Vector3.Zero, Vector3.UnitZ).Elevation, "factory elevation");
        });
        Run("ucs/elevation/finite-values", () =>
        {
            var ucs = new UCS("Finite") { Elevation = -12.5 };
            foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                Throws<ArgumentOutOfRangeException>(() => ucs.Elevation = invalid);
                Near(-12.5, ucs.Elevation, "invalid assignment changed elevation");
            }
        });
        Run("ucs/elevation/clone-and-origin", () =>
        {
            var source = new UCS("Source", new Vector3(1, 2, 3), Vector3.UnitY, -Vector3.UnitX) { Elevation = 44.125 };
            var copy = (UCS)source.Clone("Copy");
            Near(source.Elevation, copy.Elevation, "clone elevation");
            Equal(source.Origin, copy.Origin, "clone origin");
            Equal(source.GetTransformation(), copy.GetTransformation(), "clone axes");
            copy.Elevation = -9;
            Near(44.125, source.Elevation, "source elevation isolation");
            Equal(source.Origin, copy.Origin, "elevation must not move origin");
        });
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version;
                bool b = binary;
                Run($"ucs/elevation/document/{v}/{b}", () => UcsElevationRoundTrip(v, b));
                Run($"ucs/elevation/authored/{v}/{b}", () => UcsElevationAuthoredFixture(v, b));
                Run($"ucs/elevation/missing/{v}/{b}", () =>
                {
                    using var stream = UcsElevationFixture(v, b, null);
                    var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Missing-elevation fixture failed.");
                    Near(0, loaded.UCSs["Fixture"].Elevation, "omitted elevation default");
                });
            }
        }
    }

    private static void UcsElevationRoundTrip(DxfVersion version, bool binary)
    {
        foreach (double value in new[] { 0.0, -12.5, 72.125, -1.25e-6, 1.0e7 })
        {
            var document = new DxfDocument(version);
            var ucs = new UCS("Elevated", new Vector3(3, 4, 5), Vector3.UnitY, -Vector3.UnitX) { Elevation = value };
            document.UCSs.Add(ucs);
            using var stream = new MemoryStream();
            Check(document.Save(stream, binary), "UCS fixture save failed.");
            stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("UCS fixture load failed.");
            Near(value, loaded.UCSs[ucs.Name].Elevation, "UCS elevation round trip");
            Equal(ucs.Origin, loaded.UCSs[ucs.Name].Origin, "UCS origin round trip");
            Equal(ucs.XAxis, loaded.UCSs[ucs.Name].XAxis, "UCS X axis round trip");
            Equal(ucs.YAxis, loaded.UCSs[ucs.Name].YAxis, "UCS Y axis round trip");
            Check(loaded.GetObjectByHandle(loaded.UCSs[ucs.Name].Handle) == loaded.UCSs[ucs.Name], "UCS handle lookup failed.");

            // Inspect the written tags independently of the UCS semantic reader.
            stream.Position = 0;
            object reader = NewCodeReader(stream, binary);
            bool found = false;
            string record = string.Empty;
            Invoke(reader, "Next");
            while (!(TagCode(reader) == 0 && (string)Invoke(reader, "ReadString")! == "EOF"))
            {
                if (TagCode(reader) == 0) record = (string)Invoke(reader, "ReadString")!;
                if (record == "UCS" && TagCode(reader) == 146)
                {
                    Near(value, (double)Invoke(reader, "ReadDouble")!, "written group 146");
                    found = true;
                }
                Invoke(reader, "Next");
            }
            Check(found, "Writer omitted UCS elevation group 146.");
        }
    }

    private static void UcsElevationAuthoredFixture(DxfVersion version, bool binary)
    {
        using var stream = UcsElevationFixture(version, binary, -17.625);
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Authored UCS fixture failed.");
        UCS ucs = loaded.UCSs["Fixture"];
        Near(-17.625, ucs.Elevation, "authored elevation");
        Equal(new Vector3(1, 2, 3), ucs.Origin, "authored origin");
        Equal(Vector3.UnitX, ucs.XAxis, "authored X axis");
    }

    // Hand-authored semantic tags: not generated by WriteUCS.
    private static MemoryStream UcsElevationFixture(DxfVersion version, bool binary, double? elevation)
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
        Tag(9, "$DWGCODEPAGE"); Tag(3, "ANSI_1252");
        Tag(9, "$HANDSEED"); Tag(5, "FFFF"); Tag(0, "ENDSEC");
        Tag(0, "SECTION"); Tag(2, "TABLES"); Tag(0, "TABLE"); Tag(2, "UCS");
        Tag(5, "A"); Tag(330, "0"); Tag(100, "AcDbSymbolTable"); Tag(70, (short)1);
        Tag(0, "UCS"); Tag(5, "B"); Tag(330, "A"); Tag(100, "AcDbSymbolTableRecord");
        Tag(100, "AcDbUCSTableRecord"); Tag(2, "Fixture"); Tag(70, (short)0);
        // Elevation deliberately precedes the origin; DXF readers must not rely on this order.
        if (elevation.HasValue) Tag(146, elevation.Value);
        Tag(10, 1.0); Tag(20, 2.0); Tag(30, 3.0);
        Tag(11, 1.0); Tag(21, 0.0); Tag(31, 0.0);
        Tag(12, 0.0); Tag(22, 1.0); Tag(32, 0.0); Tag(79, (short)0);
        Tag(0, "ENDTAB"); Tag(0, "ENDSEC");
        Tag(0, "SECTION"); Tag(2, "OBJECTS"); Tag(0, "DICTIONARY");
        Tag(5, "C"); Tag(330, "0"); Tag(100, "AcDbDictionary"); Tag(280, (short)0); Tag(281, (short)1);
        Tag(0, "ENDSEC"); Tag(0, "EOF");
        Invoke(writer, "Flush"); stream.Position = 0;
        return stream;
    }
}
