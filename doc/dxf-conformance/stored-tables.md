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
