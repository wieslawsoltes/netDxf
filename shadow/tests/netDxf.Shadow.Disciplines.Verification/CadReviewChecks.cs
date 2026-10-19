using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Electrical;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Piping;
using netDxf.Shadow.Symbols;

internal static class CadReviewChecks
{
    internal static int Run()
    {
        var checks = 0;
        var failures = new List<string>();
        var symbol = new SymbolDefinition("test.review", "1.0.0", "Review symbol", "", "valve", "Unitless", Vec3.Zero, [],
            [new("TAG", "Tag", Required: true)], [], [new(SymbolStrokeKind.Line, Vec3.Zero, Vec3.UnitX)]);
        var nativeBlock = SymbolNative.CreateBlock(symbol);
        var document = new DxfDocument();
        var session = new ShadowSession(document); var editor = new EditorSession(session);
        SymbolAuthoring.Place(session.Current, symbol, Vec3.Zero, "pid", "V-1").Execute(editor);
        var planned = document.Entities.Inserts.Single();
        Check(planned.Block.Entities.Single().Color.IsByBlock == nativeBlock.Entities.Single().Color.IsByBlock &&
            planned.Block.Entities.Single().Lineweight == nativeBlock.Entities.Single().Lineweight,
            "Immutable symbol geometry must preserve native ByBlock color and lineweight inheritance.");
        Check(planned.Block.AttributeDefinitions.Values.Single().Color.IsByBlock && planned.Attributes.Single().Color.IsByBlock,
            "Immutable symbol attribute definitions and instances must preserve ByBlock color inheritance.");
        editor.Undo(); editor.Redo();
        using (var stream = new MemoryStream())
        {
            document.Save(stream); stream.Position = 0;
            var reloaded = DxfDocument.Load(stream)!.Entities.Inserts.Single();
            Check(reloaded.Block.Entities.Single().Color.IsByBlock && reloaded.Attributes.Single().Color.IsByBlock,
                "Symbol inheritance must survive undo/redo and DXF roundtrip.");
        }
        foreach (var kind in new[] { "insert", "polyline", "join" })
        {
            var tagged = new DxfDocument(); EntityObject entity;
            if (kind == "insert")
            { var block = new Block("Tagged"); block.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX)); entity = new Insert(block); }
            else if (kind == "polyline") entity = new Polyline2D([Vector2.Zero, Vector2.UnitX, new Vector2(2, 1)]);
            else entity = new Line(Vector2.Zero, Vector2.UnitX);
            var data = new XData(new ApplicationRegistry("REVIEW_DATA")); data.XDataRecord.Add(new(XDataCode.String, "retained payload")); entity.XData.Add(data);
            tagged.Entities.Add(entity); if (kind == "join") tagged.Entities.Add(new Line(Vector2.UnitX, new Vector2(2, 0)));
            var source = new ShadowSession(tagged); var before = source.Current;
            var request = new DraftingCommand(kind == "join" ? "JOIN" : "EXPLODE", Entities: before.DrawingEntities().Select(e => e.Id!.Value).ToImmutableArray());
            var rejected = false;
            try { new DraftingPlanner().Plan(before, request); } catch (NotSupportedException) { rejected = true; }
            Check(rejected && ReferenceEquals(before, source.Current) && entity.XData["REVIEW_DATA"] == data,
                kind + " must reject implicit XDATA loss before source mutation.");
        }
        var piping = new DxfDocument();
        SymbolNative.Place(piping, PipingSymbols.Catalog.Resolve("piping.reducer"), Vec3.Zero, "piping", "R-1",
            new Dictionary<string, string> { ["inletSize"] = "50", ["outletSize"] = "50.0" });
        Check(new PipingModule().Analyze(new ShadowSession(piping).Current).Issues.Any(i => i.Code == "invalid-reducer-sizes"),
            "Numerically equal reducer sizes must not pass by using different text representations.");
        var electrical = new DxfDocument();
        var terminal = SymbolNative.Place(electrical, ElectricalSymbols.Catalog.Resolve("electrical.terminal"), Vec3.Zero, "electrical", "X-1",
            new Dictionary<string, string> { ["circuit"] = "C1" });
        var wire = new Line(new Vector2(7, 0), new Vector2(20, 0)); electrical.Entities.Add(wire);
        DomainMetadata.Write(wire, new("electrical", "wire", "review-wire", "W-1",
            Properties: ImmutableDictionary<string, string>.Empty.Add("circuit", "C2").Add("crossSection", "1.5"),
            Ports: [new("start", "electrical", new(7, 0)), new("end", "electrical", new(20, 0))],
            References: [new("start", DomainMetadata.Read(terminal)!.Key, "2")]));
        var electricSource = new ShadowSession(electrical);
        var wireId = electricSource.Current.DrawingEntities().Single(e => e.Type == "Line").Id!.Value;
        var selectedAnalysis = new ElectricalModule().Analyze(electricSource.Current, options: new(Entities: [wireId]));
        Check(selectedAnalysis.Records.Length == 1 && selectedAnalysis.Issues.Any(i => i.Code == "circuit-mismatch"),
            "Selected-object analysis must still validate relations to unselected components.");
        var routeDocument = new DxfDocument(); var routeSource = new ShadowSession(routeDocument); var routeEditor = new EditorSession(routeSource);
        var route = DomainPlans.Route(routeSource.Current, "piping", "pipe", [Vec3.Zero, Vec3.UnitX, new(2, 0)], "P-1",
            new Dictionary<string, string> { ["spec"] = "CS", ["nominalSize"] = "50" });
        route.Execute(routeEditor);
        DomainPlans.Update(routeSource.Current, [route.Created[1]], new Dictionary<string, string> { ["spec"] = "SS" }).Execute(routeEditor);
        var selectedRoute = new PipingModule().Analyze(routeSource.Current, options: new(Entities: [route.Created[0]]));
        Check(selectedRoute.Records.Length == 1 && selectedRoute.Issues.Any(i => i.Code == "inconsistent-route-spec"),
            "Selected route segments must still compare unselected siblings in the same route.");
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        return checks;
        void Check(bool condition, string message) { checks++; if (!condition) failures.Add(message); }
    }
}
