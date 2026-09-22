# Table XData preservation and legacy block insertion units

## Block records

The block-record writer edits only the insertion-unit slot in the top-level
`ACAD` / `DesignCenter Data` list. The first Int16 is the schema version; the
second is the unit value. Existing marker spelling, version, neighboring
records, balanced nested extensions and other applications remain in order.
If the subsection is absent, the writer appends the conventional version-1
list without clearing existing ACAD data. Serialization uses a detached record
view and does not attach applications, replace source records or alter binary
payloads. The existing native group-70 output policy is unchanged.

Internal layout block records previously returned before writing any XData.
They now write their existing applications unchanged, without synthesizing
DesignCenter data or changing their existing native-field omission policy.
This is preservation of existing internal metadata, not new authoring semantics
for every reserved block name.

The reader now checks the already parsed XData before deferred attachment to
the BlockRecord. The old lookup read the still-empty record dictionary. When a
native group 70 is absent, the top-level DesignCenter unit becomes the typed
insertion unit. An explicitly present native field remains authoritative. A
nested lookalike has no effect. This selected presence/conflict policy is not
inferred native AutoCAD acceptance of every contradictory producer packet.

The parser validates bracket balance, a unique top-level subsection and the
two immediate Int16 fields. Ambiguous, incomplete or mistyped legacy lists
reject rather than being partially interpreted. This validation is applied
when loading the fallback (native field absent), and before normal block-unit
serialization. Internal block records remain opaque on writing. Unit-enum
admission, native duplicate-field policy and general malformed-input behavior
are not redesigned by this increment.

## Layer metadata

Layer serialization creates detached application views for description and
transparency instead of clearing or mutating the original applications. It
updates the last string or last Int32, respectively, matching the existing
typed reader's projection. Other values, order, application names and binary
payloads are retained. Missing slots receive conventional generated fields.
The existing packed-alpha codec and raw-alpha policy are reused. Explicit value
edits and property resets after a nondefault/stored value retain packed-alpha
presence even without a previous Save attaching XData. An internal reset flag
is copied by clones and is set only after a valid assignment. Fresh unedited
opaque values keep their previous omission policy.

An internal assignment flag distinguishes an untouched default description
from an explicit empty/null clear. A raw-only description is preserved until
the caller assigns the property; a cleared loaded description no longer returns
after save/load. No public member is added. Layer cloning now copies Description
and that assignment state as well as deeply cloning XData. Previously it omitted
the Description property entirely. Whitespace and Unicode remain literal.

Ancillary fixtures deliberately extend the conventional application payloads.
Independent checks compare the reader's complete XData, not its convenience
properties, which assume canonical layouts and do not project these extensions.
Preserving netDxf's existing last-matching-record policy does not establish all
undocumented native AcAecLayerStandard or AcCmTransparency semantics.

## Verification

The final harness has 654 cases: 60 mixed table round trips, 300 legacy-unit
cases, 24 nested-only controls, 12 native-field precedence checks, 12 layer
clone/create cases, four raw-only/explicit-clear cases, 12 malformed-save cases,
72 independently patched malformed-input cases, 12 missing-subsection append
cases, two missing-layer-slot append cases and 144 explicit-alpha-reset cases. All six existing typed profiles
and both transports are exercised. The mixed cases cover model/paper instances,
unreferenced definitions and both internal layout block records; each checks
both output transports, repeated reload, stable handles, source application and
record identity, no registry events, binary/real bits, clone isolation, following
geometry and object validation. Legacy values 0 through 24 are preserved as
stored units; no additional historical typed dialect is enabled.

The required corpus contains 900 drawings: 300 mixed source/edit drawings and
600 legacy source/output drawings. The independent checker validates exact
physical application packets, native unit presence, the legacy subsection,
loaded XData, handles, owners and geometry, then audits without repairs. Its
source legacy values are full packet checks: ezdxf does not project DesignCenter
units into its typed property when group 70 is absent. Native units in output
are checked through the independent typed reader. Changed/missing/duplicate/
mistyped packets and incomplete/extra inventories must reject.

A fresh identical compiled harness is executed against the preceding production
assembly and the changed production assembly. Full suite and hosted execution
counts, failures, artifact hashes and source qualification are recorded in the
PR, not inferred from this test definition. The initial complete independent
run passed 213 scripts but failed the existing transparency checker: 12 stored
carrier-transfer fixtures expected explicit opaque alpha after a reset. The old
presence predicate depended on a prior Save attaching an application. Production
now retains edit/reset intent without source mutation; the existing checker and
all previous assertions remain unchanged, with 144 new reset/clone/transfer tests.
One combined local build/run command
exceeded its tool-call window; its partial test log is not counted. A complete
fresh focused run provides the recorded result. No baseline assertion,
registration, independent verifier or existing conformance workflow was changed.

## Boundaries

This is scoped table metadata and legacy unit preservation, not transactional
whole-document save, adoption or update. Existing writers can still modify
other document bookkeeping, and output streams can contain partial bytes when
a later validation or I/O operation fails. No native application, private-cache
regeneration, font/fit rendering, complete FIELD/TABLE semantics, dependency-
complete import, general version conversion, pre-R11 support or full all-version
AutoCAD parity is claimed. Synthetic fixtures are not native producer evidence.

Primary reference: [Autodesk BLOCK_RECORD DXF schema](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-DXF/files/GUID-A1FD1934-7EF5-4D35-A4B0-F8AE54A9A20A.htm)
identifies native group 70 and the DesignCenter version/unit slots, including
unit codes 0 through 24. Existing layer reader/alpha codecs are retained rather
than replaced by a second production interpretation.
