# netDxf DXF version and feature comparison

> Generated from `coverage.json` by `tools/generate_dxf_coverage.py`; edit the ledger, not this file.

Audit date: **2026-09-14**. Production baseline after PR **#75**: [`6420dd213b018d6f8698167aca4473bc3d3a499f`](https://github.com/wieslawsoltes/netDxf/tree/6420dd213b018d6f8698167aca4473bc3d3a499f).
Source tree: `280e1b80946fc2e4133e69ead3bc8651825b13bd`. Branch: `netstandard`.

## 1. Current result and scope

**189 scoped feature rows; 6 typed format families; 9 raw-preservation format families. Full AutoCAD DXF capability is not yet achieved.**

`DxfDocument` is the existing typed geometry/database API. `DxfRawDocument` is a separate immutable ordered-tag/record API. Raw preservation is now implemented; it is not an automatic preservation fallback inside typed `DxfDocument.Load/Save`. A raw file containing an unfamiliar entity can survive while that entity is still missing from the typed API.

Each cell describes the exact operation and pipeline named by its row. It does not declare historical legality of every field in that record, nor that every producer uses the same defaults. In particular, raw retention of a modern record inside an older declared profile does not make that record legal in the older format. Generic opaque support is not a per-entity semantic fixture claim.

The original 113-row source audit is retained unchanged as [the 12 September historical snapshot](version-feature-matrix-2026-09-12.md). Its then-missing VIEW/raw/CLASSES/UCS features must not be read as current status. This ledger keeps broad partial-family rows and adds narrow tested increments rather than promoting an entire family after fixing one field.

| Mark | Meaning |
|---|---|
| T | Executed regression coverage for precisely the stated subset; never an entire-family certificate. |
| P | The named pipeline has a partial implementation or lacks comprehensive verification. |
| P! | Typed path exists with a known unresolved version-legality concern. |
| L | Known lossy omission/conversion/regeneration in the selected pipeline. |
| O | Tested opaque/original data preservation, not semantic evaluation. |
| M | Missing in this pipeline; does not imply historical availability in this version. |
| V | The typed writer rejects this feature/profile combination; read-side retention can be more permissive. |
| X | Version not admitted to the typed DxfDocument API. |
| N | This transport-specific implementation does not apply to the column. |
| CP | Declared legacy code-page strategy; detailed rules depend on typed versus raw pipeline. |
| U8 | UTF-8 strategy. |

## 2. Admitted version profiles

| Format | ACADVER | Typed DxfDocument | Raw DxfRawDocument | Binary group codes | Character strategy |
|---|---|---|---|---|---|
| R11/R12 | AC1009 | Rejected | Text/binary; preservation profile | one byte + 255/Int16 escape | legacy |
| R13 | AC1012 | Rejected | Text/binary; preservation profile | two-byte Int16 | legacy |
| R14 | AC1014 | Rejected | Text/binary; preservation profile | two-byte Int16 | legacy |
| 2000 | AC1015 | Text/binary; partial typed schema | Text/binary; preservation profile | two-byte Int16 | legacy |
| 2004 | AC1018 | Text/binary; partial typed schema | Text/binary; preservation profile | two-byte Int16 | legacy |
| 2007 | AC1021 | Text/binary; partial typed schema | Text/binary; preservation profile | two-byte Int16 | UTF-8 |
| 2010 | AC1024 | Text/binary; partial typed schema | Text/binary; preservation profile | two-byte Int16 | UTF-8 |
| 2013 | AC1027 | Text/binary; partial typed schema | Text/binary; preservation profile | two-byte Int16 | UTF-8 |
| 2018 | AC1032 | Text/binary; partial typed schema | Text/binary; preservation profile | two-byte Int16 | UTF-8 |

Product release years and database-format families are separate: an AC1032 file can contain later product extensions. No new 2024/2026 database format is invented here. Historical code-page support and binary framing are explained in [R12](raw-r12.md), [R13/R14](raw-r13-r14.md), and [the raw API](raw-document.md). Autodesk's [HEADER reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm) supplies the format identifiers.

### Declared enum families not admitted by either document API

| Enum family | Declared identifier | Status |
|---|---|---|
| R1.1 | MC0.0 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R1.2 | AC1.2 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R1.4 | AC1.4 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R2.0 | AC1.50 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R2.10 | AC2.10 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R2.5 | AC1002 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R2.6 | AC1003 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R9 | AC1004 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |
| R10 | AC1006 | Enum spelling is declared by the repository; raw and typed admission remain absent. Historical spelling/aliases require independent fixtures. |

`Unknown` is an error/recognition sentinel, not another supported file version. Headerless inference remains open; enum presence alone does not implement a dialect.

## 3. Transport and validation

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| Text DXF · typed | X | X | X | T | T | T | T | T | T | Version detection and four primitive geometries tested; not every record. `DxfReader.Read`, `DxfWriter.Write`, `DocumentRoundTrip`. [B](version-feature-matrix-2026-09-12.md) |
| Binary DXF: modern two-byte codes · typed | X | X | X | T | T | T | T | T | T | Typed modern codec; full sentinel and exact binary-chunk framing checks. Separate raw API now selects pre-R13 one-byte/escaped codes. [BIN](strict-binary-values.md), [CHUNK](binary-chunks.md), [R12](raw-r12.md) |
| Character encoding · typed | X | X | X | CP | CP | U8 | U8 | U8 | U8 | Typed code-page input / Unicode-escape output before 2007; UTF-8 from 2007. Custom HEADER strings now share the modeled-string codec. [UNICODE](custom-header-unicode.md) |
| Text binary-chunk decoding · typed | X | X | X | T | T | T | T | T | T | All 310–319/1004 codes, valid bytes, mixed case, malformed input and missing lines; fixed #4. [B](version-feature-matrix-2026-09-12.md) |
| Ordered unknown-record preservation · typed | X | X | X | M | M | M | M | M | M | Still absent from DxfDocument typed load/save. DxfRawDocument now retains ordered unfamiliar data; the two pipelines must not be conflated. [RAW](raw-document.md), [REC](raw-records.md) |
| Strict numeric/handle/boolean parsing · typed | X | X | X | T | T | T | T | T | T | Strict text and binary primitive-value regressions; finite-number/Boolean/hex policies are library policies, not a universal recovery mode. [TXT](strict-text-values.md), [BIN](strict-binary-values.md) |
| Target-version/downgrade diagnostics · typed | X | X | X | P | P | P | P | P | P | Selected guards for MESH, HELIX, HATCH, MTEXT and VIEW. No centralized complete schema legality or per-loss report. Explicit SaveAtomic is separately available; existing Save remains nontransactional. [MVER](mesh-version-export.md), [MTBG](mtext-background.md), [VIEW](named-views.md), [ATOMIC](atomic-file-save.md), [HELIX](helix.md), [HEMPTY](empty-hatch-retention.md) |

