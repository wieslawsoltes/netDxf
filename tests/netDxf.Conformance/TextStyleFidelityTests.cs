using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const int StyleFontBits = unchecked((int)0xF3123456);
    private const string StyleFamily = "Independent 青";

    private static void RegisterTextStyleFidelityTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            foreach (double? last in new double?[] { null, 0, -2.75 })
            {
                double? h = last;
                Run($"style/fidelity/wire/{v}/{b}/{h}", () => StyleFidelityWire(v, b, h));
            }
            for (int fault = 0; fault < 6; fault++)
            {
                int f = fault;
                Run($"style/fidelity/malformed/{v}/{b}/{f}", () => StyleFidelityMalformed(v, b, f));
            }
            Run($"style/fidelity/producer/{v}/{b}", () => StyleFidelityProducer(v, b));
            Run($"style/fidelity/api-roundtrip/{v}/{b}", () => StyleFidelityApiRoundtrip(v, b));
            Run($"style/fidelity/prefix-modes/{v}/{b}", () => StyleFidelityPrefixModes(v, b));
            for (int source = 0; source < 6; source++)
            foreach (char surrogate in new[] {'\uD800','\uDC00'})
            {
                int location = source; char c = surrogate;
                Run($"style/fidelity/surrogate/{v}/{b}/{location}/{(int)c}", () => StyleFidelitySurrogate(v,b,location,c));
            }
        }
        Run("style/fidelity/api/flags-height", StyleFidelityFlags);
        Run("style/fidelity/api/font-prefix", StyleFidelityPrefix);
        Run("style/fidelity/api/clone", StyleFidelityClone);
    }

    private static List<DxfTag> StyleFidelityTags(DxfVersion version, double? last)
    {
        string profile = version switch {
            DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018", DxfVersion.AutoCad2007 => "AC1021",
            DxfVersion.AutoCad2010 => "AC1024", DxfVersion.AutoCad2013 => "AC1027", _ => "AC1032" };
        var tags = new List<DxfTag> {
            new(0,"SECTION"), new(2,"HEADER"), new(9,"$ACADVER"), new(1,profile), new(9,"$DWGCODEPAGE"), new(3,"ANSI_1252"), new(9,"$HANDSEED"), new(5,"FFFF"), new(0,"ENDSEC"),
            new(0,"SECTION"), new(2,"TABLES"), new(0,"TABLE"), new(2,"STYLE"), new(5,"A"), new(330,"0"), new(100,"AcDbSymbolTable"), new(70,(short)2) };
        for (int shape = 0; shape < 2; shape++)
        {
            tags.AddRange(new DxfTag[] { new(0,"STYLE"), new(5,shape == 0 ? "B" : "C"), new(330,"A"), new(100,"AcDbSymbolTableRecord"), new(100,"AcDbTextStyleTableRecord"), new(2,shape == 0 ? "WIRE" : ""), new(3,shape == 0 ? "wire.shx" : "wire-shapes.shx") });
            if (last.HasValue) tags.Add(new(42,last.Value)); // Deliberately precedes fixed height and flags.
            tags.AddRange(new DxfTag[] { new(71,unchecked((short)0xC016)), new(50,12.5), new(40,1.25), new(41,0.75), new(70,(short)(0x4074 | shape)), new(1001,"STYLE_EXTERNAL"), new(1000,"after stored STYLE fields"), new(1070,(short)31) });
        }
        tags.AddRange(new DxfTag[] { new(0,"ENDTAB"), new(0,"ENDSEC"), new(0,"SECTION"), new(2,"ENTITIES"), new(0,"POINT"), new(5,"D"), new(100,"AcDbEntity"), new(8,"0"), new(100,"AcDbPoint"), new(10,123.0), new(20,456.0), new(30,789.0), new(0,"ENDSEC"), new(0,"EOF") });
        return tags;
    }

    private static List<DxfRawRecord> StyleRecords(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DxfRawDocument.Load(stream).Sections.SelectMany(s => s.Records).Where(r => r.Name == "STYLE").ToList();
    }

    private static void StyleFidelityWire(DxfVersion v, bool binary, double? height)
    {
        using var input = new MemoryStream(RawFixtureBytes(StyleFidelityTags(v, height), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Authored STYLE rejected");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var style = doc.TextStyles["WIRE"];
            var shape = doc.ShapeStyles.Items.Single();
            Equal((TextStyleFlags)0x4074, style.Flags, "Full text flags");
            Equal((TextStyleFlags)0x4075, shape.Flags, "Full shape flags");
            Equal(unchecked((short)0xC016), style.TextGenerationFlags, "Full generation flags");
            Equal(style.TextGenerationFlags, shape.TextGenerationFlags, "Shape generation flags");
            Check(style.IsBackward && style.IsUpsideDown && style.IsVertical, "Unknown bits hid boolean projections");
            Equal(height, style.LastHeight, "Optional text last height");
            Equal(height, shape.LastHeight, "Optional shape last height");
            Near(1.25, style.Height, "Last height replaced fixed height");
            Equal("after stored STYLE fields", (string)style.XData["STYLE_EXTERNAL"].XDataRecord[0].Value, "STYLE XData boundary");
            Equal(new Vector3(123,456,789), doc.Entities.Points.Single().Position, "Following entity boundary");
            using var output = new MemoryStream(); Check(doc.Save(output, binary ^ (cycle != 0)), "Authored STYLE save");
            byte[] bytes = output.ToArray();
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"style-fidelity-wire-{v}-{binary}-{(height == null ? "absent" : height == 0 ? "zero" : "negative")}.dxf"), bytes);
            foreach (var record in StyleRecords(bytes).Where(r => r.Tags.Any(t => t.Code == 3 && ((string)t.Value).StartsWith("wire", StringComparison.Ordinal))))
                Equal(height.HasValue ? 1 : 0, record.Tags.Count(t => t.Code == 42), "Group42 presence");
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Authored STYLE reload");
        }
    }

    private static void StyleFidelityMalformed(DxfVersion v, bool binary, int fault)
    {
        var tags = StyleFidelityTags(v, 3.0);
        int at = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbTextStyleTableRecord");
        switch (fault)
        {
            case 0: tags.Insert(at + 1, new(70,(short)0)); break;
            case 1: tags.Insert(at + 1, new(71,(short)0)); break;
            case 2: tags.Insert(at + 1, new(42,4.0)); break;
            case 3: tags[at] = new(100,"AcDbWrongStyle"); break;
            case 4: tags.Insert(at + 1, new(1071,123)); break;
            case 5: tags.Insert(at + 1, new(100,"AcDbTextStyleTableRecord")); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed STYLE accepted");
#endif
    }

    private static void AssertStyleProducer(DxfDocument doc, string suffix = "")
    {
        var rich = doc.TextStyles["QA_RICH" + suffix];
        Equal("Arial.ttf", rich.FontFile, "ACAD font data discarded font file");
        Equal(StyleFamily, rich.FontFamilyName, "Font prefix family, not unrelated suffix string");
        Equal(StyleFontBits, rich.ExtendedFontData!.Flags, "Full signed font flags");
        Equal(FontStyle.Bold | FontStyle.Italic, rich.FontStyle, "Font style bits");
        Equal((TextStyleFlags)0x4074, rich.Flags, "Producer flags");
        Equal((short)0x4006, rich.TextGenerationFlags, "Producer generation flags");
        Equal((double?)9.75, rich.LastHeight, "Producer last height");
        var data = rich.XData["ACAD"].XDataRecord;
        Equal(7, data.Count, "Unrelated ACAD suffix count");
        Equal("unrelated suffix", (string)data[3].Value, "Unrelated ACAD string");
        Equal(73, (int)data[4].Value, "Unrelated ACAD integer");
        Check(((byte[])data[5].Value).SequenceEqual(new byte[] {0,255,25}), "Unrelated ACAD binary data");
        Equal("external style data", (string)rich.XData["STYLE_QA"].XDataRecord[0].Value, "External XData");
        var shx = doc.TextStyles["QA_SHX" + suffix];
        Equal("bigfont.shx", shx.BigFont, "BigFont state");
        Equal((double?)0, shx.LastHeight, "Explicit zero");
        var empty = doc.TextStyles["QA_EMPTY" + suffix];
        Equal("", empty.FontFile, "Empty file was substituted");
        Check(empty.ExtendedFontData != null && empty.ExtendedFontData.FamilyName == "" && empty.ExtendedFontData.Flags == 0, "Empty font prefix lost");
        var absent = doc.TextStyles["QA_ABSENT" + suffix];
        Equal("extensionless", absent.FontFile, "Stored extensionless filename changed");
        Check(absent.LastHeight == null && absent.ExtendedFontData == null, "Missing optional data invented");
        Equal(4, absent.XData["ACAD"].XDataRecord.Count, "Nonfont ACAD data lost");
    }

    private static string StyleSnapshot(TextStyle style) => string.Join("|", style.XData.Values.Select(x => x.ApplicationRegistry.Name + ":" + string.Join(";", x.XDataRecord.Select(r => r.Code + "=" + (r.Value is byte[] b ? Convert.ToHexString(b) : r.Value)))));

    private static void StyleFidelityProducer(DxfVersion v, bool binary)
    {
        int year = int.Parse(v.ToString().Replace("AutoCad", ""));
        var doc = DxfDocument.Load(Path.Combine("tests", "fixtures", "style-fidelity", $"ezdxf-style-R{year}-{binary}.dxf")) ?? throw new InvalidOperationException("Independent STYLE input rejected");
        AssertStyleProducer(doc);
        foreach (var style in doc.TextStyles.Items.Where(s => s.Name.StartsWith("QA_",StringComparison.Ordinal)).ToList())
            doc.TextStyles.Add((TextStyle)style.Clone(style.Name + "_COPY"));
        AssertStyleProducer(doc, "_COPY");
        var shape = doc.ShapeStyles.Items.Single();
        Equal((TextStyleFlags)0x4175, shape.Flags, "Producer shape flags");
        Equal((short)0x4016, shape.TextGenerationFlags, "Producer shape generation");
        Equal((double?)3.125, shape.LastHeight, "Producer shape last height");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var rich = doc.TextStyles["QA_RICH"]; var xdata = rich.XData["ACAD"]; var records = xdata.XDataRecord.ToArray();
            string before = StyleSnapshot(rich);
            int uses = doc.ApplicationRegistries["ACAD"].GetReferences()!.Sum(r => r.Uses);
            using var output = new MemoryStream(); bool transport = binary ^ (cycle != 0);
            Check(doc.Save(output, transport), "Producer STYLE save");
            Equal(before, StyleSnapshot(rich), "Save mutated STYLE XData");
            Check(ReferenceEquals(xdata, rich.XData["ACAD"]), "Save replaced XData object");
            Check(records.Zip(xdata.XDataRecord).All(pair => ReferenceEquals(pair.First,pair.Second)), "Save replaced XData records");
            Equal(uses, doc.ApplicationRegistries["ACAD"].GetReferences()!.Sum(r => r.Uses), "Save changed APPID reference uses");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"style-fidelity-producer-{v}-{transport}-{cycle}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Producer STYLE reload");
            AssertStyleProducer(doc); AssertStyleProducer(doc,"_COPY");
            Check(ReferenceEquals(doc.Entities.Texts.Single().Style, doc.TextStyles["QA_RICH"]), "TEXT style reference not canonical");
            Check(ReferenceEquals(doc.Entities.MTexts.Single().Style, doc.TextStyles["QA_SHX"]), "MTEXT style reference not canonical");
        }
    }

    private static void StyleFidelityApiRoundtrip(DxfVersion v, bool binary)
    {
        const string literal = "Literal \\U+0041 青😀\0\r\n";
        var doc = new DxfDocument(v);
        var style = new TextStyle("API", "fonts\\U+0042青\n.shx") { BigFont = "big\\U+0043青\r.shx", ExtendedFontData = new TextStyleFontData(literal, StyleFontBits), LastHeight = 0 };
        style.XData["ACAD"].XDataRecord.Add(XDataRecord.OpenControlString);
        style.XData["ACAD"].XDataRecord.Add(new(XDataCode.String,literal));
        style.XData["ACAD"].XDataRecord.Add(XDataRecord.CloseControlString);
        doc.TextStyles.Add(style);
        doc.Entities.Add(new Text("Uses STYLE",Vector3.Zero,2.5,style));
        string before = StyleSnapshot(style);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var output = new MemoryStream(); Check(doc.Save(output,binary ^ (cycle != 0)), "API STYLE save");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"style-fidelity-api-{v}-{binary}.dxf"),output.ToArray());
            Equal(before,StyleSnapshot(style),"Literal save mutated source");
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("API STYLE reload");
            var copy = doc.TextStyles["API"];
            Equal(literal,copy.ExtendedFontData!.FamilyName,"Literal font family");
            Equal(literal,(string)copy.XData["ACAD"].XDataRecord[3].Value,"Literal unrelated XData");
            Equal(style.FontFile,copy.FontFile,"Literal font filename");
            Equal(style.BigFont,copy.BigFont,"Literal bigfont filename");
        }
    }

    private static void StyleFidelityPrefixModes(DxfVersion v, bool binary)
    {
        var doc = new DxfDocument(v);
        for (int mode = 0; mode < 4; mode++)
        {
            var style = new TextStyle("MODE"+mode,"simplex.shx");
            var x = new XData(new ApplicationRegistry("acad"));
            x.XDataRecord.Add(XDataRecord.OpenControlString);
            x.XDataRecord.Add(new(XDataCode.String,"unrelated prefix"));
            x.XDataRecord.Add(new(XDataCode.Int32,0x03000022));
            x.XDataRecord.Add(XDataRecord.CloseControlString);
            style.XData.Add(x);
            if (mode > 0) style.ExtendedFontData = new TextStyleFontData(mode == 1 ? "" : "Family",0);
            if (mode == 3) style.ExtendedFontData = null;
            doc.TextStyles.Add(style);
        }
        using var output = new MemoryStream(); Check(doc.Save(output,binary),"Prefix modes save");
        output.Position = 0; var loaded = DxfDocument.Load(output)!;
        for (int mode = 0; mode < 4; mode++)
        {
            var style = loaded.TextStyles["MODE"+mode];
            Equal(mode == 1 || mode == 2,style.ExtendedFontData != null,"Optional prefix presence");
            Equal(mode == 1 || mode == 2 ? 6 : 4,style.XData["ACAD"].XDataRecord.Count,"Prefix unrelated records");
            Equal("simplex.shx",style.FontFile,"Extended prefix setter changed filename");
        }
    }

    private static void StyleFidelitySurrogate(DxfVersion version, bool binary, int source, char surrogate)
    {
        var doc = new DxfDocument(version);
        var style = new TextStyle("UNICODE","valid.shx");
        var shape = new ShapeStyle("SHAPE","valid-shapes.shx");
        var data = new XData(new ApplicationRegistry("STYLE_UNICODE"));
        data.XDataRecord.Add(new(XDataCode.String,"valid"));
        switch (source)
        {
            case 0: style.ExtendedFontData = new TextStyleFontData("Family" + surrogate,0); break;
            case 1: style.FontFile = "font" + surrogate + ".shx"; break;
            case 2: style.BigFont = "big" + surrogate + ".shx"; break;
            case 3: data.XDataRecord[0] = new(XDataCode.String,"Data" + surrogate); style.XData.Add(data); break;
            case 4: shape.File = "shape" + surrogate + ".shx"; break;
            case 5: data.XDataRecord[0] = new(XDataCode.String,"Data" + surrogate); shape.XData.Add(data); break;
        }
        doc.TextStyles.Add(style); doc.ShapeStyles.Add(shape);
        using var stream = new MemoryStream(); byte[] original = {11,22,33,44,55}; stream.Write(original); stream.Position = 2;
        int layouts = doc.Layouts.Count;
#if DEBUG
        Throws<InvalidDataException>(() => doc.Save(stream,binary));
#else
        Check(!doc.Save(stream,binary), "Invalid STYLE Unicode accepted");
#endif
        Check(stream.ToArray().SequenceEqual(original),"Rejected Unicode save wrote output");
        Equal(2L,stream.Position,"Rejected Unicode save moved stream");
        Equal(layouts,doc.Layouts.Count,"Rejected Unicode save changed document setup");
        // Repair the same graph, then retry: preflight must leave it usable.
        style.ExtendedFontData = null; style.FontFile = "valid.shx"; style.BigFont = "";
        shape.File = "valid-shapes.shx"; data.XDataRecord[0] = new(XDataCode.String,"Repaired 😀");
        using var repaired = new MemoryStream(); Check(doc.Save(repaired,binary),"Repair/retry failed");
    }

    private static void StyleFidelityFlags()
    {
        var style = new TextStyle("FLAGS","simplex.shx") { Flags = unchecked((TextStyleFlags)(short)0xFFF4), TextGenerationFlags = unchecked((short)0xFFFF), LastHeight = -3.25 };
        style.IsVertical = false; style.IsBackward = false; style.IsUpsideDown = false;
        Equal(unchecked((TextStyleFlags)(short)0xFFF0),style.Flags,"Vertical setter lost unknown bits");
        Equal(unchecked((short)0xFFF9),style.TextGenerationFlags,"Generation setter lost unknown bits");
        var shape = new ShapeStyle("SHAPE","shape.shx") { Flags = unchecked((TextStyleFlags)(short)0xFFFF), LastHeight = 0 };
        Throws<ArgumentException>(() => style.Flags |= TextStyleFlags.Shape);
        Throws<ArgumentException>(() => shape.Flags = TextStyleFlags.None);
        foreach (double bad in new[] {double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            Throws<ArgumentOutOfRangeException>(() => style.LastHeight = bad);
            Throws<ArgumentOutOfRangeException>(() => shape.LastHeight = bad);
        }
        Equal((double?)-3.25,style.LastHeight,"Invalid height mutated STYLE");
        Equal((double?)0,shape.LastHeight,"Invalid height mutated shape STYLE");
    }

    private static void StyleFidelityPrefix()
    {
        var style = new TextStyle("FONT","Arial.ttf") { ExtendedFontData = new TextStyleFontData("Family",StyleFontBits) };
        style.FontStyle = FontStyle.Regular;
        Equal(StyleFontBits & ~0x03000000,style.ExtendedFontData!.Flags,"FontStyle lost pitch/family/charset/unknown bits");
        style.XData["ACAD"].XDataRecord[0] = new(XDataCode.String,"Direct edit");
        Equal("Direct edit",style.FontFamilyName,"Font property has stale duplicated state");
        style.XData["ACAD"].XDataRecord.Add(new(XDataCode.String,"Ambiguous tail"));
        style.XData["ACAD"].XDataRecord.Add(new(XDataCode.Int32,99));
        string before = StyleSnapshot(style);
        Throws<InvalidOperationException>(() => style.ExtendedFontData = null);
        Equal(before,StyleSnapshot(style),"Ambiguous removal mutated XData");
        Throws<InvalidOperationException>(() => style.FontFile = "new.ttf");
        Equal("Arial.ttf",style.FontFile,"Rejected file setter changed file");
        Throws<ArgumentOutOfRangeException>(() => style.FontFamilyName = new string('x',256));
        Equal("Arial.ttf",style.FontFile,"Invalid family setter changed file");
        Throws<ArgumentNullException>(() => new TextStyleFontData(null!,0));
        var constructor = new TextStyle("CONSTRUCTOR","Family",FontStyle.Bold);
        Equal(0x02000000,constructor.ExtendedFontData!.Flags,"Legacy constructor prefix");
        Check(constructor.FontFile == "", "Legacy family constructor file");
    }

    private static void StyleFidelityClone()
    {
        var source = new TextStyle("CLONE","asian.shx") { BigFont = "big.shx", Flags = (TextStyleFlags)0x4074, TextGenerationFlags = unchecked((short)0xF006), LastHeight = 9.75, ExtendedFontData = new TextStyleFontData("Family",StyleFontBits) };
        var copy = (TextStyle)source.Clone("RENAMED");
        Equal(source.BigFont,copy.BigFont,"Clone BigFont"); Equal(source.Flags,copy.Flags,"Clone flags"); Equal(source.TextGenerationFlags,copy.TextGenerationFlags,"Clone generation"); Equal(source.LastHeight,copy.LastHeight,"Clone last height");
        Equal(source.ExtendedFontData!.Flags,copy.ExtendedFontData!.Flags,"Clone font bits");
        Check(!ReferenceEquals(source.XData["ACAD"],copy.XData["ACAD"]),"Clone shares XData");
        copy.FontStyle = FontStyle.Regular; copy.IsVertical = false; copy.LastHeight = null;
        Equal(StyleFontBits,source.ExtendedFontData.Flags,"Clone changed source font bits"); Check(source.IsVertical && source.LastHeight == 9.75,"Clone changed source flags/height");
        var shape = new ShapeStyle("SHAPE","shape.shx") { Flags = (TextStyleFlags)0x4175, TextGenerationFlags = 16, LastHeight = -8 };
        var shapeCopy = (ShapeStyle)shape.Clone("SHAPE_COPY");
        Equal(shape.Flags,shapeCopy.Flags,"Shape clone flags"); Equal(shape.TextGenerationFlags,shapeCopy.TextGenerationFlags,"Shape clone generation"); Equal(shape.LastHeight,shapeCopy.LastHeight,"Shape clone last height");
    }
}
