using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Exchange.Dwg;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Rendering.SkiaSharp;
using Viewport = netDxf.Shadow.Rendering.Viewport;

internal static class Program
{
    private static int Main()
    {
        try
        {
            (string Name, Action Run)[] scenarios =
            [
                ("capture-sync", Capture),
                ("geometry-topology", Geometry),
                ("editor-drafting", Editing),
                ("dimension-projection", Dimensions),
                ("shx-program", Shx),
                ("vector-render", Svg),
                ("native-text-raster", NativeRendering),
                ("dwg-exchange", Dwg),
                ("mcp-tools", Mcp),
                ("agent-workspace", Agents)
            ];
            foreach (var scenario in scenarios)
            {
                scenario.Run();
                Console.WriteLine($"PASS {scenario.Name}");
            }
            Type[] entryPoints =
            [
                typeof(DxfDocument), typeof(ShadowSnapshot), typeof(ShadowSession), typeof(CadSceneBuilder),
                typeof(RenderCompiler), typeof(TopologyBuilder), typeof(EditorSession), typeof(DimensionEntityProjector),
                typeof(DraftingPlanner), typeof(ShxFontReader), typeof(SkiaRenderBackend), typeof(DrawingExchange),
                typeof(McpToolRegistry), typeof(AgentSession)
            ];
            var assemblies = entryPoints.Select(type =>
            {
                var assembly = type.Assembly;
                var id = assembly.GetName().Name!;
                var location = Path.GetFullPath(assembly.Location);
                Require(location == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, id + ".dll")),
                    $"{id} loaded outside the installed consumer output.");
                return new
                {
                    id,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location))).ToLowerInvariant(),
                    framework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
                    informational_version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                };
            }).ToArray();
            Require(assemblies.Select(value => value.id).Distinct().Count() == 14, "Missing package assembly.");
            var receipt = new
            {
                schema = 1, passed = true,
                version = RequiredEnvironment("NETDXF_SHADOW_VERSION"),
                commit = RequiredEnvironment("NETDXF_SHADOW_COMMIT"),
                tree = RequiredEnvironment("NETDXF_SHADOW_TREE"),
                host = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Darwin",
                runtime_version = Environment.Version.ToString(),
                scenarios = scenarios.Select(value => value.Name).ToArray(), assemblies
            };
            File.WriteAllText(RequiredEnvironment("NETDXF_SHADOW_RECEIPT"), JsonSerializer.Serialize(receipt));
            Console.WriteLine($"PASS installed Shadow consumer: {scenarios.Length} scenarios, {assemblies.Length} package assemblies.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name)
        is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing {name}.");
    private static void Mcp()
    {
        var fixture = Fixture(); var editor = new EditorSession(fixture.Session);
        var tools = new McpToolRegistry(); ShadowMcpTools.Register(tools, new(() => editor, () => fixture.Session));
        var result = tools.ExecuteAsync("shadow_state", McpJson.EmptyObject).AsTask().GetAwaiter().GetResult();
        Require(!result.IsError && result.Text.Contains(fixture.Session.Current.DocumentId.ToString(), StringComparison.Ordinal), "Installed MCP could not inspect the native source snapshot.");
        Require(ShadowMcpServer.CreateOptions(tools).Handlers.CallToolHandler is not null, "Official MCP handlers were not available.");
    }
    private static void Agents()
    {
        var workspace = new MemoryAgentWorkspace(); var registry = new McpToolRegistry();
        AgentWorkspaceTools.Register(registry, workspace);
        workspace.Write("src/example.cs", "public class Example { }", 0);
        IAgentTransactionalWorkspace transaction = workspace;
        var applied = transaction.Apply([new AgentFileChange("browser/input.json", "[2,3,5]", 0),
            new AgentFileChange("browser/check.js", "return WebAssembly.validate(new Uint8Array());", 0)]);
        Require(applied.Count == 3 && workspace.Read("browser/input.json").Content == "[2,3,5]",
            "Installed agent package could not apply a portable file transaction.");
        Require(typeof(IAgentCodeRunner).Assembly == typeof(AgentCodeTools).Assembly &&
            typeof(AgentCodeRequest).Assembly == typeof(AgentSession).Assembly,
            "Portable browser code contracts are absent from the standalone agent package.");
        // Construct the real SDK adapter without making an inference request or requiring an account.
        var client = ShadowAgentProviders.Create(new() { Provider = "openai", ApiKey = "package-smoke-not-a-real-key", Model = "test-model" });
        using var agent = new AgentSession(client, registry, new() { PermissionMode = AgentPermissionMode.ReadOnly }, workspace);
        var result = agent.ExecuteToolAsync("workspace_read", McpJson.Parse("""{"path":"src/example.cs"}""")).GetAwaiter().GetResult();
        Require(!result.IsError && result.Text.Contains("class Example", StringComparison.Ordinal), "Installed harness could not execute a workspace tool.");
        var checkpoint = agent.Export(); agent.Import(checkpoint);
        Require(agent.Workspace.Read("src/example.cs").Content == "public class Example { }", "Installed agent checkpoint lost the source file.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static ObjectId Id(DxfObject entity) => new("H:" + entity.Handle.ToUpperInvariant());
    private static (DxfDocument Document, ShadowSession Session, Line Line) Fixture()
    {
        var document = new DxfDocument();
        var line = new Line(new Vector2(0, 0), new Vector2(10, 0));
        document.Entities.Add(line);
        return (document, new ShadowSession(document), line);
    }

    private static void Capture()
    {
        var fixture = Fixture(); var before = fixture.Session.Current;
        Require(before.DrawingEntities().Count() == 1, "Capture omitted the line.");
        fixture.Line.StartPoint = new Vector3(2, 0, 0);
        var current = fixture.Session.Refresh();
        Require(before.Read(Id(fixture.Line))["StartPoint"]["X"].Number() == 0 &&
                current.Read(Id(fixture.Line))["StartPoint"]["X"].Number() == 2, "Snapshot detachment failed.");
        new EditorSession(fixture.Session).SetProperty(Id(fixture.Line), "StartPoint", ShadowValue.From(new Vec3(3, 0)));
        Require(fixture.Line.StartPoint.X == 3, "Reverse synchronization failed.");
    }

    private static void Geometry()
    {
        var fixture = Fixture();
        fixture.Document.Entities.Add(new Line(new Vector2(5, -5), new Vector2(5, 5)));
        var geometry = new CadSceneBuilder().Build(fixture.Session.Refresh());
        var topology = new TopologyBuilder().Build(geometry);
        Require(geometry.Occurrences.Length == 2 && topology.Edges.Length == 4 && topology.Nodes.Length == 5,
            "Crossing geometry/topology failed.");
        Require(topology.Junctions.Single().Edges.Length == 4, "Missing topology junction.");
    }

    private static void Editing()
    {
        var fixture = Fixture();
        var editor = new EditorSession(new ShadowMemoryStore(fixture.Session.Current));
        var plan = new DraftingPlanner().Plan(editor.Snapshot,
            new DraftingCommand("LINE", Points: [new Vec3(0, 5), new Vec3(10, 5)]));
        plan.Execute(editor);
        Require(editor.Snapshot.DrawingEntities().Count() == 2 && fixture.Document.Entities.All.Count() == 1,
            "Detached drafting transaction failed.");
        editor.Undo();
        Require(editor.Snapshot.DrawingEntities().Count() == 1, "Drafting undo failed.");
        editor.Redo();
        Require(editor.Snapshot.DrawingEntities().Count() == 2, "Drafting redo failed.");
    }

    private static void Dimensions()
    {
        var document = new DxfDocument { BuildDimensionBlocks = false };
        var dimension = new AlignedDimension(new Vector2(0, 0), new Vector2(20, 0), 5);
        document.Entities.Add(dimension);
        var snapshot = new ShadowSession(document).Current;
        Require(new DimensionEntityProjector().TryProject(snapshot.Read(Id(dimension)), out var geometry, out var diagnostic),
            "Dimension projector refused a supported entity.");
        Require(geometry.Components.Length > 0, "Dimension regeneration produced no components: " + diagnostic);
    }

    private static void Shx()
    {
        var font = ShxFontReader.ReadAscii("*0,4,PackageSmoke\n10,2,0,0\n*65,5,A\n1,8,10,0,0\n");
        var catalog = new ShxFontCatalog(); catalog.Register("package-smoke.shx", font);
        var glyph = ShxGlyphCompiler.Compile(catalog.Current.Resolve("package-smoke.shx")!, 65);
        Require(glyph.Contours.Length == 1 && glyph.Advance.X == 10, "SHX program execution failed.");
    }

    private static void Svg()
    {
        var fixture = Fixture();
        var scene = new RenderCompiler().Compile(fixture.Session.Current);
        using var backend = new SvgRenderBackend();
        var image = backend.Render(new(scene, Viewport.Fit(scene.Bounds, 160, 100)));
        Require(image.ContentType == "image/svg+xml" && XDocument.Parse(Encoding.UTF8.GetString(image.Bytes)).Root?.Name.LocalName == "svg",
            "Packaged vector backend failed.");
    }

    private static void NativeRendering()
    {
        var fixture = Fixture();
        fixture.Document.Entities.Add(new Text("Shadow Ω ffi", new Vector2(0, 5), 3));
        using var text = new SkiaTextLayoutEngine();
        var scene = new RenderCompiler(textLayout: text).Compile(fixture.Session.Refresh());
        using var backend = new SkiaRenderBackend(textLayout: text);
        var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 240, 120));
        var png = backend.Render(frame);
        Require(png.ContentType == "image/png" && png.Bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "Native Skia PNG encoding failed.");
        var pixels = backend.RenderPixels(frame);
        Require(pixels.Bytes.Length == 240 * 120 * 4 && text.CacheStatistics.ShapeCompilations > 0,
            "Native Skia/HarfBuzz text shaping or raster extraction failed.");
    }

    private static void Dwg()
    {
        var fixture = Fixture();
        fixture.Document.Entities.Add(new Circle(new Vector2(20, 5), 3));
        using var input = new MemoryStream();
        Require(fixture.Document.Save(input), "DXF transport serialization failed.");
        var exchange = new DrawingExchange();
        var encoded = exchange.Convert(input.ToArray(), DrawingEncoding.Dxf, DrawingEncoding.Dwg);
        Require(DrawingExchange.IsDwg(encoded.Content.AsSpan()) && encoded.OutputVersion == "AC1032", "DWG writing failed.");
        var decoded = exchange.Convert(encoded.Content.ToArray(), DrawingEncoding.Dwg, DrawingEncoding.Dxf);
        using var output = new MemoryStream(decoded.Content.ToArray());
        var drawing = DxfDocument.Load(output);
        Require(drawing is not null && drawing.Entities.Lines.Single().EndPoint.X == 10 &&
                drawing.Entities.Circles.Single().Radius == 3, "DWG exchange round trip failed.");
    }
}
