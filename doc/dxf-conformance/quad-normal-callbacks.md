# SOLID/TRACE transform publication and virtual normal accessors

`Solid.TransformBy` and `Trace.TransformBy` now prepare and publish geometry
through `base.Normal`, consistently with the other staged vertex transforms.
The former implementation read and wrote the overridable `this.Normal` property.
A derived getter could throw or report a plane unrelated to the stored corners;
a derived setter could discard the prepared normal, or mutate base state and
then throw. In the latter case the operation left the old OCS corners associated
with a new plane and retained the original proxy bytes.

The base transform now operates on the stored base plane and never invokes an
overridden Normal accessor. The existing preparation engine, numeric admission,
corner order, elevation and signed-thickness calculation are unchanged. Failed
preparation leaves stored geometry and proxy state unchanged. Successful changed
geometry clears its proxy, while exact identities preserve original components
and proxy bytes. The override-capable public Normal property remains unchanged
for direct callers. This intentionally changes the callback behavior of inherited
transforms: subclasses needing custom transform policy should override TransformBy.

The Matrix4 overload still validates its affine bottom row and calls the virtual
Matrix3 overload. This is not permission to bypass a subclass's TransformBy policy;
it only removes Normal callbacks from the base implementation's transaction.
Other virtual calls in cloning or writing are not changed. Concurrent mutation,
private dependency regeneration and subclass-specific extra state remain the
caller's responsibility.

## Regression evidence

The same 234-case harness, run against the actual preceding production library,
reports 4 passes and 230 failures before the four-line correction. The tests cover
both entity classes, both matrix APIs, owned/detached state, identity, translation,
reflection and plane rotation, six accessor behaviors including mutation followed
by an exception, invalid and projective inputs, source identity and wire roundtrips.
The final PR records the actual after-fix complete-suite and platform results.
No sampler, old test assertion or independent gate is removed.

`verify_quad_normal_callbacks.py` verifies 24 drawings across the six typed
profiles and text/binary. The expected WCS coordinates are derived directly from
the specified permutation/translation matrix, and are also independently read
through ezdxf's OCS transformation. The checker validates selected geometry,
extrusion, appearance and absent stale proxies, rejects changed/missing/duplicated
tags and stale-proxy injection, and enforces exact fixture inventory. It does not
compare every field or claim native AutoCAD/renderer execution.

```sh
DXF_TEST_FILTER=quad-normal-callback/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_quad_normal_callbacks.py artifacts/conformance
```

Autodesk's [SOLID](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-E0C5F04E-D0C5-48F5-AC09-32733E8848F2.htm)
and [TRACE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-EA6FBCA8-1AD6-4FB2-B149-770313E93511.htm)
schemas describe stored corners, thickness and extrusion. Callback isolation is
a library contract, not a claimed Autodesk exception or callback policy.

This is a fresh regression implementation over the recovered PR154 tree. The
previously reported local-only callback patch was not present in the mounted
archives; its exact bytes and earlier measurements are not claimed recovered.
No new historical typed dialect, pre-R11 family, general version conversion,
FIELD/TABLE cache regeneration or native AutoCAD qualification is supplied here.