## 4. Sections

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| HEADER: typed variables · typed | X | X | X | P | P | P | P | P | P | Modeled scalar/UCS subset; surface-density routing and comment handling fixed. Active DIMSTYLE fields still regenerate selected overrides. [SURF](surface-density-routing.md), [COMMENT](header-comments.md), [B](version-feature-matrix-2026-09-12.md) |
| HEADER: custom scalars/vectors · typed | X | X | X | P | P | P | P | P | P | Custom scalars/vectors retained; Unicode and comment handling fixed. Arbitrary multi-tag values, duplicates and independent DIM overrides still require the raw API. [UNICODE](custom-header-unicode.md), [COMMENT](header-comments.md), [RAW](raw-document.md) |
| CLASSES · typed | X | X | X | P | P | P | P | P | P | Ordered public DxfClass metadata, cloning and generated raster-class reconciliation. Group 91 omitted in AC1015; no preservation of unsupported entity instances through typed IO. [CLS](class-definitions.md) |
| TABLES · typed | X | X | X | P | P | P | P | P | P | Named VIEW core is now implemented alongside other typed families. VPORT remains reduced; raw indexing is the preservation alternative. [VIEW](named-views.md), [REC](raw-records.md), [B](version-feature-matrix-2026-09-12.md) |
| BLOCKS / ENTITIES · typed | X | X | X | P | P | P | P | P | P | Supported models/relationships only, not unknown-content preservation. `ReadBlocks`, `ReadEntity`, `WriteBlock`, `WriteEntity`. [B](version-feature-matrix-2026-09-12.md) |
| OBJECTS · typed | X | X | X | P | P | P | P | P | P | Eleven explicit dispatch cases; no generic ownership graph. `ReadObjects`. [B](version-feature-matrix-2026-09-12.md) |
| THUMBNAILIMAGE · typed | X | X | X | O | O | O | O | O | O | Exact preview bytes retained; no generation. Empty preview normalizes to absent section. [THUMB](thumbnail-image.md) |
| ACDSDATA · typed | X | X | X | M | M | M | M | M | M | Typed reader still discards the section. Ordered generic tags survive through raw IO; there is no typed ASM schema, decoding or dependency interpretation. [RAW](raw-document.md) |
| Unknown/custom sections · typed | X | X | X | M | M | M | M | M | M | No typed fallback; raw sections and records now have independent preservation/editing APIs. [RAW](raw-document.md), [REC](raw-records.md) |

