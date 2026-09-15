# Native R2004 composite TABLE ownership

The R2004 XRECORD wrappers `13DF` and `13F2` now bind their complete stored ownership envelope. Each wrapper owns a TABLECONTENT object, a TABLEGEOMETRY object and a DATATABLE. The DATATABLE retains its existing typed row ownership, including all 41 native XRECORD descendants across the two graphs. `DxfXRecord.IsSchemaManaged` is true for these exact packets and protects the wrapper payload from generic `Data` mutation.

The evidence is the complete [ACadSharp R2004 source drawing](https://raw.githubusercontent.com/DomCR/ACadSharp/f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11/samples/sample_AC1018_ascii.dxf), pinned at revision `f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11`. Its SHA256 is `71117bafa8f0d9816607768c8fb21676c83f99892c537711a68c24aa7e06e798`. The original compressed file and the earlier independent-reader inventory remain in `tests/fixtures/table-oracle` and `tools/table_oracle/fixtures.json`.

| Wrapper | Original external owner | TABLECONTENT | TABLEGEOMETRY | DATATABLE | Row XRECORDs |
|---|---|---|---|---|---:|
| 13DF | 13DE | 747 | 748 | 143D | 21: 143E–1452 |
| 13F2 | 13F1 | 13EF | 13F0 | 145B | 20: 145C–146F |

The accepted payload has exactly these 15 raw group codes, after the `AcDbXrecord` marker and group-280 cloning policy:

`102, 360, 70, 90, 10, 20, 30, 90, 90, 361, 102, 90, 91, 102, 360`.

The group-102 values, in order, are `ACAD_ROUNDTRIP_2008_TABLE_ENTITY`, `ACAD_ROUNDTRIP_PRE2007_TABLE` and `ACAD_ROUNDTRIP_PRE2007_TABLECELL`. The first section supplies TABLECONTENT and TABLEGEOMETRY owners; the last supplies DATATABLE ownership. Scalar values are preserved without interpreting flags, row/column meaning, backing agreement or display behavior. This evidence supports the complete observed ownership grammar; the marker alone does not qualify another private variant.

The reader binds the three slots only after all targets resolve to accepted physical source objects, have the expected types and declare the wrapper as their common owner. It rejects missing/null targets, wrong target classes, ownership cycles, foreign registration and additional undeclared owned objects. All candidate checks precede owner assignment or schema management. Unknown marker values, code order, additional fields and incomplete extra sections remain unbound XRECORD payloads and round-trip unchanged.

The existing database hooks enumerate all three direct children, preserve their order and retain reciprocal identities. The native DATATABLE's 21 or 20 row children remain its children. No public owner-assignment method or new cell-editing API is introduced. TABLECONTENT and TABLEGEOMETRY are still opaque in this checkpoint, so cloning their containing dictionary or erasing the complete ownership tree rejects before registration, handle allocation or erasure. Foreign adoption rejects as before. These restrictions remain necessary until each child's complete supported schema has independent qualification.

The new conformance carriers retain every native subclass packet and descendant common owner. The external source dictionary is represented by an explicit carrier dictionary; both the wrapper owner and its matching reactor are rebound to that dictionary. Four emitted maps identify those substitutions. Each native graph is saved in text and binary before and after an allowed wrapper XData edit. The verifier compares the complete wrapper, opaque child, DATATABLE and row-XRECORD subclass packets against the unchanged source bytes. Exactness means typed group/value preservation, including double bit patterns; the typed reader's existing lexical normalization still applies.

`RegisterCompositeTableOwnershipTests` covers these four native graph/transport combinations, reversed physical object ordering, malformed targets/owners, source-identity decoys, unknown private variants and late-child binding atomicity. The prior declared-ownership suite now expects the two exact native composites to bind and retains an explicitly private synthetic unbound control.

`tools/verify_composite_table_ownership.py` requires eight emitted DXFs and four actual ownership maps. It independently verifies all 41 native row-XRECORD packets and runs 80 corruption controls covering markers, target identities, common owners, missing descendants, scalar preservation, map ordering and common metadata. Missing outputs or maps reject. A separate qualification receipt records the final Debug/Release runs and actual file-level corruption probes.

This module does not qualify complete native source loading, private cell/style dependencies, cross-profile backing conversion, field/formula evaluation, TABLE editing, display regeneration or AutoCAD open/AUDIT/save/reopen. The source drawing and the scoped ownership carriers are distinct qualification boundaries.
