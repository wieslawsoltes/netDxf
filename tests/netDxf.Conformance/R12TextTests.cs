// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12TextTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            for (int normal = 0; normal < 4; normal++)
            for (int alignment = 0; alignment < 15; alignment++)
            {
                int n = normal, a = alignment; bool b = binary;
                Run($"r12-text/alignment/{b}/{n}/{a}", () => R12TextRoundTrip(b, n, a));
            }
            for (int fault = 0; fault < 22; fault++)
            { int f = fault; bool b = binary; Run($"r12-text/refusal/{b}/{f}", () => R12TextReadRefusal(b, f)); }
            for (int normal = 0; normal < 3; normal++)
            { int n = normal; bool b = binary; Run($"r12-text/wire/{b}/{n}", () => R12TextWire(b, n)); }
            Run("r12-text/controls/" + binary, () => R12TextControls(binary));
            Run("r12-text/defaults/" + binary, () => R12TextDefaults(binary));
            Run("r12-text/anchors/" + binary, () => R12TextAnchors(binary));
        }
        for (int fault = 0; fault < 12; fault++)
        { int f = fault; Run("r12-text/write-refusal/" + f, () => R12TextWriteRefusal(f)); }
        Run("r12-text/style-sharing-conflicts", R12TextStyles);
        Run("r12-text/style-optional-font-data", R12TextStyleOptionals);
        Run("r12-text/encoding-and-budget", R12TextEncoding);
        Run("r12-text/clone-edit-source", R12TextEditing);
    }

    private static Text R12TextSeed(int normal, int alignment)
    {
        var style = new TextStyle("ANNOTATIONS", "romans.shx")
        { BigFont = "bigfont.shx", WidthFactor = .8, ObliqueAngle = 12, LastHeight = 0,
            IsVertical = true, IsBackward = true, IsUpsideDown = true };
        return new Text("Résumé %%d ^\t", new Vector3(10, 20, 30), 2.5, style)
        { Alignment = (TextAlignment)alignment, Normal = R12Normal(normal), Width = 8,
            Rotation = 0, WidthFactor = 1.2, ObliqueAngle = -10,
            IsBackward = (alignment & 1) != 0, IsUpsideDown = (alignment & 2) != 0 };
    }

    private static void R12TextCompare(Text a, Text b)
    {
        Equal(a.Value, b.Value, "Logical TEXT content"); Equal(a.Alignment, b.Alignment, "Text alignment");
        R12Vector(a.Position, b.Position); R12Vector(a.Normal, b.Normal);
        Near(a.Height, b.Height, "Text height"); Near(a.WidthFactor, b.WidthFactor, "Text width factor");
        Near(a.ObliqueAngle, b.ObliqueAngle, "Text oblique angle");
        double rotationError = Math.Abs(a.Rotation - b.Rotation);
        Check(Math.Min(rotationError, Math.Abs(rotationError - 360)) < 1e-10, "Text rotation");
        if (a.Alignment == TextAlignment.Fit || a.Alignment == TextAlignment.Aligned) Near(a.Width, b.Width, "Two-point text width");
        Equal(a.IsBackward, b.IsBackward, "Entity X mirror"); Equal(a.IsUpsideDown, b.IsUpsideDown, "Entity Y mirror");
        Equal(a.Style.Name, b.Style.Name, "Named text style"); Equal(a.Style.FontFile, b.Style.FontFile, "Primary font reference");
        Equal(a.Style.BigFont, b.Style.BigFont, "Bigfont reference"); Equal(a.Style.Flags, b.Style.Flags, "Style flags");
        Equal(a.Style.TextGenerationFlags, b.Style.TextGenerationFlags, "Style generation flags");
        Equal(a.Style.LastHeight, b.Style.LastHeight, "Style last height presence");
        Near(a.Style.Height, b.Style.Height, "Fixed style height"); Near(a.Style.WidthFactor, b.Style.WidthFactor, "Style width");
        Near(a.Style.ObliqueAngle, b.Style.ObliqueAngle, "Style oblique");
    }

    private static void R12TextRoundTrip(bool binary, int normal, int alignment)
    {
        var text = R12TextSeed(normal, alignment); text.Rotation = (alignment * 37 + 13) % 360;
        var style = text.Style; var raw = DxfR12Codec.Create(new EntityObject[] { text }, binary);
        Check(text.Handle == null && text.Owner == null && style.Handle == null && style.Owner == null
            && ReferenceEquals(style, text.Style), "TEXT/STYLE export mutated source ownership");
        for (int pass = 0; pass < 3; pass++)
        {
            raw = R12Reload(raw, pass == 1 ? !binary : binary);
            var decoded = (Text)DxfR12Codec.ReadEntities(raw).Single(); R12TextCompare(text, decoded);
            Check(decoded.Owner == null && decoded.Style.Owner == null && decoded.Handle == "100", "Decoded TEXT ownership/identity");
            raw = DxfR12Codec.Create(new EntityObject[] { decoded }, binary);
        }
    }

    private static void R12TextWire(bool binary, int normal)
    {
        EntityObject[] texts = Enumerable.Range(0, 15).Select(a => (EntityObject)R12TextSeed(normal, a)).ToArray();
        var follow = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        var all = texts.Concat(new EntityObject[] { follow }).ToArray();
        var raw = DxfR12Codec.Create(all, binary); string prefix = $"r12-text-{normal}-{(binary ? "binary" : "text")}";
        for (int stage = 0; stage < 3; stage++)
        {
            bool format = stage == 1 ? !binary : binary;
            byte[] bytes = R12Bytes(raw, format);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + new[] { "-source.dxf", "-output.dxf", "-resave.dxf" }[stage]), bytes);
            using var input = new MemoryStream(bytes); var copy = DxfRawDocument.Load(input);
            var projected = DxfR12Codec.ReadEntities(copy);
            Equal(16, projected.Count, "TEXT/LINE inventory");
            for (int a = 0; a < 15; a++) R12TextCompare((Text)texts[a], (Text)projected[a]);
            R12Compare(follow, projected[15]); raw = DxfR12Codec.Create(projected, format);
        }
    }

    private static void R12TextControls(bool binary)
    {
        var text = R12TextSeed(0, 9);
        text.Value = string.Concat(Enumerable.Range(0, 32).Select(i => (char)i)) + "^ ^A ^Z ^^ trailing^";
        var raw = R12Reload(DxfR12Codec.Create(new EntityObject[] { text }), binary);
        var packet = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single();
        string encoded = (string)packet.Tags.Single(t => t.Code == 1).Value;
        Check(!encoded.Any(c => c < ' ') && encoded.StartsWith("^@^A^B", StringComparison.Ordinal), "Control encoding leaked framing characters");
        Equal(text.Value, ((Text)DxfR12Codec.ReadEntities(raw).Single()).Value, "C0/caret round trip");
        byte[] before = R12Bytes(raw, binary); ((Text)DxfR12Codec.ReadEntities(raw).Single()).Value = "Changed";
        Check(before.SequenceEqual(R12Bytes(raw, binary)), "Projection edit changed raw source");
    }

    private static DxfRawDocument R12TextReplace(DxfRawDocument raw, string type, Func<List<DxfTag>, List<DxfTag>> edit)
    {
        var record = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == type
            && (type != "STYLE" || r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "ANNOTATIONS"))));
        return raw.WithRecord(record, edit(record.Tags.ToList()));
    }
    private static void R12TextReadRefusal(bool binary, int fault)
    {
        var raw = R12Reload(DxfR12Codec.Create(new EntityObject[] { R12TextSeed(0, 0) }), binary);
        string kind = fault >= 14 && fault <= 18 ? "STYLE" : "TEXT";
        var edited = R12TextReplace(raw, kind, tags =>
        {
            void Set(short code, object value) { int i = tags.FindIndex(t => t.Code == code); if (i < 0) tags.Add(new DxfTag(code, value)); else tags[i] = new DxfTag(code, value); }
            switch (fault)
            {
                case 0: tags.RemoveAll(t => t.Code == 1); break;
                case 1: tags.RemoveAll(t => t.Code == 40); break;
                case 2: Set(40, 0.0); break;
                case 3: Set(41, 0.0); break;
                case 4: Set(51, 86.0); break;
                case 5: Set(71, (short)8); break;
                case 6: Set(72, (short)6); break;
                case 7: Set(72, (short)3); Set(73, (short)1); break;
                case 8: tags.RemoveAll(t => t.Code == 11); break;
                case 9: Set(7, "MISSING"); break;
                case 10: Set(39, 1.0); break;
                case 11: Set(1, "unclosed^"); break;
                case 12: Set(1, "unknown^z"); break;
                case 13: tags.Add(new DxfTag(100, "AcDbText")); break;
                case 14: Set(70, (short)1); break;
                case 15: Set(70, (short)16); break;
                case 16: Set(71, (short)8); break;
                case 17: Set(40, -1.0); break;
                case 18: Set(41, .001); break;
                case 19: Set(72, (short)3); Set(73, (short)0); break; // identical fit points
                case 20: Set(72, (short)5); Set(73, (short)0); Set(31, 31.0); break;
                case 21: tags.Add(new DxfTag(1, "duplicate")); break;
            }
            return tags;
        });
        byte[] before = R12Bytes(raw, binary); R12Refuses(() => DxfR12Codec.ReadEntities(edited));
        Check(before.SequenceEqual(R12Bytes(raw, binary)), "Rejected TEXT read changed source");
    }

    private static void R12TextDefaults(bool binary)
    {
        var tags = new[] { new DxfTag(0, "SECTION"), new DxfTag(2, "HEADER"), new DxfTag(9, "$ACADVER"), new DxfTag(1, "AC1009"), new DxfTag(0, "ENDSEC"),
            new DxfTag(0, "SECTION"), new DxfTag(2, "ENTITIES"), new DxfTag(0, "TEXT"), new DxfTag(1, "Default"),
            new DxfTag(10, 1.0), new DxfTag(20, 2.0), new DxfTag(40, 3.0), new DxfTag(0, "ENDSEC"), new DxfTag(0, "EOF") };
        var raw = R12Reload(DxfRawDocument.Create(tags), binary);
        var text = (Text)DxfR12Codec.ReadEntities(raw).Single();
        Equal(TextAlignment.BaselineLeft, text.Alignment, "Default justification"); R12Vector(new Vector3(1, 2, 0), text.Position);
        Equal(1.0, text.WidthFactor, "Default entity width"); Equal(0.0, text.ObliqueAngle, "Default entity oblique");
        Equal(TextStyle.DefaultName, text.Style.Name, "Implicit STANDARD style");
        Equal("Default", text.Value, "Default text string");
    }

    private static void R12TextAnchors(bool binary)
    {
        var raw = DxfR12Codec.Create(new EntityObject[] { R12TextSeed(0, 1) });
        var edited = R12TextReplace(raw, "TEXT", tags => { tags[tags.FindIndex(t => t.Code == 10)] = new DxfTag(10, 12345.0); return tags; });
        var centered = (Text)DxfR12Codec.ReadEntities(R12Reload(edited, binary)).Single();
        R12Vector(new Vector3(10, 20, 30), centered.Position); // justified anchor is group11
        edited = R12TextReplace(raw, "TEXT", tags =>
        {
            void Set(short code, object value) { tags[tags.FindIndex(t => t.Code == code)] = new DxfTag(code, value); }
            Set(72, (short)5); Set(73, (short)0); Set(11, 10.0); Set(21, 28.0); Set(50, 0.0); return tags;
        });
        var fit = (Text)DxfR12Codec.ReadEntities(R12Reload(edited, binary)).Single();
        Near(8, fit.Width, "Endpoint-derived fit width"); Near(90, fit.Rotation, "Endpoint-derived fit direction");
        R12TextCompare(fit, (Text)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new EntityObject[] { fit }), !binary)).Single());
    }

    private static void R12TextWriteRefusal(int fault)
    {
        var text = R12TextSeed(0, 9);
        switch (fault)
        {
            case 0: text.Value = null!; break;
            case 1: text.Height = double.NaN; break;
            case 2: text.WidthFactor = double.NaN; break;
            case 3: text.ObliqueAngle = double.NaN; break;
            case 4: text.Rotation = double.NaN; break;
            case 5: text.Alignment = (TextAlignment)123; break;
            case 6: text.Style.Height = double.NaN; break;
            case 7: text.Style.Flags = (TextStyleFlags)16; break;
            case 8: text.Style.TextGenerationFlags = 8; break;
            case 9: text.Style.XData.Add(new XData(new ApplicationRegistry("PRIVATE"))); break;
            case 10: text.Alignment = TextAlignment.Fit; text.Width = double.PositiveInfinity; break;
            case 11: text.Alignment = TextAlignment.Aligned; text.Position = new Vector3(1e100, 1e100, 0); text.Width = 1; break;
        }
        using var stream = new MemoryStream(); stream.WriteByte(29); stream.Position = 0;
        R12Refuses(() => DxfR12Codec.Save(stream, new EntityObject[] { new Line(Vector3.Zero, Vector3.UnitX), text }));
        Check(stream.Length == 1 && stream.Position == 0 && stream.ToArray()[0] == 29, "Late text refusal touched destination");
    }

    private static void R12TextStyles()
    {
        var first = R12TextSeed(0, 9); var second = R12TextSeed(0, 10);
        second.Style.Name = "annotations";
        var selected = DxfR12Codec.ReadEntities(DxfR12Codec.Create(new EntityObject[] { first, second })).Cast<Text>().ToArray();
        Check(ReferenceEquals(selected[0].Style, selected[1].Style), "Equivalent named styles not shared");
        second.Style.WidthFactor = .9;
        Throws<InvalidOperationException>(() => DxfR12Codec.Create(new EntityObject[] { first, second }));
        var raw = DxfR12Codec.Create(new EntityObject[] { first });
        var duplicate = R12TextReplace(raw, "STYLE", tags => { tags[tags.FindIndex(t => t.Code == 2)] = new DxfTag(2, "Standard"); return tags; });
        Throws<FormatException>(() => DxfR12Codec.ReadEntities(duplicate));
    }
    private static void R12TextStyleOptionals()
    {
        var text = R12TextSeed(0, 9); text.Style.Height = 4; text.Style.Flags |= (TextStyleFlags)64; text.Style.LastHeight = null;
        var raw = DxfR12Codec.Create(new EntityObject[] { text });
        var style = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "STYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "ANNOTATIONS")));
        Check(!style.Tags.Any(t => t.Code == 42), "Absent last height manufactured");
        var edited = raw.WithRecord(style, style.Tags.Select(t => t.Code == 3 ? new DxfTag(3, "legacyfont") : t.Code == 4 ? new DxfTag(4, "") : t));
        var copy = (Text)DxfR12Codec.ReadEntities(edited).Single();
        Equal("legacyfont", copy.Style.FontFile, "Extensionless font reference"); Equal("", copy.Style.BigFont, "Empty bigfont");
        Equal(4.0, copy.Style.Height, "Fixed style height"); Equal(2.5, copy.Height, "Stored entity height overridden by style");
        R12TextCompare(copy, (Text)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new EntityObject[] { copy }), true)).Single());
    }
    private static void R12TextEncoding()
    {
        var text = R12TextSeed(0, 9); text.Value = new string('^', 31);
        var exact = new DxfRawOptions(10000, 1000, 62);
        var raw = DxfR12Codec.Create(new EntityObject[] { text }, false, exact);
        Equal(text.Value, ((Text)DxfR12Codec.ReadEntities(R12Reload(raw, true)).Single()).Value, "Escaped length boundary");
        Throws<InvalidDataException>(() => DxfR12Codec.Create(new EntityObject[] { text }, false, new DxfRawOptions(10000, 1000, 61)));
        text.Value = "日本語";
        using var stream = new MemoryStream(); stream.WriteByte(7); stream.Position = 0;
        Throws<EncoderFallbackException>(() => DxfR12Codec.Save(stream, new EntityObject[] { text }));
        Check(stream.Length == 1 && stream.Position == 0, "Unencodable text modified destination");
    }
    private static void R12TextEditing()
    {
        var text = R12TextSeed(3, 4); var source = new DxfDocument(); source.Entities.Add(text);
        string handle = text.Handle; var owner = text.Owner; string styleHandle = text.Style.Handle;
        var raw = DxfR12Codec.Create(new EntityObject[] { text });
        Check(text.Handle == handle && ReferenceEquals(owner, text.Owner) && text.Style.Handle == styleHandle, "Export reassigned document identities");
        var decoded = (Text)DxfR12Codec.ReadEntities(raw).Single(); var clone = (Text)decoded.Clone();
        clone.Value = "Edited"; clone.Style.Name = "EDITED"; clone.Style.WidthFactor = .75; clone.Rotation = 77;
        clone.Alignment = TextAlignment.BaselineRight; clone.Position += Vector3.UnitX;
        Check(decoded.Value != clone.Value && !ReferenceEquals(decoded.Style, clone.Style), "Clone aliases source state");
        R12TextCompare(clone, (Text)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new EntityObject[] { clone }), true)).Single());
    }
}
