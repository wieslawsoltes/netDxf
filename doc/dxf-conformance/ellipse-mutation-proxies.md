# ELLIPSE direct-edit proxy invalidation

Changes to `Ellipse.Center`, `Rotation`, `StartAngle`, `EndAngle` and `Thickness`
now clear common proxy graphics describing the previous geometry. `SetAxis`
already invalidates changed axes. The direct setters previously retained stale
proxy bytes, even when the stored center, orientation or sweep changed.

Comparisons use stored binary64 component bits, not `MathHelper.Epsilon`.
One-ULP and signed-zero changes to center or thickness invalidate the proxy.
Rotation and endpoint comparisons occur **after** the existing angle
normalization; equivalent normalized assignments remain no-ops and retain the
proxy. Full-ellipse rotation/start/end representation changes conservatively
invalidate even when the same locus could be inferred. No private cache or
association is regenerated.

Center assignment still writes the complete Vector3 value, including its
normalization-cache flag. A cache-flag-only change with identical coordinate bits
does not invalidate geometry. Scalar setters also still assign the value.
Nonfinite values retain the previous setter admission and storage behavior;
changed nonfinite bits invalidate, repeated identical stored bits do not. This
adds no new finite-input validation rule, silent normalization or exceptions.
Operations requiring finite geometry retain their existing checks.

```csharp
var ellipse = new Ellipse(new Vector3(1, 2, 3), 8, 4);
ellipse.ProxyGraphics = cachedBytes;
ellipse.Rotation = 360; // Stored angle is still zero; proxy remains.
ellipse.Center = new Vector3(10, 20, 30); // Proxy is cleared.
```

Ownership, handles, appearance object identities and all unrelated geometry stay
unchanged. Clone initialization still copies common data after geometry; valid
source proxies survive cloning, and later clone edits invalidate only the clone.
The common inherited `Normal` setter and arbitrary derived setters are not
changed. Affine transforms, typed IO and raw-plane editing are unchanged.

## Tests and integration

New tests cover all five properties, owned/detached instances, bit-identical
assignments, normalization equivalence, one-ULP and signed-zero edits, nonfinite
storage compatibility, Vector3 cache transfer, clones and epsilon independence.
The wire matrix covers changed and no-op assignments across six existing typed
profiles and both text/binary transports, producing 120 drawings. Its independent
checker validates physical centers, relative axes, parameters, and retained or
absent proxy packets; actual-packet and inventory corruptions must reject.

Six existing transform-rejection cases edit thickness or an angle during setup.
The setup now explicitly checks the newly correct invalidation, then reattaches
synthetic cache bytes before the **unchanged** original failed-transform rollback
assertions. No previous case, assertion, verifier or serializer guard is removed.
This is a documented fixture setup correction, not a claim of identical baseline
assertion text. Fresh exact-head results are recorded in the PR.

```sh
DXF_TEST_FILTER=ellipse-mutation/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_mutation_proxies.py artifacts/conformance
```

## Boundaries

The existing typed ELLIPSE writer does not emit Thickness; its mutation tests
qualify in-memory cache invalidation, not native thickness serialization or
rendering. Tiny-arc typed reload and tiny-axis reciprocal parameter conversion
remain separate IO defects. Raw geometry/plane APIs keep their existing guards.
These synthetic tests do not establish native AutoCAD open/AUDIT/save/reopen,
visual/font equivalence, historical typed loading, pre-R11 support, complete
private FIELD/TABLE/cache regeneration, dependency-complete imports or general
version conversion. This fixes an existing editing path, not all-version parity.
