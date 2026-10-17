# netDxf

[![Build, test and package](https://github.com/wieslawsoltes/netDxf/actions/workflows/ci-build.yml/badge.svg?branch=netstandard)](https://github.com/wieslawsoltes/netDxf/actions/workflows/ci-build.yml)
[![Release](https://github.com/wieslawsoltes/netDxf/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/netDxf/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A C# library for reading, creating, editing and writing DXF drawings.
This repository develops `netDxf.netstandard`, eleven modular Shadow packages for
immutable snapshots, synchronization, geometry, rendering and editing, and their
conformance tools.

## NuGet packages

All twelve packages share version **`4.0.0-preview.1`**, defined once in
[Directory.Build.props](Directory.Build.props). The version badges include
prereleases and reflect what is currently published on nuget.org. The new preview
packages become available when the [trusted-publishing release workflow](doc/CI-RELEASE.md#one-time-trusted-publishing-setup)
is configured and its release is published.

| Package | Purpose | Targets | NuGet | Downloads |
|---|---|---|---|---|
| [netDxf.netstandard](https://www.nuget.org/packages/netDxf.netstandard) | DXF reader, object model, editing and writer | .NET Standard 2.0; .NET Framework 4.7.1 / 4.8; .NET 6 / 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.netstandard.svg)](https://www.nuget.org/packages/netDxf.netstandard) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.netstandard.svg)](https://www.nuget.org/packages/netDxf.netstandard) |
| [netDxf.Shadow](https://www.nuget.org/packages/netDxf.Shadow) | Immutable object graph and snapshot contracts | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.svg)](https://www.nuget.org/packages/netDxf.Shadow) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.svg)](https://www.nuget.org/packages/netDxf.Shadow) |
| [netDxf.Shadow.NetDxf](https://www.nuget.org/packages/netDxf.Shadow.NetDxf) | Capture and bidirectional netDxf synchronization | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.NetDxf.svg)](https://www.nuget.org/packages/netDxf.Shadow.NetDxf) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.NetDxf.svg)](https://www.nuget.org/packages/netDxf.Shadow.NetDxf) |
| [netDxf.Shadow.Geometry](https://www.nuget.org/packages/netDxf.Shadow.Geometry) | Semantic geometry projection and spatial processing | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Geometry.svg)](https://www.nuget.org/packages/netDxf.Shadow.Geometry) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Geometry.svg)](https://www.nuget.org/packages/netDxf.Shadow.Geometry) |
| [netDxf.Shadow.Graph](https://www.nuget.org/packages/netDxf.Shadow.Graph) | Topology and graph analysis | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Graph.svg)](https://www.nuget.org/packages/netDxf.Shadow.Graph) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Graph.svg)](https://www.nuget.org/packages/netDxf.Shadow.Graph) |
| [netDxf.Shadow.Rendering](https://www.nuget.org/packages/netDxf.Shadow.Rendering) | Backend-independent presentation and rendering contracts | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Rendering.svg)](https://www.nuget.org/packages/netDxf.Shadow.Rendering) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Rendering.svg)](https://www.nuget.org/packages/netDxf.Shadow.Rendering) |
| [netDxf.Shadow.Rendering.SkiaSharp](https://www.nuget.org/packages/netDxf.Shadow.Rendering.SkiaSharp) | SkiaSharp renderer and native text shaping | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Rendering.SkiaSharp.svg)](https://www.nuget.org/packages/netDxf.Shadow.Rendering.SkiaSharp) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Rendering.SkiaSharp.svg)](https://www.nuget.org/packages/netDxf.Shadow.Rendering.SkiaSharp) |
| [netDxf.Shadow.Rendering.Shx](https://www.nuget.org/packages/netDxf.Shadow.Rendering.Shx) | SHX font and shape rendering | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Rendering.Shx.svg)](https://www.nuget.org/packages/netDxf.Shadow.Rendering.Shx) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Rendering.Shx.svg)](https://www.nuget.org/packages/netDxf.Shadow.Rendering.Shx) |
| [netDxf.Shadow.Editor](https://www.nuget.org/packages/netDxf.Shadow.Editor) | Editing commands, selection and undo/redo | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Editor.svg)](https://www.nuget.org/packages/netDxf.Shadow.Editor) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Editor.svg)](https://www.nuget.org/packages/netDxf.Shadow.Editor) |
| [netDxf.Shadow.Drafting](https://www.nuget.org/packages/netDxf.Shadow.Drafting) | Drafting tools and CAD workspace commands | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Drafting.svg)](https://www.nuget.org/packages/netDxf.Shadow.Drafting) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Drafting.svg)](https://www.nuget.org/packages/netDxf.Shadow.Drafting) |
| [netDxf.Shadow.Dimensions](https://www.nuget.org/packages/netDxf.Shadow.Dimensions) | Dimension regeneration over detached snapshots | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Dimensions.svg)](https://www.nuget.org/packages/netDxf.Shadow.Dimensions) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Dimensions.svg)](https://www.nuget.org/packages/netDxf.Shadow.Dimensions) |
| [netDxf.Shadow.Exchange.Dwg](https://www.nuget.org/packages/netDxf.Shadow.Exchange.Dwg) | Explicit DWG/DXF exchange through ACadSharp | .NET 8 | [![NuGet](https://img.shields.io/nuget/vpre/netDxf.Shadow.Exchange.Dwg.svg)](https://www.nuget.org/packages/netDxf.Shadow.Exchange.Dwg) | [![Downloads](https://img.shields.io/nuget/dt/netDxf.Shadow.Exchange.Dwg.svg)](https://www.nuget.org/packages/netDxf.Shadow.Exchange.Dwg) |

Every package includes XML API documentation, a package README, MIT license
metadata and portable `.snupkg` symbols. Internal dependencies use the same
release version. Studio applications, tests and benchmarks are not NuGet packages.

## Install and use

```sh
dotnet add package netDxf.netstandard --version 4.0.0-preview.1

# Optional modules for a .NET 8 application:
dotnet add package netDxf.Shadow.NetDxf --version 4.0.0-preview.1
dotnet add package netDxf.Shadow.Rendering.SkiaSharp --version 4.0.0-preview.1
```

Install only the modules your application uses; NuGet resolves their dependencies.
The [Shadow guide](shadow/README.md) describes the modules and their APIs.

The published package may not include unreleased or draft-PR work. To use a
particular implementation, build the reviewed source revision instead.

```csharp
using System.IO;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

var document = new DxfDocument(DxfVersion.AutoCad2018);
document.Entities.Add(new Line(Vector3.Zero, new Vector3(100, 50, 0)));
document.Entities.Add(new Circle(new Vector3(25, 25, 0), 10));

if (!document.Save("example.dxf"))
    throw new IOException("DXF save failed.");

var loaded = DxfDocument.Load("example.dxf")
    ?? throw new InvalidDataException("DXF load failed.");
```

Check operation results and the relevant validation contract. Some malformed-input
paths have different Debug exception and Release failure-return behavior. For
replacement of an existing file, consult the [atomic-save contract](doc/dxf-conformance/atomic-file-save.md)
rather than assuming every save overload is transactional.

## Support boundaries

| Pipeline | Admitted format families | Important distinction |
|---|---|---|
| Typed `DxfDocument` | AutoCAD 2000, 2004, 2007, 2010, 2013 and 2018 DXF families | Individual features have additional read/write and edit restrictions |
| Raw `DxfRawDocument` | Those six families plus R11/R12, R13 and R14 | Ordered preservation and selected edits are not full typed historical support |
| Explicit `DxfR12Codec` / `DxfR12SelectionPlan` | R11/R12 selection interchange; plans can target R12 or the six modern families | A strict selection of supported entities and reachable acyclic blocks, not a whole-document converter |
| Transport | Text and binary DXF in their admitted profiles | Field framing, encoding and version legality are checked separately |

The library targets `net471`, `net48`, `netstandard2.0`, `net6.0` and `net8.0`.
Building a target is not the same as running the complete suite on every runtime
that can consume it. The [CI/release guide](doc/CI-RELEASE.md) describes the actual
build, conformance and installed-package matrices.

Supported subsets include common drawing entities, blocks and layouts, named
objects and references, retained legacy child records, raw records, selected
FIELD/TABLE operations and geometry algorithms. See the feature contracts rather
than treating an entity name in an API as an all-feature certificate. Preserved
proxy or ACIS data is not automatically decoded, evaluated or rendered.

R12 selections now retain shared BLOCK/INSERT graphs, array references, classic
ATTDEF/ATTRIB fields and SEQEND sequences. `DxfR12SelectionPlan.Prepare(entities)`
captures an immutable normalized selection; `CreateDocument(version)` creates an
independent editable graph and `Save(stream, version, binary)` stages output with
byte/tag/string limits before touching the destination. Modern output protects
logical controls and literal escape-looking content. Ordinary `DxfDocument.Save`
retains its separate framing contract.

This profile requires unitless, acyclic blocks and rejects xrefs, unsupported
entities/metadata, distinct same-named source blocks and modern-only attribute
fields. Only the exact neutral version-1 DesignCenter block-unit packet is
projected; arbitrary ACAD XData is not dropped. Original handles, unrelated
headers/objects/layouts and lexical formatting are not part of selection output.
R13/R14 typed conversion, pre-R12 typed interchange and general flattening remain
outside this API. See the XML API contract for cancellation and stream semantics.

Public mutable collections remain available for compatibility. Direct changes
can bypass entity notifications; use validated editing methods where provided
and observe the documented graphics/reference restrictions.

## Build and verify

Use the SDK and Python versions selected by the [core workflow](.github/workflows/ci-build.yml).
Independent Python checks are development dependencies, not library runtime dependencies.

```sh
python -m pip install -r tools/requirements-independent.txt
dotnet restore netDxf/netDxf.csproj
dotnet build netDxf/netDxf.csproj --no-restore -c Release
```

For direct conformance and independent-output execution:

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
python tools/generate_dxf_coverage.py --check
```

The source-pinned [coverage ledger](doc/dxf-conformance/coverage.json) and
[generated comparison](doc/dxf-conformance/version-feature-matrix.md) describe
their recorded historical snapshot; later PRs retain separate implementation
and qualification evidence. Test totals and row counts are not completeness percentages.

## Repository guide

| Location | Purpose |
|---|---|
| [netDxf](netDxf) | Library source and XML API documentation |
| [Shadow packages](shadow/src) | Immutable snapshots, synchronization, geometry, rendering, editing and exchange |
| [Shadow guide](shadow/README.md) | Architecture, APIs and Studio applications |
| [TestDxfDocument](TestDxfDocument) | Sample/test application |
| [Conformance tests](tests/netDxf.Conformance) | Versioned API and drawing regressions |
| [Tools](tools) | Independent verifiers, coverage generation and source tooling |
| [Major-gap report](doc/dxf-conformance/remaining-major-gaps.md) | Prioritized remaining capabilities and acceptance criteria |
| [Contract index](doc/dxf-conformance/README.md) | Feature-specific behavior, evidence and limits |
| [CI and release](doc/CI-RELEASE.md) | Package qualification, NuGet trusted-publishing setup and release instructions |

## Contribution and release policy

Keep changes reviewable, preserve existing test identities and negative controls,
and state version/preservation/refusal behavior explicitly. Qualify the final
source tree, not just a previous head or a passing build summary. Assess supported
DXF behavior against explicit schemas, independent implementations and regression
oracles. Keep original fixture and receipt evidence separate from constructed
checker self-tests; native application testing is not a completion gate.

The repository intentionally keeps two core workflows:
[build/test/package](.github/workflows/ci-build.yml) and
[release](.github/workflows/release.yml). Do not add one workflow per feature or
leave temporary publishing/probe workflows in the reviewed tree. Publication
remains an explicit, guarded operation; a CI package artifact is not a public release.

The JavaScript port is maintained separately in PR #98 and has its own
fixed-reference and differential requirements. A C# qualification result does
not waive those requirements.

## History and license

Feature contracts and PR discussions retain the implementation history. The
[previous expanded README](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/README.md)
is available at its immutable baseline; it is not a current missing-feature list.
The original documentation assets and licensing material remain.

netDxf was originally developed by Daniel Carvajal. This repository and its
contributors continue that work under the [MIT License](LICENSE). Existing
copyright notices and third-party licensing material must be preserved.
