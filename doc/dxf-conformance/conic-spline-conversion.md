# Rational conic-to-SPLINE conversion

`Circle.ToSpline()`, `Arc.ToSpline()` and `Ellipse.ToSpline()` are extension
methods in `netDxf.Entities`. They convert zero-thickness circular and elliptical
curves to detached, degree-two rational SPLINE entities. This is not a sampled
polyline, curve fitting, or an approximation selected by tessellation precision.
The standard rational construction represents the conic mathematically; its
stored binary64 coefficients and evaluated coordinates still have rounding error.

## Geometry and API

The positive counterclockwise sweep is divided into one to four spans of at
most a quarter turn. Each span has endpoint weights one and a middle weight
`cos(halfSweep)`. Interior controls are tangent intersections, represented in
production by a midpoint divided by that weight. Internal knots are repeated
twice and endpoint knots three times. There are at most nine control points.
Full circles and ellipses use four spans with an exactly copied seam control;
they are closed, clamped, nonperiodic splines, not periodic splines.

The ellipse's public polar endpoint angles are converted to eccentric
parameters. Its rotation, WCS center, and object-coordinate frame are retained.
The existing approximate `IsFullEllipse` convention is unchanged. Arc endpoints
wrap counterclockwise as in the source API. Equal-angle arcs reject as zero
sweep rather than being silently expanded to a circle.

The frame uses the existing arbitrary-axis implementation, and control positions
use the existing exact dyadic affine helper with a single final coordinate
rounding. The generated spline can then undergo general affine transformations
through its existing transform API, without flattening a circle to line segments.
Parameter increments are not uniform angular or arc-length increments.

```csharp
using netDxf;
using netDxf.Entities;

var circle = new Circle(new Vector3(7, -11, 3), 4);
Spline curve = circle.ToSpline();
curve.TransformBy(new Matrix3(2, .5, 0, 0, 1, 0, 0, 0, 1), Vector3.Zero);
var drawing = new DxfDocument();
drawing.Entities.Add(curve);
drawing.Save("affine-conic.dxf");
```

## Ownership, metadata and rejection

The shared curve-conversion policy independently copies ordinary appearance and
XData, including named color, visibility and shadow mode. Source identities,
handles, proxy graphics and unsupported dependencies are not copied. Mutating a
result's ordinary appearance or XData does not mutate the source or a sibling
result. Geometric XData is copied, not interpreted or transformed.

Finite positive radii, a finite center, finite angles, and the shared finite-unit
normal contract are required. A nonzero extrusion thickness rejects because a
SPLINE has no equivalent thickness field. Parent references and dependency
payloads rejected by the shared curve-conversion policy also reject here; this
is not dependency-complete graph conversion.

A rational local control that overflows rejects even when some hypothetical WCS
cancellation could make it representable. The existing affine helper rejects
nonzero final coordinates that underflow to zero. Nonquadrant ellipse endpoints
with an unrepresentable axis ratio, indistinguishable parameter spans, or an
open curve classified closed under the current spline endpoint tolerance reject
rather than silently changing the requested curve. Source state is not modified
and no partially constructed target is returned. General conic setters and the
SPLINE reader/writer contracts are unchanged.

## Independent verification

The focused harness has 389 cases. Model checks cover eight curve shapes,
three normals, three ownership contexts, closure and traversal, geometry,
source/target/sibling isolation, four circle scales, null inputs and rejection
of malformed or unsupported state. There are 288 generated SPLINE drawings:
eight shapes by three normals by six typed versions by two transports.

The independent checker regenerates controls by solving the endpoint tangent
intersection, rather than using the production midpoint construction. It checks
control positions, knots, weights, flags and selected appearance/XData fields.
It independently loads and evaluates 65 points per drawing with ezdxf, checks
the conic locus and plane, and checks the start point and counterclockwise sweep.
That totals 18,720 independent sample observations, not additional harness cases.

Fixed ordinary-scale controls admit 2e-12 absolute error; weights admit eight ULP
of one. The independent evaluated locus and traversal checks have their explicit
bounds in the checker. These bounds are not a universal relative error or native
AutoCAD rounding guarantee. The checker does not compare every unselected field
or require whole-packet order. Every drawing receives a separate graph audit.
Its 35,208 deliberately changed, missing or duplicated selected tags are
rejected through the positive validator. Fixture inventories must match.

```sh
DXF_TEST_FILTER=conic-spline/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_conic_spline.py artifacts/conformance
```

The existing typed profiles R2000/R2004/R2007/R2010/R2013/R2018 are exercised in
text and binary. No historical dialect is enabled by this API. Count fields keep
the existing writer policy; the separately saved count-policy proposal is not a
dependency. Full-suite and hosted results are recorded separately against the
actual executed source, not inferred from the focused tests.

Autodesk's primary record definitions document the geometric fields being
represented, not this library's conversion algorithm, exceptions or tolerances:

- ELLIPSE: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm
- SPLINE: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm

Native AutoCAD open/AUDIT/save/reopen, exact linetype phase, associative graph
conversion, installed-font/visual equivalence, private FIELD/TABLE regeneration,
historical typed dialects and general version conversion remain separate work.
This API does not establish complete AutoCAD equivalence.
