# Stored ACAD_TABLE entities

Loading `ACAD_TABLE` now produces `StoredTable`, available through
`document.Entities.StoredTables`, instead of an `Insert` that discarded the
original table cell data. The model retains immutable ordered subclass tags,
including private packets, redundant fields, byte arrays, source spelling and
unresolved handles. Common entity metadata and XData use the existing model.

`Grid` exposes a read-only row/column projection for the recognized version-22
flat entity representation. Heights, widths, row-major cells, stored cell types,
ACVALUE flags and optional literal values remain distinct. Literal values can be
strings, integers, reals or an explicit empty value; absent stored values are
not synthesized from formatted text. Block cells, fields and private content
remain in the immutable cell tags. The projection is absent for unknown
subclasses or versions. Recognized malformed dimension and value envelopes
reject input. The projection is limited to one million cells.

`BackingContent` identifies a unique retained TABLECONTENT owned through the
TABLE's roundtrip XRECORD extension. `BackingLiteralValuesAgree` compares the
recognized linked cell value envelopes with the flat entity representation.
It returns null whenever a complete scalar comparison is unavailable. It does
not certify style, geometry, fields, links, formulas or regenerated display.

## Lifecycle and output

Stored tables can be saved only through their source document and source DXF
version. Text and binary transport are supported. Clone, foreign-document
adoption, reattachment after removal, and nonidentity transformations reject.
`Normal` returns the actual stored vector even when accessed as `EntityObject`;
changing it rejects before mutation. `Position` and all table-specific stored
values are read-only.

The display block name follows its exact registered block's rename. Unchanged
names retain their wire spelling. Semantic owner and pointer handles protect
registered resources and object erasure; arbitrary group 320–329 values do not.
Unknown application references hidden in binary values or ordinary strings are
not interpreted. Generic common entity appearance and XData remain editable.

## Evidence and limits

The corpus in `tests/fixtures/table-oracle` contains eight source tables from
five pinned drawings. ACadSharp commit
`f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11` independently reads their flat entity
row and column data. Its TABLECONTENT association is not complete; it creates a
detached fallback rather than qualifying the registered backing object. See
`tools/table_oracle/qualification.json` and its README for source provenance.

`verify_stored_tables.py` compares sixteen extracted text/binary table packets
with their original subclass tags and deliberately corrupts dimensions to test
the gate. Extraction carriers intentionally omit native backing resources;
they qualify exact packet preservation, not complete native drawing semantics.
Full native document cases are reported separately. No AutoCAD execution,
regeneration, rendering, cell editing engine, formula evaluation, cross-version
conversion, or application-defined graph cloning is claimed by this feature.

## Named styles, fields and legacy text

The recognized AcDbTable representation resolves documented group-7 text-style
names to their exact registered STYLE objects. Resource removal is protected,
and renaming that object updates its bound name fields. Unchanged wire spelling
is retained. Group-7 strings in unknown subclasses, application control groups
or ACVALUE envelopes are preserved without name binding.

A non-null group-344 FIELD reference in direct cell scope (outside application
control groups and ACVALUE envelopes) sets `StoredTableCell.HasFieldReference`
and prevents literal projection. The stored text and field handle remain in
the packet; no FIELD result is evaluated. In R2004 text cells, ordered group-2
continuations of exactly 250 characters followed by one terminal group 1 of
fewer than 250 characters form the literal text. Decoding
happens after concatenation, and every raw chunk remains unchanged. Block-cell,
modern and ambiguous/out-of-order occurrences are not interpreted this way.

These variants are based on Autodesk's
[TABLE group-code reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-D8CCD2F0-18A3-42BB-A64D-539114A07DA0.htm).
The pinned native corpus contains none of these three variants. Their focused
regressions and `verify_stored_table_fields.py` therefore qualify explicitly
synthetic public-schema carriers, without adding a native rendering or FIELD
schema qualification claim.
