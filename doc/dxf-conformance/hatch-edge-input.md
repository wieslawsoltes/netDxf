# Single-pass HATCH boundary edge input

`HatchBoundaryPath(IEnumerable<HatchBoundaryPath.Edge>)` now captures the input
sequence once before classifying its representation. The previous implementation
called Count() on the caller's enumerable inside every foreach iteration. This
broke single-use iterators, gave inconsistent classification for stateful
sequences, and could traverse a general sequence quadratically.

The owned list snapshot determines whether the path contains exactly one
Polyline edge. A lone polyline remains a polyline boundary; polylines mixed
with other edges still use the existing explosion routine. Ordinary edge objects
retain reference identity. The list container, not every mutable edge object,
is isolated from the caller. Empty inputs retain their previous constructor
behavior; later HATCH adoption rules are not bypassed.

Null input retains ArgumentNullException("edges"). A null element now raises
ArgumentException("edges") rather than a NullReferenceException. Producer
exceptions propagate and the enumerator is disposed. No partially constructed
path is returned. Materialization adds temporary O(n) reference storage and is
not a limit on caller-controlled input size, edge evaluation cost, or concurrent
mutation. No wall-clock performance benchmark or immutable edge model is claimed.

## Evidence

The same **58 new tests** pass **5 before / 58 after** the correction. The negative
run uses the final harness with the preserved preceding library assembly, not a
stub. Tests cover empty, lone-polyline, line-only, mixed and circular paths with
single-use, repeatable and changing enumerables; traversal and disposal counts;
null items; producer failures; list isolation; clone independence; and 36
text/binary round trips in the six existing typed profiles.

`tools/verify_hatch_edge_input.py` checks the exact ordered boundary-data packet
(from group 91 through the source-handle count), not the entire HATCH record.
Its 36 drawings distinguish retained-polyline and expanded line representations.
All have zero independent graph errors/repairs. The same validator rejects
**2,196** changed, missing and duplicated boundary tags. Missing or extra fixture
inventories also reject. Full-suite and exact-head hosted results are recorded
in the PR; focused evidence is not substituted for them.

```sh
DXF_TEST_FILTER=hatch-edge-input/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_hatch_edge_input.py artifacts/conformance
```

This changes enumerable handling and null-element diagnostics, not bulge
geometry, boundary projection, source ownership, native HATCH filling, or a
historical DXF dialect. The subsequent bulge-orientation task is separate.
Native AutoCAD open/AUDIT/save/reopen, fonts/visual equivalence, private FIELD/
TABLE/cache regeneration, dependency-complete import and general version
conversion remain unqualified. No all-version AutoCAD parity is established.
