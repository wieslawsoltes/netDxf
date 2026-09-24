# Normal edits and common proxy graphics

The normal setters on EntityObject, Attribute and AttributeDefinition now clear
common proxy graphics when the normalized stored direction changes. Derived
entities using the base setter inherit this behavior. Specialized TABLE/opaque
normal overrides retain their existing policies; this change does not bypass them.
The input is validated and normalized before any field or graphics mutation.
Rejected directions leave the old vector and graphics untouched.

Normalized no-ops retain proxy bytes and their presence state, including an empty
packet. Comparison uses each normalized coordinate's exact binary64 bits, not
MathHelper.Epsilon or approximate geometric equality. A retained signed-zero bit
change therefore counts as a stored-state change. Very small and very large
finite directions use the existing NormalizeFiniteDirection implementation.

The complete Vector3 struct is assigned even for coordinate-bit no-ops. This
preserves its normalization-cache state and does not reconstruct the vector from
three coordinates. The shared internal mutation helper is reused by existing
primitive callers; their algorithms are unchanged. No public signature, raw or
typed DXF codec, version gate, or undefined-input admission rule changes.

This is invalidation of stale common proxy bytes, not proxy regeneration, native
PDMODE/viewport rendering, general geometry transformation or document rollback.
The normal edit does not itself transform the entity's stored vertices.

## Regression coverage

The 408-case suite includes 224 normalized-assignment cases for fourteen public
normal APIs and empty/nonempty proxies, 98 invalid-vector cases, fourteen
absent-proxy cases, eight dimension-family cases, sixteen WCS spline/mesh clone
cases and 48 document round trips. The wire corpus contains 144 drawings and
3,456 normal-bearing records: LINE, POINT, CIRCLE, ARC, ATTDEF and attached ATTRIB.
All six existing typed versions, both transports and modelspace, paper layouts,
referenced blocks and unreferenced blocks are covered. Exact no-ops, changed
normals, signed zeros and rejected assignments retain handles, values, XData,
ownership, caller stream lifetime and following geometry.

The independent reader checks physical normal bits, transport/version-dependent
proxy count fields and bytes, OCS versus WCS coordinates, attribute placement,
text, entity metadata and graph integrity. Actual-tag corruption and complete
inventory controls reject. Synthetic five-byte proxy payloads test retained
bytes, not valid native drawing commands. Exact numeric field checks are not a
claim of renderer equivalence. Executed counts and hashes are recorded separately
from these test definitions.

The same ordinary installed-package assertion body is also linked by all eight
existing exact-asset runtime profiles. Adding assertions does not itself establish
that all profile executions occurred; each completed run must supply its receipt.

## Existing fixture integration

The first complete run with normal invalidation exposed 1,421 failures from six
shared fixture setups. They attached synthetic proxy bytes before changing a
normal, then expected those bytes to survive a later no-op, clone or conversion.
These setups now assert that the changed normal invalidates the attached bytes,
then reattach the fixture's bytes before exercising the original operation.
Original vertex, transform, ownership, byte, identity and rejection assertions
remain. No failed case is suppressed or reclassified as expected failure.

A helper used only in test setup requires that the original proxy exists, that
normal coordinate bits really change, and that invalidation occurred before
restoring the synthetic bytes. It does not change production lifecycle behavior.

## References and boundaries

[Autodesk common entity codes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm)
distinguish proxy byte count/payload from entity-specific geometry.
[Autodesk POINT codes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-9C6AD32D-769D-4213-85A4-CA9CCB5C5317.htm)
distinguish WCS location from extrusion groups 210/220/230. The invalidation
policy is the library's explicit mutation contract, not native AutoCAD execution
proof. Native open/AUDIT/save/reopen, private FIELD/TABLE/cache regeneration,
font/fit/leader behavior, historical typed dialects/pre-R11, transaction-wide
rollback, dependency-complete imports and general version conversion remain
unqualified.