## 5. Entities

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| LINE · typed | X | X | X | T | T | T | T | T | T | Endpoints and binary XData tested; common fields partial. `ReadLine/WriteLine`. [B](version-feature-matrix-2026-09-12.md) |
| CIRCLE · typed | X | X | X | T | T | T | T | T | T | Center/radius tested; OCS/transforms/optional fields need broader fixtures. `ReadCircle/WriteCircle`. [B](version-feature-matrix-2026-09-12.md) |
| ARC · typed | X | X | X | T | T | T | T | T | T | Center/radius/angles tested; common fields partial. `ReadArc/WriteArc`. [B](version-feature-matrix-2026-09-12.md) |
| POINT · typed | X | X | X | T | T | T | T | T | T | Position tested; all display semantics not certified. `ReadPoint/WritePoint`. [B](version-feature-matrix-2026-09-12.md) |
| 3DFACE · typed | X | X | X | P | P | P | P | P | P | Face3D model; invisible-edge/degenerate cases need tests. `ReadFace3d/WriteFace3D`. [B](version-feature-matrix-2026-09-12.md) |
| SOLID · typed | X | X | X | P | P | P | P | P | P | Planar corners/thickness/normal, not a 3DSOLID. `ReadSolid/WriteSolid`. [B](version-feature-matrix-2026-09-12.md) |
| TRACE · typed | X | X | X | P | P | P | P | P | P | Geometry implemented; full OCS/default behavior unverified. `ReadTrace/WriteTrace`. [B](version-feature-matrix-2026-09-12.md) |
| ELLIPSE · typed | X | X | X | P | P | P | P | P | P | Axis/ratio/parameter model and conversion paths. `ReadEllipse/WriteEllipse`. [B](version-feature-matrix-2026-09-12.md) |
| LWPOLYLINE · typed | X | X | X | P | P | P | P | P | P | Bulges/widths/flags/elevation; per-vertex IDs/optional codes need review. `ReadLwPolyline/WriteLwPolyline`. [B](version-feature-matrix-2026-09-12.md) |
| POLYLINE: 2D / VERTEX / SEQEND · typed | X | X | X | P | P | P | P | P | P | Legacy entity representation in modern files, not a legacy file dialect. `ReadPolyline/WritePolyline`. [B](version-feature-matrix-2026-09-12.md) |
| POLYLINE: 3D · typed | X | X | X | P | P | P | P | P | P | Polyline3D topology/flags; transform fixtures needed. `ReadPolyline/WritePolyline`. [B](version-feature-matrix-2026-09-12.md) |
| POLYLINE: polyface mesh · typed | X | X | X | P | P | P | P | P | P | Topology modeled; index/invisible-edge validation needs fixtures. `ReadPolyline/WritePolyline`. [B](version-feature-matrix-2026-09-12.md) |
| POLYLINE: polygon mesh · typed | X | X | X | P | P | P | P | P | P | PolygonMesh and smoothing preprocessing. `ReadPolyline/WritePolyline`. [B](version-feature-matrix-2026-09-12.md) |
| RAY · typed | X | X | X | P | P | P | P | P | P | Origin/direction; common fields partial. `ReadRay/WriteRay`. [B](version-feature-matrix-2026-09-12.md) |
| XLINE · typed | X | X | X | P | P | P | P | P | P | Origin/direction; common fields partial. `ReadXLine/WriteXLine`. [B](version-feature-matrix-2026-09-12.md) |
| SHAPE · typed | X | X | X | P | P | P | P | P | P | Number/SHX style resolution; SHX geometry is a separate resource concern. `ReadShape/WriteShape`. [B](version-feature-matrix-2026-09-12.md) |
| TEXT · typed | X | X | X | P | P | P | P | P | P | Text/alignment/style; encoding/OCS corpus incomplete. `ReadText/WriteText`. [B](version-feature-matrix-2026-09-12.md) |
| MTEXT · typed | X | X | X | P | P | P | P | P | P | Content/direction/spacing plus clone direction and editable background subset. Column contexts, fields and complete annotations remain incomplete. [MTCL](mtext-clone-direction.md), [MTBG](mtext-background.md) |
| INSERT · typed | X | X | X | P | P | P | P | P | P | Block transforms/attributes plus column/row arrays and clone/explode handling; dynamic-block evaluation and complete referenced graphs remain absent. [INSERT](insert-arrays.md) |
| ATTDEF / ATTRIB · typed | X | X | X | P | P | P | P | P | P | Definitions/instances; newer embedded-MTEXT/context variants need audit. `ReadAttributeDefinition/ReadAttribute/WriteAttribute`. [B](version-feature-matrix-2026-09-12.md) |
| DIMENSION: aligned / linear · typed | X | X | X | P | P | P | P | P | P | Geometric models/picture blocks; full association/context graphs absent. `ReadDimension/WriteDimension`. [B](version-feature-matrix-2026-09-12.md) |
| DIMENSION: angular 2-line / 3-point · typed | X | X | X | P | P | P | P | P | P | Typed geometry; all styles/overrides/edge cases unverified. `ReadDimension/WriteDimension`. [B](version-feature-matrix-2026-09-12.md) |
| DIMENSION: diameter / radius / ordinate · typed | X | X | X | P | P | P | P | P | P | Typed geometry; dependency graph incomplete. `ReadDimension/WriteDimension`. [B](version-feature-matrix-2026-09-12.md) |
| ARC_DIMENSION: arc length · typed | X | X | X | P | P | P | P | P | P | Model present; historical eligibility still needs release fixtures. `ReadDimension/WriteDimension`. [B](version-feature-matrix-2026-09-12.md) |
| HATCH: solid / patterned · typed | X | X | X | P | P | P | P | P | P | Tested subsets include seeds, pixel size, XData, flags, closure, bulges, counted packets, fit metadata, outer path framing and empty-input retention. Empty output rejects. General geometry, affine validity and associations remain partial. [HDOUBLE](hatch-double-pattern.md), [HSEED](hatch-seed-points.md), [HPIX](hatch-pixel-size.md), [HXDATA](hatch-xdata-preservation.md), [HCLOSE](hatch-polyline-closure.md), [HPOLY](hatch-polyline-input.md), [HPAT](hatch-pattern-lists.md), [HFLAGS](hatch-boundary-flags.md), [HORDER](hatch-pattern-order.md), [HEDGE](hatch-edge-packets.md), [HFIT](hatch-spline-fit-data.md), [HPATH](hatch-path-count-validation.md), [HEMPTY](empty-hatch-retention.md) |
| HATCH: gradients · typed | X | X | X | L | P | P | P | P | P | AC1015 output drops gradient payload. Later profiles retain rotation, continuous shift, authored RGB/tint/mode, checked two-stop packets and exact optional ACI metadata. Reserved future forms and complete appearance/evaluation remain partial. [HANGLE](hatch-gradient-angle.md), [HSHIFT](hatch-gradient-shift.md), [HCOLOR](hatch-gradient-color-state.md), [HGRAD](hatch-gradient-packets.md), [HACI](hatch-gradient-aci.md) |
| HATCH: spline fit boundary data · typed | X | X | X | V | V | V | T | T | T | Editable ordered OCS fits and nullable tangent vectors; independent clones, controls-plus-fit conversion and selected transforms. Existing 2010+ packet profile retained; earlier populated exports reject before caller-stream writes. Not a historical-introduction or fitting/evaluation claim. [HFIT](hatch-spline-fit-data.md), [STANGENT](spline-tangent-transforms.md) |
| MLINE · typed | X | X | X | P | P | P | P | P | P | Vertices/segments/styles; full break/fill/joint semantics not certified. `ReadMLine/WriteMLine`. [B](version-feature-matrix-2026-09-12.md) |
| LEADER · typed | X | X | X | P | P | P | P | P | P | Path/annotation relationships; not MULTILEADER. `ReadLeader/WriteLeader`. [B](version-feature-matrix-2026-09-12.md) |
| TOLERANCE · typed | X | X | X | P | P | P | P | P | P | Geometric tolerance text; all formatting/rendering separate. `ReadTolerance/WriteTolerance`. [B](version-feature-matrix-2026-09-12.md) |
| IMAGE · typed | X | X | X | P | P | P | P | P | P | Reference/definition/clip/reactor subset; not raster rendering. `ReadImage/WriteImage`. [B](version-feature-matrix-2026-09-12.md) |
| DGNUNDERLAY / DWFUNDERLAY / PDFUNDERLAY · typed | X | X | X | P | P | P | P | P | P | References/definitions/clipping; historical admission not comprehensively gated. `ReadUnderlay/WriteUnderlay`. [B](version-feature-matrix-2026-09-12.md) |
| WIPEOUT · typed | X | X | X | P | P | P | P | P | P | Boundary and IO; full variable/dependency coverage not certified. `ReadWipeout/WriteWipeout`. [B](version-feature-matrix-2026-09-12.md) |
| VIEWPORT entity · typed | X | X | X | P | P | P | P | P | P | Paper-space viewport model, distinct from VPORT table. `ReadViewport/WriteViewport`. [B](version-feature-matrix-2026-09-12.md) |
| ACAD_TABLE / TABLE · typed | X | X | X | L | L | L | L | L | L | Imported as INSERT; cells/formulas/formatting/table semantics lost. `ReadAcadTable`. [B](version-feature-matrix-2026-09-12.md) |
| MULTILEADER · typed | X | X | X | M | M | M | M | M | M | Multi-context leader model; MLEADERSTYLE object also absent. [B](version-feature-matrix-2026-09-12.md) |
| LIGHT · typed | X | X | X | M | M | M | M | M | M | Light entity; SUN is a separate missing object. [B](version-feature-matrix-2026-09-12.md) |
| SECTION · typed | X | X | X | M | M | M | M | M | M | Section-plane entity and dependency graph. [B](version-feature-matrix-2026-09-12.md) |
| REGION / BODY / 3DSOLID · typed | X | X | X | M | M | M | M | M | M | No typed ACIS entities. Generic raw tags can retain admitted opaque payloads; ACIS/ASM-specific evaluation and dependency-qualified editing remain unimplemented. [RAW](raw-document.md), [B](version-feature-matrix-2026-09-12.md) |
| SURFACE: base / extruded / lofted / revolved / swept · typed | X | X | X | M | M | M | M | M | M | No typed surface-family schemas. Generic raw preservation is not a surface parameter editor or geometric evaluator. [RAW](raw-document.md), [B](version-feature-matrix-2026-09-12.md) |
| OLEFRAME / OLE2FRAME · typed | X | X | X | M | M | M | M | M | M | Inert embedded OLE payload preservation. [B](version-feature-matrix-2026-09-12.md) |
| ACAD_PROXY_ENTITY · typed | X | X | X | M | M | M | M | M | M | Class/unknown subclass/proxy payload preservation. [B](version-feature-matrix-2026-09-12.md) |
| COORDINATION_MODEL · typed | X | X | X | M | M | M | M | M | M | Coordination-model entity/definition dependencies. [B](version-feature-matrix-2026-09-12.md) |

