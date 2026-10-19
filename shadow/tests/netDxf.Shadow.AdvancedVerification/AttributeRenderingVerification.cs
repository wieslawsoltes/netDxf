using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Tables;
using Att = netDxf.Entities.Attribute;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class AttributeRenderingVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("attribute-render/" + name, run));
        foreach (var normal in new[] { Vector3.UnitZ, Vector3.UnitY, new Vector3(1, 2, 3) })
        foreach (bool directionLast in new[] { false, true })
        for (int attachment = 1; attachment <= 9; attachment++)
        {
            int a = attachment;
            Test($"native text conversion matches {normal}/{directionLast}/{a}", () =>
            {
                var (doc, attribute) = Seed(); var data = attribute.GetTextData();
                data.Content.Normal = normal; data.Content.AttachmentPoint = (MTextAttachmentPoint)a;
                // Setters retain original field position, so build ordered records explicitly.
                var fields = data.Content.GetStoredFields().Where(t => t.Code is not (11 or 21 or 31 or 50)).ToList();
                var direction = new[] { new netDxf.IO.DxfTag(11, 1.0), new netDxf.IO.DxfTag(21, 2.0), new netDxf.IO.DxfTag(31, 3.0) };
                if (!directionLast) fields.AddRange(direction);
                fields.Add(new(50, 37.0)); if (directionLast) fields.AddRange(direction);
                data.Content = AttributeMText.FromStoredFields(data.Content.Value, data.Content.Style, fields); attribute.SetTextData(data);
                var expected = attribute.GetMText().ToMText(); doc.Entities.Add(expected);
                var source = new ShadowSession(doc); var projector = new StandardEntityProjector();
                Check(projector.TryProject(source.Current.Read(Id(attribute)), out var actual, out _), "Attribute projection failed");
                Check(projector.TryProject(source.Current.Read(Id(expected)), out var reference, out _), "Reference projection failed");
                Same(actual.Text!, reference.Text!);
            });
        }
        Test("embedded content and canonical style edits invalidate cached projections", () =>
        {
            var (doc, attribute) = Seed(); var source = new ShadowSession(doc); var compiler = new RenderCompiler();
            var first = compiler.Compile(source.Current); var data = attribute.GetTextData(); data.Content.Value = "second visible value";
            attribute.SetTextData(data); var second = compiler.Compile(source.Refresh());
            Check(first.Items.Single().Text!.Content.Contains("Visible") && second.Items.Single().Text!.Content == "second visible value", "Content edit reused stale geometry");
            Check(!ReferenceEquals(first.Items.Single(), second.Items.Single()), "Changed packet did not invalidate the occurrence");
            Check(ReferenceEquals(second, compiler.Compile(source.Current)), "Unchanged packet did not reuse compiled scene");
            var previous = new CadSceneBuilder().Build(source.Current);
            doc.TextStyles["Embedded"].FontFile = "changed.shx";
            var updated = compiler.Compile(source.Refresh());
            Check(updated.Diagnostics.Any(d => d.Message.Contains("SHX")), "Embedded style dependency was not observed");
            Check(attribute.Value == "FALLBACK MUST NOT RENDER", "Rendering changed fallback text");
        });
        Test("SHX rendering uses the embedded font rather than the fallback font", () =>
        {
            var (doc, attribute) = Seed(); var data = attribute.GetTextData();
            data.Content.Value = "AA"; data.Content.Style = new TextStyle("Stroke", "embedded.shx"); attribute.SetTextData(data);
            var fonts = new ShxFontCatalog(); fonts.Register("embedded.shx", ShxFontReader.ReadAscii("*0,4,Test\n10,2,0,0\n*65,0,A\n9,0,10,6,0,0,-10,0,0,0"));
            var source = new ShadowSession(doc); var projector = new ShxEntityProjector(fonts);
            Check(projector.TryProject(source.Current.Read(Id(attribute)), out var projected, out _), "Embedded SHX font was not selected");
            Check(projected.Strokes.Length != 0 && projected.Text is null, "Embedded SHX text was not stroked");
            var expected = attribute.GetMText().ToMText(); doc.Entities.Add(expected); source.Refresh();
            Check(projector.TryProject(source.Current.Read(Id(expected)), out var reference, out _), "Reference SHX projection failed");
            var actualCurves = projected.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            var expectedCurves = reference.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            Check(actualCurves.Length == expectedCurves.Length, "SHX geometry differs from native MTEXT conversion");
            for (int i = 0; i < actualCurves.Length; i++) { Near(actualCurves[i].Start, expectedCurves[i].Start); Near(actualCurves[i].End, expectedCurves[i].End); }
        });
        Test("native PNG and SVG show multiline content without fallback text", () =>
        {
            var (doc, attribute) = Seed(); using var metrics = new SkiaTextLayoutEngine();
            var scene = new RenderCompiler(textLayout: metrics).Compile(new ShadowSession(doc).Current);
            var frame = new RenderFrame(scene, Camera.Fit(scene.Bounds.Inflate(2), 800, 300));
            using var renderer = new SkiaRenderBackend(textLayout: metrics); var png = renderer.Render(frame);
            using var svgRenderer = new SvgRenderBackend(); var svg = svgRenderer.Render(frame);
            var text = Encoding.UTF8.GetString(svg.Bytes);
            Check(text.Contains("Visible") && text.Contains("second") && !text.Contains("FALLBACK MUST NOT RENDER"), "SVG rendered fallback instead of embedded text");
            Check(png.Bytes.Length > 1000, "PNG is empty");
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/embedded-attribute.png", png.Bytes); File.WriteAllBytes("artifacts/embedded-attribute.svg", svg.Bytes);
        });
        Test("legacy incomplete capture refuses misleading fallback projection", () =>
        {
            var (doc, attribute) = Seed(); var snapshot = new ShadowSession(doc).Current;
            var node = snapshot.Objects[Id(attribute)];
            var legacy = snapshot with { Objects = snapshot.Objects.SetItem(node.Id, node with { Properties = node.Properties.Remove("TextData") }) };
            try { new StandardEntityProjector().TryProject(legacy.Read(Id(attribute)), out _, out _); }
            catch (NotSupportedException) { return; }
            throw new Exception("Missing embedded packet silently rendered fallback text");
        });
        Test("packet removal previews fallback before native derived metadata reconciles", () =>
        {
            var (doc, attribute) = Seed(); var snapshot = new ShadowSession(doc).Current;
            var edit = new ShadowGraphEdit(snapshot); edit.Set(Id(attribute), "TextData", ShadowValue.Null);
            var text = CadTextProjection.Read(edit.Build().Apply(snapshot).Read(Id(attribute))).Text;
            Check(!text.Multiline && text.Content == attribute.Value, "Detached packet removal retained stale embedded metadata");
        });
    }
    private static (DxfDocument, Att) Seed()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var block = new Block("TEXT");
        var definition = new AttributeDefinition("VALUE") { Value = "FALLBACK MUST NOT RENDER", Height = 1, Rotation = 78, WidthFactor = 4, IsBackward = true };
        block.AttributeDefinitions.Add(definition); var insert = new Insert(block); doc.Entities.Add(insert); var attribute = insert.Attributes.Single();
        attribute.SetMText(new AttributeMText { Value = "Visible {\\C1;red}\\Psecond Ω", Style = new TextStyle("Embedded", "DejaVuSans.ttf"),
            Position = new Vector3(10, 20, 0), Height = 4, RectangleWidth = 60, Rotation = 15, LineSpacingFactor = 1.2, LineSpacingStyle = MTextLineSpacingStyle.Exact });
        return (doc, attribute);
    }
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Same(CadText actual, CadText expected)
    {
        Check(actual.Content == expected.Content && actual.FontFamily == expected.FontFamily && actual.Alignment == expected.Alignment
            && actual.Multiline == expected.Multiline && actual.Height == expected.Height && actual.Width == expected.Width
            && actual.LineSpacing == expected.LineSpacing, "Effective text differs from native MTEXT conversion");
        Near(actual.Origin, expected.Origin); Near(actual.XAxis, expected.XAxis); Near(actual.YAxis, expected.YAxis);
    }
    private static void Near(Vec3 a, Vec3 b) => Check(Vec3.Distance(a, b) < 1e-10, $"Text geometry differs: {a} / {b}");
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
