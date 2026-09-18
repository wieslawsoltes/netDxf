# Finite, scale-safe arbitrary-axis frames

`MathHelper.ArbitraryAxis` now validates and normalizes a finite, nonzero normal
using the existing finite-direction helper. The former unscaled normalization
could overflow or underflow and depended on the caller's global epsilon. Cached
normalized values are checked using their components, rather than trusting an
inaccurate cached flag from the general-purpose normalization utility.

The exact positive Z axis retains the identity shortcut. A real tilt is no longer
rounded away by approximate vector equality. The X and Y basis axes use the same
scale-safe, epsilon-independent normalization. Autodesk's 1/64 polar-cap branch
and cross-product order are unchanged:

- If both normalized X and Y magnitudes are below 1/64, X = world-Y cross normal.
- Otherwise X = world-Z cross normal; normalize X, then Y = normal cross X.

This depends on the preceding exact-matrix-identity correction. Without that
correction, matrix transpose/multiplication can still discard the tilt, even
when ArbitraryAxis itself returns the correct frame. An independent physical
DXF check found that interaction; same-library save/reload alone did not.

Zero and nonfinite normals now throw argument exceptions. Finite nonzero normals
including subnormal and maximum-finite magnitudes are admitted. Unrepresentably
small normalized components may round to zero. The public Vector3 normalization
utilities, arbitrary point arithmetic and unrelated transform contracts remain
unchanged. MathHelper.Epsilon is global mutable state; concurrent changes are not
made thread-safe by this work.

## Executed focused evidence

The same 412 new cases pass 217 before this OCS correction and 412 afterward,
with the corrected matrix implementation present in both runs. Forty-seven
normal vectors cover extreme scales, polar-cap boundaries, signed zero and real
small tilts; model cases vary global epsilon and exercise cached inputs, invalid
normals, basis handedness and the existing coordinate-transform overloads.

The physical corpus has 144 drawings, containing 432 CIRCLE/TEXT/ARC placements,
in direct and nested blocks across the six existing typed profiles and both
transports. The independent checker uses 1,500-digit Decimal calculations of the
published algorithm, not either library's OCS helper. It compares numerical
frames within 16 ULP and OCS positions within eight ULP of the largest original
WCS coordinate; other entity properties and independent graph audits are checked
separately. Real 1e-13 tilts at 1e12-scale coordinates must survive.

The numerical and parsed-record corruption controls run the same positive
validators, and extra/missing file inventories reject. Full-suite and remote
qualification are reported in the PR against the executed source, not inferred
from this focused result. This is synthetic mathematical coverage, not a native
AutoCAD producer corpus or universal correctly rounded arithmetic.

```sh
DXF_TEST_FILTER=ocs-axis/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_arbitrary_axis.py artifacts/conformance
```

Primary reference: [Autodesk Arbitrary Axis Algorithm](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-E19E5B42-0CC7-4EBA-B29F-5E1D595149EE.htm).
The exception policy and numerical tolerances above are library contracts, not
Autodesk-prescribed algorithms for floating-point admission. Historical typed
dialects, native AutoCAD open/AUDIT/save/reopen, private FIELD/TABLE regeneration,
dependency-complete imports and general version conversion remain separate work.
