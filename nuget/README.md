# netDxf packages

C# libraries for DXF reading and writing, immutable drawing snapshots, geometry,
rendering, graph analysis and editing. All packages in this repository share the
same release version. The shadow libraries target .NET 8; the DXF reader/writer
also supports .NET Standard 2.0, .NET Framework 4.7.1/4.8 and .NET 6.

## Choose a package

| Package | Purpose |
| --- | --- |
| [netDxf.netstandard](https://www.nuget.org/packages/netDxf.netstandard) | Mutable DXF object model, text/binary reader and writer |
| [netDxf.Shadow](https://www.nuget.org/packages/netDxf.Shadow) | Detached immutable values, object identities, snapshots and graph patches |
| [netDxf.Shadow.NetDxf](https://www.nuget.org/packages/netDxf.Shadow.NetDxf) | Capture and bidirectional synchronization with netDxf |
| [netDxf.Shadow.Geometry](https://www.nuget.org/packages/netDxf.Shadow.Geometry) | Analytic geometry, drawing styles and spatial indexing |
| [netDxf.Shadow.Rendering](https://www.nuget.org/packages/netDxf.Shadow.Rendering) | Render scenes, picking, text layout, paper-space composition and SVG |
| [netDxf.Shadow.Rendering.Shx](https://www.nuget.org/packages/netDxf.Shadow.Rendering.Shx) | Managed SHX/SHP stroke fonts and shape/text projection |
| [netDxf.Shadow.Rendering.SkiaSharp](https://www.nuget.org/packages/netDxf.Shadow.Rendering.SkiaSharp) | SkiaSharp canvas/PNG output and HarfBuzz typography |
| [netDxf.Shadow.Graph](https://www.nuget.org/packages/netDxf.Shadow.Graph) | Drawing topology, connectivity and graph algorithms |
| [netDxf.Shadow.Editor](https://www.nuget.org/packages/netDxf.Shadow.Editor) | Editing, selection, snapping and immutable undo/redo |
| [netDxf.Shadow.Dimensions](https://www.nuget.org/packages/netDxf.Shadow.Dimensions) | Dimension regeneration from immutable snapshots |
| [netDxf.Shadow.Drafting](https://www.nuget.org/packages/netDxf.Shadow.Drafting) | Reusable CAD drafting commands and editing plans |
| [netDxf.Shadow.Exchange.Dwg](https://www.nuget.org/packages/netDxf.Shadow.Exchange.Dwg) | Explicit diagnostic DWG/DXF exchange using ACadSharp |
| [netDxf.Shadow.Mcp](https://www.nuget.org/packages/netDxf.Shadow.Mcp) | Official MCP transport, low-level DXF/shadow discovery and transactional tools |
| [netDxf.Shadow.Agents](https://www.nuget.org/packages/netDxf.Shadow.Agents) | Official OpenAI/Anthropic/Google SDKs, streaming coding agents, workspace files, permissions, compaction and ChatGPT sign-in |

Install the libraries your application uses. NuGet resolves their dependencies:

```sh
dotnet add package netDxf.netstandard --prerelease
dotnet add package netDxf.Shadow.NetDxf --prerelease
dotnet add package netDxf.Shadow.Rendering.SkiaSharp --prerelease
```

For reproducible builds, pin every directly referenced netDxf package to the same
published version. Studio applications, verification projects and benchmarks are
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
