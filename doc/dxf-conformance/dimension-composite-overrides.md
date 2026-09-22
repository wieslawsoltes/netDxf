# Inherited components in composite dimension overrides

## Corrected writer behavior

Six DXF dimension variables combine multiple public settings into one stored
value: DIMZIN (78), DIMAZIN (79), DIMALTZ (285), DIMTZIN (284), DIMALTTZ (286),
and DIMALTU (273). Previously, the override collector initialized unselected
components from fixed defaults, rather than the entity's current base style.
Overriding one flag could therefore reset the other flags on save/load.

The collector now starts from the effective base-style values and replaces only
explicitly overridden components. This covers primary and angular zero
suppression, alternate-unit zero suppression, primary/alternate tolerance zero
suppression, and alternate-unit format plus stacking. A stacking-only override
also now requests DIMALTU output; that switch case previously never enabled the
field, so its override was silently omitted.

DIMENSION, ARC_DIMENSION and LEADER share this writer. The regression matrix
here focuses on aligned DIMENSION and LEADER; the preceding affix tests retain
all eight dimension families. No public signature, reader, bit decoder, unit
formatter, table/header serializer or version gate is changed in this task.
The collector reuses the base-style argument introduced in the parent affix fix.

## Presence and compatibility

An untouched group remains absent from DSTYLE. Explicit false, true and
redundant assignments still request the complete group. Saving does not alter
the source style or materialize entries in its sparse override dictionary.
The existing reader already materializes the complete group after loading,
which preserves effective settings but not sparse component inheritance.
Changing a base style later does not override a complete stored group until the
corresponding override components are removed by the caller.

DIMALTU has an independent stacking distinction only for Architectural and
Fractional formats. Its active-format combinations are qualified here. Other
formats retain the existing encoding and reader normalization of inactive
stacking; preserving an inactive true flag as an independent wire setting is
not claimed. Invalid/unrepresentable unit-enum admission is unchanged.

## Verification

The focused harness contains 1,632 cases. Its 1,056 exhaustive Boolean cases
cover every base flag combination, every individually selected component,
changed and redundant assignments, and both DIMENSION and LEADER. It compares
physical integer packets, source dictionary presence, all loaded Boolean
components, clones, unrelated scalar overrides and other-application XData.

The 576 matrix cases exercise all six groups across six typed versions, both
input/output transports, both host types, modelspace, a paper layout, a referenced
block and an unreferenced block. Each contains an untouched-group control plus
systematic base/override combinations. DIMALTU includes both stacking choices
and both Architectural/Fractional formats. Repeated save/load and dimension
regeneration retain the stored settings, handles and fixed literal labels.

The independent checker requires all 1,728 source/output drawings. It derives
DXF integer encodings independently, inspects actual DSTYLE and base DIMSTYLE
packets, then loads entities with ezdxf and audits without repairs. Missing,
extra, mistyped, duplicated and changed packets/inventories reject. The source
corpus is synthetic. The PR records executed counts and provenance rather than
inferring success from these definitions.

An initial focused run passed 1,536 cases and failed all 96 DIMALTU matrix cases.
The physical files showed every stacking-only row had no DIMALTU field. Enabling
output in that production switch case corrected the failure; the complete
1,632-case harness and all assertions were retained.

## Boundaries and primary source

These changes preserve compact settings, not native zero-suppression/unit/font
rendering, complete tolerance/alternate label layout, fit/leader placement,
other composite variables, arbitrary same-ACAD-application data, transactional
adoption/Update/save or private-cache regeneration. Historical typed versions,
pre-R11, dependency-complete import, general version conversion and AutoCAD
open/AUDIT/save/reopen qualification remain separate work. No full all-version
AutoCAD parity is implied.

[Autodesk DIMSTYLE DXF schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm)
identifies the six stored variables. Existing encoder/decoder algorithms are
reused, not replaced by a second production implementation.
