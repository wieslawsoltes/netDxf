# Stored TABLE, DATATABLE and LAYER_INDEX checkpoint

The implementation snapshot is [295e5eef289036dcdc7e0d2134f21d370a239404](https://github.com/wieslawsoltes/netDxf/commit/295e5eef289036dcdc7e0d2134f21d370a239404), tree `f7a1d389a0144d2f5e55434a943cba2cb3aa3cc2`, published in [PR #87](https://github.com/wieslawsoltes/netDxf/pull/87) against `netstandard`. This qualification documentation follows the implementation commit. The [PR checks](https://github.com/wieslawsoltes/netDxf/pull/87/checks) qualify the final published documentation head separately.

The baseline is merged [PR #86](https://github.com/wieslawsoltes/netDxf/pull/86), commit `5d61266a589e0fc7198b4b48b7c2096561d60237`. Its four final Linux/Windows Debug/Release jobs passed 22,994 cases each; its post-merge checks passed. All 945 archived source files matched its final published tree. See the [object lifecycle checkpoint](checkpoint-lifecycle-2026-09-15.md) for that increment's scope.

## Delivered behavior

This increment adds 885 cases to the baseline:

| Area | Qualified behavior | Added cases |
| --- | --- | ---: |
| [Stored TABLE](stored-tables.md) | Immutable source payload, conservative literal grids, scoped public STYLE bindings, FIELD precedence and legacy text chunks | 174 |
| [DATATABLE](datatable.md) | Version-2 rectangular data, eleven stored cell kinds, explicit references, reciprocal ownership, atomic replacement, copying and erasure | 467 |
| [LAYER_INDEX](layer-index.md) | Ordered names and owned IDBUFFERs, stored timestamps, complete repeated/null counts and explicit lifecycle | 140 |
| [Imported source references](source-reference-identity.md) | Retained same-record identity, explicit legacy conversions and common typed reference resolution | 76 |
| [Numeric handles](source-reference-identity.md) | Equivalent hexadecimal lookup, semantic nulls, mapped copying and erasure guards | 16 |
| [Mixed lifecycle](../FifthMixedModulesQualification.md) | Combined graphs, exact external maps, APPID rename, source/copy teardown and independent output checks | 12 |

`StoredTable` replaces the lossy conversion of ACAD_TABLE to an INSERT. The complete ordered subclass payload stays in its source document and version. The recognized flat representation exposes an immutable row/column grid, stored cell types and flags, and optional literal values. Unknown or ambiguous content does not become an evaluated value. Complete known backing scalars can be compared; incomplete backing schemas produce an unknown result. Table-specific editing, nonidentity transforms, cloning, cross-document adoption, reattachment and profile conversion reject.

Known display-block and public text-style names bind to exact retained resources. Renaming the resource updates only the bound fields, and removal is guarded. Direct cell FIELD references take precedence over literal projection; private application and ACVALUE fields remain separate. Legacy text continuations are combined only when their 250-character chunks and shorter terminal chunk satisfy the public structure. Common entity metadata and XData retain their ordinary interfaces.

DATATABLE supports R2004 and later typed storage with independent column/cell values, finite numeric data and explicit null object cells. Object IDs, hard/soft pointers, and hard/soft owners remain distinct. Owner cells require reciprocal ownership; replacing registered columns validates the complete retained child set before mutation. Copying requires explicit external mappings, and terminal erasure checks incoming references. Unknown variants and older-profile records remain opaque.

LAYER_INDEX retains ordered unresolved layer-name strings and distinct owned IDBUFFERs. Public grouped and interleaved forms preserve ordinal correspondence. Input counts include repeated and null buffer entries and must match; output counts follow explicit buffer edits. Private leading-count variants remain wholly opaque. The implementation does not evaluate or regenerate a spatial/layer index.

The shared reader no longer lets generated defaults stand in for missing or discarded source records. An accepted target must come from the same physical record's common identity and remain that registered instance. Named collection conversions require their explicit source dictionary designation and owner. The consumed layer-state extension mapping applies only to its owning LAYER table. The checks cover DATATABLE cells, common database owners, dictionary entries/defaults, extension dictionaries, persistent reactors, IDBUFFER, SORTENTSTABLE and LAYER_INDEX references; they do not interpret hidden private handles.

Public lookup now treats case and leading zeros as the same numeric hexadecimal identity. Semantic zero references remain absent during validation and copying, independently of the document's public zero identity. Writers preserve spelling held in the model; mapped copies use actual destination handles for semantic references and leave arbitrary 320–329 data untouched. The established typed reader canonicalizes hexadecimal strings on load. This increment does not promise lexical round trips through typed loading.

## Executed evidence

The [machine-readable receipt](table-storage-qualification.json) records the implementation pin and matching results.

| Check | Result |
| --- | --- |
| Full .NET 8 Debug conformance | 23,879 passed, zero failed; all names unique |
| Full .NET 8 Release conformance | 23,879 passed, zero failed; identical result set |
| Retained DXF artifacts, each configuration | 2,397 |
| Mandatory independent scripts, each configuration | 78 passed, zero failed |
| Distinct DXFs opened by the independent scripts, each configuration | 2,296 |
| Release library targets | netstandard2.0, net471, net48, net6.0 and net8.0 compiled successfully |
| Additional API build | netstandard2.0 Debug compiled successfully |
| Python coverage/runner tests | 18 passed |
| Roslyn field audit | Self-test passed; 77 IO files, 633 methods and 232 model/header files; input file hashes match the implementation |
| Coverage comparison | 251 scoped rows across nine profile columns |

Every library target reports zero errors and retains 561 existing XML-documentation warnings. Runtime tests execute on .NET 8; compilation does not establish runtime behavior on every consumer framework. No production package dependency was added. ezdxf 1.4.4, the pinned independent producers and the audit tools are development dependencies.

The TABLE gate compares sixteen extracted native packets and four full-source outputs, and rejects 32 altered packet controls. The separate public-field gate requires 44 synthetic carriers and rejects 44 actual parsed-output alterations. The pinned native corpus does not contain those public-field variants, so that evidence remains explicitly synthetic. An independent field review reproduced eight failures before correction and passed all 24 probes afterward.

The DATATABLE gate requires 34 outputs. Its two pinned native R2004 tables and 41 owned XRECORD children yield 86 complete-record comparisons across transports. LAYER_INDEX requires 72 outputs and compares twelve independent producer originals/extractions and 72 mapped source packets. No actual native LAYER_INDEX record was found in the assessed native corpus; producer evidence is not described as native AutoCAD execution. The focused [DATATABLE](datatable-qualification.json) and [LAYER_INDEX](layer-index-qualification.json) receipts retain these boundaries.

The mixed gate requires 48 drawings and twelve actual clone maps per configuration, rejects 90 parsed-output corruption controls, and also rejects omitted required drawings/maps. Its native TABLE subclass packets are unchanged. The fixture's synthetic XRECORD substitution for one table pointer is disclosed and does not qualify TABLESTYLE semantics. Source and copied graphs preserve ownership, null/repeated references, explicit external identities and arbitrary handle data through allowed edits and teardown.

Independent source-reference review passes the existing 106-case matrix, two consumed-extension ownership cases, and the intended results of 36 retained provenance observations. Twelve of those provenance observations are valid source-dictionary controls and remain accepted with clean validation. The final shipping suite additionally covers payload-only identities and nested INSERT/ATTRIB/SEQEND parsing. All independent gates operate on actual exported files; a read-only accounting hook records opened paths without modifying inputs or outcomes.

## Remaining boundaries

The [coverage ledger](coverage.json) retains `full_standard_complete: false` and `autocad_executed: false`. Native AutoCAD open/AUDIT/save/reopen, rendering, formula and FIELD evaluation, table editing/regeneration, complete TABLESTYLE/TABLECONTENT/TABLEGEOMETRY/CELLSTYLEMAP schemas and universal dependency import remain unqualified.

SECTION/SECTIONSETTINGS, stored TABLESTYLE and SUN ownership are being developed on separate branches. Other open work includes FIELD/DIMASSOC, material/rendering families, modern SAB/ACDSDATA and surfaces, historical typed dialects, typed unknown entities and hidden private dependencies. Partial record work remains in HATCH geometry/associativity, MESH overrides, internal VERTEX metadata, UCS base relationships and VPORT frozen-layer behavior. Preserving stored values and opaque packets does not establish application evaluation or complete native interoperability.