## 6. Implemented field-level increments

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| SPLINE · typed | X | X | X | P | P | P | P | P | P | Partial typed family. Stored clone state, Bezier domains, flag composition, supported periodic input, tangent transforms and knot-aware reversal are tested separately; full NURBS/degenerate schemas remain incomplete. [B](version-feature-matrix-2026-09-12.md), [STANGENT](spline-tangent-transforms.md), [SCLONE](spline-clone-state.md), [BEZK](bezier-knot-parameterization.md), [SFLAG](spline-export-flags.md), [SPER](spline-periodic-input.md), [SREV](spline-knot-reversal.md) |
| MESH: subdivision mesh · typed | X | X | X | V | V | V | P | P | P | Partial typed family. Core topology, creases, blend flags, counted input and mutable writer preflight are tested; subentity overrides and subdivision evaluation remain incomplete. [MVER](mesh-version-export.md), [MBLEND](mesh-blend-crease.md), [MREAD](mesh-read-validation.md), [MWRITE](mesh-write-validation.md) |
| HELIX · typed | X | X | X | V | V | P | P | P | P | PR67/68: inherited SPLINE and independent finite HELIX parameters, clones, conservative2007+ output and explicit analytic authoring. No implicit constraint solving; inherited complete SPLINE grammar remains partial. [HELIX](helix.md), [HAUTH](helix-authoring.md) |
| Text physical EOF and missing-value detection · typed | X | X | X | T | T | T | T | T | T | Explicit truncation handling and caller-stream ownership; not arbitrary malformed-record recovery. [FRAME](text-framing.md) |
| Binary exact-length input chunks · typed | X | X | X | T | T | T | T | T | T | Declared payload length must be consumed; truncated data cannot become a short successful value. [CHUNK](binary-chunks.md) |
| Binary output chunk-length guard · typed | X | X | X | T | T | T | T | T | T | Reject >255 before code/state writes. This physical limit does not expand standard 127-byte semantic limits. [CHOUT](binary-chunk-output.md) |
| Text binary64 numeric precision · typed | X | X | X | T | T | T | T | T | T | G17 with explicit signed zero; exact .NET 8 primitive/document tests. Older runtime parsing is not certified by target compilation. [PREC](double-output-precision.md) |
| HEADER version/encoding probe · typed | X | X | X | T | T | T | T | T | T | Structured probe and malformed/stream-position cases; not a payload substring search. [PROBE](header-probe.md) |
| Custom HEADER string Unicode · typed | X | X | X | T | T | T | T | T | T | Code-page escape and UTF-8 output, independently authored input and scalar/vector controls. [UNICODE](custom-header-unicode.md) |
| Interleaved HEADER comments · typed | X | X | X | T | T | T | T | T | T | Skip group 999 at every HEADER advance; raw comments remain accessible in their separate API. [COMMENT](header-comments.md) |
| SURFU/SURFV field routing · typed | X | X | X | T | T | T | T | T | T | Distinct surface-density variables remain independent instead of being assigned to the wrong destination. [SURF](surface-density-routing.md) |
| Minimal modern documents · typed | X | X | X | T | T | T | T | T | T | Initialize omitted required collections/model-space graph; cannot reconstruct absent authored object metadata. [MIN](minimal-documents.md) |
| CLASS names, proxy flags and identity reconciliation · typed | X | X | X | T | T | T | T | T | T | Documented names/flags and custom ordering; no plugin execution or custom entity count verification. [CLS](class-definitions.md) |
| CLASS optional instance-count export · typed | X | X | X | L | T | T | T | T | T | Group 91 retained in memory; AC1015 writer omits it, later profiles retain supplied counts. [CLS](class-definitions.md) |
| UCS elevation · typed | X | X | X | T | T | T | T | T | T | Group 146 read/write and clone. [UCS](ucs-elevation.md) |
| UCS orthographic origin overrides · typed | X | X | X | T | T | T | T | T | T | Repeated 71 plus 13/23/33; absent override differs from explicit zero. Not base-UCS graph resolution. [ORTHO](ucs-orthographic-origins.md) |
| UCS TABLE XData isolation · typed | X | X | X | T | T | T | T | T | T | UCS TABLE metadata no longer aliases BLOCK_RECORD metadata. [UCSX](ucs-table-xdata.md) |
| Named VIEW core fields · typed | X | X | X | T | T | T | T | T | T | Name, camera/target/size/clip/twist/modes/XData with indexed identity. See detailed field list; no complete VIEW schema claim. [VIEW](named-views.md) |
| VIEW plottable-camera export · typed | X | X | X | V | V | T | T | T | T | Group 73: profile rejects true for pre-2007 output. Historical boundary corroborated by independent implementation, not AutoCAD execution. [VIEW](named-views.md) |
| INSERT row/column arrays · typed | X | X | X | T | T | T | T | T | T | Counts/spacing, read/write, clone and explosion; not dynamic blocks. [INSERT](insert-arrays.md) |
| MTEXT clone drawing direction · typed | X | X | X | T | T | T | T | T | T | Group 72 state survives direct/nested copies and explosion. [MTCL](mtext-clone-direction.md) |
| MTEXT background-fill data · typed | X | X | X | V | V | T | T | T | T | Groups 90/45/63/421/431/441; explicit conservative writer profile, not a first-historical-release assertion. [MTBG](mtext-background.md) |
| MTEXT text-frame export · typed | X | X | X | V | V | V | V | V | T | Frame flag bit16: conservative AC1032 writer profile; earlier real-world eligibility still needs historical fixtures. [MTBG](mtext-background.md) |
| MESH version preflight · typed | X | X | X | T | T | T | T | T | T | All registered blocks inspected; pre-2010 rejection does not affect legacy POLYLINE meshes. [MVER](mesh-version-export.md) |
| MESH blend-crease metadata · typed | X | X | X | V | V | V | T | T | T | Group 72 read/write/clone and 0/1 validation. Not subdivision evaluation. [MBLEND](mesh-blend-crease.md) |
| MESH counted topology input · typed | X | X | X | V | V | V | T | T | T | Type/count/index/crease validation; incremental allocation from consumed data, not a universal memory quota. [MREAD](mesh-read-validation.md) |
| HATCH double-pattern flag · typed | X | X | X | T | T | T | T | T | T | Group 77 Boolean, clone/INSERT/transform and explicit field omission for solid fills. No visual hatch evaluation claimed. [HDOUBLE](hatch-double-pattern.md) |
| RASTERVARIABLES owner edge · typed | X | X | X | T | T | T | T | T | T | Group 330 agrees with ACAD_IMAGE_VARS owning dictionary; IMAGEDEF ownership stays separate. [RASTER](raster-ownership.md) |
| Binary XData clone independence · typed | X | X | X | T | T | T | T | T | T | No shared mutable byte-array storage through cloned records/entities. [XDCL](xdata-clone.md) |
| Owned file-stream disposal · typed | X | X | X | T | T | T | T | T | T | Owned file streams dispose on all success/setup/read/write/probe exits; caller streams stay open. Existing Save remains nontransactional; explicit SaveAtomic is a separate operation. [FILE](file-stream-lifetime.md), [ATOMIC](atomic-file-save.md) |
| Support-resource lookup isolation · typed | X | X | X | T | T | T | T | T | T | No process-CWD mutation; direct resource then ordered folder precedence. Not a sandbox or concurrently mutable collection. [LOOKUP](support-folder-lookup.md) |
| HATCH authored seed points · typed | X | X | X | T | T | T | T | T | T | Group98 count and ordered OCS10/20 points; finite edits, independent clones, selected OCS/INSERT transforms and malformed-list validation. No flood-fill evaluation or arbitrary-affine repair. [HSEED](hatch-seed-points.md) |
| HATCH pixel-size metadata · typed | X | X | X | T | T | T | T | T | T | Nullable group47 retains absence/zero/finite values; clone/INSERT and export. Sampling hint is stored, not evaluated. [HPIX](hatch-pixel-size.md) |
| Explicit typed atomic file replacement (SaveAtomic) · typed | X | X | X | T | T | T | T | T | T | PR70: sibling staging, flush and single replacement/move, cancellation and failure cleanup. Opt-in destination-byte protection, not all-document rollback, power-loss durability or hostile-filesystem isolation. [ATOMIC](atomic-file-save.md) |
| HATCH ACAD XData and pattern-origin projection · typed | X | X | X | T | T | T | T | T | T | Scoped origin update preserves unrelated ACAD records without mutating source XData; no general application-schema validation. [HXDATA](hatch-xdata-preservation.md) |
| HATCH polyline closure · typed | X | X | X | T | T | T | T | T | T | Group73 retained through loading/cloning/INSERT; open paths stay open and conversion does not invent a closing segment. [HCLOSE](hatch-polyline-closure.md) |
| HATCH optional bulges and counted polyline lists · typed | X | X | X | T | T | T | T | T | T | Optional42 defaults zero; checked components/counts/references, comments, adjacent data and incremental storage. Writer normalizes zero bulges. [HPOLY](hatch-polyline-input.md) |
| HATCH counted pattern lines and dashes · typed | X | X | X | T | T | T | T | T | T | Group78/53 lines, keyed43-46 fields and counted79/49 dash packets; rejects negative/duplicate/surplus data; local comments allowed. [HPAT](hatch-pattern-lists.md) |
| HATCH boundary classification · typed | X | X | X | T | T | T | T | T | T | Exact group92 bits through read/clone/transform; only structural Polyline bit follows representation changes; constructor defaults unchanged. [HFLAGS](hatch-boundary-flags.md) |
| HATCH pattern metadata ordering · typed | X | X | X | T | T | T | T | T | T | Intact pattern packets and scalar metadata can reorder; PAT-local conversion happens once final angle/scale are known. Gradient packet remains separate. [HORDER](hatch-pattern-order.md) |
| HATCH edge dispatch and counted packets · typed | X | X | X | T | T | T | T | T | T | Checked LINE/ARC/ELLIPSE/SPLINE components and incremental lists. Separate fit retention and outer-count validation are now implemented; knot/degree/periodicity relations and geometry remain incomplete. [HEDGE](hatch-edge-packets.md), [HFIT](hatch-spline-fit-data.md), [HPATH](hatch-path-count-validation.md) |
| HATCH fractional gradient shift · typed | X | X | X | L | T | T | T | T | T | Finite group461 numeric Shift retained through import/edit/clone/export in modern profiles; legacy Centered endpoint convenience retained. AC1015 export remains a solid downgrade. [HSHIFT](hatch-gradient-shift.md) |
| HATCH gradient rotation · typed | X | X | X | L | T | T | T | T | T | Group460 rotation retained independently of pattern-only group52, with normalized public degrees, clone and repeated transport changes. AC1015 output still omits gradients. [HANGLE](hatch-gradient-angle.md) |
| HATCH authored gradient RGB, tint and mode · typed | X | X | X | L | T | T | T | T | T | Authored RGB endpoints, dormant tint, finite tint validation and independent clone/mode state. Optional ACI retention is separately implemented by PR60. AC1015 still downgrades gradients. [HCOLOR](hatch-gradient-color-state.md) |
| HATCH gradient scalar and two-stop packet grammar · typed | X | X | X | L | T | T | T | T | T | Singleton fields dispatch by group code; two bounded stops, optional per-stop ACI input, comments and malformed-packet diagnostics. Canonical writer ordering and AC1015 downgrade unchanged; raw-only future schemas are not interpreted. [HGRAD](hatch-gradient-packets.md) |
| SPLINE tangent vector transformations · typed | X | X | X | T | T | T | T | T | T | Nullable tangent vectors receive the linear part only; translation changes control/fit positions, not tangent vectors. Clone/INSERT and six-profile transport coverage; not all spline geometric semantics. [STANGENT](spline-tangent-transforms.md) |
| HATCH outer path-count grammar · typed | X | X | X | T | T | T | T | T | T | Nonnegative group91, per-path group92 and edge count93; complete packet boundaries, incremental growth, comments, duplicates/orphans and record/section boundary rejection. Empty/absent boundary discard remains unchanged. [HPATH](hatch-path-count-validation.md) |
| HATCH gradient optional ACI metadata · typed | X | X | X | L | T | T | T | T | T | PR60: exact per-stop Int16 value/absence independent of RGB, tint and mode; clones retain automatic/explicit/absent authoring state. AC1015 output still omits gradients. [HACI](hatch-gradient-aci.md) |
| Empty HATCH typed export · typed | X | X | X | V | V | V | V | V | V | PR73: reject zero-boundary HATCH in all registered blocks before preprocessing or caller-stream writes. Input is retained; no invented boundaries or empty-HATCH rendering guarantee. [HEMPTY](empty-hatch-retention.md) |
| MESH mutable output preflight · typed | X | X | X | V | V | V | T | T | T | PR61: finite vertices/creases, face sizes, serialized Int32 limits and topology indices across registered blocks. Stream preflight, not manifoldness or subdivision evaluation. [MWRITE](mesh-write-validation.md) |
| SPLINE clone without refitting · typed | X | X | X | T | T | T | T | T | T | PR62: independent exact control/fit representations, weights, knots, tolerances, tangents and common metadata, without regeneration. [SCLONE](spline-clone-state.md) |
| Typed ASCII comment skipping · typed | X | X | X | T | T | T | T | T | T | PR63: skip group999 in typed text parsers while retaining the leading preamble. Raw codecs retain tags; binary comment rejection unchanged. [TCOM](typed-comments.md) |
| Composite Bezier knot domains · typed | X | X | X | T | T | T | T | T | T | PR64: equal normalized spans for authored quadratic/cubic chains and constructor validation. Imported and explicitly supplied knots unchanged. [BEZK](bezier-knot-parameterization.md) |
| SPLINE output flag composition · typed | X | X | X | T | T | T | T | T | T | PR65: closure, periodic/rational and compatibility flags composed by OR, not ordinal addition. No universal historical flag or geometry-regeneration claim. [SFLAG](spline-export-flags.md) |
| Standard periodic SPLINE input · typed | X | X | X | T | T | T | T | T | T | PR66: standard bit2 independent of legacy2048; retain exact degree-fold overlap/weights in supported compact layouts; reject nonrepresentable layouts. [SPER](spline-periodic-input.md) |
| HELIX separate parameter persistence · typed | X | X | X | V | V | T | T | T | T | PR67: AcDbHelix and inherited AcDbSpline remain independently authored. Conservative2007+ export, class counts, clones and selected transforms. [HELIX](helix.md) |
| HELIX analytic authoring · typed | X | X | X | V | V | T | T | T | T | PR68: cylindrical/tapered/fractional helices and planar spirals; signed pitch/zero radii; explicit cubic approximation with segment budget and truncation bound excluding floating-point error. [HAUTH](helix-authoring.md) |
| SPLINE knot-aware reversal · typed | X | X | X | T | T | T | T | T | T | PR69: reflect full knot domain and reverse periodic control/weight phase plus fit/tangent state. Preflight before mutation; no refitting or automatic HELIX parameter reconciliation. [SREV](spline-knot-reversal.md) |
| Empty HATCH typed input retention · typed | X | X | X | T | T | T | T | T | T | PR73: preserve identity, fill metadata, seeds, elevation and XData on zero/absent boundary lists; clones remain independent. Typed output rejection is a separate row. [HEMPTY](empty-hatch-retention.md) |
| Existing Save file-path behavior · typed | X | X | X | L | L | L | L | L | L | The original Save overload still creates/truncates before serialization. Deterministic disposal is not a transaction; SaveAtomic is a separate explicit API. [FILE](file-stream-lifetime.md), [ATOMIC](atomic-file-save.md) |

