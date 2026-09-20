# Atomic 3DFACE invisible-edge assignment

`Face3D.EdgeFlags` now validates that only the four defined invisible-edge bits
are present before changing the stored field. Invalid enum casts throw
`ArgumentOutOfRangeException` with parameter `value` and preserve previous
flags, geometry, proxy bytes, handles and ownership.

Previously arbitrary integer casts were accepted. The writer's conversion to
the DXF group-70 Int16 could lose bits (for example 65536 became zero). Undefined
low-16-bit flags could also be serialized despite lacking defined edge meaning.
The new public setter rejects both kinds of invalid input rather than silently
repairing, masking or narrowing them.

Valid changed edge visibility clears the parent's stale proxy graphics.
Assigning the same mask preserves proxy bytes. Clone construction still copies
valid source proxies after its geometry initialization; subsequently changing
only the clone's edge flags does not change the source. All combinations from
zero through fifteen remain supported, including the fourth-edge bit on a
triangle. Corner ordering and geometric values are unchanged.

```csharp
face.EdgeFlags = Face3DEdgeFlags.First | Face3DEdgeFlags.Third;
// A changed mask invalidates proxy graphics; a no-op assignment does not.
```

## Compatibility boundaries

This is an intentional admission correction for previously accepted undefined
enum values. Typed reading reaches the same setter and rejects invalid masks
rather than pretending they describe valid edges. Generic raw snapshots still
preserve unknown tag data; the selected raw 3DFACE geometry API also requires
defined masks. This task does not change the writer into a preflight validator
for private fields corrupted through reflection, nor regenerate private caches
or application-specific dependencies. It does not assert that every conventional
filename save failure is atomic. Valid assignments use existing proxy clearing;
no rendering engine or proxy regeneration is added.

## Verification

The unchanged focused module contains 482 cases: 256 mask transitions,
18 invalid-value/ownership cases, 16 clone-isolation cases and 192 physical DXF
round trips. Against the actual preceding production DLL it reports 16 passes
(the unchanged-mask controls) and 466 failures. With the correction all 482
pass. No baseline test, fixture, assertion or production save guard was relaxed.

The independent checker verifies all 16 masks across the six typed version
families and text/binary transports. Each of its 192 drawings has the expected
physical group-70 value, unchanged corner coordinates and no stale proxy packet.
The independently loaded entity and graph audit agree. All 768 actual-packet
corruptions and two missing/extra inventory controls are rejected.

```sh
DXF_TEST_FILTER=face-edge-visibility/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_face_edge_visibility.py artifacts/conformance
```

Final whole-suite, platform and source fingerprints are recorded in the task PR
and delivery receipt. This focused corpus is not a native AutoCAD execution.

Autodesk's [3DFACE schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-747865D5-51F0-45F2-BEFE-9572DBC5B151.htm)
defines the four low invisible-edge bits and default zero. The exception and
proxy-invalidation contracts are this library's explicit behavior. Historical
typed read/write, pre-R11 dialects, full private FIELD/TABLE/cache regeneration,
dependency-complete import, general version conversion and native AutoCAD
open/AUDIT/save/reopen remain separate work.
