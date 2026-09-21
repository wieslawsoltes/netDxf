# ELLIPSE point and derivative evaluation

`Ellipse.PointAt(parameter)` and `Ellipse.EvaluateDerivatives(parameter, order)`
evaluate the supporting ellipse directly in WCS without tessellation or fitting.
The API follows the existing SPLINE point/derivative naming. Parameter values are
**eccentric radians** in the closed interval `[0, 2*pi]`, not the public
`StartAngle` / `EndAngle` polar angles in degrees. A full turn's endpoints denote
the same point. Out-of-domain and nonfinite parameters reject rather than wrap
or clamp. `MaximumDerivativeOrder` is ten; default order is one.

```csharp
var ellipse = new Ellipse(new Vector3(1, 2, 3), 12, 4);
Vector3 point = ellipse.PointAt(Math.PI / 2); // (1, 4, 3)
Vector3[] values = ellipse.EvaluateDerivatives(Math.PI / 2, 2);
// values[0] is position; values[1] = (-6, 0, 0); values[2] = (0, -2, 0).
```

Derivatives are with respect to the eccentric parameter: they are neither unit
tangents nor arc-length derivatives. Only the point includes the center offset.
Rotation and the stored base extrusion normal orient every output in WCS.
Evaluation does not invoke a derived `Normal` getter. It evaluates the carrier
ellipse even outside an elliptical arc's trim; stored endpoints and thickness do
not restrict or offset it. Relevant center, axes, rotation and normal must be
valid. This is a read-only operation: source fields, ownership, IDs, appearance
and proxy bytes remain unchanged on success or failure. Returned arrays are
independent. Concurrent mutation is not a supported snapshot mechanism.

## Numerical contract

The existing conic helper supplies exact quadrants. A four-step recurrence
computes derivatives without adding multiples of pi/2 to the input, avoiding
unnecessary parameter rounding. Rotation, noncardinal trigonometry and local
semi-axis products use binary64. Final WCS coordinates reuse the existing exact
dyadic affine dot, with one final binary64 rounding after summing the computed
inputs. This does not claim correctly rounded analytic trigonometry or exact
orthogonality of a floating-point frame. No new arithmetic engine is introduced.

Positive representable semi-axes are required; no axis ratio or reciprocal is
needed, so extreme aspect ratios can be evaluated when their individual terms
remain representable. Nonzero local-product or final-coordinate underflow to
zero rejects. Overflow rejects; exact zero is positive zero. An exception does
not expose a partially filled result. Allocation is bounded by eleven output
vectors, with fixed-size existing dyadic work; no global CPU-time guarantee is
claimed. The short example and all relevant public signatures are exercised by
the conformance harness rather than compiled duplicate production sources.

## Verification and scope

Tests cover independent explicit WCS bases, rotation, scale extremes, all
orders, cardinal and noncardinal parameters, array/source isolation, callbacks,
epsilon independence, invalid inputs, cancellation, overflow and underflow.
The wire matrix emits 36 drawings and paired numerical reports for six existing
typed profiles in text and binary. An independent checker compares physical
ELLIPSE fields, derives all WCS derivatives independently, loads source curves
with ezdxf, and rejects numerical/packet corruption and missing/extra inventory.
Executed results and exact source hashes are recorded in the PR.

```sh
DXF_TEST_FILTER=ellipse-parameter/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_parameter_evaluation.py artifacts/conformance
```

Autodesk's [ELLIPSE reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm)
defines the WCS center, relative major semi-axis, ratio, extrusion and eccentric
parameters. The domain and derivative contracts above are library API decisions,
not assertions of identical native API behavior. This adds evaluation, not a new
DXF dialect or serializer. Existing tiny-arc typed reload and tiny-axis reciprocal
conversion defects remain separate IO work. Synthetic cases are not native
AutoCAD open/AUDIT/save/reopen evidence. Historical typed loading, pre-R11
profiles, private FIELD/TABLE/cache regeneration, dependency-complete imports,
general version conversion and native font/visual equivalence are not completed
by this task.
