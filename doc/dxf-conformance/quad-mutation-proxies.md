# SOLID/TRACE geometry assignment and proxy invalidation

The four corner setters, `Elevation` and `Thickness` on `Solid` and `Trace`
now invalidate stale common proxy graphics when their stored geometry components
change. Previously these direct edits could leave graphics representing the old
shape. Existing affine transforms already clear proxies; their implementation
is unchanged by this task.

```csharp
solid.FourthVertex = new Vector2(8, 9);
solid.Thickness = -2.5;
// Changed geometry clears common proxy graphics. Identical assignments retain them.
```

## Assignment semantics

Change detection compares binary64 components bit-for-bit, independently of
`MathHelper.Epsilon`. A one-ULP change or a change of zero sign invalidates the
proxy; identical components do not. The entire Vector2 value is still assigned,
including its cached normalization state, even if its coordinates are identical.
Thus a cache-only assignment retains both existing assignment semantics and
valid geometry-dependent proxy bytes.

Clone initialization continues to copy valid source proxies after initializing
the clone's geometry. A later clone edit clears only the clone's proxy; source
geometry and proxy bytes remain unchanged. Direct edits do not alter ownership,
handles, normals or unrelated appearance objects.

This is a cache-invalidation correction, not a new finite-input admission policy.
The setters still accept the same values as before. Existing transformation and
save guards remain responsible for rejecting invalid geometry where applicable.
The common normal setter, direct edits on other entity types, native private
caches, XData semantics, dependency graphs and proxy regeneration are unchanged.
No universally atomic-save or full rendering-equivalence guarantee is added.

## Regression and wire verification

`QuadMutationTests.cs` covers every affected setter on both entity types, owned
and detached objects, unchanged values, changed values, one-ULP changes, signed
zero, normalization-cache assignment, clone isolation, and typed text/binary
save/load. It retains all preceding tests and their original guards.

`tools/verify_quad_mutation.py` independently checks selected physical corner,
elevation, thickness and extrusion values plus absence of stale proxy records.
It loads the resulting objects through ezdxf and audits their graphs. Deliberate
coordinate omissions, duplicates and changes, reintroduced proxy packets and
missing/extra fixture inventories must fail the same positive checks. It is a
selected geometry/proxy check, not a whole-document exact comparator. Fixtures
are synthetic, not new native AutoCAD producer evidence.

```sh
DXF_TEST_FILTER=quad-mutation/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_quad_mutation.py artifacts/conformance
```

Executed counts and hosted qualification are recorded in the task PR. No local
.NET execution or same-harness before/after run is claimed without actual logs.
The six typed profiles remain R2000/R2004/R2007/R2010/R2013/R2018. Full historical
typed loading, pre-R11 formats, native AutoCAD open/AUDIT/save/reopen, private
FIELD/TABLE/cache regeneration, dependency-complete imports and general version
conversion remain separate work.
