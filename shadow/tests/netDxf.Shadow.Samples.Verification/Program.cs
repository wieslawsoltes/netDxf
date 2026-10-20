using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Shadow.Samples;
using netDxf.Shadow.Pid;
using netDxf.Shadow.Symbols;
using Viewport = netDxf.Shadow.Rendering.Viewport;

var checks = 0;
var document = ProcessDiagramFactory.Create();
Verify(document);
using var stream = new MemoryStream();
Require(document.Save(stream), "The native P&ID exports as DXF.");
stream.Position = 0;
var reopened = DxfDocument.Load(stream) ?? throw new InvalidOperationException("The exported P&ID must load.");
Verify(reopened);
Require(reopened.Entities.All.Count() == document.Entities.All.Count(), "DXF round trip preserves every model entity.");

// The same factory remains usable independently of the Studio and its gallery helper.
var gallery = StudioSamples.CreateDocument();
Require(gallery.Layouts.Count >= 8, "Studio keeps its feature gallery layouts.");
Require(gallery.Entities.Inserts.Count(i => i.Block.Name.StartsWith("PID_", StringComparison.Ordinal)) == 10,
    "Studio opens the P&ID in model space.");

if (args.Length == 2 && args[0] == "--render")
{
    var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using var fonts = new SkiaTextLayoutEngine();
    var render = new RenderCompiler(textLayout: fonts).Compile(new ShadowSession(reopened).Current);
    using var backend = new SkiaRenderBackend(textLayout: fonts);
    File.WriteAllBytes(output, backend.Render(new(render, Viewport.Fit(render.Bounds, 2000, 1200, .025))).Bytes);
    Console.WriteLine("Rendered " + output);
}
else if (args.Length != 0) throw new ArgumentException("Usage: [--render output.png]");
Console.WriteLine($"{checks} sample verification checks passed.");
return;

void Verify(DxfDocument drawing)
{
    var analysis = new PidModule().Analyze(new ShadowSession(drawing).Current);
    Require(analysis.Complete && analysis.Records.Length == 26, "P&ID module reads exactly 26 authored network roots.");
    Require(analysis.Records.Count(r => r.Class == "pipe") == 12 && analysis.Records.Count(r => r.Class == "signal") == 4,
        "Arrow strokes, legend and symbol outlines do not become process-network segments.");
    Require(analysis.Issues.IsEmpty, "Default P&ID has complete consistent explicit references: " + string.Join("; ", analysis.Issues.Select(i => i.Code + ": " + i.Message)));
    Require(drawing.Entities.Inserts.All(i => DomainMetadata.Read(i)?.SymbolVersion == "1.1.0"), "Every sample component retains its explicit-passage catalog version.");
    var inserts = drawing.Entities.Inserts.ToArray();
    var tags = inserts.Select(i => i.Attributes.AttributeWithTag("TAG").Value).ToArray();
    Require(tags.Length == tags.Distinct(StringComparer.Ordinal).Count(), "Equipment and PCE tags are unique.");
    Require(new[] { "T-101", "P-101", "V-101", "V-102", "V-103", "V-104", "NRV-101", "LI-101", "PI-102", "FI-103" }.Order()
        .SequenceEqual(tags.Order()), "Every equipment, isolation, non-return and PCE function is identified.");
    Require(inserts.All(i => i.Attributes.AttributeWithTag("DESCRIPTION").Value.Length > 0 &&
        i.Attributes.AttributeWithTag("BASIS").Value.Length > 0), "Native blocks carry inspectable purpose and drawing basis.");
    var pce = inserts.Where(i => i.Block.Name == "PID_PCE_Request").ToArray();
    Require(pce.Length == 3 && pce.All(i => i.Block.Entities.OfType<Ellipse>().Count() == 1), "PCE requests use distinct functional ovals.");
    Require(pce.Select(i => i.Attributes.AttributeWithTag("FUNCTION").Value).Order().SequenceEqual(new[] { "FI", "LI", "PI" }),
        "Level, pressure and flow indication are separate functions.");
    var pipes = drawing.Entities.Lines.Where(l => l.Layer.Name == "Process piping").ToArray();
    foreach (var item in inserts.Where(i => i.Block.Name is "PID_LiquidPump" or "PID_ManualValve" or "PID_CheckValve"))
        Require(!pipes.Any(l => InteriorContains(l, item.Position)), $"Process pipe does not run through {item.Attributes.AttributeWithTag("TAG").Value}.");
    var signals = drawing.Entities.Lines.Where(l => l.Layer.Name == "PCE associations").ToArray();
    Require(signals.Length >= 4 && signals.All(l => l.Layer.Linetype.Segments.Any(s => s.Length < 0)),
        "PCE associations have real DXF dashed linetypes.");
    Require(pipes.All(l => l.Layer.Linetype.Name == "Continuous"), "Physical process pipe remains continuous.");
    using var fonts = new SkiaTextLayoutEngine();
    var scene = new RenderCompiler(textLayout: fonts).Compile(new ShadowSession(drawing).Current);
    Require(scene.Diagnostics.IsEmpty, "The complete native drawing projects without diagnostics: " + string.Join("; ", scene.Diagnostics));
    var text = scene.Items.Where(i => i.Text is not null).Select(i => i.Text!.Content).ToArray();
    Require(text.Contains("T-101") && text.Contains("P-101") && text.Contains("PI") && text.Contains("102"), "Visible tags project as editable attribute text.");
    Require(!text.Contains("Liquid transfer pump") && !text.Any(t => t.Contains("/ PCE request", StringComparison.Ordinal)),
        "Hidden metadata does not leak into drawing text.");
    Require(scene.Bounds.MinX >= 9.9 && scene.Bounds.MaxX <= 410.1 && scene.Bounds.MinY >= 9.9 && scene.Bounds.MaxY <= 242.1,
        "The drawing and labels stay inside the sheet frame.");
    var visibleLabels = scene.Items.Where(i => i.Text is not null && i.Style.Visible).ToArray();
    for (var a = 0; a < visibleLabels.Length; a++)
        for (var b = a + 1; b < visibleLabels.Length; b++)
        {
            var left = visibleLabels[a]; var right = visibleLabels[b];
            var width = Math.Min(left.Bounds.MaxX, right.Bounds.MaxX) - Math.Max(left.Bounds.MinX, right.Bounds.MinX);
            var height = Math.Min(left.Bounds.MaxY, right.Bounds.MaxY) - Math.Max(left.Bounds.MinY, right.Bounds.MinY);
            Require(width <= .01 || height <= .01, $"Labels must not overlap: '{left.Text!.Content}' and '{right.Text!.Content}'.");
        }
}
void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
static bool InteriorContains(Line line, Vector3 point)
{
    var d = line.EndPoint - line.StartPoint; var p = point - line.StartPoint;
    var length2 = Vector3.DotProduct(d, d); if (length2 <= 0) return false;
    var t = Vector3.DotProduct(p, d) / length2;
    return t > .0001 && t < .9999 && Vector3.Distance(line.StartPoint + d * t, point) < .0001;
}
