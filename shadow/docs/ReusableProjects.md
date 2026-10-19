# Reusable Studio projects

Studio's drawing model, agent integration, HTTP host, browser runtime and web UI are
standalone .NET 8 libraries. Executable samples contain only application startup,
host policy and browser bootstrapping. No production library compiles files from a
sample project, and the browser sample no longer links native sample source/assets.

| Project | Responsibility |
| --- | --- |
| `netDxf.Shadow.Samples` | Deterministic DXF gallery factories and the tank/pump P&ID |
| `netDxf.Shadow.Studio.Core` | CAD documents/workspaces, editing, rendering, graph analysis, schemas and MCP discovery |
| `netDxf.Shadow.Studio.Agents` | Agent sessions, permissions, tool routing, UI relay and companion integration |
| `netDxf.Shadow.Studio.Hosting` | ASP.NET Core endpoints for CAD and agents |
| `netDxf.Shadow.Studio.WebAssembly` | JS interop and browser-local transport for the same Core operations |
| `netDxf.Shadow.Studio.Web` | Shared static web assets: CAD ribbon/viewport, graph analysis and chat UI |
| `netDxf.Shadow.Agents.Hosting` | Local provider bridge, companion workspace, supported local-agent transports and MCP host |
| `netDxf.Shadow.Agents.Codex` | Independent Codex app-server client with managed authentication and session callbacks |
| `netDxf.Shadow.Agents.Acp` | Independent ACP v1 client with advertised authentication and session capabilities |
| `netDxf.Shadow.Agents.Evaluation` | Frozen native drawing tasks, injected targets, independent graders and measured tool receipts |
| `netDxf.Shadow.Markdown` | Shared streaming CommonMark/GFM static renderer |

These sit above the existing functionality-specific libraries: Shadow, NetDxf,
Geometry, Graph, Rendering, Rendering.Shx, Rendering.SkiaSharp, Dimensions, Editor,
Drafting, Exchange.Dwg, Mcp and Agents. The new Modules registry and Symbols
catalogs support the Pid, Piping and Electrical libraries through the shared
Disciplines adapter. Health analyzes immutable source data; Workflows provides
recording and isolated replay contracts. Add references only to the layers your
application needs. Core does not depend on ASP.NET Core, JS interop or the agent
providers. Samples does not depend on Studio. Both transport implementations use
Core and Studio.Agents; they do not depend on one another.

## Examples

Create a document without starting a UI or server:

```csharp
using netDxf.Shadow.Samples;

var document = ProcessDiagramFactory.Create();
document.Save("tank-and-pump.dxf");
```

Host the reusable CAD services (the application is responsible for access control):

```csharp
using netDxf.Shadow.Studio;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Studio>();
builder.Services.AddSingleton<StudioDocumentHub>();
var app = builder.Build();
app.MapStudioWorkspaces();
app.MapCadWorkspace();
app.MapStudioAgents();
app.Run();
```

To serve the UI, reference Studio.Web and enable ASP.NET static web assets. Its
`studio-shell.html` and `diagnostics-shell.html` entry points are exported at the
web root alongside `cad/`, `agents/`, and diagnostic assets. The native sample
shows default-file routing and a loopback-only host policy. The browser sample
loads the same shell after its .NET runtime initializes; browser-specific startup
files remain with that host. Publish the complete output so fonts and native
WebAssembly assets remain together.

Build the native host with .NET 8 or newer. The standalone browser application
uses .NET 10 and the `wasm-tools` workload; the reusable browser transport library
itself targets .NET 8.

```sh
dotnet build shadow/samples/netDxf.Shadow.Studio -c Release -p:TargetFrameworks=net8.0
dotnet build shadow/samples/netDxf.Shadow.AgentHost -c Release -p:TargetFrameworks=net8.0
dotnet build shadow/samples/netDxf.Shadow.Studio.Browser -c Release
```

All 33 library packages are explicitly registered in `tools/ci/package_catalog.py`,
with one version and reviewed dependency edges. Verification projects reference
libraries directly. The installed-package consumer runs 14 functional scenarios,
including native symbol/module authoring, health/preview operations, recorded CAD
workflow replay and a credential-free evaluation target. It verifies each of the
33 loaded library assemblies against the candidate package's hash. Development
roll-forward checks on newer runtimes are separate from release qualification,
which still requires the exact .NET 8 runtime and a clean committed source tree.
No API credentials are required for the fixture verification suites.
