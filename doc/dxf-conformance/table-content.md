# Immutable TABLECONTENT storage

`DxfStoredTableContent` retains a loaded TABLECONTENT in its source document and
DXF profile. The complete ordered `Payload` and four `Subclasses` are immutable
snapshots. The later [explicit scalar editing module](table-content-editing.md)
adds bounded header, same-kind scalar and standalone style replacement while
retaining the storage and lifecycle contracts below.
Each nonzero exposed semantic handle resolves through the physical source
identity proof, including exact retained metadata carriers where supported.
`References` preserves repeated targets in packet order. The terminal group
340 binds an actual TABLESTYLE object, which may itself be typed or opaque;
other group-340 fields retain their actual target kinds.

Autodesk's ObjectARX references document the inheritance and separate roles of
[AcDbLinkedData](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbLinkedData.html),
[AcDbLinkedTableData](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbLinkedTableData.html),
[AcDbFormattedTableData](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbFormattedTableData.html)
and [AcDbTableContent](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbTableContent.html).
The linked table can hold heterogeneous and multiple cell contents; formatting
and table styles are separate layers. These API descriptions are not a complete
DXF wire grammar. The 2009 DXF OBJECTS reference does not list TABLECONTENT.

The stored mapping is supported separately by
[ACadSharp at the pinned source commit](https://github.com/DomCR/ACadSharp/blob/f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11/src/ACadSharp/Objects/TableContent.cs)
and eight actual source records in five pinned drawings. They span R2004,
R2007, R2010, R2013 and R2018, and contain all four subclass markers in that
order. The pinned LibreDWG `dwg2.spec` also models these layers, but its partial
DXF sequence differs from the actual outer row-count code; it is not used as
an exhaustive packet grammar or malformed-input oracle.

The public projections are deliberately small: `Name` and `Description` require
the exact linked-data header shape; nullable `ColumnCount` and `RowCount`
require recognized, balanced outer column/row framing. Native examples include
3-by-3, 5-by-10, 3-by-7 and 5-by-4 column/row shapes. AcValue string contents
remain data even when their text equals a structural frame marker. Unrecognized
outer shapes retain their complete packet and expose null counts. The native
DATAMAP shapes are empty maps, the R2004 single named numeric-value envelope,
or a single named AcValue in newer profiles; value text is skipped as data.
Other custom-map shapes leave counts null instead of interpreting
marker-looking custom values. Nested cell
values, private flags, formatting, links and geometry remain uninterpreted.

Typed admission requires the known four-class hierarchy in an R2004 or later
supported profile. Malformed recognized ordering, terminal style references,
known frame/count relationships, public stray XData and invalid source owner
ancestry reject. Unqualified common headers, subclasses and application groups,
and earlier profiles remain whole opaque objects. Private application fields
are not subjected to the typed extended-data rules. Storage is bounded to
1,048,576 input object tags and 64 recognized frame levels.

Cloning and erasing the object or an ancestor ownership tree reject before
mutation. Ordinary removal protects its source ownership ancestry and exposed
dependencies, including the same visible semantic handles in opaque fallback
objects. Arbitrary handles such as groups 320 and 329 do not become semantic
dependencies. Source version changes reject during preflight before output.
Unknown payload dependencies hidden inside strings or binary data are not
interpreted. Common metadata and XData retain their ordinary APIs. General cell
authoring, formula execution, external-file access, rendering and geometry
regeneration remain outside this model; the later scalar edit boundary is
documented separately.

The native fixture README and manifest distinguish two whole unchanged examples
from three exact selected-packet carriers. All TABLECONTENT payload and resource
handles remain original; source resource packets and required block members are
copied rather than substituted. Only listed common carrier ownership fields
change. These checks extend stored-object preservation and do not change the
scope of the separate ACadSharp oracle for the TABLE entity's flat cell view.

The `StoredTable.BackingContent` API now returns `DxfDatabaseObject`, preserving
the exact registered identity for either typed or opaque content. Callers use
`StoredBackingContent.Payload` for a typed object, or pattern-match an opaque
`BackingContent` and inspect its `Tags`. The existing conservative literal-value
comparison uses the same logic for both forms and is now recomputed when read.
This return-type change affects both source compatibility and the CLR getter
signature, so existing compiled consumers must rebuild. It affects the stored
TABLE API introduced during this implementation; longstanding unrelated APIs
are unchanged.

The mandatory independent `tools/verify_stored_table_content.py` gate requires
ten native output files and sixteen private/older opaque output files. It
compares the complete ordered subclass packets against the pinned originals,
checks actual source resource types and names, preserved ownership wrappers,
CLASS flags and emitted instance counts, and rejects deliberate corruptions of
actual output packets. The native extractor separately proves all 316 selected
source packets after only manifest-indexed common-owner substitutions and checks
every exposed nonzero semantic handle against the carrier's physical records.
For both output transports the gate requires all 316 selected records and every
original exposed dependency to retain their physical identity and source type,
and checks emitted selected records for dangling references. Its 130 corruption
controls include removing or changing an indirectly retained INSERT and adding a
dangling XData reference. A zero external audit alone does not establish that
reference closure.

Complete source-carrier packet equality is an extraction guarantee. Ordinary
typed resource writers may normalize their output; for example, optional
BLOCK_RECORD group-331 backlinks are omitted in the current outputs. The exact
ordered output-packet guarantee here covers TABLECONTENT and its native XRECORD
wrapper, not every typed resource in the carrier.

The isolated qualification runs 301 focused cases in each of Debug and Release:
134 dedicated TABLECONTENT cases, 142 stored TABLE/API/lifecycle regressions,
and 25 private XRECORD cases. The dedicated cases cover both input and output
transports, all five native source profiles, malformed public envelopes, opaque
variants, source ownership cycles, exact dependencies, profile preflight,
immutable lifecycle refusal, reserved marker strings and qualified/unqualified
custom-map projections. The final 20 map-specific controls are also run against
a frozen preceding library: six pass and fourteen expose the earlier defects;
all twenty pass against the final library. These controls retain the same test
harness and expectations. The full native gate is required independently of the
C# test outcome.

The module's mandatory command, run from the repository root, is:

```sh
python tools/verify_stored_table_content.py /path/to/conformance-artifacts
```

The receipt records exact source and library identities, input fixture hashes,
normal build diagnostics, both configurations and the independent review scope.
Native AutoCAD execution, formula evaluation, general backing-cell authoring,
automatic regeneration and full-drawing byte identity remain outside this
storage qualification. The later scalar-edit receipt covers its separate scope.
