# Spline-to-polyline conversion fidelity

This task builds on the shared projection policy in PR130. It corrects the
existing `Spline.ToPolyline3D(int)` and `Spline.ToPolyline2D(int)` conversions
and adds `ToPolyline2D(int precision, double elevation)`.

## Geometry and metadata contract

The existing overload keeps its zero-elevation object-XY projection semantics.
The new overload selects a finite signed elevation along the source unit normal.
It is explicit projection, not a planarity check or inference of average height.
The 3D overload retains sampled WCS coordinates. Both outputs preserve the
existing closed/periodic classification and are unsmoothed, so samples are not
fitted a second time.

Ordinary layer, linetype, color, transparency and XData are deep-cloned.
Visibility, named color, shadow mode, lineweight, linetype scale and normal are
retained. Source/sibling metadata does not alias, and source controls, knots,
weights, fit points, ownership, handles and proxies remain unchanged. Source
identities, fitted definitions and stale proxies are not copied onto the
approximation. Extension dictionaries, persistent/ordinary reactors and XData
handles require explicit dependency conversion and reject here.

`MaximumConvertedVertices` is 1,000,000. Precision below two or above that bound
rejects before sampling or output allocation. Source controls, fit points,
optional tangents, weights and knots must be finite; knots must not decrease;
stored normals and sampled coordinates are checked. This is not an exhaustive
validator for every mutable NURBS definition. General `PolygonalVertexes` and
`NurbsEvaluator` behavior, including their separate rational/periodic limits,
remains unchanged by this task. No new negative-weight or native fitting contract
is asserted.

Projection reuses the shared exact binary affine XY evaluator, deliberately not
computing a discarded Z coordinate. Its existing conservative policy rejects
unrepresentable nonzero XY outputs, including underflow to zero. Floating OCS
frames and curve samples are approximations; no universal correct-rounding claim
is made. Sampling and metadata cloning have allocation/CPU costs; the vertex cap
is not a bound on arbitrary metadata size or spline-evaluation work.

```csharp
var spatial = spline.ToPolyline3D(128);
var atOrigin = spline.ToPolyline2D(128);       // original projection contract
var atElevation = spline.ToPolyline2D(128, -7.5);
```

## Executed regression evidence

The same final **540 harness cases** pass **20 before / 540 after** the changes.
They cover degree-one, rational quadratic, closed cubic, periodic and fitted
curves; six normal orientations; three conversion modes; three ownership
contexts; exact sampled 3D components; mutable-data isolation; malformed values;
unsupported graphs and allocation admission. Reflection invokes the new overload
in the negative run without a temporary production stub.

`verify_spline_polyline_conversion.py` independently evaluates a rational
quadratic curve with exact Fraction Bernstein polynomials, then obtains OCS
frames with 150-digit Decimal arithmetic. It checks **216 drawings / 1,944
physical samples**: 144 LWPOLYLINE projections and 72 3D POLYLINE sequences,
across six existing typed DXF profiles and text/binary transports. Every selected
ordered field is checked except entity/owner handle values; their framing,
uniqueness and child-owner relationships are separately verified. Each drawing
receives an independent graph audit. All have zero graph errors or repairs.

The same positive validator rejects **57,996 packet corruptions** and two
missing/extra inventory corruptions. XY/WCS components admit eight ULP of the
maximum source-coordinate magnitude; normals admit eight component ULP. Other
selected metadata and record order are exact. This fixed rational wire corpus is
not exhaustive NURBS evaluation or native producer evidence. Fitted and periodic
curves receive model-level conversion checks against the unchanged sampler.

Complete-suite and hosted Linux/Windows/netstandard2.0 results are recorded in
the PR against each executed head, separately from this focused checkpoint.

```sh
DXF_TEST_FILTER=spline-conversion/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_spline_polyline_conversion.py artifacts/conformance
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance --jobs 4
```

Autodesk documents SPLINE control and fit points in WCS, LWPOLYLINE vertices in
OCS with an elevation and extrusion normal, and 3D POLYLINE VERTEX points in WCS:
[SPLINE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm),
[LWPOLYLINE](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm),
[VERTEX](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm).
These define the stored geometry, not the library's projection, sampling,
exception or numerical policies.

Native AutoCAD open/AUDIT/save/reopen, native visual/font equivalence, historical
typed dialects, dependency-complete imports, full private FIELD/TABLE/cache
regeneration and general document-version conversion remain separate work.
This does not establish full AutoCAD/all-DXF-version parity.
