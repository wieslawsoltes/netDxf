# Rectangular INSERT arrays (MINSERT)

Baseline: `16b7aee24faa3e24acac7d5735b1822ac90839fc`, after merged PR #24.

## Implemented data and API

`Insert.ColumnCount`, `RowCount`, `ColumnSpacing` and `RowSpacing` retain DXF groups 70, 71, 44 and 45. Counts are positive 16-bit integers, default 1. Spacing is finite, signed and defaults to zero. Invalid input counts are rejected rather than normalized to a singleton. `IsMultiple` and `InstanceCount` expose the array shape without expanding it; the maximum product of the two supported counts fits in a 32-bit integer.

The DXF record type remains INSERT. Export selects `AcDbMInsertBlock` when either count exceeds 1 and `AcDbBlockReference` otherwise. Default fields are omitted; nonzero spacing survives even when its count is 1. Cloning retains array fields and independent block/attribute data. Reading handles reordered fields and comments, including records in nested block definitions. Existing attribute sequences, XData, handles and unit conversion remain integrated.

```csharp
var component = new Block("Component");
component.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 0, 0)));
var array = new Insert(component, new Vector3(100, 200, 0))
{
    RowCount = 3,
    ColumnCount = 4,
    RowSpacing = 20,
    ColumnSpacing = 30,
    Rotation = 45,
    Scale = new Vector3(2, 2, 1)
};
Vector3 cellPosition = array.GetGridPosition(row: 2, column: 3);
List<EntityObject> oneCell = array.ExplodeCell(row: 2, column: 3);
foreach (EntityObject entity in array.ExplodeEnumerable())
{
    // Consume one cell's expanded entities at a time, without materializing the full grid.
}
var document = new DxfDocument(DxfVersion.AutoCad2000);
document.Entities.Add(array);
document.Save("array.dxf");
```

## Geometry contract

A cell offset is `OCS(normal) * Rz(rotation) * (column * columnSpacing, row * rowSpacing, 0)`. The block's scale and block-unit conversion do **not** scale this grid offset. They still apply to the referenced block geometry. Spacing therefore uses the containing drawing/block coordinate units. The block origin is subtracted only when expanding geometry, not when querying the insertion point.

`GetGridPosition` is indexed O(1) access. `ExplodeCell` expands one cell. `ExplodeEnumerable` is row-major and retains only a single cell's intermediate entity list; `Explode()` materializes the entire sequence and therefore consumes memory proportional to the output. Empty arrays of empty blocks return immediately. No global expansion quota is introduced; callers handling untrusted drawings should use indexed/lazy access with their own budget. Non-finite calculated coordinates are rejected. Mutating an INSERT or its referenced block during enumeration is unsupported.

Coincident zero-spacing cells retain logical multiplicity; no deduplication changes the declared array. Nested INSERTs remain nested after one explosion level and keep their own array fields. Existing attribute-to-TEXT conversion is repeated with a translated position per cell; values and formatting are not independently stored for each cell.

For active arrays, `TransformBy` transforms the grid axes, insertion, block scale and attached attributes together. Rotation, translation, reflection and anisotropic scaling are supported when the resulting frame remains orthogonal. A collapsed axis or shear that cannot be encoded by a rectangular INSERT is rejected before changing array state. The orthogonality check uses normalized axes and a `1e-10` dot-product tolerance; zero-scale checks use the existing library tolerance. The prior singleton INSERT transformation path is unchanged. General singleton shear/normal transformation issues, dormant singleton spacing transforms, clipping and recursive expansion policies remain outside this feature.

## Version matrix

| Capability | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Read/write array fields | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Correct array/singleton subclass and defaults | Tested | Tested | Tested | Tested | Tested | Tested |
| Attributes, XData, cloning and repeated reload | Tested | Tested | Tested | Tested | Tested | Tested |
| Nested arrays and block/drawing unit separation | Tested | Tested | Tested | Tested | Tested | Tested |
| Independent cell-placement/geometry comparison | Passed | Passed | Passed | Passed | Passed | Passed |

No pre-2000 reader/writer dialect is added, even though array INSERT records have a longer history.

## Regression evidence

215 new registered cases. With the new model/geometry but unchanged document IO: **3,450 passed / 156 failed**, reproducing discarded array fields, accepted invalid counts and singleton export. With reader/writer integration: **3,606 passed / 0 failed**, both Debug and Release, using local Roslyn/.NET 8 reference compilation.

The tests cover independently assembled records, seven shape/spacing profiles including maximum counts, malformed counts, exact wire types/subclasses, singleton defaults, clone isolation, attributes/XData, nested arrays, differing block/document units and repeated cross-transport saves. Geometry tests combine four normals and four rotations with signed/zero spacings, negative/nonuniform scales, all cell coordinates, transformed endpoints, attribute placement and source isolation. Indexed/lazy access is exercised on a 32,767 × 32,767 grid without materializing it. Rejected shear and singular transforms leave the source unchanged.

Independent ezdxf 1.4.4 read/audit of all 12 generated `insert-array-*.dxf` fixtures expands their six cells and compares all WCS insertion positions and LINE endpoints against separately retained .NET results. All match within `1e-10` tolerance, with **zero errors and zero repairs**. Those fixtures include a tilted extrusion normal, rotation, a negative scale component and nonzero block origin. No AutoCAD process is executed, and this is not certification of unrelated entity transformation/rendering behavior.

## Primary references

- Autodesk INSERT group codes: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-28FA4CFB-9D5E-4880-9F11-36C97578252F.htm
- Independent ezdxf INSERT API and multi-insert semantics: https://ezdxf.readthedocs.io/en/stable/blocks/insert.html
- Independent implementation: https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/insert.py

This supersedes the baseline INSERT row's missing array fields and array expansion subset, not generic dynamic/associative block support.
