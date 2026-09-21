# ELLIPSE sweep state and callback-safe affine publication

`Ellipse` now normalizes rotation and polar endpoints using exact modular
reduction, independently of `MathHelper.Epsilon`. Equal normalized endpoint
values represent a full ellipse; distinct values remain an arc. In particular,
a short positive sweep near zero is no longer silently rounded into a full
ellipse by a process-wide approximate comparison. Negative finite angles wrap
into [0,360), exact multiples of 360 become positive zero, and a negative
remainder whose addition rounds to 360 also becomes zero. This remains binary64
arithmetic, not exact real-number modular reduction. Nonfinite setter inputs
retain the preceding storage behavior; geometry operations validate them.

The existing reviewed affine engine reads and publishes `base.Normal` rather
than invoking arbitrary derived accessors. Normal getters that return another
plane, setters that discard the prepared normal, and setters that mutate then
throw cannot interrupt that engine's publication. Direct Normal access remains
virtual, as do TransformBy overrides. Matrix4 still validates affine input and
dispatches through the virtual Matrix3 overload. Existing ownership, handles,
metadata, finite-input checks, rank/thickness restrictions and proxy policy are
unchanged: exact identity retains the proxy, changed transforms clear it, and
failed preparation leaves the source untouched. Concurrent mutation is outside
this transaction contract.

The transform's endpoint-degeneracy check now uses exact stored equality rather
than the application epsilon. Polar point evaluation and transformed arc endpoint
conversion reuse exact-quadrant trigonometric helpers, avoiding sin(pi)'s residual
on very thin cardinal geometry. The existing floating-point axis factorization
is retained. Ill-conditioned and unrepresentable results may still reject;
correctly rounded affine arithmetic is not claimed.

```csharp
var arc = new Ellipse(Vector3.Zero, 12, 4) {
    StartAngle = 30, EndAngle = 30.0000000001
};
arc.TransformBy(Matrix3.Scale(2), Vector3.Zero);
// The distinct endpoint values remain an arc, not a full ellipse.
```

## Verification

The focused harness covers derived callbacks, owned/detached instances, both
matrix APIs, identity, translation, rotation, nonuniform scale and reflection,
failed-input rollback, exact normalization under multiple epsilon settings,
short/wrapped sweeps, and extreme-axis cardinal points. Wire fixtures exercise
full, quarter and short arcs in all six existing typed profiles and both
transports. An independent checker validates physical centers, semi-axis vectors,
extrusions, ratio, endpoint parameters and independently derived WCS samples,
and rejects changed/omitted/repeated fields, stale proxies and missing/extra files.
Executed configuration counts and exact source hashes are recorded in the PR;
source inspection or Python syntax checks are not substituted for C# execution.

```sh
DXF_TEST_FILTER=ellipse-affine-safety/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_affine_safety.py artifacts/conformance
```

## Explicit boundaries

This task does **not** replace the typed ELLIPSE reader or writer. Their general
parameter conversion, tiny-axis reciprocal behavior and typed reload of extremely
short arcs remain separate IO work. The narrow-arc wire check verifies the actual
exported parameter packet; only ordinary quarter/full fixtures claim typed
round-trip qualification. Tiny cardinal API samples are not native or independent
reader evidence for tiny-axis DXF files. Other property setters, native rendering,
private associations and cache regeneration are not changed.

Autodesk's [ELLIPSE group-code reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm)
defines WCS center, relative WCS major semi-axis, extrusion, ratio and eccentric
parameters. Public polar-angle and callback policies here are library contracts.
No new typed historical dialect, pre-R11 support, general version conversion,
full private FIELD/TABLE/cache regeneration, dependency-complete import or native
AutoCAD open/AUDIT/save/reopen and font/visual equivalence is established.
