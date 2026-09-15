# TABLESTYLE and CELLSTYLEMAP assessment

This evidence supports the implemented [stored, immutable TABLESTYLE API](stored-table-styles.md).
It does not qualify an editable style model, custom cell-style map schema or TABLE
regeneration. Existing TABLE preservation remains a separate feature.

## Primary references

Autodesk's [AutoCAD 2009 DXF Reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf),
printed pages 229–231 (PDF pages 237–239), documents AcDbTableStyle. It identifies
the description, flow direction, margins, suppression flags and repeated text
style, height, alignment, colors, fill, data/unit types and border fields. Group
90 describes cell data type; group 91 describes cell unit type. Border visibility
codes 284–289 use zero for invisible and one for visible. The searchable PDF has
no CELLSTYLEMAP entry.

The official [ObjectARX AcDbTableStyle class reference](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-RefGuide/files/OARX-RefGuide-AcDbTableStyle.html)
places style objects in the named object's ACAD_TABLESTYLE dictionary and
describes their formatting role. The [method index](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-RefGuide/files/OARX-RefGuide-__MEMBERTYPE_Methods_AcDbTableStyle.html)
includes named cell-style creation, deletion and enumeration. These API-level
operations do not specify the stored CELLSTYLEMAP packet grammar.

## Exact native packets

Run `python tools/table_oracle/assess_table_styles.py` to verify five packet sets
against the original pinned drawings in `tests/fixtures/table-oracle`. The tool
checks the source hashes, reconstructs source records without changing any byte,
checks reciprocal style/extension/map ownership and compares the extracted
fragments with `tests/fixtures/table-style-oracle`. Use `--write` only to regenerate
evidence deliberately. The generated [manifest](../../tools/table_oracle/table-style-assessment.json)
pins every source URL, source digest, fragment digest, record digest and raw
scalar observations. The `.records.dxf.gz` files are record fragments and are
not standalone drawing fixtures. Source provenance is inherited from the
[existing native corpus](../../tools/table_oracle/fixtures.json).

| Source | Profile | TABLESTYLE | Extension dictionary | CELLSTYLEMAP | TABLESTYLE tags |
| --- | --- | --- | --- | --- | --- |
| acad_table_simple.dxf | AC1027 | 6B | B6 | B7 | 99 |
| acad_table_with_blk_ref.dxf | AC1032 | 6B | 11A | 11B | 99 |
| sample_AC1024_ascii.dxf | AC1024 | 87 | 104 | 130F | 101 |
| sample_AC1018_ascii.dxf | AC1018 | 87 | 104 | 13D9 | 91 |
| sample_AC1021_ascii.dxf | AC1021 | 87 | 104 | 136F | 100 |

Each style has three group-7 packets referencing its exact STYLE object at
handle 11. Each extension dictionary owns its 323-tag map through group 360
under `ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP`. Even the R2004 source retains
this map. R2004 has no row group 90/91/1 values; later samples have these values.
The AC1024 sample additionally has group 280 immediately after AcDbTableStyle,
as well as group 280 after its margins. Treating both occurrences as one title
suppression field would lose a distinct part of the source schema.

## Independent reader/writer limits

The existing oracle pin is ACadSharp
`f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11`. Inspection of that exact source finds
three reasons to restrict what an oracle pass can establish:

- Its [TABLESTYLE reader](https://github.com/DomCR/ACadSharp/blob/f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11/src/ACadSharp/IO/DXF/DxfStreamReader/DxfObjectsSectionReader.cs)
  assigns group 91 to `StyleClass`, while its
  [writer](https://github.com/DomCR/ACadSharp/blob/f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11/src/ACadSharp/IO/DXF/DxfStreamWriter/DxfObjectsSectionWriter.cs)
  emits `ValueDataType` into that group. Neither assignment follows the
  documented cell-unit role directly.
- The reader assigns the raw visibility boolean to `IsInvisible`; the writer
  emits zero when `IsInvisible` is true and one when false. This is an explicit
  reader/writer polarity mismatch. A successful parse alone cannot qualify a
  visibility projection or a rewrite.
- The writer orders the three row packets as data, title and header. Its reader
  ignores undocumented group 1. The stored implementation retains packet order and
  complete source fields, without assigning row roles.

These are source-inspection findings, not a completed new executable oracle
matrix. The previously qualified ACadSharp TABLE oracle covers flat entity cell
values and does not extend automatically to TABLESTYLE or CELLSTYLEMAP.

## Implemented boundary

`DxfTableStyle` preserves the loaded source packet, source version, exact named
STYLE dependencies and owned opaque map. Its classic projections expose only
qualified scalar values and ordered row packets. The [module contract](stored-table-styles.md)
describes the public API, opaque fallback, lifecycle rules, executable carriers
and independent corruption controls.

Raw group 90/91/1 and border visibility values remain uninterpreted. The extra
leading 280 variant retains its payload and declines the header projection.
Changing TABLECONTENT alone would leave entity caches, display blocks and
geometry potentially inconsistent, so cell editing remains a separate problem.
