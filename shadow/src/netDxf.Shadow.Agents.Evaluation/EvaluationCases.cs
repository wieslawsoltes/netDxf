using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Graph;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;

namespace netDxf.Shadow.Agents.Evaluation;

/// <summary>Fixed native drawing tasks with independent geometric graders and negative controls.</summary>
public static class EvaluationCases
{
    public static IReadOnlyList<EvaluationCase> All { get; } =
    [
        new("create", "Create a layered mounting plate with batched geometry and a text label.", """
            Create exactly eleven entities in this empty drawing. Draw a 40 by 30 rectangle using four LINE
            entities with corners (0,0), (40,0), (40,30), (0,30), on a new layer named Outline. Add six CIRCLE
            entities of radius 2 on a new layer named Holes, centered at (10,10), (20,10), (30,10), (10,20),
            (20,20), (30,20). Add one TEXT entity saying Mounting plate at (0,35), height 3, on Outline.
            All coordinates have Z=0. Use batched atomic construction, sharing the same layer resources.
            Inspect the result and report what was created. Do not add extra entities.
            """, () => new(), CreatePlate, ValidatePlate),
        new("edit", "Edit six selected circles while preserving other geometry.", """
            In the existing drawing, update every CIRCLE on the layer Target: set Radius to 3 and Center.Y
            to 15. Keep each Center.X and Center.Z unchanged. Preserve the Reference-layer circle, the
            line, all layers, and the entity count. Inspect current IDs and properties, make the changes
            together in one atomic property-edit transaction, then verify the actual stored results.
            """, () => CreateEditFixture(false), () => CreateEditFixture(true), ValidateEdit),
        new("graph", "Create an intersecting network and verify topology and a shortest path.", """
            Create exactly six LINE entities at Z=0 on a shared new layer named Network: rectangle edges
            (0,0)-(20,0), (20,0)-(20,10), (20,10)-(0,10), (0,10)-(0,0), horizontal crossing
            (-5,5)-(25,5), and vertical crossing (10,-5)-(10,15). Use one batched construction transaction.
            Analyze geometric connectivity with graph_summary and splitIntersections=true, then use
            graph_nearest and graph_path for the shortest path from (-5,5) to (25,5).
            Write analysis.json in the workspace as a JSON object with these numeric fields copied from
            the tool results: nodeCount, edgeCount, componentCount, cycleRank, bridgeCount,
            articulationPointCount, deadEndCount, junctionCount, totalLength, shortestPathLength.
            Verify the result and do not add extra geometry.
            """, () => new(), CreateNetwork, ValidateNetwork),
    ];

    private static DxfDocument CreatePlate()
    {
        var drawing = new DxfDocument(); var outline = new Layer("Outline"); var holes = new Layer("Holes");
        foreach (var line in Rectangle(40, 30)) { line.Layer = outline; drawing.Entities.Add(line); }
        foreach (var x in new[] { 10d, 20d, 30d }) foreach (var y in new[] { 10d, 20d })
            drawing.Entities.Add(new Circle(new Vector3(x, y, 0), 2) { Layer = holes });
        drawing.Entities.Add(new Text("Mounting plate", new Vector3(0, 35, 0), 3) { Layer = outline });
        return drawing;
    }

    private static DxfDocument CreateEditFixture(bool edited)
    {
        var drawing = new DxfDocument(); var target = new Layer("Target"); var reference = new Layer("Reference");
        for (var i = 0; i < 6; i++) drawing.Entities.Add(new Circle(new Vector3(i * 10, edited ? 15 : 0, 0), edited ? 3 : 1.5) { Layer = target });
        drawing.Entities.Add(new Circle(new Vector3(100, 100, 0), 8) { Layer = reference });
        drawing.Entities.Add(new Line(new Vector3(-10, -10, 0), new Vector3(60, -10, 0)) { Layer = reference });
        return drawing;
    }

    private static DxfDocument CreateNetwork()
    {
        var drawing = new DxfDocument(); var network = new Layer("Network");
        foreach (var line in Rectangle(20, 10).Concat(new[]
        {
            new Line(new Vector3(-5, 5, 0), new Vector3(25, 5, 0)),
            new Line(new Vector3(10, -5, 0), new Vector3(10, 15, 0)),
        })) { line.Layer = network; drawing.Entities.Add(line); }
        return drawing;
    }

    private static IEnumerable<Line> Rectangle(double width, double height)
    {
        Vector3[] points = [new(0, 0, 0), new(width, 0, 0), new(width, height, 0), new(0, height, 0)];
        for (var i = 0; i < points.Length; i++) yield return new(points[i], points[(i + 1) % points.Length]);
    }

