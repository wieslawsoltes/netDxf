# Explicit SPLINE count output

This recovers the previously unpublished September 18 count-policy proposal,
whose saved patch SHA-256 is
`e852b10d3f9efb364f9095a523d91e3a63f389dd36e93247f9b4871561de18c0`,
and adapts its writer integration to the merged September 20 baseline
`30acef75d3a8010180e11a2f4fb51314d824b812`. The original geometric serializer
and the JavaScript port remain unchanged.

## Public policy

`DxfDocument.SplineCountPolicy` selects a `DxfSplineCountPolicy` value:

| Policy | SPLINE groups 72, 73 and 74 |
| --- | --- |
| `LegacyOmit` (default) | Retains the existing omission of all three fields. |
| `WhenRepresentable` | Writes all three only if every count is in 0–32,767; otherwise omits all three. |
| `RequireRepresentable` | Writes all three; an unrepresentable count rejects in preflight. |

Counts follow degree. They describe actual serialized knots, controls and fit
points, including the repeated degree-sized prefix of periodic control nets.
Missing fits produce an explicit zero count when enabled. No count wraps,
truncates or saturates. The AcDbSpline part of HELIX follows the same policy.
All registered blocks are checked, including unreferenced definitions.
The preference is per-document and not stored as a DXF variable; loaded
instances start with the legacy default. Invalid enum values reject atomically.

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
document.SplineCountPolicy = DxfSplineCountPolicy.RequireRepresentable;
document.Entities.Add(new Spline(new[] {
    new Vector3(0, 0, 0), new Vector3(2, 4, 0), new Vector3(5, 1, 0)
}, null, (short)2, false));
document.SaveAtomic("counted-spline.dxf");
```

SPLINE retains the six typed R2000–R2018 profiles; HELIX still requires R2007+.
No new historical dialect is enabled. Raw and opaque entities are unaffected.
The existing reader continues deriving geometry from payloads rather than
trusting count declarations. Strict count validation on input is not added.
Fallback omission is not a guarantee that every count-dependent consumer will
accept arbitrarily large curves. Required mode gives an explicit refusal path.

## Serialization and failure contract

Count validation runs at the first shared entity preflight, before text checks,
OBJECTS initialization, handle allocation or stream output. Conventional
filename saves reach the same check before opening/truncating their destination.
SaveAtomic retains its existing staging and path-restoration behavior.

Both dispatch overloads use the existing geometric writer: single-argument
SPLINE dispatch and the more-specific HELIX overload select the policy entry
point. The legacy optional-bool serializer remains the sole geometry writer.
For representable opt-in output, a scoped code-writer adapter inserts counts
after degree and delegates all other writes. A finally block restores the
original code sink even if emission throws. Default and large-curve fallback
paths use the legacy serializer directly, without the adapter.

Existing exception conventions are retained: ordinary Save throws validation
failures in Debug and returns false in Release, while SaveAtomic propagates
failures in both. Required-count failures preserve destination bytes, stream
position, source handles, object-database state and paths in the exercised
cases. This is not rollback for arbitrary I/O errors: a failing sink may already
contain partial output. Restoring the writer adapter does not rewind that sink.
Concurrent document mutation during save is unsupported; rechecking counts
at emission does not provide a concurrent snapshot or locks.

Repeated successful saves have existing handle-allocation side effects.
Default-versus-explicit-legacy byte tests use equivalent independently loaded
snapshots, not two lifecycle stages of the same object.

## Verification and recovery provenance

The recovered 592-case harness covers three policies, rational/periodic/fit
curves and HELIX, three placements, all eligible versions and both transports,
actual Int16 boundary arrays, invalid enums, stream refusal and conventional/
atomic filename refusal with existing and absent destinations. Fit-limit cases
inject a retained fit array to avoid an unrelated huge fitting computation.
An additional 28 cases check mixed SPLINE/HELIX/LINE output, no count leakage,
code-sink restoration after faults at degree and each count, and successful
reuse of the same writer. These tests do not assert transactional I/O rollback.

`verify_spline_count_policy.py` checks 532 drawings, including 344 explicit
packets and 28 Int16-boundary drawings. It independently inventories payloads,
validates count placement and Int16 range, and compares complete selected
entity packets against legacy counterparts after removing only the count
fields. It requires zero graph errors/repairs and rejects wrong, negative,
missing or duplicate count values, missing payloads, and fixture mismatches.
The fixtures are synthetic, not native AutoCAD producer evidence.

```sh
DXF_TEST_FILTER=spline-counts/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_count_policy.py artifacts/conformance
```

Final source-bound execution results belong in the task PR and delivery receipt.
The September 18 local results do not qualify the current integration. This
continuation has no local .NET SDK; no new local C# or old/new execution is
claimed. Independent Python checkers can be replayed against hosted artifacts.
The saved patch was recovered byte-for-byte; the adapted integration is not
claimed to reproduce the old complete tree or old local commit identifiers.

## References and remaining parity

Autodesk's [SPLINE schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm)
defines the count fields. The [group-code type reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-2553CF98-44F6-4828-82DD-FE3BC7448113.htm)
describes their range as 16-bit integers. Nonnegative signed-Int16 admission,
default omission and fallback/refusal policies here are library contracts,
not claims about native AutoCAD's behavior.

Full historical typed loading, pre-R11 profiles, private FIELD/TABLE/cache
regeneration, dependency-complete import, general version conversion, native
font/visual equivalence and AutoCAD open/AUDIT/save/reopen remain unqualified.
This policy does not establish full AutoCAD parity across all DXF versions.
