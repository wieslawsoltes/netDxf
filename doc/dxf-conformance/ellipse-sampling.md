# Scale-safe, bounded ellipse sampling

`Ellipse.PolygonalVertexes` continues to return fixed-count samples relative to
the center in the ellipse's rotated object XY plane. Full ellipses omit the
repeated terminal point; elliptical arcs include their two endpoints. This is
not adaptive tessellation and does not promise a maximum chord error.

## Corrected arithmetic

The previous arc path constructed tiny polar coordinates and multiplied them by
reciprocal semi-axes, which could overflow for valid subnormal axes. The new path
computes eccentric parameters from the dimensionless axis ratio and polar angle.
Exact quadrants are handled explicitly, so a sin(pi) residual cannot move a thin
ellipse's negative major endpoint.

Rotation now halves diameters before combining coordinate terms. Adding rotated
diameter contributions could overflow although every ellipse point was finite.
The array is pre-sized; `MaximumSampledVertices = 1000000` is checked before
allocation. Requests outside 2..1000000 throw `ArgumentOutOfRangeException`.

Nonfinite axes or angles reject. A nonzero diameter whose half rounds to zero,
a nonquadrant endpoint with an unrepresentable axis ratio, or distinct stored
angles whose eccentric parameters coincide reject explicitly. Subnormal sample
components may round to zero under binary64 arithmetic. Cardinal endpoints are
exact when the rotation is cardinal.

The existing angle setters, approximate `IsFullEllipse` convention, polar-point
API, typed ELLIPSE reader/writer, transforms, and conversion metadata policy are
unchanged. This task does not fix every imported ellipse endpoint or every use
of configurable geometric epsilon.

## Verification

The same 217-case harness passes 170 against the preceding library and 217 after
the fix. It covers diameters from 1e-310 to double.MaxValue, six sweeps, four
rotations, thin-axis endpoints, invalid stored state, allocation guards,
repeatability, and six-profile text/binary output. Dangerous old-library
allocation requests are not invoked when the new guard is absent; that absence
fails the test directly.

The independent checker regenerates 120 analytic scenarios / 2,040 samples and
checks 72 LWPOLYLINE drawings. Its bound is 32 ULP of the source semi-major axis,
not of a tiny output coordinate. Numerical rows and selected physical
coordinates/count/closure tags are checked; other metadata is outside this
checker. Every file receives an independent ezdxf graph audit. Deliberate
corruptions use the positive validators; fixture inventory is exact.

```sh
DXF_TEST_FILTER=ellipse-sampling/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_sampling.py artifacts/conformance
```

Autodesk's ELLIPSE reference specifies a WCS center, relative WCS major axis,
minor/major ratio and eccentric start/end parameters; it does not prescribe this
library's polar-angle API or numerical/resource policy:
https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm

No new dialect is enabled. Native AutoCAD open/AUDIT/save/reopen, adaptive
sampling, universal correct rounding and complete AutoCAD equivalence are not
established. Full-suite and hosted results are recorded against the executed PR
head, not inferred from focused tests.
