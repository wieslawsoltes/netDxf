using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf.Shadow.Studio;

internal static class GalleryVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("sample/appearance gallery, native metrics, structural history and save/reload", () =>
        {
            using var studio = new Studio();
            var initial = State(studio);
            var revision = initial.GetProperty("revision").GetInt64();
            Require(initial.GetProperty("layouts").EnumerateArray().Any(l => l.GetString() == "Appearance"));
            studio.SetLayout(new(revision, "Appearance"));
            var state = State(studio);
            Require(state.GetProperty("occurrences").GetInt32() >= 10);
            foreach (var diagnostic in state.GetProperty("diagnostics").EnumerateArray())
            {
                Require(diagnostic.GetProperty("severity").GetString() != "Error", diagnostic.ToString());
                Require(diagnostic.GetProperty("code").GetString() != "text.metrics", "Studio must use native font metrics.");
            }
            var png = studio.Render(1300, 780, null, null, null, "skia", null, default);
            var svg = studio.Render(1300, 780, null, null, null, "svg", null, default);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
            Require(xml.Descendants().Any(e => e.Name.LocalName == "image"));
            Require(xml.Descendants().Any(e => e.Name.LocalName == "linearGradient"));
            Require(xml.Descendants().Any(e => e.Attribute("font-weight")?.Value == "700"));
            _ = JsonSerializer.Serialize(studio.Graph(default));
            studio.Retain(revision);
            var count = state.GetProperty("occurrences").GetInt32();
            studio.Add(new(revision, "line", 240, 100));
            var added = State(studio); Require(added.GetProperty("occurrences").GetInt32() == count + 1);
            studio.History("undo", added.GetProperty("revision").GetInt64());
            Require(State(studio).GetProperty("occurrences").GetInt32() == count);
            Require(studio.Render(1300, 780, null, null, null, "skia", revision, default).Bytes.SequenceEqual(png.Bytes));
            var dxf = studio.Save(); studio.Load(new MemoryStream(dxf));
            studio.SetLayout(new(State(studio).GetProperty("revision").GetInt64(), "Appearance"));
            Require(State(studio).GetProperty("occurrences").GetInt32() == count);
            Require(studio.Render(1300, 780, null, null, null, "skia", null, default).Bytes.Length > 1000);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/appearance-gallery.png", png.Bytes);
            File.WriteAllBytes("artifacts/appearance-gallery.svg", svg.Bytes);
            File.WriteAllBytes("artifacts/appearance-gallery.dxf", dxf);
        }));
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Require(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }
}
