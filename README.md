# netDxf
netDxf Copyright(C) 2009-2023 Daniel Carvajal, licensed under MIT License
## Description
netDxf is a .net library programmed in C# to read and write AutoCAD DXF files. The typed `DxfDocument` API reads and writes the AutoCad2000, AutoCad2004, AutoCad2007, AutoCad2010, AutoCad2013, and AutoCad2018 DXF database families in text and binary format. Feature coverage within each family is partial.

The library is easy to use and I tried to keep the procedures as straightforward as possible, for example you will not need to fill up the table section with layers, styles or line type definitions. The DxfDocument will take care of that every time a new item is added.

If you need more information, you can find the official DXF documentation [here](https://help.autodesk.com/view/OARX/2021/ENU/?guid=GUID-235B22E0-A567-4CF6-92D3-38A2306D73F3).

Code example:

```c#
public static void Main()
{
	// your DXF file name
	string file = "sample.dxf";

	// create a new document, by default it will create an AutoCad2000 DXF version
	DxfDocument doc = new DxfDocument();
	// an entity
	Line entity = new Line(new Vector2(5, 5), new Vector2(10, 5));
	// add your entities here
	doc.Entities.Add(entity);
	// save to file
	doc.Save(file);

	// this check is optional but recommended before loading a DXF file
	DxfVersion dxfVersion = DxfDocument.CheckDxfFileVersion(file);
	// Typed DxfDocument admits AutoCad2000 and higher DXF families
	if (dxfVersion < DxfVersion.AutoCad2000) return;
	// load file
	DxfDocument loaded = DxfDocument.Load(file);
}
```

## DXF version and feature coverage

See the [current version-by-feature comparison](doc/dxf-conformance/version-feature-matrix.md) and its [machine-readable coverage ledger](doc/dxf-conformance/coverage.json). Each entry distinguishes tested field-level support, partial typed models, known losses, rejected writer profiles and opaque preservation. The comparison is generated and checked in CI; its production commit and evidence are pinned. The [earlier merged checkpoint](doc/dxf-conformance/checkpoint-merged-2026-09-14.md) is retained as the historical PR #75 snapshot; current source and verification are recorded by the comparison and its linked feature notes.

`netDxf.IO.DxfRawDocument` is a separate immutable ordered-tag/record API for R11/R12, R13, R14 and the six modern families above. It supports exact unedited same-transport saves, scoped raw edits, an immutable contextual handle index, selected outgoing traversal and guarded simultaneous remapping of exposed interpreted handles. It does not add historical versions to typed `DxfDocument`, evaluate unknown entities, resolve private dependencies, produce dependency-complete cross-document imports, or provide an automatic fallback inside typed load/save. See [raw preservation](doc/dxf-conformance/raw-document.md), [record editing](doc/dxf-conformance/raw-records.md), [R12 framing](doc/dxf-conformance/raw-r12.md), and [R13/R14 profiles](doc/dxf-conformance/raw-r13-r14.md).

The typed database exposes editable named dictionaries, XRECORD values, default dictionaries, dictionary variables, placeholders, extension dictionaries, and persistent reactors. Graph copying remaps known references and requires explicit mappings for external cross-document targets. See [named-object editing](doc/dxf-conformance/named-object-database.md) for ownership, reserved-entry, clone, and opaque-object limits. The separate [raw object transaction API](doc/dxf-conformance/raw-object-transactions.md) provides immutable staged editing, owned-graph cloning/deletion, and draw-order storage.

[MTEXT columns](doc/dxf-conformance/mtext-columns.md) support legacy linked entities, modern embedded definitions, and documented direct column tags, with explicit storage/version gates and caller-supplied text partitions for conversion. [VPORT configurations](doc/dxf-conformance/vport-records.md) retain physical repeated-name tile records and the current view. [Lightweight polyline integrity](doc/dxf-conformance/lwpolyline-integrity.md) covers vertex packet validation and tapered-segment reversal.

[MULTILEADER contexts and styles](doc/dxf-conformance/multileader-contexts.md) expose nested stored text/block content and exact resource references in the qualified 2007+ profiles. [BODY, REGION and 3DSOLID SAT](doc/dxf-conformance/acis-sat.md) retain inert modeler envelopes through 2010. [STYLE font metadata](doc/dxf-conformance/text-style-fidelity.md), [DIMSTYLE stored settings](doc/dxf-conformance/dimstyle-stored-settings.md), and [standalone and embedded output settings](doc/dxf-conformance/output-settings.md) preserve their documented fields and reference relationships. The [parallel module checkpoint](doc/dxf-conformance/checkpoint-modules-2026-09-14.md) records source pins, combined verification and remaining scope.

Both pipelines now offer an explicit [SaveAtomic API](doc/dxf-conformance/atomic-file-save.md) for destination-byte protection through sibling-file staging and replacement. Existing `Save` overloads remain nontransactional. [Handle operations](doc/dxf-conformance/raw-handle-operations.md) reject ambiguous/colliding changes and affected opaque slots, including [embedded-object tails](doc/dxf-conformance/raw-embedded-handle-context.md); this does not infer references hidden in private strings or binary data.

Full AutoCAD DXF capability is not yet achieved. Passing regression tests and preserving opaque records are not a native AutoCAD interoperability certificate. The [conformance guide](doc/dxf-conformance/README.md) records scope, verification commands and remaining work.

## Samples and Demos 
Are contained in the source code.
Well, at the moment they are just tests for the work in progress.
## Dependencies and distribution

The signed library targets `netstandard2.0`, `net471`, `net48`, `net6.0`, and `net8.0`. The production project has no third-party package dependency. The .NET 8 SDK runs the conformance harness; independent fixture verification uses the separately installed, development-only ezdxf reader.

## Compiling and verification

```sh
dotnet restore tests/netDxf.Conformance/netDxf.Conformance.csproj
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
dotnet build netDxf/netDxf.csproj -f netstandard2.0 -c Release
python -m pip install -r tools/requirements-independent.txt
python tools/run_independent_verifiers.py artifacts/conformance
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
```

GitHub Actions runs Debug and Release on Linux and Windows and builds the `netstandard2.0` target. The Linux Release job also runs every checked-in independent verifier, retains per-script logs, and fails on a failed check, timeout, or missing fixture. Verification on .NET 8 does not establish runtime qualification for every legacy framework target.

Set `DXF_TEST_ARTIFACTS` to separate directories when running configurations concurrently. In environments that restrict compiler build servers, use `dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false`, then execute the generated conformance DLL.
## Development Status 
See [changelog.txt](https://github.com/haplokuon/netDxf/blob/master/doc/Changelog.txt) or the [wiki page](https://github.com/haplokuon/netDxf/wiki) for information on the latest changes.
## Supported DXF entities

* 3dFace
* Arc
* Body, Region and Solid3D (inert SAT envelopes; DXF 2000–2010)
* Circle
* Dimensions (aligned, linear, radial, diametric, 3 point angular, 2 line angular, arc length, and ordinate)
* Ellipse
* Hatch (including Gradient patterns)
* Helix (stored spline and explicit analytic authoring)
* Image
* Insert (block references and attributes, dynamic blocks are not supported)
* Leader
* Line
* Light (published parameters; DXF 2007+ export)
* LwPolyline (light weight polyline)
* Mesh
* MLine
* MText
* MultiLeader (one stored context; DXF 2007+)
* OleFrame and Ole2Frame (inert byte and metadata storage)
* Point
* Polyline (Polyline2D, Polyline3D, PolyfaceMesh, and PolygonMesh)
* Ray
* Shape
* Solid
* Spline
* Text
* Tolerance
* Trace
* Underlay (DGN, DWF, and PDF underlays)
* Wipeout
* XLine (aka construction line)

All entities can be grouped.
All DXF objects may contain extended data information. 
AutoCad Table entities will be imported as Inserts (block references).
Both simple and complex line types are supported.
Geometry/modeler evaluation, modern SAB/ACDSDATA and typed SURFACE families remain unimplemented. SAT and raw payload retention do not interpret or validate proprietary geometry.
