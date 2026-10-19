# netDxf packages

C# libraries for DXF reading and writing, immutable drawing snapshots, geometry,
rendering, graph analysis and editing. All packages in this repository share the
same release version. The shadow libraries target .NET 8; the DXF reader/writer
also supports .NET Standard 2.0, .NET Framework 4.7.1/4.8 and .NET 6.

## Choose a package

The release catalog contains **33 packages**: the multi-target DXF library and 32 .NET 8 Shadow libraries.

| Package | Purpose |
| --- | --- |
| [netDxf.netstandard](https://www.nuget.org/packages/netDxf.netstandard) | Mutable DXF object model and text/binary reader and writer |
| [netDxf.Shadow](https://www.nuget.org/packages/netDxf.Shadow) | Detached immutable values, object identities, snapshots and graph patches |
| [netDxf.Shadow.NetDxf](https://www.nuget.org/packages/netDxf.Shadow.NetDxf) | Capture and bidirectional synchronization with native netDxf |
| [netDxf.Shadow.Geometry](https://www.nuget.org/packages/netDxf.Shadow.Geometry) | Analytic geometry, drawing styles and spatial indexing |
| [netDxf.Shadow.Rendering](https://www.nuget.org/packages/netDxf.Shadow.Rendering) | Render scenes, picking, text layout, paper-space composition and SVG |
| [netDxf.Shadow.Graph](https://www.nuget.org/packages/netDxf.Shadow.Graph) | Drawing topology, traversal, connectivity and bounded analysis |
| [netDxf.Shadow.Editor](https://www.nuget.org/packages/netDxf.Shadow.Editor) | Editing, selection, snapping, graph previews and immutable undo/redo |
| [netDxf.Shadow.Dimensions](https://www.nuget.org/packages/netDxf.Shadow.Dimensions) | Dimension regeneration from immutable snapshots |
| [netDxf.Shadow.Drafting](https://www.nuget.org/packages/netDxf.Shadow.Drafting) | Reusable CAD drafting commands, block authoring and resource-editing plans |
| [netDxf.Shadow.Health](https://www.nuget.org/packages/netDxf.Shadow.Health) | Bounded read-only geometry, ownership, resource and reference diagnostics |
| [netDxf.Shadow.Workflows](https://www.nuget.org/packages/netDxf.Shadow.Workflows) | Versioned operation recording, JSON exchange and isolated deterministic replay contracts |
| [netDxf.Shadow.Modules](https://www.nuget.org/packages/netDxf.Shadow.Modules) | Discoverable CAD module contracts, capability registry and command validation |
| [netDxf.Shadow.Symbols](https://www.nuget.org/packages/netDxf.Shadow.Symbols) | Versioned symbol catalogs, native attributes, persistent ports and domain references |
| [netDxf.Shadow.Disciplines](https://www.nuget.org/packages/netDxf.Shadow.Disciplines) | Shared symbol-module adapter, domain commands and bounded validation reporting |
| [netDxf.Shadow.Pid](https://www.nuget.org/packages/netDxf.Shadow.Pid) | P&ID equipment and PCE symbols, process-pipe authoring and flow/data validation |
| [netDxf.Shadow.Piping](https://www.nuget.org/packages/netDxf.Shadow.Piping) | Schematic fittings, pipe routes, nominal sizes and specification continuity |
| [netDxf.Shadow.Electrical](https://www.nuget.org/packages/netDxf.Shadow.Electrical) | Schematic devices, circuit-tagged wires and terminal/data validation |
| [netDxf.Shadow.Rendering.Shx](https://www.nuget.org/packages/netDxf.Shadow.Rendering.Shx) | Managed SHX/SHP stroke fonts and shape/text projection |
| [netDxf.Shadow.Rendering.SkiaSharp](https://www.nuget.org/packages/netDxf.Shadow.Rendering.SkiaSharp) | SkiaSharp canvas/PNG output and HarfBuzz typography |
| [netDxf.Shadow.Exchange.Dwg](https://www.nuget.org/packages/netDxf.Shadow.Exchange.Dwg) | Explicit diagnostic DWG/DXF exchange using ACadSharp |
| [netDxf.Shadow.Mcp](https://www.nuget.org/packages/netDxf.Shadow.Mcp) | Official MCP transport, DXF/shadow discovery and transactional drawing/graph tools |
| [netDxf.Shadow.Agents](https://www.nuget.org/packages/netDxf.Shadow.Agents) | Official provider SDK adapters, persistent coding harness, workspaces and permissions |
| [netDxf.Shadow.Agents.Evaluation](https://www.nuget.org/packages/netDxf.Shadow.Agents.Evaluation) | Frozen drawing tasks, injected agent targets, independent graders and measured tool receipts |
| [netDxf.Shadow.Samples](https://www.nuget.org/packages/netDxf.Shadow.Samples) | Reusable DXF gallery and explicitly connected tank-and-pump P&ID factories |
| [netDxf.Shadow.Studio.Core](https://www.nuget.org/packages/netDxf.Shadow.Studio.Core) | Host-independent CAD workspaces, drawing operations, modules, health, previews and workflows |
| [netDxf.Shadow.Studio.Agents](https://www.nuget.org/packages/netDxf.Shadow.Studio.Agents) | Studio agent lifecycle, drawing context, browser tool bridge and companion integration |
| [netDxf.Shadow.Studio.Hosting](https://www.nuget.org/packages/netDxf.Shadow.Studio.Hosting) | ASP.NET Core HTTP endpoints for CAD workspaces and Studio agents |
| [netDxf.Shadow.Studio.WebAssembly](https://www.nuget.org/packages/netDxf.Shadow.Studio.WebAssembly) | Browser transport, JavaScript interop and in-process Studio runtime |
| [netDxf.Shadow.Agents.Codex](https://www.nuget.org/packages/netDxf.Shadow.Agents.Codex) | Codex app-server stdio, managed sign-in, sessions and approval callbacks |
| [netDxf.Shadow.Agents.Acp](https://www.nuget.org/packages/netDxf.Shadow.Agents.Acp) | Agent Client Protocol v1 transport and capability-aware sessions |
| [netDxf.Shadow.Agents.Hosting](https://www.nuget.org/packages/netDxf.Shadow.Agents.Hosting) | Reusable ASP.NET Core companion, local agent bridges and MCP hosting |
| [netDxf.Shadow.Markdown](https://www.nuget.org/packages/netDxf.Shadow.Markdown) | CommonMark/GFM DOM renderer with streaming presentation |
| [netDxf.Shadow.Studio.Web](https://www.nuget.org/packages/netDxf.Shadow.Studio.Web) | Shared CAD ribbon, viewport, module panels and agent chat static assets |

Install the libraries your application uses. NuGet resolves their dependencies:

```sh
dotnet add package netDxf.netstandard --prerelease
dotnet add package netDxf.Shadow.NetDxf --prerelease
dotnet add package netDxf.Shadow.Rendering.SkiaSharp --prerelease
```

For reproducible builds, pin every directly referenced netDxf package to the same
published version. Studio executable hosts, verification projects and benchmarks are
available in the source repository and are not NuGet packages.

## Read and write a drawing

```csharp
using netDxf;
using netDxf.Entities;

var document = new DxfDocument();
document.Entities.Add(new Line(Vector3.Zero, new Vector3(100, 50, 0)));
if (!document.Save("example.dxf"))
    throw new System.IO.IOException("DXF save failed.");

var loaded = DxfDocument.Load("example.dxf")
    ?? throw new System.IO.InvalidDataException("DXF load failed.");
```

The [shadow API guide](https://github.com/wieslawsoltes/netDxf/blob/netstandard/shadow/README.md)
includes snapshot capture, editing, rendering and graph examples. The
[DWG exchange guide](https://github.com/wieslawsoltes/netDxf/blob/netstandard/shadow/src/netDxf.Shadow.Exchange.Dwg/README.md)
documents supported conversion profiles and diagnostics.
The [Codex app-server guide](https://github.com/wieslawsoltes/netDxf/blob/netstandard/shadow/src/netDxf.Shadow.Agents.Codex/README.md)
describes managed ChatGPT sign-in and local agent sessions. The
[ACP client guide](https://github.com/wieslawsoltes/netDxf/blob/netstandard/shadow/src/netDxf.Shadow.Agents.Acp/README.md)
describes ACP v1 capability negotiation, authentication, sessions and permissions.

The [discipline-module guide](https://github.com/wieslawsoltes/netDxf/blob/netstandard/shadow/docs/DisciplineModules.md)
describes symbols, P&ID, piping and electrical data. The
[reusable-project guide](https://github.com/wieslawsoltes/netDxf/blob/netstandard/shadow/docs/ReusableProjects.md)
explains host boundaries and standalone composition.

SkiaSharp rendering uses native SkiaSharp/HarfBuzz assets and suitable host fonts.
Other backends can use the rendering contracts and SVG output without SkiaSharp.
The DWG adapter includes `THIRD-PARTY-NOTICES.txt` with the ACadSharp attribution;
dependency packages retain their own licenses and notices.

## Source and debugging

Packages include XML API documentation and an MIT license notice. Matching
`.snupkg` symbol packages contain portable PDBs; repository/Source Link metadata
connects release builds to their source revision.

- [Repository and examples](https://github.com/wieslawsoltes/netDxf)
- [Release and installed-package verification](https://github.com/wieslawsoltes/netDxf/blob/netstandard/doc/CI-RELEASE.md)
- [Issue tracker](https://github.com/wieslawsoltes/netDxf/issues)
