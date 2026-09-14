# Parallel DXF module checkpoint

The current implementation snapshot is [1c199e95133725d07a5573c76422edd57184065a](https://github.com/wieslawsoltes/netDxf/commit/1c199e95133725d07a5573c76422edd57184065a), tree `e54d79fe70e9326efe1ec66bc4d519edd416cca3`, published in [PR #84](https://github.com/wieslawsoltes/netDxf/pull/84) against `netstandard`. Documentation is committed after that implementation snapshot so the source pin is reproducible. The [PR checks](https://github.com/wieslawsoltes/netDxf/pull/84/checks) record platform qualification for the final published head.

## Delivered increments

[PR #83](https://github.com/wieslawsoltes/netDxf/pull/83) merged as `8adf300d9a68a5587fbcf95dcc5396ccf3b43c31`. It added raw OBJECTS transactions, typed named-object graphs, MTEXT column storage, physical VPORT configurations and LWPOLYLINE packet/reversal integrity. Its final [CI run](https://github.com/wieslawsoltes/netDxf/actions/runs/34901296627) passed Linux/Windows Debug/Release with 19,948 cases in each job; post-merge CI also passed. All 624 files in its retained source archive matched the published Git tree. Byte-pinned DXF fixtures use `-text` so Windows checkout preserves their original bytes.

PR #84 adds five coordinated modules:

| Module | Qualified behavior | Documentation |
| --- | --- | --- |
| LWPOLYLINE fidelity | Optional constant/per-vertex width presence, identifiers, reversal and representable width transforms | [Fidelity contract](lwpolyline-fidelity.md) |
| Common entity metadata | Color names, shadow mode and bounded opaque proxy caches on entities, ATTRIB and ATTDEF | [Common data](common-entity-data.md) |
| Typed containers | IDBUFFER, SORTENTSTABLE, SPATIAL_FILTER, explicit reference mapping and extension graph cloning | [Container schemas](typed-containers.md) |
| VIEW/VPORT UCS | Stored UCS bundle, named/base references, exact reference counts, flags and callback-safe lifecycle | [UCS relationships](view-ucs-relationships.md) |
| GEODATA | Public version-2 coordinate metadata, chunked definition and paired mesh data | [Geographic metadata](geodata.md) |

Twenty additional mixed-module scenarios exercise all six admitted profiles and both transports, plus cross-document copies and table removal. A single host extension holds draw order, geographic metadata and pointer lists; INSERT clipping metadata refers to the same entities. The tests deliberately allocate different source/destination handles and verify remapping, unchanged opaque sort keys, exact common bytes, independent mesh/metadata state and UCS reference release.

## Executed local evidence

| Check | Result |
| --- | --- |
| Full .NET 8 Debug conformance | 21,097 passed, zero failed; all case names unique |
| Full .NET 8 Release conformance | 21,097 passed, zero failed; all case names unique |
| Added cases since PR #83 | 1,149 |
| Independent verifier scripts, each configuration | 58 passed, zero failed |
| Independently opened DXF artifacts, each configuration | 1,540 distinct files, counted separately from overlapping verifier checks |
| Release library targets | netstandard2.0, net471, net48, net6.0 and net8.0 compiled successfully |
| Additional API build | netstandard2.0 Debug compiled successfully |
| Python ledger/runner tests | 18 passed |
| Roslyn field audit | Self-test passed; inventory covered 56 IO files, 563 IO methods and 190 model/header files |
| Coverage comparison | 222 scoped rows across nine profile columns |

The library builds retain 561 existing XML-documentation warnings per target and report zero errors. Compiling a target is not evidence of executing it on that target's runtime. Full runtime execution here uses .NET 8; CI additionally executes that harness on Linux and Windows.

The independent reader is ezdxf 1.4.4. Each checked-in `tools/verify_*.py` gate defines its own mandatory files and exact semantic/tag checks. The runner fails on missing fixtures, script errors and timeouts, retaining one log per gate and an aggregate JSON result. A separate read-only accounting hook counted the distinct DXF inputs opened by passing verifier processes; it does not change repository code or the CI gate. Geometric checks include 2,448 LWP arc/taper samples and 1,360 mixed-module reversal samples per configuration. Proxy bytes, clipping transforms and geographic metadata are storage checks, not rendering or coordinate-evaluation claims.

Review also used independent adversarial probes: 60 container checks, 142 GEODATA checks and 10 VIEW/UCS lifecycle checks. These exposed and verified fixes for failed-attachment ownership leaks, caller-enumerator destination mutation, opaque sort-key allocator interference and rename/reference consistency. They supplement the checked-in regression tests and mandatory independent gates.

## Remaining scope

The [coverage ledger](coverage.json) and [generated matrix](version-feature-matrix.md) are authoritative for precisely scoped support. `full_standard_complete` and `autocad_executed` remain false. Native AutoCAD open/AUDIT/save/reopen, historical typed dialects, universal dependency import, unknown typed entity preservation, rendering/geometric evaluation and private payload schemas remain distinct qualifications.

Further public work includes style/font fidelity, DIMSTYLE field/override parity, standalone output settings, MULTILEADER/MLEADERSTYLE, structured TABLE/TABLESTYLE, SECTION, FIELD/DIMASSOC, material/rendering families and ACIS/surface payloads. Later work must update individual rows with implementation and evidence rather than treating regression count as a completeness percentage. Unsupported private GEODATA versions, UCS-record base relationships, VPORT frozen-layer ambiguities and internal VERTEX common metadata remain explicitly outside this checkpoint.
