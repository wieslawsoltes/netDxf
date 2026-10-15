using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Shadow.Studio;
using netDxf.Tables;

internal static class DimensionExportVerification
{
    private static void Require(bool value, string message = "Dimension export assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected)
    { Require(Math.Abs(actual - expected) < 1e-7, $"Expected {expected}, got {actual}"); }
    private static DxfDocument Read(byte[] bytes)
    { using var stream = new MemoryStream(bytes); return DxfDocument.Load(stream) ?? throw new InvalidOperationException("Invalid exported DXF"); }
    private static byte[] Export(DxfDocument document, bool binary = false, DxfDimensionPictureOptions? options = null)
    { using var stream = new MemoryStream(); document.SaveWithDimensionPictures(stream, binary, options); return stream.ToArray(); }
    private static IEnumerable<Dimension> Dimensions(DxfDocument document) => document.Blocks.SelectMany(b => b.Entities).OfType<Dimension>();
    private static Dimension[] Families() =>
    [
        new AlignedDimension(new(0, 0), new(40, 0), 10),
        new LinearDimension(new(0, 0), new(40, 5), 12, 0),
        new Angular2LineDimension(new(0, 0), new(40, 0), new(0, 0), new(30, 30), 15),
        new Angular3PointDimension(new(0, 0), new(40, 0), new(30, 30), 15),
        new RadialDimension(new Vector2(0, 0), new Vector2(20, 10)),
        new DiametricDimension(new Vector2(0, 0), new Vector2(20, 10)),
        new OrdinateDimension(new Vector2(0, 0), new Vector2(30, 10), new Vector2(30, 30), OrdinateDimensionAxis.X, DimensionStyle.Default),
        new ArcLengthDimension(new Vector2(0, 0), 20, 0, 70, 25)
    ];
    private static string State(Dimension dimension) => JsonSerializer.Serialize(new
    {
        // Capture every publicly observable geometric value, including subtype-specific
        // dimension/arc line positions; Dimension.DefinitionPoint is intentionally internal.
        geometry = dimension.GetType().GetProperties()
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 &&
                (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(p => p.Name, p => p.GetValue(dimension)),
        dimension.TextReferencePoint, dimension.TextPositionManuallySet,
        dimension.Normal, dimension.Elevation, dimension.Measurement, block = dimension.Block?.Name
    });
    private static void Refuse(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidOperationException or ArgumentException or OperationCanceledException or IOException or NotSupportedException) { return; }
        throw new InvalidOperationException("Expected dimension export refusal");
    }
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var binary in new[] { false, true }) foreach (var prototype in Families())
        {
            tests.Add(($"cad/export picture {prototype.GetType().Name} {(binary ? "binary" : "text")}", () =>
            {
                var document = new DxfDocument(); var dimension = (Dimension)prototype.Clone(); document.Entities.Add(dimension);
                string original = State(dimension); int blocks = document.Blocks.Count;
                var exported = Read(Export(document, binary)); var actual = exported.Entities.Dimensions.Single();
                Require(actual.GetType() == dimension.GetType()); Require(actual.Block is { Entities.Count: > 0 });
                Near(actual.Measurement, dimension.Measurement); Require(State(dimension) == original);
                Require(document.Blocks.Count == blocks + 1, "Only the ordinary default Layout1 block may be registered");
                Require(document.Blocks.All(b => !b.Name.StartsWith("*D", StringComparison.Ordinal)), "Temporary pictures leaked into the source");
                int exportedBlocks = exported.Blocks.Count; var block = actual.Block!; var originalEntities = block.Entities.ToArray();
                var again = Read(Export(exported, binary)); Require(again.Blocks.Count == exportedBlocks, "Repeated export accumulated picture blocks");
                Require(ReferenceEquals(actual.Block, block)); Require(block.Entities.SequenceEqual(originalEntities), "Original picture entities were not restored");
                Require(again.Entities.Dimensions.Single().Block is { Entities.Count: > 0 });
            }));
        }
        tests.Add(("cad/export derives dimension headers from edited public values without mutating source", () =>
        {
            var document = new DxfDocument(); var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10); document.Entities.Add(dimension);
            dimension.SecondReferencePoint = new(80, 0); dimension.Offset = 30;
            string before = State(dimension); var actual = (AlignedDimension)Read(Export(document)).Entities.Dimensions.Single();
            Near(actual.Measurement, 80); Near(actual.Offset, 30); Near(actual.DimLinePosition.X, 80); Near(actual.DimLinePosition.Y, 30);
            Require(State(dimension) == before, "Export normalized a live dimension outside editor history");
        }));
        tests.Add(("cad/export regenerates previously exported pictures after semantic edits", () =>
        {
            var document = new DxfDocument(); document.Entities.Add(new AlignedDimension(new(0, 0), new(40, 0), 10));
            var loaded = Read(Export(document)); var dimension = (AlignedDimension)loaded.Entities.Dimensions.Single();
            var original = dimension.Block.Entities.ToArray(); string oldText = string.Join("|", dimension.Block.Entities.OfType<MText>().Select(t => t.Value));
            dimension.SecondReferencePoint = new(90, 0); dimension.Offset = 25; string before = State(dimension);
            var actual = (AlignedDimension)Read(Export(loaded)).Entities.Dimensions.Single();
            Near(actual.Measurement, 90); Near(actual.Offset, 25);
            Require(string.Join("|", actual.Block.Entities.OfType<MText>().Select(t => t.Value)) != oldText, "Stale measurement remained in the picture");
            Require(State(dimension) == before && dimension.Block.Entities.SequenceEqual(original));
        }));
        tests.Add(("cad/export preserves imported custom pictures and common XData", () =>
        {
            var document = new DxfDocument(); var custom = new Block("CUSTOM"); custom.Entities.Add(new Circle(Vector3.Zero, 17));
            var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10) { Block = custom };
            var data = new XData(new ApplicationRegistry("CAD_USER_DATA")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "retain-me")); dimension.XData.Add(data);
            document.Entities.Add(dimension); var actual = Read(Export(document)).Entities.Dimensions.Single();
            Require(actual.Block.Entities.Single() is Circle circle && Math.Abs(circle.Radius - 17) < 1e-7);
            Require(actual.XData["CAD_USER_DATA"].XDataRecord.Single().Value.Equals("retain-me"));
            Require(ReferenceEquals(dimension.Block, custom) && custom.Entities.Single() is Circle);
        }));
        tests.Add(("cad/export prepares pictures in named block and paper-space owner context", () =>
        {
            var document = new DxfDocument(); var named = new Block("NESTED"); var nested = new AlignedDimension(new(0, 0), new(40, 0), 10);
            named.Entities.Add(nested); document.Blocks.Add(named); document.Entities.Add(new Insert(named, Vector3.Zero));
            var layout = new Layout("Sheet"); document.Layouts.Add(layout);
            var paper = new LinearDimension(new(0, 0), new(40, 0), 10, 0) { Style = new DimensionStyle("Paper scale") { DimScaleLinear = -2 } };
            layout.AssociatedBlock.Entities.Add(paper);
            var result = Read(Export(document)); Require(Dimensions(result).Count() == 2 && Dimensions(result).All(d => d.Block is { Entities.Count: > 0 }));
            var text = string.Join("|", result.Layouts["Sheet"].AssociatedBlock.Entities.OfType<Dimension>().Single().Block.Entities.OfType<MText>().Select(t => t.Value));
            Require(text.Contains("80"), "Paper owner context was lost in negative DIMLFAC generation");
        }));
        tests.Add(("cad/export budgets cancellation and output failure restore temporary pictures", () =>
        {
            var document = new DxfDocument(); document.Entities.Add(new AlignedDimension(new(0, 0), new(40, 0), 10));
            document.Entities.Add(new LinearDimension(new(0, 20), new(40, 20), 10, 0));
            Refuse(() => Export(document, options: new(maximumPictures: 1)));
            Refuse(() => Export(document, options: new(maximumPictureEntities: 1)));
            Refuse(() => Export(document, options: new(rawOptions: new(maximumBytes: 128))));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Refuse(() => document.SaveWithDimensionPictures(Stream.Null, cancellationToken: cancelled.Token));
            Refuse(() => document.SaveWithDimensionPictures(new FailingStream()));
            Require(Dimensions(document).All(d => d.Block is null)); Require(document.Blocks.All(b => !b.Name.StartsWith("*D", StringComparison.Ordinal)));
            Require(Read(Export(document)).Entities.Dimensions.Count() == 2);
        }));
        tests.Add(("cad/export preserves Studio dimension history across repeated saves", () =>
        {
            using var studio = new Studio(false);
            studio.CadCommand(new(studio.CadRevision, new("DIMALIGNED", [new(0, 0), new(40, 0), new(0, 10)])));
            Require(Read(studio.Save()).Entities.Dimensions.Single().Block is { Entities.Count: > 0 });
            studio.CadCommand(new(studio.CadRevision, new("MOVE", [new(0, 0), new(20, 20)])));
            Require(Read(studio.Save()).Entities.Dimensions.Single().Block is { Entities.Count: > 0 });
            studio.History("undo", studio.CadRevision); var returned = (AlignedDimension)Read(studio.Save()).Entities.Dimensions.Single();
            Near(returned.FirstReferencePoint.X, 0); Near(returned.FirstReferencePoint.Y, 0);
            studio.History("undo", studio.CadRevision); Require(!Read(studio.Save()).Entities.Dimensions.Any());
            studio.History("redo", studio.CadRevision); Require(Read(studio.Save()).Entities.Dimensions.Single().Block is { Entities.Count: > 0 });
        }));
    }
    private sealed class FailingStream : MemoryStream
    { public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Simulated destination failure"); }
}
