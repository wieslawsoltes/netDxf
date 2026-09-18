# Detached Polyline3D projection and explicit elevation

`Polyline3D.ToPolyline2D(int precision)` continues to project onto the object XY
plane **at elevation zero**. It does not infer a best-fit plane, choose an average
height, or silently preserve a nonplanar source's Z coordinates. The new overload
`ToPolyline2D(int precision, double elevation)` explicitly selects a parallel
plane at a finite signed offset along the source unit normal.

The result now independently copies ordinary layer, linetype, color,
transparency, visibility, lineweight, linetype scale, named color, shadow mode and
XData. The continuous-linetype flag and closure are retained. Identities,
ownership and proxy graphics are deliberately not copied to a new entity kind.
The source and its original proxy/metadata remain unchanged. Unsupported parent
graphs, XData handles and decorated retained children reject under the existing
standalone explosion dependency rules, rather than silently losing associations.

## Geometry and resource contract

Unsmoothed controls are projected directly. Quadratic/cubic curves use the
existing `PolygonalVertexes` sampler with the caller's precision (minimum two),
not document sampling defaults. Negative precision still rejects even for an
unsmoothed source; unused large nonnegative precision is ignored for unsmoothed
geometry. Output has at most `MaximumConvertedVertices = 1000000` vertices;
this bound is checked before generated sampling or projection allocation.
It is not a general bound on metadata or existing spline evaluation CPU cost.

Source and sampled coordinates and the stored unit normal must be finite. The
OCS basis is computed once. Its XY dot products reuse the existing exact dyadic
point helper, rounding each once to binary64; an unrepresentable nonzero XY
coordinate rejects. The discarded object Z coordinate is not computed: it may
overflow even when the requested projection is fully representable. The explicit
elevation is copied unchanged. Exact arithmetic is relative to the computed
floating-point OCS frame, not an exact symbolic normalization of the normal.
The staged arrays and exact products have CPU/allocation cost; no speedup is
claimed. Generated coordinates do not retain a particular zero sign.

Output vertices are straight sampled segments with zero bulge and width,
`NoSmooth`, and zero thickness. This does not preserve arc/spline parameterization,
continuous linetype phase after changed lengths, arbitrary XData coordinate
semantics, source identities, or native AutoCAD CONVERT/FLATTEN behavior.

```csharp
var source = new netDxf.Entities.Polyline3D(new[] {
    new netDxf.Vector3(0, 0, 8), new netDxf.Vector3(3, 2, 10)
});
var onWorldXY = source.ToPolyline2D(0);       // elevation 0, previous behavior
var onParallelPlane = source.ToPolyline2D(0, 8); // explicit elevation 8
```

## Regression and independent checks

`PolylineProjectionTests.cs` contains 685 cases. The same module passes 11 cases
against the preceding implementation and 685 after this correction. Missing API
and budget probes use reflection so the negative run neither needs a production
stub nor attempts an unsafe enormous allocation.

Model cases cover six normals, three curve modes, open/closed paths,
detached/detached-block/registered ownership, default/explicit projection,
mutable-state isolation, finite admission, retained children, empty/singleton
paths, sampling bounds and irrelevant normal-coordinate overflow.

`verify_polyline_projection.py` reconstructs the OCS frame with 150-digit Decimal
arithmetic, then checks 432 text/binary drawings and 2,160 physical OCS vertices.
Every selected LWPOLYLINE field is checked in order, except handle values whose
framing is checked separately. Expected XY permits eight ULP of the largest
source coordinate; normal components permit eight ULP. Every drawing also
receives a separate ezdxf graph audit. The checker rejects 55,296 changed,
missing, duplicated or nonfinite record variants and missing/extra inventories.
These synthetic fixtures are not native AutoCAD producer evidence. Full local
and hosted results are recorded against their exact heads in the PR.

```sh
DXF_TEST_FILTER=polyline-projection/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_polyline_projection.py artifacts/conformance
```

## DXF references and remaining scope

Autodesk specifies LWPOLYLINE XY vertices in OCS, group 38 elevation and groups
210/220/230 extrusion direction:
https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm
Legacy 3D VERTEX coordinates are WCS:
https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm
The projection API, admission limits and numerical bounds here are library
contracts, not claims that Autodesk prescribes this conversion policy.

Wire checks use the existing R2000/R2004/R2007/R2010/R2013/R2018 profiles.
No historical typed dialect, native open/AUDIT/save/reopen, font/visual parity,
private FIELD/TABLE/cache regeneration, dependency-complete import or general
version conversion is established by this task.
