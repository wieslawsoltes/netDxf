# LINE, POINT, RAY and XLINE direct-edit proxy fidelity

Ten existing setters now invalidate common proxy graphics when their stored
geometry changes: LINE StartPoint/EndPoint/Thickness, POINT
Position/Thickness/Rotation, and RAY/XLINE Origin/Direction. Identical stored
values preserve the existing proxy. The internal bit-comparison helper previously
used by CIRCLE/ARC is generalized to PrimitiveGeometryMutation without changing
those entities' behavior or public signatures.

Each assignment transfers the complete value. Vector normalization-cache flags
are retained while only coordinate bits decide whether geometry changed. A
one-ULP change or a sign change on stored zero coordinates/thickness invalidates
cached graphics. Existing direction normalization and admission run before
assignment, so proportional directions that normalize identically are no-ops;
rejected zero or nonfinite directions leave both geometry and graphics intact.
Existing nonfinite admission for unconstrained coordinates/scalars is unchanged,
including retention on repeated identical NaN bits. POINT rotation still uses
its previous angle-normalization policy; no new epsilon-independence or UCS-angle
interpretation is claimed.

No serialization path is added. Typed loading still attaches the retained common
proxy after constructing geometry. Cloning copies common data after constructing
the clone, preserving valid bytes until the clone is edited. Metadata identity,
ownership, handles and unrelated application XData remain intact. Common proxy
bytes are removed, not regenerated; private dependencies, cached extents and
associative geometry are not updated. No-op means unchanged stored components,
not proof that external cached graphics are valid.

## Qualification

The focused suite contains 290 cases: 120 owned/detached assignment and clone
cases, 32 nonfinite storage cases, 17 signed-zero cases, five complete-vector
assignment cases, 20 rejected directions, and 96 document cases. The wire matrix
uses six existing typed profiles, both source/output transports, four ownership
placements and two coordinate-plane settings. Each drawing contains 20 target
entities (one unchanged and one changed assignment per field) and a following
LINE. All 288 source/output drawings are required by the independent checker.

The checker inspects exact physical fields, proxy presence/bytes and the
independent proxy decoder, then reads all 5,760 target records through ezdxf,
checking owners, WCS geometry, POINT's existing group-50 convention, appearance,
other-application data and graph audits. Packet and file-inventory corruptions
must reject. Synthetic proxy bytes test opaque retention/invalidation only;
they are not native AutoCAD display programs. Execution evidence is recorded
separately rather than inferred from test definitions.

The common package-smoke body exercises all ten edits and normalized no-ops, then
text/binary round trips. It is already shared by ordinary package consumption
and the eight packaged-asset/runtime profiles from PR #196. No prior assertion,
registration or independent verifier is removed. The initial independent checker
used `unitvector` instead of ezdxf's `unit_vector` attribute; its spelling was
corrected without altering production or any expected geometry. The new checker
also initially assumed group 160 before R2013; it now checks the established
R2013+ group-160 versus earlier group-92 policy explicitly.

Primary references: [Autodesk common entity groups](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm)
and [POINT geometry](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-9C6AD32D-769D-4213-85A4-CA9CCB5C5317.htm).
These changes do not qualify native rendering, POINT affine thickness handling,
normal/appearance cache invalidation, historical typed dialects/pre-R11,
FIELD/TABLE/private-cache regeneration, transaction-wide rollback, dependency
imports, general version conversion or AutoCAD open/AUDIT/save/reopen.

Three existing LINE affine boundary fixtures edit their geometry after initially attaching
proxy bytes. They now assert invalidation during that setup and explicitly reattach
the same bytes before their original identity/no-change/coincident-reverse assertions.
The first complete runs exposed these three setup dependencies; those failing runs
are retained separately, not counted as successful qualification. No original
test identity or operation-under-test assertion is removed.
