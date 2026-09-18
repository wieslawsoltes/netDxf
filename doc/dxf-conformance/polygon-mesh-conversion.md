# Polygon mesh conversion: density, closure and appearance

This C# task starts from merged #120–123, commit
`4388e97de1ea9ec511c2901723d30af5e491044c`, tree
`eb3b1089176ec5ec656a326292351b17ec4083a1`. It does not change JavaScript.

## Corrected existing APIs

`PolygonMesh.MeshVertexes()`, `ToMesh()` and `Explode()` now resolve the same
surface density. Explicit DensityU/V wins; otherwise a registered mesh uses its
own document's SURFU/SURFV plus one, and a detached mesh uses DefaultSurfU/V plus
one, including a mesh belonging to an unregistered block. Each default is clamped to at least three samples. Previously `ToMesh()`
failed for defaults below two, while `Explode()` used unresolved zero density
fields and could return no faces after generating a valid sampled surface.

`ToMesh(int,int)` and `Explode()` use one row/column incidence routine. The cell
count is `(closedU ? U : U-1) * (closedV ? V : V-1)` for the selected sampled or
unsmoothed grid. The doubly closed corner seam cell is included. Corners retain
the original orientation; no projection, triangulation or subdivision is added.

Generated objects independently clone ordinary layer, linetype, color,
transparency, lineweight, linetype scale, visibility, auxiliary normal,
color-name/shadow metadata and ordinary XData. Siblings do not share mutable
appearance objects. Source identity and proxy bytes are unchanged; output
objects are detached, have no copied identity and carry no stale proxy cache.

Parent associations and XData handles require an explicit graph conversion and
reject. Ordinary loaded unadorned VERTEX/SEQEND records are supported; private,
associated, child-XData or child-specific appearance packets reject instead of
silently dropping them. Zero legacy widths and child layer/color matching the
parent are admitted, including ordinary writer-generated records. This is not
dependency-complete document import, and it
does not replace or remove the original object.

`MaximumSurfaceSamples` limits generated sampling grids to 1,000,000 points.
The product is computed as Int64 and checked before surface allocation.
Unsmoothed grids retain their existing fixed controls and do not allocate from
unused precision values. This limit does not bound all downstream appearance
cloning costs or claim production-scale performance qualification.

## Tests and evidence

The initial unchanged 223-case module passes 56 before / 223 after the main
correction. A further 20 cases cover detached-block ownership and ordinary versus
child-decorated loaded grids (indexed and true color, both transports), bringing
the final module to 243 cases. They cover
ordinary/quadratic/cubic surfaces, all four closure combinations, default and
explicit density, detached/registered ownership, low/default density settings,
source and sibling isolation, malformed geometry, handle guards and bounded
allocation. Negative budget tests first check that the new public limit exists,
so the old unbounded allocator is never invoked with a dangerous request.

The independent checker constructs its own fixed 4-by-5 WCS control grid and
compares complete ordered selected MESH/3DFACE packets except the values of
entity and owner handles. Handle framing is checked and each file receives an
ezdxf graph audit. It checks 72 drawings / 1,134 cells and rejects 46,980 actual
parsed-packet corruptions; all drawings have zero graph errors and repairs.
Smoothed conversion is model-level tested against the existing sampler, not
independently qualified as native AutoCAD surface evaluation by this task.

3DFACE output is tested in the six existing typed profiles R2000–R2018 in text
and binary. MESH output is tested in R2010/R2013/R2018, respecting the existing
writer admission policy. This does not enable a new historical typed dialect.
Autodesk's POLYLINE reference defines the M/N counts and independent closure
flags; 3DFACE stores four WCS corners:

- https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm
- https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-Core/files/GUID-BA35CABA-6FDF-419C-AE83-9E28690A4B15.htm

```sh
DXF_TEST_FILTER=grid-conversion/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_polygon_mesh_conversion.py artifacts/conformance
# The complete suite and every independent checker remain enabled:
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
```

Full final-head local/hosted results are recorded in the task PR. Native AutoCAD
open/AUDIT/save/reopen, installed fonts, visual equivalence, private FIELD/TABLE
regeneration, arbitrary retained-child conversion and all-version parity are
not established by these synthetic model and file checks.