    private static bool Near(double a, double b) => double.IsFinite(a) && Math.Abs(a - b) <= 1e-8;
    private static bool Same(Vector3 a, Vector3 b) => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
    private static bool SameLine(Line a, Line b) => a.Layer.Name == b.Layer.Name &&
        (Same(a.StartPoint, b.StartPoint) && Same(a.EndPoint, b.EndPoint) || Same(a.StartPoint, b.EndPoint) && Same(a.EndPoint, b.StartPoint));
    private static bool LinesMatch(DxfDocument actual, DxfDocument expected)
    {
        var unmatched = actual.Entities.Lines.ToList();
        foreach (var line in expected.Entities.Lines)
        {
            var index = unmatched.FindIndex(candidate => SameLine(candidate, line));
            if (index < 0) return false;
            unmatched.RemoveAt(index);
        }
        return unmatched.Count == 0;
    }
    private static bool CirclesMatch(DxfDocument actual, DxfDocument expected)
    {
        var unmatched = actual.Entities.Circles.ToList();
        foreach (var circle in expected.Entities.Circles)
        {
            var index = unmatched.FindIndex(candidate => candidate.Layer.Name == circle.Layer.Name && Same(candidate.Center, circle.Center) && Near(candidate.Radius, circle.Radius));
            if (index < 0) return false;
            unmatched.RemoveAt(index);
        }
        return unmatched.Count == 0;
    }
    private static IEnumerable<EvaluationCheck> ValidatePlate(DxfDocument document, IAgentWorkspace workspace)
    {
        var expected = CreatePlate();
        yield return new("exactly eleven entities", document.Entities.All.Count() == 11);
        yield return new("four Outline lines have requested endpoints", LinesMatch(document, expected));
        yield return new("six Holes circles have requested centers and radii", CirclesMatch(document, expected));
        var texts = document.Entities.Texts.ToArray();
        yield return new("Outline text has requested content, position, and height", texts.Length == 1 && texts[0].Value == "Mounting plate" &&
            texts[0].Layer.Name == "Outline" && Same(texts[0].Position, new(0, 35, 0)) && Near(texts[0].Height, 3));
    }
    private static IEnumerable<EvaluationCheck> ValidateEdit(DxfDocument document, IAgentWorkspace workspace)
    {
        var expected = CreateEditFixture(true);
        yield return new("entity count remains eight", document.Entities.All.Count() == 8);
        yield return new("all Target circle updates and protected Reference circle match", CirclesMatch(document, expected));
        yield return new("Reference line is unchanged", LinesMatch(document, expected));
        yield return new("layer names remain unchanged", document.Layers.Select(layer => layer.Name).Order().SequenceEqual(expected.Layers.Select(layer => layer.Name).Order()));
    }
    private static IEnumerable<EvaluationCheck> ValidateNetwork(DxfDocument document, IAgentWorkspace workspace)
    {
        yield return new("exactly six requested Network lines", document.Entities.All.Count() == 6 && LinesMatch(document, CreateNetwork()));
        var graph = new TopologyBuilder().Build(new ShadowSession(document).Current);
        var expected = GraphNumbers();
        var actual = new Dictionary<string, double>
        {
            ["nodeCount"] = graph.Nodes.Length, ["edgeCount"] = graph.Edges.Length,
            ["componentCount"] = graph.ConnectedComponents().Length, ["cycleRank"] = graph.CycleRank,
            ["bridgeCount"] = graph.Bridges().Length, ["articulationPointCount"] = graph.ArticulationPoints().Length,
            ["deadEndCount"] = graph.DeadEnds.Count(), ["junctionCount"] = graph.Junctions.Count(),
            ["totalLength"] = graph.Edges.Sum(edge => edge.Length),
        };
        var start = graph.Nodes.FirstOrDefault(node => Near(node.Position.X, -5) && Near(node.Position.Y, 5));
        var end = graph.Nodes.FirstOrDefault(node => Near(node.Position.X, 25) && Near(node.Position.Y, 5));
        actual["shortestPathLength"] = start is not null && end is not null ? graph.ShortestPath(start.Id, end.Id)?.Length ?? -1 : -1;
        yield return new("native topology and shortest path match fixed geometric expectations", expected.All(pair => Near(actual[pair.Key], pair.Value)));
        JsonElement? analysis = null;
        try { analysis = McpJson.Parse(workspace.Read("analysis.json").Content); } catch (Exception error) when (error is IOException or JsonException) { }
        yield return new("agent recorded accurate graph analysis.json from tool results", analysis is { ValueKind: JsonValueKind.Object } report &&
            expected.All(pair => report.TryGetProperty(pair.Key, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out var value) && Near(value, pair.Value)));
    }
    private static Dictionary<string, double> GraphNumbers() => new()
    {
        ["nodeCount"] = 13, ["edgeCount"] = 16, ["componentCount"] = 1, ["cycleRank"] = 4,
        ["bridgeCount"] = 4, ["articulationPointCount"] = 4, ["deadEndCount"] = 4,
        ["junctionCount"] = 5, ["totalLength"] = 110, ["shortestPathLength"] = 30,
    };

    public static void SelfTest()
    {
        foreach (var scenario in All)
        {
            var workspace = new MemoryAgentWorkspace();
            if (scenario.Id == "graph") workspace.Write("analysis.json", JsonSerializer.Serialize(GraphNumbers()));
            var document = scenario.Expected();
            void Check(DxfDocument drawing)
            {
                var failed = scenario.Validate(drawing, workspace).Where(check => !check.Passed).ToArray();
                if (failed.Length > 0) throw new InvalidOperationException(scenario.Id + ": " + string.Join(", ", failed.Select(check => check.Name)));
            }
            Check(document);
            using var stream = new MemoryStream();
            if (!document.Save(stream)) throw new InvalidOperationException("Could not serialize reference fixture.");
            stream.Position = 0; Check(DxfDocument.Load(stream));
            if (scenario.Validate(scenario.Seed(), workspace).All(check => check.Passed)) throw new InvalidOperationException("Unmodified fixture was falsely accepted: " + scenario.Id);
            if (scenario.Id == "graph")
            {
                workspace.Write("analysis.json", "{}");
                if (scenario.Validate(document, workspace).All(check => check.Passed)) throw new InvalidOperationException("Missing graph evidence was falsely accepted.");
            }
        }
    }
}
