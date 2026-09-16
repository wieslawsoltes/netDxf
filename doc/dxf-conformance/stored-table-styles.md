# Stored TABLESTYLE objects

A recognized loaded TABLESTYLE is available as `DxfTableStyle` through
`DxfDocument.Objects.Items` or `GetObjectByHandle`. It has immutable stored
snapshots and conservative formatting projections. Explicit existing-packet
[scalar/border edits](table-style-editing.md) and [data/unit and STYLE edits](table-row-settings.md)
are available. It does not author new table styles, evaluate custom cell-style
maps, synchronize TABLECONTENT or regenerate TABLE display blocks and geometry.

## Profiles and projection

Typed dispatch requires R2004 or later and `AcDbTableStyle` as the first subclass.
R2000 records and records beginning with an unknown subclass retain the existing
`DxfOpaqueObject` behavior. Known TABLESTYLE records with unknown later fields
remain typed stored packets; individual projections can be unavailable.

| API | Qualified meaning |
| --- | --- |
| `SourceVersion` | Exact source DXF version; typed output must retain that version. |
| `Tags` | Immutable complete retained packet, excluding recognized common metadata and XData. Unknown fields, raw escapes, binary chunks and private application/subclass packets remain stored. |
| `Header` | Description, raw flags, flow direction, margins and title/heading suppression, for the unambiguous classic sequence `3,70,71,40,41,280,281`, with an optional fixed leading `(280,0)` in R2010+. Otherwise null. |
| `Rows` | Three ordered direct public group-7 packets, without assigning data/title/header roles. Empty when that count is not recognized. |
| `Rows[i].TextStyle` | Exact accepted source STYLE object, or null when no retained source identity establishes the binding. |
| `Rows[i].Values` | Unique finite nonnegative height, stored alignment and color values, and the background flag. Null when required scalar fields are missing, repeated or invalid. |
| `Rows[i].Borders` | Six complete stored lineweight/visibility/color triples, or null when incomplete or ambiguous. |
| `Rows[i].DataTypes` | Unique stored group-90 data and group-91 unit codes, or null when absent or ambiguous. |
| `References` | Exact resolved direct public STYLE-name dependencies and exposed semantic handle dependencies. Arbitrary handles in groups 320–329 do not bind. |
| `CellStyleMap` | The owned opaque CELLSTYLEMAP in the known extension dictionary slot, if its target type and reciprocal owner agree. No map schema interpretation is asserted. |

The native AC1024 header has two group 280 fields in different positions.
The first is a fixed version prefix; the later field is title suppression. They
are projected separately and never conflated. Row data/unit and border values
have explicit storage projections; group 1 format strings remain raw. The special
stored fill color257 is retained as an integer rather than forced into a color
object with narrower semantics. See the [primary reference and oracle assessment](table-style-assessment.md)
for the native record inventory and known independent reader/writer inconsistencies.

Only direct group 7 fields inside the recognized public subclass bind by STYLE
name. Private 102 application groups and later unknown subclasses do not supply
STYLE names or projected row values. An actual STYLE rename updates each bound
group 7 on output; unchanged names retain the original wire spelling. Explicit
row STYLE selection additionally validates actual source-table membership and
atomically replaces named dependency snapshots; it never imports resources. Other raw
strings are not rewritten. Accepted source tokens must identify the same physical
record and its actual constructor collection. A generated default, discarded
entity or private group 5 cannot authorize a semantic or named STYLE dependency.
Retained ATTRIB and ENDBLK references use that same token test before looking up
their owner-held identities.

## Lifecycle and output

Typed styles stay in their source document and source DXF version. Unresolved
exposed handle fields retain their raw values and reserve allocation slots; they
do not acquire a generated object merely because its handle matches. Bound
references must still identify the registered object when validating output.
ASCII output rejects CR/LF string payloads before bytes are written; binary can
retain those values. Invalid UTF-16 is rejected before output.

