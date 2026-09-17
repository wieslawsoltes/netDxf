# Entity clone fidelity review

This C# audit continues merged PR #106 at
`cd2b4b2d0949eaa85ff517b438a1838f8fb25ffb`, tree
`68dd970d70c3d79c8759aef02e4c9f492f2a8f06`. JavaScript PR #98 is not changed.

## Corrections

`Solid.Clone()` and `Trace.Clone()` now retain elevation as well as the four
OCS corners and thickness. Previously a nonzero-elevation face moved to the
zero-elevation plane when cloned, including through nested block cloning.

`Shape.Clone()` now retains the authored width factor, including a negative
factor. It does not render or reinterpret SHX geometry.

`Polyline3D.Clone()` now copies the stored smoothing type alongside its flags
and control vertices. Previously a quadratic/cubic clone retained the spline-fit
flag but defaulted to `NoSmooth`. Copying the backing field avoids recomputing or
normalizing the original flags. Existing retained-source-record clone guards
remain unchanged; this is not a new dependency-import API.

`Leader.Clone()` now copies its stored direction without repeating normalization
and deep-clones its mutable `LineColor`. Previously an edited clone could change
the original's line color. Hookline topology, annotation state and explicit
`Update(...)` responsibilities remain unchanged; cloning does not regenerate
leader geometry.

```csharp
var original = new Solid(new Vector2(1, 2), new Vector2(4, 3),
    new Vector2(2, 7), new Vector2(6, 8)) { Elevation = 7.25, Thickness = -2.5 };
var copy = (Solid)original.Clone();
// copy.Elevation == 7.25; source and clone have independent common metadata.
```

These are corrections to existing clone APIs, not new public APIs. Callers which
worked around omitted fields by assigning them after cloning may remove those
workarounds. Native handles are not copied. A cloned entity in a cloned block
still belongs to that cloned block; it is not implicitly detached from it.

## Regression evidence

The same 141 new tests were executed against the unmodified production baseline
and the corrected implementation. Baseline: **10 passed, 131 failed**. Corrected:
**141 passed, zero failures**. Seven fixture kinds cover SOLID, TRACE, SHAPE,
quadratic/cubic Polyline3D, and LEADER with/without an MTEXT annotation. Model
cases cover positive/negative values, oblique normals, direct/block/nested INSERT
cloning, independently mutable colors, layers, annotations, arrays, proxies and
XData. Wire cases save/reload twice with alternating text/binary transport in all
six existing typed profiles, R2000/R2004/R2007/R2010/R2013/R2018.

Local Linux .NET 8.0.31 / SDK 8.0.425 runs pass **38,856 unique cases in both Debug
and Release**, with zero failures and all 38,715 baseline cases retained. Each
configuration emits 9,501 DXF files. Their result JSON is byte-identical,
SHA-256 `9d088d3ccab2d777e8f06dda1a376076726b20346c609a995ae15755373e33fd`.
The official SDK archive was checked against Microsoft's SHA-512 release metadata
before use. Local builds targeted net8.0 only; Windows and netstandard2.0 results
must be taken from the PR's actual CI runs, not inferred from these Linux runs.

All **146 independent verifiers** passed against the full Debug output. The new
`verify_entity_clone_review.py` checks exactly **168 drawings** and rejects
**20,904 actual-packet corruptions**. It independently decodes physical records
with ezdxf 1.4.4, checks author-supplied field constants, then compares complete
ordered source/clone packets. Only corresponding root/owned-child/annotation
identities are renamed; all other tags must match. POLYLINE includes control,
fit and SEQEND packets. LEADER includes annotation packets and reactor links.
Every drawing also passes ezdxf audit with zero errors and zero repairs.

```sh
DXF_TEST_FILTER=clone-review/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_entity_clone_review.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance --jobs 4
```

The historical 297-row coverage comparison is unchanged; all 18 ledger tests
pass. These results do not qualify native AutoCAD open/AUDIT/save/reopen, visual
or font fidelity, the spline-fitting algorithm, or arbitrary clone/import graphs.
The SHAPE round trips use the existing checked-in `ltypeshp.shx` only for name
resolution. No new proprietary fonts or external implementation code are added.

## Ongoing audit

This task fixes the six identified clone-state defects in five entity classes.
It is not a claim that every entity, table, object, private packet or historical
DXF dialect has completed audit. SOLID/TRACE affine normals, signed thickness,
projective-input handling and mutation safety are a separate follow-on task.
Full TABLE/private-cache regeneration, native FIELD equivalence, recursive
resource import, historical typed dialects and general version conversion remain
outside this clone contract. The shared ChatGPT page could not be fetched; the
merged GitHub source and its checksum-verified qualification artifact were used
as the continuation baseline.
