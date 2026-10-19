using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Agents.Evaluation;
using netDxf.Shadow.Health;
using netDxf.Shadow.Modules;
using netDxf.Shadow.Symbols;
using netDxf.Shadow.Workflows;
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
using netDxf.Shadow.Studio;
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
                ("agent-workspace", Agents),
                ("domain-symbols-modules", DomainModules),
                ("drawing-health-preview", HealthPreview),
                ("workflow-replay", Workflows),
                ("agent-evaluation", Evaluation)
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
                typeof(McpToolRegistry), typeof(AgentSession),
                typeof(netDxf.Shadow.Samples.StudioSamples), typeof(netDxf.Shadow.Studio.Studio),
                typeof(netDxf.Shadow.Studio.StudioAgentController), typeof(netDxf.Shadow.Studio.StudioHttp),
                typeof(netDxf.Shadow.Studio.Browser.BrowserRuntime),
                typeof(netDxf.Shadow.AgentHost.AgentHostApplication), typeof(netDxf.Shadow.Studio.StudioWebAssets),
                typeof(netDxf.Shadow.Agents.Codex.CodexAppServerClient), typeof(netDxf.Shadow.Agents.Acp.AcpClient),
                typeof(netDxf.Shadow.Markdown.MarkdownAssets),
                typeof(netDxf.Shadow.Health.DrawingHealth), typeof(netDxf.Shadow.Workflows.WorkflowRunner),
                typeof(netDxf.Shadow.Modules.ICadModule), typeof(netDxf.Shadow.Symbols.SymbolCatalog),
                typeof(netDxf.Shadow.Disciplines.DomainCadModule), typeof(netDxf.Shadow.Pid.PidModule),
                typeof(netDxf.Shadow.Piping.PipingModule), typeof(netDxf.Shadow.Electrical.ElectricalModule),
                typeof(netDxf.Shadow.Agents.Evaluation.EvaluationRunner)
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
            Require(assemblies.Select(value => value.id).Distinct().Count() == entryPoints.Length, "Missing package assembly.");
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
        var discovery = ShadowAgentProviders.DiscoverModelsAsync(
            new AgentModelDiscoveryRequest { Provider = "codex" }, limits: new AgentModelDiscoveryLimits())
            .GetAwaiter().GetResult();
        Require(discovery.Status == "unavailable" && discovery.Models.Count == 0 &&
            typeof(AgentDiscoveredModel).Assembly == typeof(AgentSession).Assembly,
            "Installed model-discovery contracts must support explicit unavailable results without network access.");
        // Construct the real SDK adapter without making an inference request or requiring an account.
        var client = ShadowAgentProviders.Create(new() { Provider = "openai", ApiKey = "package-smoke-not-a-real-key", Model = "test-model" });
        using var agent = new AgentSession(client, registry, new()
        {
            PermissionMode = AgentPermissionMode.FullAccess, Mode = AgentRunMode.Plan,
            SuccessCriteria = "Inspect the installed package without changing project files."
        }, workspace);
        var result = agent.ExecuteToolAsync("workspace_read", McpJson.Parse("""{"path":"src/example.cs"}""")).GetAwaiter().GetResult();
        Require(!result.IsError && result.Text.Contains("class Example", StringComparison.Ordinal), "Installed harness could not execute a workspace tool.");
        var denied = agent.ExecuteToolAsync("workspace_write", McpJson.Parse("""{"path":"must-not-exist.cs","content":"denied","expectedVersion":0}""")).GetAwaiter().GetResult();
        Require(denied.IsError && workspace.List().All(file => file.Path != "must-not-exist.cs") &&
            agent.Options.MaxTurns == 0 && agent.Poll().Run?.Mode == AgentRunMode.Plan &&
            agent.ToolDefinitions.Any(tool => tool.Name == "agent_finish"),
            "Installed persistent run contracts or Plan mode enforcement are unavailable.");
        var checkpoint = agent.Export(); agent.Import(checkpoint);
        Require(agent.Workspace.Read("src/example.cs").Content == "public class Example { }", "Installed agent checkpoint lost the source file.");
        ExtractedStudio();
    }
    private static void ExtractedStudio()
    {
        Require(netDxf.Shadow.Samples.ProcessDiagramFactory.Create().Entities.All.Any(),
            "Installed sample factories did not construct a drawing independently of the executable sample.");
        using var studio = new netDxf.Shadow.Studio.Studio(demo: false);
        using var documents = new StudioDocumentHub(studio);
        var controller = new StudioAgentController(id => documents.Use(id, drawing => drawing.CreateMcpRegistry()));
        try
        {
            Require(controller.Catalog().Any(tool => tool.Name == "shadow_create_entities") &&
                controller.Catalog().Any(tool => tool.Name == "graph_summary"), "Extracted Studio controller omitted shared drawing/graph tools.");
            var result = controller.Call("shadow_state", McpJson.EmptyObject).GetAwaiter().GetResult();
            Require(!result.IsError, "Extracted Studio controller could not inspect its document without provider credentials.");
            var poll = JsonSerializer.SerializeToElement(controller.Poll(0), McpJson.Options);
            Require(poll.GetProperty("usage").GetProperty("inferences").GetInt32() == 0,
                "Extracted Studio polling omitted harness usage.");
            var discovery = controller.ModelsAsync("""{"provider":"codex"}""").GetAwaiter().GetResult();
            Require(discovery.Status == "unavailable" && !controller.Session.IsRunning,
                "Extracted Studio model discovery must not start or reconfigure inference.");
        }
        finally { controller.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        var reply = netDxf.Shadow.Studio.Browser.BrowserReply.Value(new { installed = true });
        Require(reply.Status == 200 && reply.Bytes is { Length: > 0 } &&
            JsonDocument.Parse(reply.Bytes).RootElement.GetProperty("installed").GetBoolean(),
            "Installed browser transport could not serialize a public response contract.");
        Require(StudioWebAssets.Shell == "studio-shell.html" && StudioWebAssets.Diagnostics == "diagnostics-shell.html",
            "Installed static asset entry points changed unexpectedly.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(studio); builder.Services.AddSingleton(documents);
        var app = builder.Build();
        try
        {
            app.MapStudioLegacy(); app.MapStudioWorkspaces(); app.MapCadWorkspace(); app.MapStudioAgents();
            var patterns = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToHashSet();
            Require(patterns.Contains("/api/agent/{action}") && patterns.Contains("/api/cad/{id}/command") && patterns.Contains("/api/branches"),
                "Installed hosting library failed to register public routes without sample-source links.");
        }
        finally { app.StopAsync().GetAwaiter().GetResult(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        var options = new netDxf.Shadow.AgentHost.AgentHostOptions(5091, ["https://fixture.example"],
            "package-smoke-bridge-fixture-32-bytes", Path.Combine(Path.GetTempPath(), "package-smoke-unused-state.json"));
        var rejected = false;
        try { _ = new netDxf.Shadow.AgentHost.CompanionWorkspace(options); }
        catch (ArgumentException) { rejected = true; }
        Require(options.ValidateToken("package-smoke-bridge-fixture-32-bytes") && rejected,
            "Installed companion API did not preserve explicit workspace grants.");
    }
    private static void DomainModules()
    {
        var registry = new CadModuleRegistry([new netDxf.Shadow.Pid.PidModule(), new netDxf.Shadow.Piping.PipingModule(), new netDxf.Shadow.Electrical.ElectricalModule()]);
        foreach (var state in registry.States)
        {
            var id = state.Descriptor.Id; var symbol = registry.Catalog(id).Items[0];
            var document = new DxfDocument(); var source = new ShadowSession(document); var editor = new EditorSession(source);
            var plan = registry.Plan(id, source.Current, new("place", Points: [new Vec3(25, 10)],
                SymbolId: symbol.SymbolId, SymbolVersion: symbol.Version,
                Properties: ImmutableDictionary<string, string>.Empty.Add("tag", "PKG-101")));
            Require(!document.Entities.All.Any(), "Module planning mutated the native drawing.");
            plan.Execute(editor);
            var insert = document.Entities.Inserts.Single(); var metadata = DomainMetadata.Read(insert);
            Require(metadata?.Module == id && metadata.Tag == "PKG-101" && metadata.SymbolVersion == symbol.Version &&
                insert.Attributes.AttributeWithTag("TAG").Value == "PKG-101" &&
                registry.Analyze(id, source.Current).Records.Single().Ports.Length == symbol.Ports.Length,
                "Installed discipline lost attributed block identity or explicit ports.");
            editor.Undo(); Require(!document.Entities.Inserts.Any(), "Installed symbol transaction did not undo.");
            editor.Redo(); Require(document.Entities.Inserts.Count() == 1, "Installed symbol transaction did not redo.");
            // Native save performs writer housekeeping; finish session edits before testing transport bytes.
            using var stream = new MemoryStream(); Require(document.Save(stream, true), "Installed symbol DXF save failed."); stream.Position = 0;
            Require(DomainMetadata.Read(DxfDocument.Load(stream).Entities.Inserts.Single())?.Key == metadata!.Key,
                "Native DXF round trip lost stable domain identity.");
            registry.SetEnabled(id, false); var refused = false;
            try { registry.Catalog(id); } catch (InvalidOperationException) { refused = true; }
            Require(refused && document.Entities.Inserts.Count() == 1, "Disabling a module did not isolate capabilities from stored data.");
        }
        var demo = netDxf.Shadow.Samples.ProcessDiagramFactory.Create();
        var librarySource = new ShadowSession(demo).Current;
        var libraryBlock = librarySource.Document["Blocks"].Items.First(block => !block["Name"].Text.StartsWith('*')).Id!.Value;
        var exportedLibrary = DrawingBlockLibrary.Read(DrawingBlockLibrary.Export(librarySource, libraryBlock));
        var importedDrawing = new DxfDocument(); var importedSource = new ShadowSession(importedDrawing); var importedEditor = new EditorSession(importedSource);
        var entry = exportedLibrary.Entries.First(item => !item.ModelSpace);
        exportedLibrary.Plan(importedSource.Current, entry.Name, "PackageLibrary").Edit.Execute(importedEditor);
        Require(importedDrawing.Blocks.Contains("PackageLibrary"), "Installed drawing library import failed.");
        importedEditor.Undo(); importedEditor.Redo();
        Require(importedDrawing.Blocks.Contains("PackageLibrary"), "Installed drawing library history failed.");
        var report = new netDxf.Shadow.Pid.PidModule().Analyze(new ShadowSession(demo).Current);
        Require(report.Complete && report.Records.Length == 26 && report.Issues.IsEmpty,
            "Installed P&ID sample lost its explicit process network.");
    }
    private static void HealthPreview()
    {
        var fixture = Fixture(); fixture.Document.Entities.Add(new Line(fixture.Line.StartPoint, fixture.Line.EndPoint));
        var before = fixture.Session.Refresh(); var report = DrawingHealth.Analyze(before);
        Require(report.Complete && report.Issues.Any(issue => issue.Code == "geometry.duplicate") &&
                ReferenceEquals(fixture.Session.Current, before), "Installed health analysis omitted a duplicate or changed the source.");
        Require(!DrawingHealth.Analyze(before, new(MaximumWork: 1)).Complete, "Health work budget did not mark its report incomplete.");
        var editor = new EditorSession(fixture.Session);
        var plan = new DraftingPlanner().Plan(before, new("LINE", Points: [new Vec3(0, 5), new Vec3(10, 5)]));
        var preview = editor.Preview("Package line preview", plan.Patch);
        Require(fixture.Document.Entities.Lines.Count() == 2 && editor.State.UndoCount == 0 &&
            preview.Proposed.DrawingEntities().Count() == 3 && preview.Summary.Added > 0,
            "Installed preview was not detached from native state/history.");
        editor.ApplyPreview(preview); Require(fixture.Document.Entities.Lines.Count() == 3 && editor.State.UndoCount == 1, "Preview apply did not form one native transaction.");
        editor.Undo(); Require(fixture.Document.Entities.Lines.Count() == 2, "Preview apply did not undo.");
    }
    private static void Workflows()
    {
        using var studio = new Studio(demo: false);
        studio.CadWorkflowStart(new(studio.CadRevision, "Installed workflow"));
        studio.CadCommand(new(studio.CadRevision, new("LINE", Points: [new Vec3(0, 0), new Vec3(20, 5)])));
        studio.CadWorkflowStop(new(studio.CadRevision));
        var before = JsonSerializer.Serialize(studio.State(), StudioJson.CreateOptions()); var scenario = WorkflowJson.Read(WorkflowJson.Write(studio.CadWorkflowExport()));
        Require(scenario.Steps.Length == 1 && scenario.Steps[0].Operation == "command", "Installed workflow did not record a real CAD command.");
        var driver = new StudioWorkflowDriver();
        try
        {
            var report = WorkflowRunner.RunAsync(scenario, driver).GetAwaiter().GetResult();
            Require(report.Passed && report.Complete && report.Steps.Single().Passed && JsonSerializer.Serialize(studio.State(), StudioJson.CreateOptions()) == before,
                "Installed workflow replay failed or mutated its recording document.");
        }
        finally { driver.DisposeAsync().GetAwaiter().GetResult(); }
        var rejectedDriver = new StudioWorkflowDriver();
        try
        {
            var rejected = WorkflowRunner.RunAsync(scenario with { Baseline = scenario.Baseline with { Sha256 = "wrong" } }, rejectedDriver).GetAwaiter().GetResult();
            Require(!rejected.Passed && !rejected.Complete && rejected.Steps.IsEmpty, "Installed workflow accepted a corrupted baseline.");
        }
        finally { rejectedDriver.DisposeAsync().GetAwaiter().GetResult(); }
    }
    private static void Evaluation()
    {
        EvaluationCases.SelfTest();
        var fixture = EvaluationFixture.Freeze(EvaluationCases.All.Single(c => c.Id == "edit"));
        Require(new EvaluationFixture(fixture.Scenario, fixture.ExportSeed()).Fingerprint == fixture.Fingerprint,
            "Installed evaluation fixture did not preserve its frozen identity.");
        var runner = new EvaluationRunner();
        var report = runner.RunAsync(fixture, new PackageEvaluationTarget()).GetAwaiter().GetResult();
        Require(report.Passed && report.ToolCalls == 1 && report.ToolErrors == 0 && report.UndoTransactions == 1 &&
            report.Telemetry.InputTokens is null && fixture.CreateDrawing().Entities.Circles.Count(c => c.Radius == 1.5) == 6,
            "Installed evaluation did not grade actual tool edits independently from its frozen seed.");
        var control = runner.RunAsync(fixture, new PackageEvaluationTarget { SkipEdits = true }).GetAwaiter().GetResult();
        Require(!control.Passed && control.ToolCalls == 0, "Installed evaluation accepted an unchanged drawing as task completion.");
    }
    private sealed class PackageEvaluationTarget : IAgentEvaluationTarget
    {
        internal bool SkipEdits { get; init; }
        public EvaluationTarget Target => new("package-fixture", "fixture", "local", "explicit-fixture", "not-applicable");
        public async Task<EvaluationExecution> RunAsync(EvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (!SkipEdits)
            {
                var snapshot = context.Editor.Snapshot;
                var edits = snapshot.DrawingEntities().Where(entity => entity.Type == "Circle" && entity["Layer"]["Name"].Text == "Target")
                    .SelectMany(entity => new object[] { new { id = entity.Id!.Value.Value, path = "Radius", value = 3 },
                        new { id = entity.Id!.Value.Value, path = "Center/Y", value = 15 } }).ToArray();
                var response = await context.Tools.ExecuteAsync("shadow_edit", McpJson.Element(new { revision = snapshot.Revision, edits }), cancellationToken);
                Require(!response.IsError, "Installed evaluation tool edit failed: " + response.Text);
            }
            return new("completed");
        }
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
        var subset = new RenderCompiler().Compile(fixture.Session.Current,
            new() { RootEntities = [Id(fixture.Line)] });
        Require(subset.Items.Length == 1 && subset.Items[0].Entity == Id(fixture.Line),
            "Installed renderer did not isolate the requested native root.");
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

        var document = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);
        var source = new ShadowSession(document); var nativeEditor = new EditorSession(source);
        var owner = source.Current.Layout()["AssociatedBlock"].Id!.Value;
        var definitionPlan = DraftingAttributeDefinitions.Plan(source.Current, owner,
            new(Add: [new("TAG", Value: "Cooling\nwater", Position: new(5, 10), Multiline: true)]));
        var definitionId = definitionPlan.Created.Single();
        Require(new CadSceneBuilder().Build(definitionPlan.Patch.Apply(source.Current)).Occurrences.Single().Geometry.Text!.Content == "TAG",
            "Installed standalone attribute preview did not render its tag.");
        definitionPlan.Execute(nativeEditor);
        var definition = document.Layouts["Model"].AssociatedBlock.AttributeDefinitions["TAG"];
        var grip = DraftingGrips.Read(source.Current, [definitionId]).Single();
        DraftingGrips.Move(source.Current, definitionId, grip.Id, new(15, 30)).Execute(nativeEditor);
        Require(definition.Position == new Vector3(15, 30, 0) && definition.GetMText().Position == definition.Position,
            "Installed attribute grip lost an independent text frame.");
        nativeEditor.Undo(); nativeEditor.Redo();
        Require(ReferenceEquals(document.Layouts["Model"].AssociatedBlock.AttributeDefinitions["TAG"], definition) &&
            definition.GetMText().Value == "Cooling\\Pwater", "Installed attribute history lost identity or content.");

        var block = new netDxf.Blocks.Block("Preview");
        block.Entities.Add(new Line(Vector2.Zero, new Vector2(10, 0)));
        block.AttributeDefinitions.Add(new("TAG") { Value = "P-101", Position = new(0, 2, 0), Height = 1 });
        document.Blocks.Add(block);
        using var stream = new MemoryStream(); Require(document.Save(stream), "Installed preview fixture did not save."); stream.Position = 0;
        using var studio = new Studio(false); studio.Load(stream);
        var before = JsonSerializer.Serialize(studio.CadState(), StudioJson.CreateOptions());
        var preview = studio.CadInsertPreview(new(studio.CadRevision,
            new(new("INSERT", [Vec3.Zero], Reference: Id(block)))));
        Require(preview.Items == 2 && preview.Svg.Contains("P-101") &&
            JsonSerializer.Serialize(studio.CadState(), StudioJson.CreateOptions()) == before,
            "Installed transient insertion preview lost attributes or changed the drawing.");
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
