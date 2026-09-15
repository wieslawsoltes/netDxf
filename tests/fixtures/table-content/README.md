# Native TABLECONTENT fixtures

The five unchanged compressed originals and their URLs/hashes are pinned in
`../table-oracle` and `tools/table_oracle/fixtures.json`. The two ezdxf v1.4.4
examples qualify whole original loading. The three ACadSharp samples are pinned
to commit `f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11`; they require selected
carriers because unrelated original entities include unqualified R2004
MULTILEADER and live ACIS history.

`extract_fixtures.py` uses ezdxf 1.4.4 to generate the independent scaffolds. It
copies all six ACadSharp TABLECONTENT packets, their wrapper ownership trees,
actual referenced STYLE/LTYPE/TABLESTYLE packets and the style's owned map, plus
actual BLOCK_RECORD/BLOCK/ENDBLK/member packets. Native block-record backlinks
and TABLESTYLE persistent reactors also require their real INSERTs, containing
display blocks, and both ACAD_TABLE entities with their extension graphs. The R2004 carrier additionally
retains both native DATATABLE objects and all 41 owned row XRECORDs. There are
136 selected source records in R2004 and 90 each in R2007 and R2010.

Every TABLECONTENT identity, common owner and subclass field remains exact.
All exposed content handles retain their original values and point to copied
source resource identities. The generated scaffold's own handles are shifted
to the `F00000` range to avoid collisions. The generated scaffold is ordinary
carrier context; it does not replace the source STYLE/LTYPE/BLOCK_RECORD
dependencies with same-number placeholders or empty block definitions.

The manifest lists every selected source record and every permitted change by
tag index, code, old value and new value. The changes are restricted to common
ownership metadata: native symbol-table record owners become the corresponding
generated tables; the first wrapper and TABLESTYLE owner/reactor become the
generated root dictionary; the copied top-level TABLE and INSERT owners become
the generated model-space block record. The second wrapper retains its native
parent extension dictionary and TABLE owner. No subclass body is adapted.
Generated CLASS order is sorted, native CLASS definitions retain their fields
except the carrier's actual instance counts, and metadata is fixed for repeatable
generation. No audit repairs are applied: all three carriers must audit with
zero errors and zero fixes after writing.

The generator proves complete selected-packet equality after only the listed
metadata changes and verifies every retained exposed nonzero semantic handle
against a physical carrier record, including common persistent reactors. The
shipping native tests then load these carriers and both whole ezdxf examples,
save in ASCII and binary, and reload. The independent output gate also requires
all selected identities and all original exposed dependencies to retain their
source types and checks emitted selected records for dangling references.
This qualifies
stored content packets, explicit resource identities and ownership context.
It does not qualify the other omitted contents of the ACadSharp drawings,
editable cells, formula evaluation, table layout, rendering or regeneration.
Output packet equality covers TABLECONTENT and its native wrapper. Other typed
resource packets may be normalized; current BLOCK_RECORD output omits optional
group-331 backlinks while retaining their original target records.

Run from the repository root:

```sh
python tests/fixtures/table-content/extract_fixtures.py
```