## 7. Symbol tables

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| APPID · typed | X | X | X | P | P | P | P | P | P | Registry/XData registration. `ReadApplicationId/WriteApplicationRegistry`. [B](version-feature-matrix-2026-09-12.md) |
| BLOCK_RECORD · typed | X | X | X | P | P | P | P | P | P | Block ownership/layout links; all flags/dependencies unverified. `ReadBlockRecord/WriteBlockRecord`. [B](version-feature-matrix-2026-09-12.md) |
| DIMSTYLE · typed | X | X | X | P | P | P | P | P | P | Large typed subset, not a complete versioned override catalog. `ReadDimensionStyle/WriteDimensionStyle`. [B](version-feature-matrix-2026-09-12.md) |
| LAYER · typed | X | X | X | P | P | P | P | P | P | Layer/style/color subset; material/plot-style graphs missing. `ReadLayer/WriteLayer`. [B](version-feature-matrix-2026-09-12.md) |
| LTYPE · typed | X | X | X | P | P | P | P | P | P | Simple/complex text/shape segments; resources/missing-style cases need fixtures. `ReadLinetype/WriteLinetype`. [B](version-feature-matrix-2026-09-12.md) |
| STYLE · typed | X | X | X | P | P | P | P | P | P | Text/shape styles; defaults/obsolete fields need review. `ReadTextStyle/WriteTextStyle/WriteShapeStyle`. [B](version-feature-matrix-2026-09-12.md) |
| UCS · typed | X | X | X | P | P | P | P | P | P | Origin/axes, elevation 146, orthographic origin pairs and table XData now retained. Base-UCS references and complete VIEW/VPORT contexts remain open. [UCS](ucs-elevation.md), [ORTHO](ucs-orthographic-origins.md), [UCSX](ucs-table-xdata.md) |
| VIEW · typed | X | X | X | P | P | P | P | P | P | Public named collection and core camera/target/clip/mode/XData IO. Camera plottability requires 2007+ writer profile; extended UCS/render/background references remain open. [VIEW](named-views.md) |
| VPORT · typed | X | X | X | L | L | L | L | L | L | Only first *Active configuration retained; other records/many fields discarded. `ReadTableEntry/ReadVPort/WriteVPort`. [B](version-feature-matrix-2026-09-12.md) |

