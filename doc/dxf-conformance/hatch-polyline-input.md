# HATCH polyline input: optional bulges and counted-list validation

Baseline: `713f88f3676c174d2297970f10921093420b6251`, after merged PR #45.

## Corrected parsing and allocation defects

The reader treated the has-bulge flag as a promise that every vertex had a group-42 tag. Autodesk instead defines the per-vertex bulge as optional, with zero as its default. Sparse bulges therefore shifted the old positional parser into the next vertex or reference count. Interleaved group-999 comments caused the same misalignment. Unchecked group codes could substitute unrelated coordinates, and untrusted vertex/reference counts directly controlled array/list capacities.

The polyline sub-parser now checks required group codes 72, 73, 93, 10, 20, 97 and 330; consumes group 42 only when present; and ignores comments on every local advance. It validates canonical 0/1 flags, nonnegative counts, exact vertex/reference list boundaries and excess list data. Vertex and reference storage grows from actually consumed records, not declared capacities. Physical truncation retains the strict codec's EndOfStreamException; structural errors provide HATCH/polyline, group-code and position context. Public Load keeps its existing Debug exception / Release null behavior.

The supplied flag is validated, but an actual group 42 is retained even when a zero has-bulge flag contradicts it. This is a deliberate producer-tolerance policy, not a claim that contradictory flags are canonical authoring. Zero-sized lists remain accepted by this transport parser without certifying valid filled-area geometry. Source references are passed through the existing deferred ownership resolver; this change does not repair unresolved references or implement a dependency graph.

## Version and fidelity contract

| Operation | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Sparse/dense/omitted vertex bulges | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Local comment skipping | Text | Text | Text | Text | Text | Text |
| Count, component and source-reference validation | Tested | Tested | Tested | Tested | Tested | Tested |
| Adjacent paths, seeds and XData remain intact | Tested | Tested | Tested | Tested | Tested | Tested |

The writer remains unchanged: normalized output includes explicit zero bulges. The contract is preservation of vertex/bulge values and topology, not original optional-tag presence or lexical spelling. The separate raw API provides unedited byte preservation. No historical typed profile, global comment filter, general HATCH edge parser, automatic boundary repair or writer preflight is introduced.

## Executed evidence

The initial 54 safely executable regressions cover masks with no/one/sparse/all bulges plus interleaved comments across six versions and both transports. Unchanged PR #45 production reports **6,658 passed / 42 failed** in both Debug and Release. The revised parser passes the same 6,700 cases.

After removing count-driven allocation, another 396 cases exercise 24 malformed-list scenarios per version/transport, contradictory flags, empty lists, actual associative source references, adjacent boundaries, huge declared counts and physical truncation. These additional probes were not executed against the unsafe old allocation path. The complete signed-library suite reports **7,096 passed / 0 failed** in both local .NET 8 configurations: 450 additional cases over PR #45. The forged-count tests measure allocation for a tiny input and require less than 4 MiB, not a universal memory budget for arbitrarily large valid files.

The optional ezdxf 1.4.4 verifier checks twelve output drawings for exact vertices, sparse bulges, closure, elevation, seeds and following XData before calling audit; all have zero errors and zero repairs. It does not run AutoCAD. Final-head Linux/Windows Debug/Release, netstandard2.0 and documentation/source checks remain merge gates.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_polyline_input.py artifacts/conformance
```

## Primary sources and remaining work

Autodesk Boundary Path Data defines group 42 as optional/default zero, group 93 as the vertex count, group 97 as the source-reference count and group 330 as source handles:
https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm

Path flags, counted line/arc/ellipse/spline edges, spline fit/tangent metadata, ownership closure, arbitrary-affine fidelity and native CAD qualification remain separate work. These tests do not make the whole HATCH record complete.
