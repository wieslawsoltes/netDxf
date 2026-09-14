# HELIX analytic authoring and bounded cubic approximation

## API and relationship to typed persistence

PR #67 added typed storage of `AcDbSpline` and `AcDbHelix` representations. Those independently authored representations can disagree; importing or editing metadata must not silently refit the stored curve. This feature adds explicit geometric authoring without changing that persistence contract:

```csharp
var coil = Helix.Create(
    axisBasePoint: new Vector3(0, 0, 0),
    startPoint: new Vector3(5, 0, 0),
    axisVector: new Vector3(0, 0, 3),
    radius: 2,                 // Terminal radius; initial radius is 5.
    turns: 2.25,
    turnHeight: 1.5,            // Signed pitch; zero produces a planar spiral.
    isRightHanded: true,
    tolerance: 1e-5,
    maximumSegments: 65536);
Vector3 point = coil.EvaluateDefinition(0.4);
Vector3 derivative = coil.EvaluateDefinitionDerivative(0.4);
coil.Radius = 3;                // Metadata only: the old spline remains intact.
Helix replacement = coil.WithRegeneratedSpline(tolerance: 1e-6);
var document = new DxfDocument(DxfVersion.AutoCad2018);
document.Entities.Add(replacement);
```

`EvaluateDefinition` evaluates the helix parameters, **not** an arbitrary imported or edited spline. Its parameter is normalized to `[0,1]`; derivatives are with respect to that parameter and retain magnitude. `WithRegeneratedSpline` returns an unowned deep copy with new geometry, preserving helix parameters, common style metadata and XData. It does not mutate the source, preserve its database identity, or merge old fit constraints into the new approximation. Existing automatic constraint solving is not implied by the stored `Constraint` selector.

## Analytic definition

Let `n` be the unit axis, `b` its base, `u` the normalized initial radial vector and `v = n × u`. Let `r0` be the initial radius, `r1` the terminal radius, `w = ±2π Turns`, and `H = Turns × TurnHeight`. The factory uses

```
r(t) = (1-t) r0 + t r1
p(t) = b + n H t + r(t) [u cos(w t) + v sin(w t)]
p'(t) = n H + (r1-r0) [u cos(w t) + v sin(w t)]
        + r(t) w [-u sin(w t) + v cos(w t)]
```

Both fractional turns and signed/zero pitch are supported. A zero terminal radius is allowed. If both radii are zero, the geometric result is a line represented by one cubic segment.

The initial radial vector must lie in the plane through the axis base perpendicular to the axis. A relative `1e-10` orthogonality residual is accepted and projected away. Nonfinite coordinates, zero axes, unrepresentable angles/heights/derivatives and out-of-range evaluation parameters are rejected. Axis normalization uses direct component division, not reciprocal multiplication, so finite subnormal axes and very large axis magnitudes are not spuriously lost. The shared stable-length helper likewise avoids a reciprocal that rounds its largest scaled component away from one.

### Zero initial radius and phase

When the initial radius is zero but the terminal radius is not, the HELIX parameter fields alone do not specify a radial azimuth. New authoring chooses a deterministic arbitrary-axis frame. It writes the initial spline derivative, whose radial component retains the otherwise missing phase. Instance evaluation and regeneration use that retained tangent as the phase hint. This preserves phase through tested rigid/similarity transformations, reflections and transport. Without a usable tangent hint, evaluation uses the deterministic frame again; it does **not** claim to infer an application's unrecorded azimuth. Editing that tangent on a zero-start-radius helix can therefore change the analytically interpreted phase.

## Approximation and work budget

The stored curve is a composite cubic Hermite spline. On each equal parameter interval, endpoint positions and derivatives produce four Bezier controls. This is an approximation of a general helix, not a claim that finite rational cubics represent every helix exactly. Existing corrected composite-Bezier knot generation supplies the equal spans without refitting the sampled curve.

For an interval of length `h=1/N`, a bound on the fourth derivative is

```
M4 <= max(r0,r1) |w|^4 + 4 |r1-r0| |w|^3
E(N) = sqrt(3) / 384 * [max(r0,r1) (|w|/N)^4
                       + 4 |r1-r0|/N (|w|/N)^3]
```

