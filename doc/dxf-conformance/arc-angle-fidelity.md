# Exact ARC endpoint parameter storage

ARC constructors and endpoint setters now normalize degree values using an exact
remainder rather than geometric near-equality. Finite endpoints already in
[0,360) retain their binary64 values, including positive subnormals and the
representable value immediately below 360. Changing MathHelper.Epsilon no longer
snaps those parameters to zero during assignment, cloning or typed DXF loading.
Previously a valid small endpoint could silently become zero, and a near-full
turn could be treated as an exact turn.

Normalization retains the existing half-open [0,360) contract. Exact multiples
of a turn and either zero sign become positive zero. A negative remainder so
small that adding 360 rounds to exactly 360 also becomes zero, since that value
cannot be represented below 360 at binary64 precision. Nonfinite inputs retain
the previous modulo-to-NaN behavior; this task does not tighten their admission.

Only ARC parameter storage changes. MathHelper.NormalizeAngle and every other
entity's normalization policy remain untouched. Bulge construction, affine
angle derivation, sampling, trigonometry and native rendering are not made
universally epsilon-independent by this change. Sampling may round extremely
small angles after conversion to radians; exact stored bits are not a promise
of visually distinguishable geometry.

The direct-edit proxy rules from the circular-mutation work still apply after
normalization: an unchanged stored value retains proxy data; a real endpoint
change invalidates it. The reader and writer keep their existing group-50/51
paths, entity ownership and version restrictions. There is no second serializer,
new public method, mutation of the global tolerance, or private-cache regeneration.

## Verification

The 342-case focused matrix includes 192 constructor/setter/clone checks, six
nonfinite compatibility cases, and 144 document cases. Explicit normalization
examples cover turn wrapping, both zero signs, subnormals, one-ULP values,
near-360 endpoints and the largest finite values. Four global tolerances range
from the smallest subnormal through 100. The same compiled test assembly is
executed against the preceding and changed production libraries; actual results
are recorded in the PR, not inferred from the test definitions.

Wire fixtures cover six existing typed versions, text/binary input and both
output transports, modelspace, paper layouts and referenced blocks, in +Z/+X
planes. Each drawing contains ten boundary-angle arcs and following LINE
geometry. Half of the inputs patch ordinary 30/210-degree seeds independently
of the new endpoint encoder. Their immutable raw snapshots are re-resolved
between replacements. Proxy bytes are synthetic, opaque preservation markers,
not a claim of valid native proxy graphics for tiny arcs.

The complete corpus has 432 drawings and 4,320 ARC records. The independent
checker verifies actual endpoint bits, physical group presence, coordinate
planes, owners, proxy packets and database integrity. Endpoint zeroing, one-ULP
changes, deletion, duplication and wrong-code controls must reject, as must
missing/extra fixture inventories. Existing circular-mutation and all earlier
regressions remain registered and unmodified. The installed-package consumer
also round-trips a tiny start and a near-turn end.

```sh
DXF_TEST_FILTER=arc-angle-fidelity/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_arc_angle_fidelity.py artifacts/conformance
```

Primary schema: [Autodesk ARC group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-0B14D8F1-0EBA-44BF-9108-57D8CE614BC8.htm)
identifies start/end groups 50/51 and the OCS plane. The synthetic tests establish
stored-data fidelity, not AutoCAD open/AUDIT/save/reopen acceptance. Historical
typed dialects/pre-R11, full FIELD/TABLE/private-cache regeneration, dependency
imports and general version conversion remain separate work.
