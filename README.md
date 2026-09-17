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

The [PR #95 recovery and editing checkpoint](doc/dxf-conformance/checkpoint-recovery-editing-2026-09-15.md) brings forward [ordinary legacy 2D POLYLINE records](doc/dxf-conformance/polyline2d-records.md), [MESH declaration framing](doc/dxf-conformance/mesh-field-framing.md), and [qualified periodic HATCH conversion and evaluation](doc/dxf-conformance/hatch-periodic-conversion.md). Loaded [TABLESTYLE classic header/row scalars](doc/dxf-conformance/table-style-editing.md) and [CELLSTYLEMAP entry names](doc/dxf-conformance/cell-style-map-editing.md) have explicit atomic editing APIs. These operations preserve their documented source identities and stored packets; they do not add automatic TABLE regeneration or complete style-format interpretation. The [mixed graph tests](doc/dxf-conformance/eleventh-mixed.md) exercise these capabilities together.

The aggregate comparison is pinned to PR #95. Later TABLESTYLE increments add [complete border edits and fixed version-zero headers](doc/dxf-conformance/table-style-borders.md), followed by [stored data/unit editing and explicit row STYLE reassignment](doc/dxf-conformance/table-row-settings.md). The latter note includes a post-PR95 profile comparison. These changes preserve immutable snapshots and guarded dependencies; raw format strings, cross-object style synchronization and table regeneration remain separate work.

Loaded CELLSTYLEMAP entries also expose [nested formatting projections and atomic edits](doc/dxf-conformance/cell-style-format-editing.md): content values and stored format expressions, margins, grid-border values, and explicit STYLE/LTYPE references. Unsupported shapes remain stored without an editing projection. These edits do not regenerate TABLE geometry or synchronize duplicated style data.

[Structural CELLSTYLEMAP authoring](doc/dxf-conformance/cell-map-authoring.md) now supports new maps, complete qualified entry/frame replacement, immutable exported definitions, and explicit STYLE/LTYPE-mapped transfer into R2004–R2018 destination profiles. This scoped transfer does not import resource graphs or synchronize TABLE consumers.

`netDxf.IO.DxfRawDocument` is a separate immutable ordered-tag/record API for R11/R12, R13, R14 and the six modern families above. It supports exact unedited same-transport saves, scoped raw edits, an immutable contextual handle index, selected outgoing traversal and guarded simultaneous remapping of exposed interpreted handles. It does not add historical versions to typed `DxfDocument`, evaluate unknown entities, resolve private dependencies, produce dependency-complete cross-document imports, or provide an automatic fallback inside typed load/save. See [raw preservation](doc/dxf-conformance/raw-document.md), [record editing](doc/dxf-conformance/raw-records.md), [R12 framing](doc/dxf-conformance/raw-r12.md), and [R13/R14 profiles](doc/dxf-conformance/raw-r13-r14.md).

The typed database exposes editable named dictionaries, XRECORD values, default dictionaries, dictionary variables, placeholders, extension dictionaries, and persistent reactors. Graph copying remaps known references and requires explicit mappings for external cross-document targets. See [named-object editing](doc/dxf-conformance/named-object-database.md) for ownership, reserved-entry, clone, and opaque-object limits. The separate [raw object transaction API](doc/dxf-conformance/raw-object-transactions.md) provides immutable staged editing, owned-graph cloning/deletion, and draw-order storage.

[MTEXT columns](doc/dxf-conformance/mtext-columns.md) support legacy linked entities, modern embedded definitions, and documented direct column tags, with explicit storage/version gates and caller-supplied text partitions for conversion. [VPORT configurations](doc/dxf-conformance/vport-records.md) retain physical repeated-name tile records and the current view. [Lightweight polyline integrity](doc/dxf-conformance/lwpolyline-integrity.md) covers vertex packet validation and tapered-segment reversal.

[MULTILEADER contexts and styles](doc/dxf-conformance/multileader-contexts.md) expose nested stored text/block content and exact resource references in the qualified 2007+ profiles. [BODY, REGION and 3DSOLID SAT](doc/dxf-conformance/acis-sat.md) retain inert modeler envelopes through 2010. [STYLE font metadata](doc/dxf-conformance/text-style-fidelity.md), [DIMSTYLE stored settings](doc/dxf-conformance/dimstyle-stored-settings.md), and [standalone and embedded output settings](doc/dxf-conformance/output-settings.md) preserve their documented fields and reference relationships. The [parallel module checkpoint](doc/dxf-conformance/checkpoint-modules-2026-09-14.md) records source pins, combined verification and remaining scope.

[LAYER_FILTER and OBJECT_PTR](doc/dxf-conformance/layer-filter-pointer.md), [SPATIAL_INDEX and inert VBA_PROJECT](doc/dxf-conformance/stored-envelopes.md), and [LIGHTLIST](doc/dxf-conformance/lightlist.md) retain their qualified public fields and database relationships. [Terminal object erasure](doc/dxf-conformance/typed-object-erasure.md) validates incoming references before removing an owned subtree. [APPID lifecycle](doc/dxf-conformance/appid-xdata-lifecycle.md) preserves canonical bindings and independent BLOCK/ENDBLK XData through cloning. [Native MLEADER compatibility](doc/dxf-conformance/mleader-native-inputs.md) preserves absent version and line-color fields and retains private styles opaquely. The [object lifecycle checkpoint](doc/dxf-conformance/checkpoint-lifecycle-2026-09-15.md) records the combined evidence and remaining boundaries.

[Stored TABLE payloads and literal grids](doc/dxf-conformance/stored-tables.md), [DATATABLE cells and ownership](doc/dxf-conformance/datatable.md), and [LAYER_INDEX graphs](doc/dxf-conformance/layer-index.md) preserve their qualified public storage and lifecycle. [Source identity and numeric handle checks](doc/dxf-conformance/source-reference-identity.md) distinguish retained input objects from generated defaults and keep null references separate from document identity. The [table-storage checkpoint](doc/dxf-conformance/checkpoint-table-storage-2026-09-15.md) records 23,879 passing cases per configuration, independent emitted-file checks and the remaining editing/evaluation boundaries.

Both pipelines now offer an explicit [SaveAtomic API](doc/dxf-conformance/atomic-file-save.md) for destination-byte protection through sibling-file staging and replacement. Existing `Save` overloads remain nontransactional. [Handle operations](doc/dxf-conformance/raw-handle-operations.md) reject ambiguous/colliding changes and affected opaque slots, including [embedded-object tails](doc/dxf-conformance/raw-embedded-handle-context.md); this does not infer references hidden in private strings or binary data.

Full AutoCAD DXF capability is not yet achieved. Passing regression tests and preserving opaque records are not a native AutoCAD interoperability certificate. The [conformance guide](doc/dxf-conformance/README.md) records scope, verification commands and remaining work.

[Coordinated cell-style consumer remapping](doc/dxf-conformance/cell-style-consumers.md) now updates qualified TABLECONTENT column, row and cell IDs atomically with their TABLESTYLE-owned CELLSTYLEMAP. Renames, cycles and explicit deletion fallbacks are supported; unqualified consumers reject before publication. This does not regenerate TABLE layout or private rendering caches.

[Addressed table calculations and measured display layout](doc/dxf-conformance/table-calculation-layout.md) add bounded numeric formulas, atomic backing-scalar result updates, explicit cell-format cascades with provenance, and fresh detached display blocks with measured row growth and merged cells. These APIs do not automatically regenerate source TABLE inline/private caches, choose native style precedence, or execute persistent FIELD/date/angle expressions.

The [legacy feature review](doc/dxf-conformance/legacy-feature-review.md) improves existing linear/angular unit formatting, DXF calendar/elapsed precision, palette isolation, ordinary RGB serialization, and ARC/CIRCLE/ELLIPSE affine geometry. It includes explicit finite/representation checks, source-version RGB fallback, immutable failure behavior, and independent world-space/file checks. This broadens the audit beyond TABLE features without claiming complete private-schema or native AutoCAD qualification.

[Explicit FIELD result persistence](doc/dxf-conformance/field-results.md) adds immutable evaluation/cache projections, atomic cached-result batches and bounded child-first host evaluation of loaded FIELD ownership trees. Evaluator code, private data and host text/geometry remain unchanged; this is not automatic native FIELD execution or complete host-cache regeneration.

[The standard FIELD evaluator continuation](doc/dxf-conformance/standard-field-evaluation.md)
adds opt-in explicit AcVar bindings, bounded numeric AcExpr children, date masks,
and angular format controls. **This continuation is uncompiled:** its new C#
cases and emitted-output gate remain unrun. Its independent checker's model tests
are not a substitute for C# or native AutoCAD qualification.

[Atomic FIELD text-host updates](doc/dxf-conformance/field-text-hosts.md) optionally publish
successful root outcomes to qualified TEXT, standalone MTEXT, ATTRIB and ATTDEF hosts
in the same guarded transaction. Literal escaping, stale-proxy invalidation and
owner-held attribute metadata are qualified separately from native font/reflow or
private-cache regeneration. Existing FIELD-result APIs remain cache-only.

[Finite normal and direction assignment](doc/dxf-conformance/direction-assignment.md) now rejects zero/nonfinite vectors before mutation and accepts very small or large finite directions without normalization overflow. The audit covers inherited entity normals, ATTRIB/ATTDEF normals and RAY/XLINE directions, with independent physical-tag and numerical checks. Public vector utilities, arbitrary transforms and native AutoCAD qualification retain their separate scope.

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
Qualified AutoCAD TABLE entities are retained as source-bound `StoredTable` packets with explicit backing-content and geometry edits. Complete TABLE authoring, formula/layout evaluation and automatic regeneration remain outside that storage contract.
Both simple and complex line types are supported.
Geometry/modeler evaluation, modern SAB/ACDSDATA and typed SURFACE families remain unimplemented. SAT and raw payload retention do not interpret or validate proprietary geometry.