This follows from the scalar cubic Hermite remainder `h^4 M4 / 384` and a conservative component-to-Euclidean `sqrt(3)` bound. The linear axial term has zero fourth derivative. `GetApproximationErrorBound(N)` evaluates this **exact-arithmetic** truncation bound for a newly generated approximation with `N` segments; it does not measure an imported/edited spline, and it does not bound floating-point coordinate, trigonometric or evaluator roundoff.

The implementation evaluates the two terms in log space to avoid intermediate `infinity × zero`, adds rounding slack and rounds the resulting positive binary64 value upward. This is not a formal directed-rounding proof for the platform's transcendental functions. Tests include huge-radius/tiny-angle cylindrical and tapered cases separately: taper contributes a third-order angular term and must not be mistaken for a constant-radius curve.

Selection begins with at most a quarter turn per segment and doubles the segment count until the bound meets the requested positive finite absolute tolerance. `maximumSegments` is an explicit work/allocation ceiling in `[1,1048576]`, default `65536`; the algorithm decides whether the tolerance is achievable before allocating the segment/control lists. It rejects exhausted budgets instead of silently relaxing the tolerance. This is an operation-specific budget, not a document-wide resource quota.

## Version comparison

| Profile | Geometric API | Typed HELIX output |
|---|---|---|
| R11/R12, R13, R14 | Independent of a document | Historical typed input/output still not admitted; raw preservation separate |
| AC1015 / 2000 | Available | Existing conservative HELIX rejection remains; explicitly convert to SPLINE to drop HELIX metadata |
| AC1018 / 2004 | Available | Same |
| AC1021 / 2007 | Available | Text and binary |
| AC1024 / 2010 | Available | Text and binary |
| AC1027 / 2013 | Available | Text and binary |
| AC1032 / 2018 | Available | Text and binary |

The existing 2007+ writer boundary is a conservative implementation profile, not a newly asserted historical introduction date. Similarity transformations preserve the HELIX model; nonuniform transforms require conversion of the stored curve to SPLINE as documented by PR #67.

## Executed evidence

Baseline: merged PR #67, commit `19f4dfc48bb1c573bf8ba29c1a98a138efb902f8`, tree `169051af1b5a5c0d42a7bfc9180ae1ff23f13116`.

`HelixGeometryTests.cs` adds **95 registered cases**: six curve families, both handednesses, three world poses, all four conservative export profiles and both transports, zero-radius phase regeneration/round trips, clone isolation, budgets, invalid inputs and extreme-scale bounds. Cases compare cubic samples against the analytic definition and analytic derivatives against independent centered differences. The complete signed-library .NET 8 suite reports **15,592 passed / zero failed** in Debug and Release. The pre-feature binary cannot compile these newly introduced API calls, so no unchanged-production red-run count is fabricated. An early test incorrectly treated taper as a cylindrical fourth-derivative bound; the final test now checks both terms separately.

The independent ezdxf 1.4.4 verifier reads **48 files / 96 curves**, evaluates their **actual stored knots and controls at 24,672 sample points**, and compares with an independently written analytic formula. The maximum sampled distance is approximately `1.22055e-6`, below the authored `1e-5` tolerance. All files report **zero audit errors and repairs**. Sampled agreement supplements the error-bound derivation; neither sampling nor AUDIT proves native AutoCAD appearance. The 15 ledger-integrity tests are a separate count.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_helix_authoring.py artifacts/conformance
```

Final-head Linux/Windows Debug/Release, actual netstandard2.0, ledger checks and source audit are required before merge. Native AutoCAD execution, automatic constraint solving, tolerance budgeting across later arbitrary edits/transforms, and full inherited SPLINE grammar remain separate work.

## Primary references

- [Autodesk HELIX DXF reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-76DB3ABF-3C8C-47D1-8AFB-72942D9AE1FF.htm): separate SPLINE data and axis/start/radius/turns/pitch/handedness/constraint parameter subclass.
- [Autodesk SPLINE DXF reference](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm): spline controls, knots, weights and optional WCS tangents.
- [ezdxf HELIX model documentation](https://ezdxf.readthedocs.io/en/stable/dxfentities/helix.html): terminal-radius interpretation and initial radius from the start point and axis base.

The geometric model and explicit approximation contract above are library authoring behavior, not additional fields attributed to the DXF tables.
