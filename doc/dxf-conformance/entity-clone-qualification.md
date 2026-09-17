# Supplementary entity clone qualification

This task extends PR #107 without changing its production implementation.
Its pinned base is `f9261b741c1e8d5c5f51a6fdb6ecdaea1c6fc6fb`, source tree
`664662aa92f0808c7cc3ca8a689980f6fdde3dec`. All 141 original clone cases,
their original `clone-review` fixture inventory, verifier, guide and the five
corrected entity classes remain unchanged. The separate JavaScript PR #98 and
its pinned C# source are untouched.

## Additional coverage

The new `clone-qualification` prefix is intentionally disjoint from the original
suite. Its 646 cases comprise 540 wire scenarios, 90 model/lifecycle scenarios,
three annotated leaders, one 512-operation exact-direction case, and twelve
retained-source clone guards. Operations inside a case are not counted as
additional harness cases.

The matrix exercises SOLID, TRACE, SHAPE, authored 3D POLYLINE and LEADER with
three authored variants and zero, one or two levels of BLOCK/INSERT nesting.
Model tests cover detached and registered source identities, ownership and
handle stability, independently mutable colors, vertices, proxies and XData,
annotation reactor rebinding, and retained-record rejection guards.

Each wire case writes the source and clone separately, preventing same-name
block registration from accidentally deduplicating the subjects under test.
The six existing typed profiles are R2000, R2004, R2007, R2010, R2013 and R2018,
in both text and binary transports: 540 pairs / 1,080 drawings.

The independent checker compares complete ordered ENTITIES/BLOCKS packets
apart from identity groups 5 and 330, and independently derives expected
geometry and authored property values. It checks raw proxy bytes because some
ezdxf simple-entity loaders omit the proxy from their high-level projection.
Independent numerical comparisons use relative and absolute tolerances of
1e-13; ordered packet comparisons remain exact. All 1,080 outputs undergo
independent graph audit. HEADER, TABLES, OBJECTS and CLASSES are not part of the
ordered-record comparator; a graph audit does not establish their complete
native semantics.

## Executed local qualification

Actual Linux .NET SDK 8.0.425 / runtime 8.0.31, net8.0:

| Check | Result |
| --- | --- |
| Complete Debug suite | 39,502 unique passing cases; zero failures |
| Complete Release suite | 39,502 unique passing cases; zero failures |
| Retention | All 38,856 PR #107 cases retained; 646 additional cases |
| Complete independent Release suite | 147 gates passed; zero failures |
| New drawing audit | 1,080 files; zero errors and zero repairs |
| New actual-output corruption controls | 109,944 ordered-record and 10,584 property corruptions rejected |

Missing and extra new fixture inventories were separately challenged and
rejected. Builds have zero errors and retain the 561 existing XML-documentation
warnings. Windows and netstandard2.0 qualification require the actual remote
CI result for the final integrated head; local Linux runs are not substitutes.

```sh
dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Debug
dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Release
python tools/verify_entity_clone_qualification.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

## Integration and remaining scope

An interrupted upload was corrupt and only supplied an unverified readable
prefix identifying the original audit. It was never applied. The reconstructed
standalone suite initially observed 54 passes and 592 failures on the merged
PR #106 production baseline, and 646 passes after correction. PR #107 was then
discovered to contain the same production corrections. This integration retains
that established implementation and both independent test suites, rather than
replacing PR #107 or maintaining a competing fix. Its 39,502-case results are
from combined-source execution, not a sum presented as an executed result.

SHAPE width and model state are tested without an installed SHX font; typed
SHAPE reload needs an external definition and native glyph placement/rendering
is not qualified here. These tests do not establish full AutoCAD parity,
historical typed authoring, recursive dependency-complete import, private
TABLE/cache regeneration, arbitrary native FIELD evaluation or general
version conversion. Native AutoCAD open/AUDIT/save/reopen and native visual
qualification remain unexecuted. See the unchanged
[original clone review](entity-clone-review.md) for the underlying fixes and
primary wire-format references.