## 8. Objects

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| DICTIONARY · typed | X | X | X | P | P | P | P | P | P | Known named collections, not arbitrary nested extension dictionaries. `ReadDictionary/WriteDictionary`. [B](version-feature-matrix-2026-09-12.md) |
| GROUP · typed | X | X | X | P | P | P | P | P | P | Named groups/entity references. `ReadGroup/WriteGroup`. [B](version-feature-matrix-2026-09-12.md) |
| LAYOUT · typed | X | X | X | P | P | P | P | P | P | Model/paper layouts and embedded plot settings subset. `ReadLayout/WriteLayout`. [B](version-feature-matrix-2026-09-12.md) |
| MLINESTYLE · typed | X | X | X | P | P | P | P | P | P | Multiline styles, not multileader styles. `ReadMLineStyle/WriteMLineStyle`. [B](version-feature-matrix-2026-09-12.md) |
| IMAGEDEF · typed | X | X | X | P | P | P | P | P | P | Definitions/selected dictionaries. `ReadImageDefinition/WriteImageDef`. [B](version-feature-matrix-2026-09-12.md) |
| IMAGEDEF_REACTOR · typed | X | X | X | L | L | L | L | L | L | Independent authored object not retained; regenerated. `ReadImageDefReactor/WriteImageDefReactor`. [B](version-feature-matrix-2026-09-12.md) |
| RASTERVARIABLES · typed | X | X | X | P | P | P | P | P | P | Raster display values/XData; owner 330 now points to the actual named dictionary containing ACAD_IMAGE_VARS. Broader object-graph validation remains open. [RASTER](raster-ownership.md) |
| DGNDEFINITION / DWFDEFINITION / PDFDEFINITION · typed | X | X | X | P | P | P | P | P | P | Three explicit underlay object cases. `ReadUnderlayDefinition/WriteUnderlayDefinition`. [B](version-feature-matrix-2026-09-12.md) |
| XRECORD · typed | X | X | X | L | L | L | L | L | L | Private cache/layer states, not generic public ownership graph. `ReadXRecord/ReadLayerState/WriteLayerState`. [B](version-feature-matrix-2026-09-12.md) |
| PLOTSETTINGS standalone · typed | X | X | X | M | M | M | M | M | M | Type exists in LAYOUT; standalone OBJECTS dispatch absent. `ReadObjects/ReadPlotSettings`. [B](version-feature-matrix-2026-09-12.md) |
| ACAD_PROXY_OBJECT / ACDBPLACEHOLDER · typed | X | X | X | M | M | M | M | M | M | Proxy/class/payload fidelity. [B](version-feature-matrix-2026-09-12.md) |
| ACDBDICTIONARYWDFLT / DICTIONARYVAR · typed | X | X | X | M | M | M | M | M | M | Default dictionaries and typed dictionary variables. [B](version-feature-matrix-2026-09-12.md) |
| ACDBNAVISWORKSMODELDEF · typed | X | X | X | M | M | M | M | M | M | Coordination-model definition. [B](version-feature-matrix-2026-09-12.md) |
| DATATABLE · typed | X | X | X | M | M | M | M | M | M | Object data table schema. [B](version-feature-matrix-2026-09-12.md) |
| DIMASSOC · typed | X | X | X | M | M | M | M | M | M | Associative dimension records/links. [B](version-feature-matrix-2026-09-12.md) |
| FIELD · typed | X | X | X | M | M | M | M | M | M | Field expression/evaluation records. [B](version-feature-matrix-2026-09-12.md) |
| GEODATA · typed | X | X | X | M | M | M | M | M | M | Georeferencing object. [B](version-feature-matrix-2026-09-12.md) |
| IDBUFFER / OBJECT_PTR · typed | X | X | X | M | M | M | M | M | M | Pointer-container records. [B](version-feature-matrix-2026-09-12.md) |
| LAYER_FILTER / LAYER_INDEX · typed | X | X | X | M | M | M | M | M | M | Layer filters/indexes. [B](version-feature-matrix-2026-09-12.md) |
| LIGHTLIST · typed | X | X | X | M | M | M | M | M | M | Light-object lists. [B](version-feature-matrix-2026-09-12.md) |
| MATERIAL · typed | X | X | X | M | M | M | M | M | M | Materials/textures/mappers and references. [B](version-feature-matrix-2026-09-12.md) |
| RENDERSETTINGS / MENTALRAYRENDERSETTINGS · typed | X | X | X | M | M | M | M | M | M | Rendering setting families. [B](version-feature-matrix-2026-09-12.md) |
| RENDERENVIRONMENT / RENDERGLOBAL · typed | X | X | X | M | M | M | M | M | M | Environment/global records. [B](version-feature-matrix-2026-09-12.md) |
| RAPIDRTRENDERENVIRONMENT / RAPIDRTRENDERSETTINGS · typed | X | X | X | M | M | M | M | M | M | RapidRT families. [B](version-feature-matrix-2026-09-12.md) |
| SECTION manager/settings/type/geometry objects · typed | X | X | X | M | M | M | M | M | M | Section-settings graph. [B](version-feature-matrix-2026-09-12.md) |
| SORTENTSTABLE · typed | X | X | X | M | M | M | M | M | M | Draw-order records. [B](version-feature-matrix-2026-09-12.md) |
| SPATIAL_FILTER / SPATIAL_INDEX · typed | X | X | X | M | M | M | M | M | M | Spatial clipping/filter/index graph. [B](version-feature-matrix-2026-09-12.md) |
| SUN / SUNSTUDY · typed | X | X | X | M | M | M | M | M | M | Sun configuration/study objects. [B](version-feature-matrix-2026-09-12.md) |
| TABLESTYLE · typed | X | X | X | M | M | M | M | M | M | Table style schema. [B](version-feature-matrix-2026-09-12.md) |
| VBA_PROJECT · typed | X | X | X | M | M | M | M | M | M | Inert VBA payload preservation, not execution. [B](version-feature-matrix-2026-09-12.md) |
| VISUALSTYLE · typed | X | X | X | M | M | M | M | M | M | Visual-style records and dependent references. [B](version-feature-matrix-2026-09-12.md) |
| WIPEOUTVARIABLES · typed | X | X | X | M | M | M | M | M | M | Standalone wipeout settings. [B](version-feature-matrix-2026-09-12.md) |