Referenced resources, entities, block members and retained ATTRIB/ENDBLK carriers
are protected by the existing central removal checks. Operations that would
remove a dependency, including INSERT synchronization, reject before changing
membership, allocation or removal callbacks. References disappear when the
style is validly erased. A map-free style can be erased through ordinary common
ownership rules, including one whose optional projection is unavailable. A style
subtree containing an opaque CELLSTYLEMAP is rejected by ordinary opaque-child
erasure policy before mutation. Erased objects cannot be reattached.

Cloning a style or a containing dictionary is rejected before source or destination
allocation because complete application-specific handle remapping is not known.
Foreign adoption and cross-profile output are likewise rejected before mutation
or output. No additional generic erasure rule is imposed for unknown style fields.
Common metadata and XData keep their ordinary interfaces. The subsequent
[explicit stored-style edit API](table-style-editing.md) replaces qualified
recognized header, row scalar and border values through immutable snapshots.
The subsequent [row settings API](table-row-settings.md) adds data/unit pairs and
explicit registered STYLE selection. Raw format strings, structural authoring,
map synchronization and layout remain outside these editing contracts.

When a typed style is present, output checks the TABLESTYLE CLASS's object kind
and `AcDbTableStyle` C++ name before any write. It preserves compatible application
metadata and recomputes the instance count. Private CLASS records remain intact
when all instances are opaque or no typed instance exists; their compatibility
is not inferred from the DXF name alone.

## Historical stored-object qualification

The following counts describe the original stored-object increment, not the
current complete test suite. Later editing contracts and checks are linked above.

`RegisterTableStyleTests` contains native, profile, lifecycle, ambiguity,
malformed-envelope, semantic-reference, source-token and opaque-boundary cases
for ASCII and binary. The final focused matrix passes 137 tests per configuration
in Debug and Release: 89 TABLESTYLE cases, four TABLE resource-name cases and
44 existing TABLE public-field regressions. The [qualification receipt](table-style-qualification.json)
pins the tested production commit, DLL hashes and exact result digests.

`python tools/verify_table_styles.py ARTIFACT_DIRECTORY`
independently parses the outputs with ezdxf and compares stored tag sequences and
object graph links. It also corrupts actual parsed STYLE names, margins, map
values, dependency handles or private payload bytes and requires the same
validators to reject those altered records. Per configuration, the TABLESTYLE
gate checks 70 outputs and 98 corruption controls; the separate TABLE name gate
checks eight outputs and eight controls, and the existing public-field gate checks
44 outputs and 44 controls. These gates pass for both configurations.

The five pinned source drawings yield ten scoped native carriers. Each carrier
inserts the exact native TABLESTYLE, extension dictionary, CELLSTYLEMAP and
owning ACAD_TABLESTYLE dictionary records. The R2004 carrier also includes the
exact owned `ACAD_XREC_ROUNDTRIP` XRECORD at handle 1401; its retained PRE2007 TABLESTYLE
packet remains unbound. Their original core handles remain
unchanged; the referenced STYLE remains at handle 11. Only that STYLE resource's
external collection owner is adapted to the carrier. External entity reactor
targets use explicitly named placeholder objects at their original handles.
Output checks preserve the complete TABLESTYLE and map subclass packets, named
STYLE identity, dictionary entries and common graph relationships. Common
metadata formatting and generic dictionary default fields may normalize.
These scoped carriers do not qualify the external native entity graphs.

Four additional outputs load and save the two complete `acad_table_*` source
drawings. The other three whole drawings encounter existing unrelated
MULTILEADER or live ACIS-history limits; only their scoped TABLESTYLE graphs
are qualified here. Synthetic invalid/private variants and lifecycle controls
are documented test constructions, not new native corpus discoveries. No
external renderer or editable TABLESTYLE/CELLSTYLEMAP oracle is claimed.

`python tools/table_oracle/assess_table_styles.py` separately verifies the exact
source-byte fragments and hashes from all five native record sets. This evidence
is unchanged by normalized carrier output.
