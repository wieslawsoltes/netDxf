using System.Globalization;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class MTextColorVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("text/color/all 255 indexed colors match the source palette", AllIndexedColors));
        tests.Add(("text/color/all indexed colors round-trip through the independent MTEXT writer", IndexedWriter));
        tests.Add(("text/color/true-color controls decode red from the low byte", TrueColorControls));
        tests.Add(("text/color/independently written unequal RGB channels round-trip", TrueColorWriter));
        tests.Add(("text/color/scopes and inherited paints remain isolated", ScopesAndInheritance));
        tests.Add(("text/color/malformed true colors remain literal without changing paint", MalformedColors));
        tests.Add(("text/color/save and reload retain authored formatting and snapshot paint", SaveReload));
        tests.Add(("text/color/native pixels and SVG consume the same decoded paints", BackendColors));
    }

    private static void AllIndexedColors()
    {
        for (var index = 1; index <= 255; index++)
        {
            var source = "\\C" + index.ToString(CultureInfo.InvariantCulture) + ";ink";
            var parsed = Parse(source);
            Require(parsed.Diagnostics.IsEmpty, $"ACI {index} produced a diagnostic.");
            Require(parsed.Inlines.Length == 1 && parsed.Inlines[0].Text == "ink");
            Require(parsed.Inlines[0].Style.Color == Argb(new AciColor((short)index)),
                $"ACI {index} does not match netDxf's captured palette.");
            Require(parsed.Inlines[0].SourceStart == source.Length - 3);
            Require(parsed.Inlines[0].SourceLength == 3);
        }
    }

    private static void IndexedWriter()
    {
        for (var index = 1; index <= 255; index++)
        {
            var color = new AciColor((short)index);
            var text = Written(color);
            var parsed = Parse(text.Value);
            Require(parsed.Diagnostics.IsEmpty, $"The writer's ACI {index} produced a diagnostic.");
            Require(parsed.Inlines.Single().Style.Color == Argb(color));
        }
    }

    private static void TrueColorControls()
    {
        // Literal independent fixtures: the integer is 0x00BBGGRR, not 0x00RRGGBB.
        foreach (var (encoded, expected) in new (int Encoded, uint Expected)[]
        {
            (0, 0xff000000u), (255, 0xffff0000u), (65280, 0xff00ff00u),
            (16711680, 0xff0000ffu), (16777215, 0xffffffffu),
            (0x563412, 0xff123456u), (0x010203, 0xff030201u),
        })
        {
            var parsed = Parse("\\c" + encoded.ToString(CultureInfo.InvariantCulture) + ";ink");
            Require(parsed.Diagnostics.IsEmpty);
            Require(parsed.Inlines.Single().Style.Color == expected,
                $"Packed MTEXT value {encoded} was decoded in the wrong channel order.");
        }
    }

    private static void TrueColorWriter()
    {
        var random = new Random(240);
        for (var index = 0; index < 128; index++)
        {
            // Avoid relying on the decoder's own packing operation as the oracle.
            var color = new AciColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            var text = Written(color);
            Require(text.Value.Contains("\\c", StringComparison.Ordinal), "Fixture must exercise the true-color writer.");
            var parsed = Parse(text.Value);
            Require(parsed.Diagnostics.IsEmpty);
            Require(parsed.Inlines.Single().Text == "ink");
            Require(parsed.Inlines.Single().Style.Color == Argb(color),
                $"Writer RGB ({color.R}, {color.G}, {color.B}) did not round-trip.");
        }
    }

    private static void ScopesAndInheritance()
    {
        var initial = new RichTextStyle("sans-serif", 10, Color: 0xff123456u);
        const string content = @"{\C42;a\c255;b}c\C0;d\C256;e";
        var parsed = RichText.Parse(content, true, initial);
        Require(parsed.Diagnostics.IsEmpty);
        Require(parsed.Inlines.Select(inline => inline.Text).SequenceEqual(new[] { "a", "b", "c", "d", "e" }));
        Require(parsed.Inlines.Select(inline => inline.Style.Color).SequenceEqual(new uint?[]
        {
            Argb(new AciColor(42)), 0xffff0000u, 0xff123456u, null, null,
        }));
        Require(initial.Color == 0xff123456u, "Parsing must not mutate the inherited style.");
        foreach (var inline in parsed.Inlines)
            Require(inline.SourceStart >= 0 && inline.SourceLength > 0
                && inline.SourceStart + inline.SourceLength <= content.Length);
        var separate = Parse(@"\C255;white");
        Require(separate.Inlines.Single().Style.Color == 0xffffffffu);
        Require(Parse("plain").Inlines.Single().Style.Color is null);
    }

    private static void MalformedColors()
    {
        foreach (var control in new[] { @"\c-1;", @"\c16777216;", @"\cNaN;", @"\c999999999999999999999;" })
        {
            var parsed = Parse(@"\C1;" + control + "ink");
            Require(parsed.Diagnostics.Any(issue => issue.Code == "text.format"));
            Require(string.Concat(parsed.Inlines.Select(inline => inline.Text)) == control + "ink");
            Require(parsed.Inlines.All(inline => inline.Style.Color == 0xffff0000u),
                "Malformed controls must not partially change the current paint.");
        }
        foreach (var value in new[] { -1, 257, int.MaxValue })
        {
            var parsed = Parse("\\C" + value.ToString(CultureInfo.InvariantCulture) + ";ink");
            Require(parsed.Diagnostics.Any(issue => issue.Code == "text.palette"));
            Require(parsed.Inlines.Single().Style.Color is null);
        }
        var unterminated = Parse(@"\c255");
        Require(unterminated.Diagnostics.Any(issue => issue.Code == "text.terminator"));
        Require(unterminated.Inlines.Single().Text == @"\c255");
    }

    private static void SaveReload()
    {
        var path = Path.Combine(Path.GetTempPath(), "shadow-mtext-color-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var color = new AciColor((byte)18, (byte)52, (byte)86);
            var text = Written(color);
            var document = new DxfDocument();
            document.Entities.Add(text);
            var snapshot = new ShadowSession(document).Current;
            Require(document.Save(path));
            var reloaded = DxfDocument.Load(path) ?? throw new InvalidOperationException("DXF reload failed.");
            var loadedText = reloaded.Entities.All.OfType<MText>().Single();
            Require(loadedText.Value == text.Value, "Color formatting must survive DXF serialization.");
            Require(Parse(loadedText.Value).Inlines.Single().Style.Color == Argb(color));
            text.Value = @"\c255;changed";
            var retainedText = snapshot.DrawingEntities().Single(entity => entity.Type == "MText")["Value"].Text;
            Require(Parse(retainedText).Inlines.Single().Style.Color == Argb(color),
                "Live source changes must not alter retained text color.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void BackendColors()
    {
        using var metrics = new SkiaTextLayoutEngine();
        var text = new CadText(@"\c255;RED \c65280;GREEN \c16711680;BLUE \C42;INDEX",
            Vec3.Zero, Vec3.UnitX, Vec3.UnitY, 10, "sans-serif", "TopLeft", true, 0);
        var scene = RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model",
            [new("colors", new("text:colors"), [], CadStyle.Default, new([], Text: text))], []), textLayout: metrics);
        var frame = new RenderFrame(scene, Camera.Fit(scene.Bounds.Inflate(2), 1200, 200)) { Background = 0xff000000u };
        using var svgRenderer = new SvgRenderBackend();
        using var nativeRenderer = new SkiaRenderBackend(textLayout: metrics);
        var svg = svgRenderer.Render(frame);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
        var fills = xml.Descendants().Select(element => element.Attribute("fill")?.Value).ToHashSet(StringComparer.Ordinal);
        Require(fills.Contains("#ff0000") && fills.Contains("#00ff00") && fills.Contains("#0000ff"));
        var palette = new AciColor(42);
        Require(fills.Contains($"#{palette.R:x2}{palette.G:x2}{palette.B:x2}"));
        var png = nativeRenderer.Render(frame);
        using var bitmap = SKBitmap.Decode(png.Bytes) ?? throw new InvalidOperationException("PNG decode failed.");
        var red = 0; var green = 0; var blue = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 160 && pixel.Green < 60 && pixel.Blue < 60) red++;
                if (pixel.Green > 160 && pixel.Red < 60 && pixel.Blue < 60) green++;
                if (pixel.Blue > 160 && pixel.Red < 60 && pixel.Green < 60) blue++;
            }
        Require(red > 10 && green > 10 && blue > 10, "Native rendering did not retain each decoded primary color.");
        Directory.CreateDirectory("artifacts");
        File.WriteAllBytes("artifacts/mtext-colors.png", png.Bytes);
        File.WriteAllBytes("artifacts/mtext-colors.svg", svg.Bytes);
    }

    private static MText Written(AciColor color)
    {
        var text = new MText(string.Empty, Vector2.Zero, 10);
        text.Write("ink", new MTextFormattingOptions { Color = color, FontName = "sans-serif" });
        return text;
    }

    private static RichTextDocument Parse(string value) => RichText.Parse(value, true, new("sans-serif", 10));
    private static uint Argb(AciColor color) => 0xff000000u | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    private static void Require(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
