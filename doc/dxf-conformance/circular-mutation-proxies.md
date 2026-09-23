# CIRCLE and ARC direct-edit proxy invalidation

CIRCLE Center, Radius and Thickness and ARC Center, Radius, Thickness, StartAngle
and EndAngle now invalidate common proxy graphics when their stored geometric
components change. Previously all eight setters could retain graphics describing
old geometry. A shared internal helper compares binary64 component bits and always
assigns the value. It does not invoke user callbacks or change public signatures.

Radius retains its existing positive-value validation. Zero and negative values
reject before mutation; the prior NaN/positive-infinity admission is not changed.
Center/thickness nonfinite admission remains unchanged. Operations requiring finite
geometry retain their own existing checks. Rejected setters leave both fields and
proxy bytes unchanged. Identical stored values preserve proxies, including repeated
identical NaN bits. One-ULP and signed-zero changes to center/thickness invalidate.

ARC endpoint comparison occurs after the existing MathHelper.NormalizeAngle call.
Equivalent normalized endpoints retain their cache. This task does not replace
that normalization or its existing dependence on MathHelper.Epsilon. The helper's
bit comparison itself uses no tolerance. Center assignment transfers the entire
Vector3, including its normalization-cache flag; changing only that flag does not
invalidate the coordinate geometry. Normal's inherited setter is not changed.

Clone construction continues to copy common data after geometry initialization.
A valid source proxy therefore survives cloning; a later clone edit invalidates
only that clone. Metadata object identities, handles, ownership, unrelated geometry
and source objects remain untouched. Proxy data is cleared, not regenerated;
private associations, dependent entities and header extents are not rebuilt.

```csharp
var circle = new Circle(new Vector3(1, 2, 3), 3);
circle.ProxyGraphics = cachedBytes;
circle.Radius = 3; // Same stored value: valid proxy retained.
circle.Radius = 4; // Changed geometry: stale proxy cleared.
```

## Verification

Tests cover eight properties, owned/detached objects, exact and normalized no-ops,
ULP changes, signed zero, rejected radii, nonfinite storage compatibility, complete
Vector3 transfer, unrelated fields and clone isolation. Wire fixtures use six
existing typed profiles, text/binary, changed/no-op assignments, and +Z/+X planes.
The independent checker verifies physical OCS coordinates, WCS geometry, radius,
angles, signed thickness, appearance and common proxy packets, with real packet
mutation and inventory controls. Executed results are recorded in the PR.

Existing circular transform tests that edit geometry during setup now explicitly
check correct invalidation and reattach synthetic cache bytes before their original
unchanged clone-isolation and failed-transform rollback assertions. No prior case,
assertion or independent verifier is removed. This is an intentional setup change,
not a claim that all preceding test text is identical.

```sh
DXF_TEST_FILTER=circular-mutation/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_circular_mutation.py artifacts/conformance
```

This is direct-edit cache correctness, not exact native rendering, a new historical
typed dialect, pre-R11 support, FIELD/TABLE/private cache regeneration, complete
dependency import, general version conversion or AutoCAD open/AUDIT/save/reopen.
The preserved normalization/admission policies are not claims of native API parity.