## 9. Common fields and dependency graphs

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| Layer/linetype/ACI/lineweight/scale/visibility · typed | X | X | X | P | P | P | P | P | P | Implemented common subset; defaults/off-layer semantics need fixtures. `ReadEntity/WriteEntityCommonCodes`. [B](version-feature-matrix-2026-09-12.md) |
| RGB true color · typed | X | X | X | P | P | P | P | P | P | 420 emitted without complete version policy; TrueColor appears in 2004 API history. [B](version-feature-matrix-2026-09-12.md) |
| Transparency · typed | X | X | X | P | P | P | P | P | P | 440 path exists; version eligibility/all flags unverified. [B](version-feature-matrix-2026-09-12.md) |
| Material / plot-style references · typed | X | X | X | M | M | M | M | M | M | Common 347/390 graph relationships incomplete. [B](version-feature-matrix-2026-09-12.md) |
| Color names / shadow mode / proxy graphics · typed | X | X | X | M | M | M | M | M | M | Common 430,284,92/310 paths absent. [B](version-feature-matrix-2026-09-12.md) |
| XData basic typed records · typed | X | X | X | P | P | P | P | P | P | Basic records/APPID registration; binary clone storage is now isolated. Full type-dependent transforms, quotas and handle-closure remapping remain open. [XDCL](xdata-clone.md), [B](version-feature-matrix-2026-09-12.md) |
| XData binary round trips · typed | X | X | X | T | T | T | T | T | T | Exact bytes/chunk counts across all formats and transports, #4. [B](version-feature-matrix-2026-09-12.md) |
| Extension dictionaries / persistent reactors · typed | X | X | X | P | P | P | P | P | P | Selected relationships; arbitrary dependency closure absent. [B](version-feature-matrix-2026-09-12.md) |
| Dynamic blocks / annotation / associative networks · typed | X | X | X | M | M | M | M | M | M | Static appearance may survive; graphs/evaluation absent. [B](version-feature-matrix-2026-09-12.md) |
| Unknown application/subclass fields · typed | X | X | X | M | M | M | M | M | M | No universal typed fallback. The raw API retains admitted primitive tag streams without interpreting unknown subclasses. [RAW](raw-document.md), [TAG](raw-tags.md) |

