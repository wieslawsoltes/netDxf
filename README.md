# netDxf

[![Build, test and package](https://github.com/wieslawsoltes/netDxf/actions/workflows/ci-build.yml/badge.svg?branch=netstandard)](https://github.com/wieslawsoltes/netDxf/actions/workflows/ci-build.yml)
[![Release](https://github.com/wieslawsoltes/netDxf/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/netDxf/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A C# library for reading, creating, editing and writing DXF drawings.
This repository develops `netDxf.netstandard`, 32 modular Shadow packages for immutable snapshots, synchronization, geometry,
rendering, domain authoring, CAD workflows and agent integration, and their
conformance tools.

## NuGet packages

Choose modules from the [package catalog](nuget/README.md#choose-a-package),
which lists all 33 packages, their purposes and direct dependencies. The core
library supports .NET Standard 2.0, .NET Framework 4.7.1/4.8 and .NET 6/8;
Shadow packages target .NET 8.

All packages share the version in [Directory.Build.props](Directory.Build.props).
The [release guide](doc/CI-RELEASE.md) covers publishing and installed-package
verification. Packages include XML API documentation, an MIT license notice and
portable `.snupkg` symbols. Studio hosts, tests and benchmarks are source-only.

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
replacement of an existing file, consult the [atomic-save contract](doc/dxf-conformance/io.md#saving-files)
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
python -m unittest discover -s tests/independent_verifiers -p 'test_*.py'
```

The [DXF guides](doc/dxf-conformance/README.md) explain compatibility, IO,
editing, geometry and FIELD/TABLE behavior. Historical audits and per-feature
review notes are available through the [history guide](doc/dxf-conformance/history.md).
Use current CI reports for execution results; historical test totals do not
measure feature completeness.

## Repository guide

| Location | Purpose |
|---|---|
| [netDxf](netDxf) | Library source and XML API documentation |
| [Shadow packages](shadow/src) | Immutable snapshots, synchronization, geometry, rendering, editing and exchange |
| [Shadow guide](shadow/README.md) | Architecture, APIs and Studio applications |
| [Coding agents and MCP](shadow/docs/coding-agents.md) | Browser coding workspace, official .NET provider SDKs, ChatGPT sign-in, native project companion and generic DXF/Shadow/Studio tools |
| [TestDxfDocument](TestDxfDocument) | Sample/test application |
| [Conformance tests](tests/netDxf.Conformance) | Versioned API and drawing regressions |
| [Tools](tools) | Independent verifiers and source tooling |
| [DXF guides](doc/dxf-conformance/README.md) | Compatibility, IO, editing, geometry, FIELD/TABLE and verification |
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
