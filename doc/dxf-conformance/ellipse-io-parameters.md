# Typed ELLIPSE parameter and scale fidelity

The typed reader no longer determines closure by comparing endpoint coordinates
with `Vector2.Equals`. Distinct short arcs and near-full arcs retain their
parameter-defined sweep instead of becoming full ellipses. The new internal
codec converts eccentric radians to public polar degrees using a dimensionless
axis ratio and exact quadrant handling, independent of `MathHelper.Epsilon`.
Coincident reduced parameter phases retain the existing full-ellipse meaning.
This is floating-point phase reduction, not exact real-number modulo arithmetic.

The writer converts polar angles directly with the existing conic parameter
helper instead of forming points and multiplying them by reciprocal semi-axes.
It retains the previous signed atan2 interval for non-full arcs and writes
`0, 2*pi` for full ellipses. Coordinate magnitude no longer controls angle
conversion. Nonfinite or indistinguishable converted endpoints reject rather
than silently turning a non-full arc into a full ellipse. An imported interval
whose endpoints cannot be represented distinctly by the public polar-angle
model still rejects; arbitrary exact source-parameter retention is not added.

Major-axis length is computed from component-scaled coordinates, avoiding the
old sum-of-squares underflow/overflow. Orientation is derived with atan2 from
the scaled OCS projection, not epsilon-based Vector2.Angle. Typed diameters and
semi-axes must remain finite and nonzero. Output uses exact quadrant rotation
and rejects an underflowed DXF ratio or completely collapsed WCS semi-axis.
The parser's existing nonperpendicular-axis policy is not expanded or qualified.

The entity model, ownership, common data, XData, proxy attachment, typed version
eligibility and surrounding parsers are unchanged. Conversion helper names in
the reader/writer remain for existing internal regression entry points. Writing
prepares ellipse-specific axes/parameters before that subclass payload, but
this is not a whole-document transactional-save guarantee: common headers or
other records may already have been emitted on failure. Existing Save/Load
Debug-versus-Release failure conventions continue to apply.

## Verification

The harness covers the six existing typed profiles (R2000, R2004, R2007, R2010,
R2013, R2018), both input/output transports, ENTITIES and referenced BLOCKS,
short/near-full/wrapped/negative/equal parameter intervals, exact quadrants,
semi-axis scales from 1e-310 to 1e307 and extreme aspect ratios. Every successful
input is saved in both transports and loaded again. The independent checker
requires complete source/output inventory and validates actual physical packets.
Ordinary-magnitude drawings also undergo independent-reader graph and WCS curve
checks. Extreme-value physical checks are not native or independent geometric
acceptance claims. Executed results, failures and exact source hashes belong in
the PR receipt, not inferred from this planned test description.

```sh
DXF_TEST_FILTER=ellipse-io/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_io_parameters.py artifacts/conformance
```

Autodesk's [ELLIPSE reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm)
defines WCS center/relative semi-axis, ratio, extrusion and eccentric start/end
parameters. It does not list a thickness field; this task does not invent group
39 output for the legacy public Thickness property. No new historical typed
dialect, pre-R11 support, private FIELD/TABLE regeneration, dependency-complete
import, general version conversion or native AutoCAD open/AUDIT/save/reopen and
font/visual equivalence is established.
