# Single-pass polyface construction

The `IEnumerable<short[]>` constructor formerly counted its source and then
called `ElementAt` for every face. A one-shot source failed; repeatable lazy
sources were traversed quadratically and could yield inconsistent snapshots.
The constructor now materializes faces in one traversal, copying each current
index sequence before advancing its enumerator. Reused iterator buffers are safe.

Both constructor overloads report `faces` rather than `vertexes` for an empty
face collection. Existing index validation, signed-edge semantics, copied vertex
arrays, and borrowed face-object identities remain unchanged. Event callbacks
are attached only after every materialized face passes validation. Input iterators
are disposed on failure. Infinite sources are not supported and no new hard
face-count limit is imposed. Concurrent mutation is outside the contract.

The 17 model regressions exercise five single-use sizes (including 257 faces),
reused buffers, borrowed face instances, array isolation, null/empty inputs,
disposal on invalid input, and absence of attached callbacks after a failed
construction. The same tests pass 9 before / 17 after the correction.

```sh
DXF_TEST_FILTER=polyface-construction/ dotnet run --project tests/netDxf.Conformance -c Release
```

This corrects enumerable API behavior, not a new DXF dialect or native AutoCAD
command equivalence. No wire encoding or geometry calculation changes. Complete
source-bound regression and hosted results are recorded separately in the PR.
