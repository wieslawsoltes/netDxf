# Explicit replacement of stored TABLEGEOMETRY

`DxfStoredTableGeometry.ReplaceGeometry(rowCount, columnCount, cells)` replaces
the qualified public packet of an already loaded TABLEGEOMETRY object. Public
constructors for `DxfStoredTableGeometryCell` and `DxfStoredTableCellGeometry`
create immutable values for that operation. The TABLEGEOMETRY object itself
still has no public constructor and remains bound to its source document,
owner graph and DXF profile.

This API writes the values explicitly supplied by the caller. It does not
calculate table layout, infer cell addresses, evaluate TABLECONTENT, update
TABLESTYLE/CELLSTYLEMAP, synchronize related records or regenerate a TABLE.
The [existing native evidence and wire grammar](table-geometry.md) are unchanged.
Unknown subclasses, unknown fields and private shapes remain whole opaque
objects and do not acquire this editing API.

```csharp
var cells = geometry.Cells.Select(cell => new DxfStoredTableGeometryCell(
    cell.GeometryDataFlags,
    cell.WidthWithGap + 0.5,
    cell.HeightWithGap,
    cell.GeometryReference,
    cell.Geometry));

geometry.ReplaceGeometry(geometry.RowCount, geometry.ColumnCount, cells);
```

## Values and admission limits

The public value constructors expose only the existing decoded fields. Geometry
flags and group 95 retain their full signed integer range. Every double and
vector component must be finite; negative dimensions and signed zero remain
permitted. Row and column counts must be between zero and 1,048,576 inclusive.
Cell and content counts come from the supplied ordered lists. Repeated values
retain their positions, and empty lists are accepted. Row count multiplied by
column count does not have to equal the number of stored cells.

An object payload uses four header tags, five tags per cell and eleven tags per
content-geometry packet. The complete emitted record is limited to 1,048,576
tags, including its group-5 identity, common group-330 owner, extension dictionary,
distinct persistent-reactor block and XData. Group 0 is excluded, matching reader
admission. The operation bounds enumeration before allocating the new tag packet,
then checks the full metadata-inclusive size. Save validation applies the same
limit if later ordinary metadata changes increase the record size.

The constructor for a cell copies its content-geometry sequence. The replacement
method separately copies the cell sequence once. Neither retains caller lists
or allocates document handles.

## Atomicity and source identities

Replacement finishes caller enumeration and enumerator disposal before checking
the current document state. It rejects a reentrant replacement attempt even when
the caller catches the nested exception. The loaded object must still be the
registered object in its source database; read-only database validation checks
the source profile, owner ancestry, dictionary/root links, declared owning
wrapper slots, metadata and current dependencies.

Every nonnull `GeometryReference` is an explicit object supplied by the caller.
It must be that exact currently registered identity in the source document.
Authored objects and owner-held metadata may be selected when they meet the same
identity checks. A foreign object with the same name or handle is rejected.
No lookup substitutes another object, and no implicit import or handle assignment
occurs. References retained from loaded cells continue to use their accepted
source identities. The target's geometry meaning and a narrower reference type
are not inferred.

All new tags and dependency collections are prepared before committing state.
The operation then swaps the completed packet, cells, references and counts
without running caller code. Previously obtained `Payload`, `Cells`, nested
`Geometry` and `References` snapshots remain unchanged. Resource-removal guards
follow the new dependencies: a released target can be removed, while a newly
selected target and its relevant owning entity/block are protected.

A request with equal counts, raw integers, exact object identities and bit-equal
finite floating-point values is a no-op. It keeps the original payload and cell
snapshots, including all handle tags already retained by the reader. This does
not restore lexical input spelling normalized by the underlying codec. Rejected requests also keep
the prior packet. Independent document changes made by enumeration callbacks
are caller operations and are not rolled back; the replacement revalidates them
before accepting any geometry change.

Cloning, erasure, cross-document adoption and source-profile conversion remain
unsupported for the loaded object. Successful packet editing does not change
these lifecycle restrictions or create a full TABLE authoring API.

## Qualification

`RegisterEditableTableGeometryTests` exercises all eight pinned native objects
through both input and output transports, both no-op reconstruction and explicit
known-field replacement. Native replacement changes every known scalar/vector
family and appends a cell while retaining the owning wrapper and related native
TABLECONTENT, TABLESTYLE and CELLSTYLEMAP packets.

Additional controls cover all supported source profiles, explicit authored
resource/entity/metadata references, dependency release, immutable snapshots,
constructor validation, callback/disposal failures, caught reentry, changed
profile/root ownership during enumeration, and the exact complete-record limit.
The size boundary includes a real binary save/reload of a 1,048,576-tag record
and a separate metadata-inclusive overflow rejection before output.

`verify_editable_table_geometry.py` requires all 46 exported artifacts. It checks
the deterministic native edits and original no-op packets, exact synthetic
fields including signed zero, explicit physical reference identities, complete
carrier dependency closure and CLASS/ownership relationships. It rejects 368
actual output corruptions and independently opens and audits every output with
ezdxf. The [final qualification receipt](table-geometry-editing-qualification.json)
records 125 new editing cases and 761 unique cases across nine related suites in
each of Debug and Release, with all eight output gates passing. Normal net8.0
library builds report zero errors and the existing 561 CS1591 warnings;
conformance builds report zero warnings and errors.

The separately frozen independent review passed 40 cases in each configuration,
including all eight native packets, and audited 26 outputs per configuration
with zero errors or repairs. Its unchanged
[receipt](table-geometry-editing-review/summary.json), detailed results and
[reproducible harness](../../tools/table_geometry_edit_review/README.md) are
preserved. The reviewer requested no production changes.

Native AutoCAD open/AUDIT/save/reopen remains unexecuted. Acceptance by a native
application, table rendering and regeneration are not inferred from stored
packet qualification.
