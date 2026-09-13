using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly (short Code, object Value)[] CompleteBackground =
    {
        (431, "Book$Zażółć 東京"), (441, unchecked((int)0x8100007F)), (421, 0x123456),
        (45, 2.25), (63, (short)3), (90, 3)
    };

    private static void RegisterMTextBackgroundTests()
    {
        Run("mtext/background/model", MTextBackgroundModel);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"mtext/background/read-reordered/{v}/{b}", () => MTextBackgroundRead(v, b, false));
                Run($"mtext/background/read-reversed/{v}/{b}", () => MTextBackgroundRead(v, b, true));
                Run($"mtext/background/absent/{v}/{b}", () => MTextBackgroundAbsent(v, b));
                Run($"mtext/background/promote/{v}/{b}", () => MTextBackgroundPromote(v, b));
                Run($"mtext/background/writer-gates/{v}/{b}", () => MTextBackgroundGate(v, b));
                foreach (int flags in new[] { 0, 1, 2, 3, 16, 17, 18, 19 })
                {
                    int f = flags;
                    Run($"mtext/background/flags/{v}/{b}/{f}", () => MTextBackgroundFlags(v, b, f));
                }
                foreach (int scenario in Enumerable.Range(0, 7))
                {
                    int c = scenario;
                    Run($"mtext/background/invalid/{v}/{b}/{c}", () => MTextBackgroundInvalid(v, b, c));
                }
                if (version >= DxfVersion.AutoCad2007)
                {
                    Run($"mtext/background/authored-roundtrip/{v}/{b}", () => MTextBackgroundAuthoredRoundTrip(v, b));
                    Run($"mtext/background/model-roundtrip/{v}/{b}", () => MTextBackgroundModelRoundTrip(v, b));
                    Run($"mtext/background/edit-clone-block/{v}/{b}", () => MTextBackgroundEdit(v, b));
                }
            }
    }

    private static void MTextBackgroundModel()
    {
        Check(new MText().BackgroundFill == null, "New MTEXT invented a background.");
        var bg = new MTextBackgroundFill();
        Equal(MTextBackgroundFillFlags.UseColor, bg.Flags, "default flags");
        Equal((double?)1.5, bg.ScaleFactor, "default scale"); Equal((short?)7, bg.ColorIndex, "default ACI");
        Check(bg.TrueColor == null && bg.ColorName == null && bg.Transparency == null, "Default created optional fields.");
        foreach (int flags in new[] { -1, 4, 8, 20, 32, int.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => bg.Flags = (MTextBackgroundFillFlags)flags);
        foreach (double bad in new[] { -1.0, 0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => bg.ScaleFactor = bad);
        foreach (short bad in new short[] { -1, 257, short.MinValue, short.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => bg.ColorIndex = bad);
        foreach (string bad in new[] { "a\0b", "a\nb", "a\rb" })
            Throws<ArgumentException>(() => bg.ColorName = bad);
        Equal(MTextBackgroundFillFlags.UseColor, bg.Flags, "Rejected flags modified the object.");
        Equal((double?)1.5, bg.ScaleFactor, "Rejected scale modified the object.");
        Equal((short?)7, bg.ColorIndex, "Rejected ACI modified the object.");
        Check(bg.TrueColor == null && bg.ColorName == null, "Rejected color changed the object.");
        foreach (short index in new short[] { 0, 1, 255, 256 }) bg.ColorIndex = index;
        foreach (int rgb in new[] { int.MinValue, -1, 0, 1, int.MaxValue, 0xFFFFFF }) bg.TrueColor = rgb;
        bg.ScaleFactor = null; bg.ColorIndex = null; bg.ColorName = ""; bg.Transparency = int.MinValue;
        var copy = (MTextBackgroundFill)bg.Clone(); EqualBackground(bg, copy);
        copy.TrueColor = 7; copy.ColorName = "changed"; copy.Transparency = int.MaxValue;
        Equal((int?)0xFFFFFF, bg.TrueColor, "Clone changed source RGB"); Equal("", bg.ColorName, "Clone changed source name");
        Equal((int?)int.MinValue, bg.Transparency, "Clone changed source transparency");
        var aci = AciColor.FromTrueColor(0x112233);
        MTextBackgroundFill converted = MTextBackgroundFill.FromColor(aci, 2);
        Equal((int?)0x112233, converted.TrueColor, "True color conversion");
        short fallback = aci.Index; aci.Index = 3;
        Equal((short?)fallback, converted.ColorIndex, "Retained mutable source ACI");
        Equal((int?)0x112233, converted.TrueColor, "Retained mutable source RGB");
        Throws<ArgumentNullException>(() => MTextBackgroundFill.FromColor(null!));
        Equal((MTextBackgroundFillFlags)3, MTextBackgroundFill.FromDrawingWindow().Flags, "Window factory flags");
        MTextBackgroundFill frame = MTextBackgroundFill.CreateTextFrame();
        Equal(MTextBackgroundFillFlags.TextFrame, frame.Flags, "Frame factory flags");
        Check(frame.ScaleFactor == null && frame.ColorIndex == null, "Frame factory invented fill data.");
    }

    private static MemoryStream BackgroundFixture(DxfVersion version, bool binary, IEnumerable<(short Code, object Value)> tags)
    {
        var stream = new MemoryStream(); object writer = NewCodeWriter(stream, binary);
        void T(short code, object value) => Invoke(writer, "Write", code, value);
        string id = version switch { DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018",
            DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024", DxfVersion.AutoCad2013 => "AC1027", _ => "AC1032" };
        T(0, "SECTION"); T(2, "HEADER"); T(9, "$ACADVER"); T(1, id); T(9, "$HANDSEED"); T(5, "1000");
        T(9, "$DWGCODEPAGE"); T(3, "ANSI_1252"); T(0, "ENDSEC"); T(0, "SECTION"); T(2, "ENTITIES");
        T(0, "MTEXT"); T(5, "200"); T(100, "AcDbEntity"); T(8, "0"); T(62, (short)2); T(420, 0xABCDEF);
        T(100, "AcDbMText"); T(10, 1.0); T(20, 2.0); T(30, 3.0); T(40, 2.5); T(41, 10.0); T(1, "Background fixture");
        foreach (var (code, value) in tags)
        {
            // Old DXF transports carry non-ASCII text as explicit Unicode escapes.
            object v = value;
            if (version < DxfVersion.AutoCad2007 && value is string text)
                v = string.Concat(text.Select(c => c > 127 ? "\\U+" + ((int)c).ToString("X4") : c.ToString()));
            T(code, v);
            if (!binary) T(999, "interleaved comment");
        }
        T(72, (short)3); T(1001, "BACKGROUND_TEST"); T(1000, "after background");
        T(0, "ENDSEC"); T(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0; return stream;
    }

    private static MText ReadBackgroundFixture(DxfVersion v, bool b, IEnumerable<(short Code, object Value)> tags)
    {
        using var stream = BackgroundFixture(v, b, tags);
        DxfDocument doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Background fixture failed to load.");
        Check(stream.CanRead, "Reader closed caller stream.");
        MText text = doc.Entities.MTexts.Single();
        Equal("200", text.Handle, "Background changed entity handle");
        Equal(new Vector3(1, 2, 3), text.Position, "Background changed geometry");
        Equal(0xABCDEF, AciColor.ToTrueColor(text.Color) & 0xFFFFFF, "Background overwrote foreground RGB");
        Equal(MTextDrawingDirection.TopToBottom, text.DrawingDirection, "Background disrupted following field");
        Equal("after background", (string)text.XData["BACKGROUND_TEST"].XDataRecord[0].Value, "Background disrupted XData");
        return text;
    }

    private static MTextBackgroundFill ExpectedBackground(int flags = 3) => new()
    {
        Flags = (MTextBackgroundFillFlags)flags, ScaleFactor = 2.25, ColorIndex = 3,
        TrueColor = 0x123456, ColorName = "Book$Zażółć 東京", Transparency = unchecked((int)0x8100007F)
    };

    private static void MTextBackgroundRead(DxfVersion v, bool b, bool reverse)
    {
        MText text = ReadBackgroundFixture(v, b, reverse ? CompleteBackground.Reverse() : CompleteBackground);
        EqualBackground(ExpectedBackground(), text.BackgroundFill);
    }

    private static void EqualBackground(MTextBackgroundFill? expected, MTextBackgroundFill? actual)
    {
        if (expected == null) { Check(actual == null, "An absent background was invented."); return; }
        Check(actual != null, "Background data was discarded.");
        Equal(expected.Flags, actual!.Flags, "background flags"); Equal(expected.ScaleFactor, actual.ScaleFactor, "background scale");
        Equal(expected.ColorIndex, actual.ColorIndex, "background ACI fallback"); Equal(expected.TrueColor, actual.TrueColor, "background RGB");
        Equal(expected.ColorName, actual.ColorName, "background color name"); Equal(expected.Transparency, actual.Transparency, "background transparency bits");
    }

    private static void MTextBackgroundAbsent(DxfVersion v, bool b)
    {
        MText text = ReadBackgroundFixture(v, b, Array.Empty<(short, object)>());
        EqualBackground(null, text.BackgroundFill);
        var doc = new DxfDocument(v); doc.Entities.Add((MText)text.Clone());
        using var stream = new MemoryStream(); Check(doc.Save(stream, b), "Background-free document failed to save.");
        Check(!BackgroundTags(stream.ToArray(), b).Any(), "Writer invented background tags.");
    }

    private static void MTextBackgroundFlags(DxfVersion v, bool b, int f)
    {
        var text = ReadBackgroundFixture(v, b, new (short, object)[] { (90, f) });
        EqualBackground(new MTextBackgroundFill { Flags = (MTextBackgroundFillFlags)f, ScaleFactor = null, ColorIndex = null }, text.BackgroundFill);
        // Reading retains a field even when this conservative writer profile would reject its target version.
        if (v < DxfVersion.AutoCad2007 || ((f & 16) != 0 && v < DxfVersion.AutoCad2018)) return;
        var doc = new DxfDocument(v); doc.Entities.Add((MText)text.Clone());
        using var output = new MemoryStream(); Check(doc.Save(output, b), "Flag-only background failed to save.");
        var tags = BackgroundTags(output.ToArray(), b).ToArray();
        bool active = (f & 3) != 0;
        Equal(active ? 3 : 1, tags.Length, "Flag-only background tag count");
        Equal((short)90, tags[0].Code, "flags code"); Equal(f, (int)tags[0].Value, "flags value");
        if (active)
        {
            Equal(1.5, (double)tags.Single(t => t.Code == 45).Value, "Active fill scale fallback");
            Equal((short)7, (short)tags.Single(t => t.Code == 63).Value, "Active fill ACI fallback");
        }
        Check(text.BackgroundFill.ScaleFactor == null && text.BackgroundFill.ColorIndex == null, "Writer modified source presence.");
    }

    private static void MTextBackgroundInvalid(DxfVersion v, bool b, int scenario)
    {
        (short, object) tag = scenario switch { 0 => (90, 4), 1 => (90, -1), 2 => (45, 0.0), 3 => (45, -1.0),
            4 => (63, (short)(-1)), 5 => (63, (short)257), _ => (431, "bad\\U+0000name") };
        using var stream = BackgroundFixture(v, b, new[] { tag });
#if DEBUG
        Throws<System.IO.InvalidDataException>(() => DxfDocument.Load(stream));
#else
        Check(DxfDocument.Load(stream) == null, "Malformed background was silently accepted.");
#endif
        Check(stream.CanRead, "Malformed background closed caller stream.");
    }

    private static void MTextBackgroundGate(DxfVersion v, bool b)
    {
        foreach (bool nested in new[] { false, true })
            foreach (bool frame in new[] { false, true })
            {
                var doc = new DxfDocument(v);
                var text = new MText("gated", Vector3.Zero, 2) { BackgroundFill = frame ? MTextBackgroundFill.CreateTextFrame() : ExpectedBackground() };
                if (nested) { var block = new Block("Masked"); block.Entities.Add(text); doc.Entities.Add(new Insert(block)); }
                else doc.Entities.Add(text);
                bool allowed = v >= DxfVersion.AutoCad2007 && (!frame || v >= DxfVersion.AutoCad2018);
                using var stream = new MemoryStream(); byte[] prefix = { 10, 20, 30 }; stream.Write(prefix); long position = stream.Position;
                int layoutCount = doc.Layouts.Count;
                if (allowed) Check(doc.Save(stream, b), "Supported background version was rejected.");
                else
                {
#if DEBUG
                    Throws<NotSupportedException>(() => doc.Save(stream, b));
#else
                    Check(!doc.Save(stream, b), "Down-save silently lost background data.");
#endif
                    Equal(position, stream.Position, "Preflight changed stream position");
                    Check(prefix.SequenceEqual(stream.ToArray()), "Preflight wrote partial output.");
                    Equal(layoutCount, doc.Layouts.Count, "Preflight added layouts before rejecting the feature");
                }
                Check(stream.CanWrite, "Writer closed caller stream.");
            }
    }

    private static IEnumerable<(short Code, object Value)> BackgroundTags(byte[] bytes, bool binary)
    {
        using var stream = new MemoryStream(bytes); object reader = NewCodeReader(stream, binary);
        bool entity = false, subclass = false;
        while (true)
        {
            Invoke(reader, "Next"); short code = TagCode(reader);
            object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0) { if (Equals(value, "EOF")) yield break; entity = Equals(value, "MTEXT"); subclass = false; }
            if (entity && code == 100 && Equals(value, "AcDbMText")) subclass = true;
            if (entity && subclass && code is 45 or 63 or 90 or 421 or 431 or 441) yield return (code, value);
        }
    }

    private static void MTextBackgroundAuthoredRoundTrip(DxfVersion v, bool b)
    {
        using var input = BackgroundFixture(v, b, CompleteBackground);
        DxfDocument doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Authored MTEXT background failed.");
        using var output = new MemoryStream(); Check(doc.Save(output, !b), "Cross-transport background save failed.");
        var tags = BackgroundTags(output.ToArray(), !b).ToArray(); Equal(6, tags.Length, "Background tag count");
        foreach (var expected in CompleteBackground)
        {
            var actual = tags.Single(t => t.Code == expected.Code); Equal(expected.Value, actual.Value, "Exact emitted background value");
        }
        output.Position = 0; DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Background reload failed.");
        EqualBackground(ExpectedBackground(), loaded.Entities.MTexts.Single().BackgroundFill);
    }

    private static void MTextBackgroundPromote(DxfVersion v, bool b)
    {
        using var input = BackgroundFixture(v, b, CompleteBackground);
        DxfDocument doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Promotion source failed to load.");
        doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        using var output = new MemoryStream(); Check(doc.Save(output, !b), "Promoted background failed to save.");
        output.Position = 0;
        DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Promoted background failed to load.");
        EqualBackground(ExpectedBackground(), loaded.Entities.MTexts.Single().BackgroundFill);
    }

    private static void MTextBackgroundModelRoundTrip(DxfVersion v, bool b)
    {
        foreach (var bg in new[] { ExpectedBackground(0), ExpectedBackground(1), ExpectedBackground(2), ExpectedBackground(),
            new MTextBackgroundFill { ColorIndex = 256, TrueColor = 0, ColorName = "", Transparency = int.MaxValue },
            new MTextBackgroundFill { ColorIndex = 0, TrueColor = 0xFFFFFF, Transparency = int.MinValue },
            new MTextBackgroundFill { TrueColor = unchecked((int)0xC2123456), ColorName = null, Transparency = null } })
        {
            var doc = new DxfDocument(v); doc.Entities.Add(new MText("typed", Vector3.Zero, 2) { BackgroundFill = bg });
            using var stream = new MemoryStream(); Check(doc.Save(stream, b), "Typed background failed to save."); stream.Position = 0;
            DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Typed background failed to load.");
            EqualBackground(bg, loaded.Entities.MTexts.Single().BackgroundFill);
        }
    }

    private static void MTextBackgroundEdit(DxfVersion v, bool b)
    {
        var original = new MText("isolated", Vector3.Zero, 2) { BackgroundFill = ExpectedBackground() };
        var block = new Block("Masked"); block.Entities.Add(original);
        var insert = new Insert(block); var copy = (Insert)insert.Clone();
        MText child = copy.Block.Entities.OfType<MText>().Single();
        Check(!ReferenceEquals(child.BackgroundFill, original.BackgroundFill), "INSERT clone shares background object.");
        EqualBackground(original.BackgroundFill, child.BackgroundFill);
        child.BackgroundFill.ColorIndex = 1; child.BackgroundFill.TrueColor = 0x654321;
        Equal((int?)0x123456, original.BackgroundFill.TrueColor, "Clone edit changed source background");
        MText exploded = insert.Explode().OfType<MText>().Single();
        EqualBackground(original.BackgroundFill, exploded.BackgroundFill);
        Check(!ReferenceEquals(exploded.BackgroundFill, original.BackgroundFill), "Explode shares background object.");
        original.BackgroundFill = null;
        var doc = new DxfDocument(v); doc.Entities.Add(exploded); using var output = new MemoryStream();
        Check(doc.Save(output, b), "Exploded mask failed to save."); output.Position = 0;
        DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Exploded mask failed to reload.");
        EqualBackground(ExpectedBackground(), loaded.Entities.MTexts.Single().BackgroundFill);
        loaded.Entities.MTexts.Single().BackgroundFill = null; using var removed = new MemoryStream();
        Check(loaded.Save(removed, !b), "Removing the mask failed to save."); Check(!BackgroundTags(removed.ToArray(), !b).Any(), "Removed background returned.");
    }
}