## 10. Raw preservation pipeline

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| Raw text/binary admitted profiles · raw | T | T | T | T | T | T | T | T | T | Complete admitted section grammar and primitive tags; typed historical geometry is not enabled. [RAW](raw-document.md), [R13](raw-r13-r14.md), [R12](raw-r12.md) |
| Raw unchanged same-transport bytes · raw | O | O | O | O | O | O | O | O | O | Original input bytes are retained for exact output; normalizing or editing intentionally loses lexical identity. [RAW](raw-document.md), [R13](raw-r13-r14.md), [R12](raw-r12.md) |
| Raw ordered unknown sections/records · raw | O | O | O | O | O | O | O | O | O | Generic preservation of admitted primitive tags, repeats, subclasses and inert application payloads. Not per-family schema validation. [RAW](raw-document.md), [REC](raw-records.md) |
| Raw scoped immutable record edits · raw | T | T | T | T | T | T | T | T | T | Record replacement/removal preserves other tag ranges; stale/foreign indexes rejected. Records are not BLOCK/POLYLINE dependency aggregates. [REC](raw-records.md) |
| Raw profile/framing consistency · raw | T | T | T | T | T | T | T | T | T | Explicit HEADER version/code page, correct binary dialect; no automatic schema down-conversion. [RAW](raw-document.md), [R13](raw-r13-r14.md), [R12](raw-r12.md) |
| Raw pre-R13 binary code widths · raw | T | N | N | N | N | N | N | N | N | AC1009 uses a single code byte plus 255/Int16 escape; modern codec constructors retain two-byte behavior. [R12](raw-r12.md) |
| Raw R13+ binary code widths · raw | N | T | T | T | T | T | T | T | T | Little-endian Int16 group code; validated against pinned legacy and modern fixtures. [R13](raw-r13-r14.md), [R12](raw-r12.md), [RAW](raw-document.md) |
| Raw declared character encoding · raw | CP | CP | CP | CP | CP | U8 | U8 | U8 | U8 | Legacy ANSI/DOS aliases; UTF-8 for 2007+. Strings retain raw escapes; invalid encoding is rejected, not replaced. [RAW](raw-document.md), [R13](raw-r13-r14.md), [R12](raw-r12.md) |
| Raw DIMSTYLE group-5 name context · raw | T | T | T | T | T | T | T | T | T | Arrow block names preserve spelling and are not remapped as identity handles; normal group 5 remains a strict handle elsewhere. [DIMBLK](dimstyle-arrow-names.md), [R13](raw-r13-r14.md), [R12](raw-r12.md) |
| Raw budgets, cancellation and stream ownership · raw | T | T | T | T | T | T | T | T | T | Encoded bytes, tags and decoded-string budgets; not total-heap accounting. Staged output can still be partial on final destination IO failure. [RAW](raw-document.md), [R13](raw-r13-r14.md), [R12](raw-r12.md) |
| Raw dependency-closed clone/import/remapping · raw | P | P | P | P | P | P | P | P | P | Partial: contextual index, selected outgoing traversal and guarded simultaneous exposed-handle renaming exist. Lexical aggregate extraction, cross-document import, private references and typed integration remain missing. [HINDEX](raw-handle-index.md), [HOPS](raw-handle-operations.md), [EMBED](raw-embedded-handle-context.md) |
| Raw headerless version inference · raw | M | M | M | M | M | M | M | M | M | This explicit-profile API requires HEADER and ACADVER. Headerless/default-version recovery is not implemented. [RAW](raw-document.md), [R12](raw-r12.md) |
| Raw atomic file replacement · raw | T | T | T | T | T | T | T | T | T | PR70: same-transport preservation or explicit conversion staged beside the destination before replacement/move. Failure cleanup and cancellation; no destructive fallback or full durability guarantee. [ATOMIC](atomic-file-save.md) |
| Contextual raw handle index · raw | T | T | T | T | T | T | T | T | T | PR71/75: identities, common owners, pointers, reactors, dictionaries, XData, arbitrary handles and seeds with diagnostic/occurrence budgets. Embedded/private payloads stay opaque; no historical-schema certification. [HINDEX](raw-handle-index.md), [EMBED](raw-embedded-handle-context.md) |
| Selected raw outgoing reference traversal · raw | T | T | T | T | T | T | T | T | T | PR72: selectable interpreted outgoing links with unresolved, ambiguous and opaque evidence. Not a standalone drawing extractor or proof of private dependency completeness. [HOPS](raw-handle-operations.md), [EMBED](raw-embedded-handle-context.md) |
| Guarded raw simultaneous handle remapping · raw | T | T | T | T | T | T | T | T | T | PR72/75: permutations, collision/ambiguity/dangling-capture/affected-opaque guards, seed advancement, immutable snapshots and numeric no-op behavior. Private strings/binary and arbitrary handles are not inferred references. [HOPS](raw-handle-operations.md), [EMBED](raw-embedded-handle-context.md) |
| Raw embedded-object handle isolation · raw | T | T | T | T | T | T | T | T | T | PR75: recognize the exposed101 Embedded Object separator outside established private controls/payloads; retain its entire tail as opaque until the record boundary. No typed columns or implicit post-embedded XData grammar. [EMBED](raw-embedded-handle-context.md) |

## 11. Confirmed next field-level work

| Feature / pipeline | R11/R12 | R13 | R14 | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Scope and evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| MESH subentity overrides · typed | X | X | X | V | V | V | M | M | M | Repeated90/91/92 need a distinct override context; current core topology checks do not implement this grammar. [MREAD](mesh-read-validation.md) |
| MTEXT column contexts · typed | X | X | X | M | M | M | M | M | M | Column type/count/flow/autoheight/width/gutter/heights, legacy linked records and context-sensitive code50 remain open. [MTBG](mtext-background.md), [B](version-feature-matrix-2026-09-12.md) |

## 12. Remaining implementation sequence

| Workstream | Required scope |
|---|---|
| Class-specific dependencies and typed integration | Embedded/private payload grammars, standalone BLOCK/POLYLINE aggregate extraction, cross-document clone/import and typed unknown-record preservation. Selected raw traversal/remapping is implemented, not complete semantic closure. |
| Finish partial typed records | MTEXT columns and linked legacy contexts; HATCH inner scalar ordering, spline relations, full affine geometry and associative source closure; MESH subentity overrides; extended UCS/VIEW/VPORT relationships. Existing Save remains nontransactional; SaveAtomic is opt-in. |
| Missing typed entity families | MULTILEADER, structured TABLE, LIGHT/SECTION and inert OLE/proxy/ACIS/surface payloads. HELIX is already implemented with scoped limitations, not a missing family. |
| Missing object families | Generic XRECORD/dictionary variants, draw order/spatial filters, MLEADERSTYLE/TABLESTYLE, FIELD/DIMASSOC/GEODATA, MATERIAL/VISUALSTYLE/rendering and sun families. |
| Historical typed dialects and schema-aware down-save | R12/R13/R14 typed grammar, earlier raw profiles, headerless inference and explicit per-property downgrade reports. Marker safety on raw profiles is not proof of historical feature legality. |
| Native interoperability and robustness | Native AutoCAD open/AUDIT/save/reopen remains unexecuted. Expand independent corpora, resource accounting, fuzzing and actual older-runtime execution; netstandard2.0 compilation is not execution on every consumer runtime. |

Every feature/fix requires an isolated PR, independently authored positive and malformed fixtures, applicable version/transport tests, explicit normalization/downgrade semantics, and green final-head CI before merge. Shared reader/writer/graph edits are sequenced; nonconflicting work can proceed independently. Do not substitute raw preservation, static appearance or synthesized values for tested semantic support.

## 13. Evidence and qualification

At the pinned production baseline, the .NET 8.0 conformance harness reports **16,765 passed / 0 failed** in Debug and Release. Linux/Windows GitHub Actions execute the SDK harness and compile netstandard2.0. These counts are regression evidence, not a percentage of DXF completeness.

Selected retained fixtures are also checked with **ezdxf 1.4.4** using the development-only `tools/verify_*.py` scripts. Some scripts compare ordered tags; others invoke that implementation's audit. Their individual notes specify which claim was actually tested. **No AutoCAD process was executed for this qualification.**

The Roslyn `tools/netDxf.FieldAudit` inventory records group-code branches, output expressions, declared properties, source locations and hashes. Literal code coverage is not semantic conformance: a consumed code may be ignored, derived or context-dependent. The detailed feature notes link the applicable Autodesk definitions and independent evidence.

Production target frameworks and strong naming are retained. net471/net48/net6.0 compilation or runtime behavior is not inferred from .NET 8 test success. Full qualification still requires native/versioned CAD fixtures, dependency-closure checks, malformed/resource-limited corpora, actual legacy runtime execution and explicit per-loss down-save reporting.

```sh
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
dotnet build netDxf/netDxf.csproj -f netstandard2.0 -c Release
```
