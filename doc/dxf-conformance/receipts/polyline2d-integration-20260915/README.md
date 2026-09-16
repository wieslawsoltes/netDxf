# Legacy 2D recovery against the PR94 baseline

The five pending source/evidence commits `cf41048`, `b2a4e86`, `ae754cb`,
`1dc4e91` and `171bcf0` were recovered onto `bb8c73c`. Merge resolution retained
the newer opaque-entity clone and graph-removal protections. The final source
commit for this qualification is `ee59528`.

The integration adds legacy VERTEX/SEQEND records to version compatibility
analysis and excludes them from the lightweight-only vertex-identifier rule.
The existing ordinary-target removal matrix now includes both legacy child
roles, testing authored and reloaded references, entity and containing-block
targets, release, and clean parent detachment.

| Gate | Debug | Release |
| --- | ---: | ---: |
| Legacy retained records | 280/280 | 280/280 |
| Complete version compatibility suite | 734/734 | 734/734 |
| Retained record target removal | 432/432 | 432/432 |
| Lightweight fidelity | 328/328 | 328/328 |
| Polyline/mesh sibling regressions | 2,578/2,578 | 2,578/2,578 |
| Total conformance cases | 4,352/4,352 | 4,352/4,352 |
| Replayed independent legacy probe | 220/220 | 220/220 |
| Independent probe output audit | 372/372 | 372/372 |

The eight raw verifier gates qualify 832 outputs and exercise 922 actual
corruption controls per configuration. The lightweight gate additionally checks
2,448 reversed cross sections. The independent reader is ezdxf 1.4.4; these gates
reported no audit errors or repairs. `actual-outputs.tar.gz` preserves all 3,124
produced DXFs across both configurations, including additional compatibility
and sibling files outside those eight named verifier inventories. Do not
interpret the total archive count as the raw-gate qualification count.

The first new profile matrix expected only retained-chain and CLASS diagnostics
from complete producer documents. Sixteen cases correctly included an unrelated
producer VPORT SUN diagnostic. The final matrix isolates a clean retained clone
in a new source-profile document, as the existing Polyface test does. The first
results are preserved in `initial-compatibility-results.json`; this was a test
fixture correction and did not change production code.

The earlier `polyline2d-records` receipt remains historical evidence for the
original source commits. This receipt is a replay against the later integrated
source, using the unchanged independent probe from that historical receipt.
It does not claim execution in a native CAD application, new native-source
coverage, or other target-framework/runtime qualification.

## Reproduction

Run from the repository root with .NET SDK 8.0.408 and dependencies from
`tools/requirements-independent.txt`. Build the conformance executable with:

```sh
dotnet build tests/netDxf.Conformance/netDxf.Conformance.csproj -c Debug -p:TargetFrameworks=net8.0
```

Run that executable separately with `DXF_TEST_FILTER` set to each filter below
and `DXF_TEST_ARTIFACTS` set to a fresh directory for that filter:

| Output directory | Filter | Independent verifier |
| --- | --- | --- |
| legacy | `legacy2d-records/` | `verify_polyline2d_records.py` |
| compatibility | `version-compatibility/` | Report checked against actual writer in the conformance test |
| removal | `retained-record-target/` | Registration/reference checks in the conformance test |
| lw | `lw-fidelity/` | `verify_lwpolyline_fidelity.py` |
| siblings | `poly` | `verify_polyline3d_records.py`, `verify_polygonmesh_records.py`, `verify_polyface_records.py`, `verify_polyface_grammar.py`, `verify_polygonmesh_cardinality.py`, `verify_polyline_topology.py` |

Each verifier is under `tools` and accepts its corresponding output directory.
The conformance executable is
`tests/netDxf.Conformance/bin/Debug/net8.0/netDxf.Conformance.dll`.

Build the independent probe at
`doc/dxf-conformance/receipts/polyline2d-records/independent/probe/Probe.csproj`
with `-c Debug` and `-p:DxfReviewLibrary=` pointing to the absolute path of the
newly built `netDxf/bin/Debug/net8.0/netDxf.netstandard.dll`. Run its `Probe.dll`
with the fixture directory `tests/fixtures/polyline2d-records` and a fresh
independent-output directory as arguments. Run
`doc/dxf-conformance/receipts/polyline2d-records/independent/audit_outputs.py`
against that output directory. Repeat all steps with Release.

`qualification.json` records final source and assembly identities. The archive
checksum and per-output checksums in `output-manifest.json` cover the preserved
actual output bytes. Build, verifier, probe and individual case results are
stored under each configuration directory.
