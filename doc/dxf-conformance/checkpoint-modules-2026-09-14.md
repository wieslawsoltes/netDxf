# Parallel DXF module checkpoint

The current implementation snapshot is [005d5401c5c6514e54a64121ac5888f7525a4eca](https://github.com/wieslawsoltes/netDxf/commit/005d5401c5c6514e54a64121ac5888f7525a4eca), tree `731b4c04178e4888af6921cabbd99eb958f0e3b2`, published in [PR #85](https://github.com/wieslawsoltes/netDxf/pull/85) against `netstandard`. Documentation follows the implementation commit so the source pin is reproducible. The [PR checks](https://github.com/wieslawsoltes/netDxf/pull/85/checks) record platform qualification for the final published head.

## Previously merged increments

[PR #83](https://github.com/wieslawsoltes/netDxf/pull/83) merged as `8adf300d9a68a5587fbcf95dcc5396ccf3b43c31`. It added raw OBJECTS transactions, typed named-object graphs, MTEXT column storage, physical VPORT configurations and LWPOLYLINE packet/reversal integrity. Its final [CI run](https://github.com/wieslawsoltes/netDxf/actions/runs/34901296627) passed Linux/Windows Debug/Release with 19,948 cases per job; post-merge CI also passed. All 624 files in the retained source archive matched the published Git tree. Byte-pinned DXF fixtures use `-text` so Windows checkout preserves their original bytes.

[PR #84](https://github.com/wieslawsoltes/netDxf/pull/84) merged as `13af5f9ff83d005d9e796ed7c99bcb71ab5ebdd9`. It added [optional LWPOLYLINE widths and identifiers](lwpolyline-fidelity.md), [common graphical metadata and proxy caches](common-entity-data.md), [typed pointer/draw-order/clipping containers](typed-containers.md), [VIEW/VPORT UCS relationships](view-ucs-relationships.md), and [public GEODATA metadata](geodata.md). Twenty mixed scenarios exercise those modules together. Its final [CI run](https://github.com/wieslawsoltes/netDxf/actions/runs/34904853320) passed all four platform/configuration jobs with 21,097 cases each; post-merge CI passed. All 709 source-archive files matched the published tree. Local independent gates passed 58 scripts and opened 1,540 distinct DXF files per configuration.

## Current delivered increment

PR #85 adds five coordinated modules and an attribute-construction fix:

| Module | Qualified behavior | New scenarios | Documentation |
| --- | --- | ---: | --- |
| DIMSTYLE parity | Stored 142/145/288, explicit header presence, corrected DIMRND/DIMALT overrides and clone fields | 39 | [Stored settings](dimstyle-stored-settings.md) |
| STYLE fidelity | Complete stored flags, optional last height, independent font files and canonical ACAD font prefix | 291 | [Font metadata](text-style-fidelity.md) |
| ACIS SAT | Inert BODY/REGION/3DSOLID envelopes through 2010, exact chunks, decoded lines and absent/zero history | 242 | [SAT contract](acis-sat.md) |
| Output settings | Standalone PLOTSETTINGS/WIPEOUTVARIABLES, shared LAYOUT payload, independent scales and shade references | 66 | [Output settings](output-settings.md) |
| MULTILEADER | One stored context, nested text/block/leader data, exact resources and MLEADERSTYLE objects | 269 | [Contexts and styles](multileader-contexts.md) |
| Attribute defaults | Empty values/prompts and consistent null-style argument validation | 25 | [Empty values](attribute-default-values.md) |
| Mixed integration | Twelve profile/transport drawings and six mapped copies with shared styles, blocks and object metadata | 18 | [Executable scenarios](../../tests/netDxf.Conformance/ThirdMixedModuleTests.cs) |

The mixed cases combine STYLE, DIMSTYLE/DSTYLE, TEXT/ATTDEF/ATTRIB, page setups, layouts and WIPEOUT variables in every admitted profile. SAT block content participates through 2010 and MULTILEADER participates from 2007, with all major modules overlapping in 2007/2010. Unsupported combinations are tested explicitly. Copied documents use deliberately different handles; independent sidecars and file inspection verify actual reference remapping. The shade object uses a pinned, reference-free VISUALSTYLE payload from the independent producer fixture; it is separately registered in the destination and mapped explicitly.

Shared fixes include exact-identity clone maps, callback-safe STYLE/LTYPE/BLOCK rename indexes, coherent BLOCK/BLOCK_RECORD removal and re-addition, complete Matrix4 identity guards, malformed embedded subclass rejection, and before-output validation that avoids lazy-database allocation on rejected fresh-layout saves. The clone-map review reproduced six same-name decoy-key acceptances and three mutable-name hash failures before the fix; all nine now pass with exact reference identity and stable hashing.

## Executed local evidence

| Check | Result |
| --- | --- |
| Full .NET 8 Debug conformance | 22,047 passed, zero failed; all names unique |
| Full .NET 8 Release conformance | 22,047 passed, zero failed; all names unique |
| Added cases since PR #84 | 950 |
| Retained DXF artifacts, each configuration | 1,843 |
| Independent verifier scripts, each configuration | 65 passed, zero failed |
| Distinct DXF files opened by verifiers, each configuration | 1,742 |
| Release library targets | netstandard2.0, net471, net48, net6.0 and net8.0 compiled successfully |
| Additional API build | netstandard2.0 Debug compiled successfully |
| Python ledger/runner tests | 18 passed |
| Roslyn field audit | Self-test passed; 63 IO files, 604 IO methods and 218 model/header files |
| Coverage comparison | 234 scoped rows across nine profile columns |

The library builds retain 561 existing XML-documentation warnings per target and report zero errors. Compilation does not establish runtime behavior on those targets. Local full-suite execution uses .NET 8; CI also executes that harness on Linux and Windows.

The independent reader is ezdxf 1.4.4. Each checked-in `tools/verify_*.py` gate defines mandatory files and exact semantic/tag checks. The runner fails on missing fixtures, script failures and timeouts, retaining a log per gate and aggregate JSON results. A separate read-only accounting hook records distinct opened DXF files without changing the primary gate. The new mandatory corpora include 24 DIMSTYLE, 72 STYLE, 48 SAT, 24 output-settings, 16 MULTILEADER and 18 mixed drawings. Output-settings checks cover 792 named page setups and 24 embedded layouts per configuration. SAT checks compare exact chunks and full mesh vertices/faces for the known cube/planar fixtures; this is not a modeler implementation.

Separate adversarial reviews passed 192 STYLE API probes, 66 SAT probes, 126 output-settings/clone probes and 12 MULTILEADER parser probes. The attribute review independently reproduced six Debug writer aborts and verified their fixes. These supplement the checked-in cases and mandatory independent gates; they do not substitute for final-head CI.

## Remaining scope

The [coverage ledger](coverage.json) and [generated matrix](version-feature-matrix.md) define exact support. `full_standard_complete` and `autocad_executed` remain false. Native AutoCAD open/AUDIT/save/reopen, historical typed dialects, actual older-runtime execution, rendering/geometric evaluation and private payload schemas remain separate qualifications.

Further public work includes structured TABLE/TABLESTYLE and their backing objects, SECTION, FIELD/DIMASSOC, material/rendering and sun families, OBJECT_PTR/SPATIAL_INDEX/LAYER_FILTER/VBA_PROJECT, modern SAB/ACDSDATA and surfaces. Typed dictionary name removal still only unlinks; an explicit erase operation with ownership closure and incoming-reference checks remains pending at this snapshot. Generic object cloning requires explicit external mappings and rejects opaque subtrees.

Other remaining areas include unknown typed entity preservation, universal dependency import, complete HATCH geometry/association behavior, MESH subentity overrides, internal VERTEX common metadata, UCS-record base relationships and VPORT frozen-layer ambiguities. MULTILEADER style evaluation, annotation contexts, tolerance content and transforms are not implemented. Output settings retain inherited shade groups whose complete historical legality is not yet qualified. Existing entity-specific association teardown still applies during block removal. SaveAtomic remains opt-in; legacy Save(string) is nontransactional.
