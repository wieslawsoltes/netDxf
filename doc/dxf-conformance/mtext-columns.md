# MTEXT column storage and editing

`MText.Columns` is an optional `MTextColumns` definition. It supports static columns, dynamic columns with automatic height, and dynamic columns with individual heights. Editable properties include count, reversed flow, width, gutter, common height, ordered individual heights, saved total height, and linked entities. `MText.DefinedHeight` also preserves height on an ordinary or linked MTEXT without introducing columns.

This API stores and edits DXF data. It does not measure fonts, balance text, calculate automatic line breaks, render MTEXT, or evaluate AutoCAD layout rules.

## Representation and version contract

| Storage | Output versions | Representation |
|---|---|---|
| `LegacyLinked` | R2000, R2004, R2007, R2010, R2013 | First MTEXT holds ACAD XDATA. Other columns are separate MTEXT entities referenced by handle. |
| `Embedded` | R2018 | One MTEXT holds the entire content and the column definition after group 101. |
| `Direct` | R2007 onward | Autodesk's documented groups 75/76/78/79/48/49 and counted group-50 heights in AcDbMText. |

Saving rejects incompatible storage/version combinations before writing bytes. Following the existing `DxfDocument.Save` contract, Debug builds throw the validation exception and Release builds return `false`. Reading direct tags is tolerant of the header version. Legacy and embedded output are independently verified with ezdxf; direct-tag input/output is qualified by authored wire fixtures, without an ezdxf rendering claim.

In direct storage, common group-50 rotation must precede column metadata. For manual dynamic columns, a group-50 sequence after metadata starts with the count and must contain exactly `Count` further group-50 heights. Incomplete and ambiguous sequences are rejected. This prevents height values from becoming rotation.

Legacy links resolve after the whole file loads, even if entities precede their first column or appear in a different physical order. Linked entities remain in their block. Missing, duplicate, cross-block, nested, cyclic, and multiply claimed links are rejected. The actual linked graph overrides a stale legacy metadata count, after which manual heights must match the actual count.

Known ACAD column sections are promoted into typed properties and regenerated on save. Other ACAD records and other applications' XDATA remain intact. Combining typed columns with raw ACAD column sections is rejected.

## Creating and converting columns

```csharp
var text = new MText("FirstSecondThird")
{
    Columns = new MTextColumns
    {
        Storage = MTextColumnStorage.Embedded,
        Type = MTextColumnType.Static,
        Count = 3,
        Width = 12.5,
        Gutter = 1.75,
        DefinedHeight = 20.25,
        TotalHeight = 20.25
    }
};
var modern = new DxfDocument(DxfVersion.AutoCad2018);
modern.Entities.Add(text);
modern.Save("columns-2018.dxf");

// Partitions come from your content model or renderer, not guessed wrapping.
var parts = text.ConvertToLinkedColumns(new[] { "First", "Second", "Third" });
var legacy = new DxfDocument(DxfVersion.AutoCad2013);
legacy.Entities.Add(parts); // Add every returned entity to the same block.
legacy.Save("columns-2013.dxf");
MText modernCopy = parts[0].ConvertToEmbeddedColumns();
```

Conversion returns detached copies and never removes source entities. Replace the complete old group explicitly. Legacy-to-embedded conversion concatenates strings exactly, inventing no paragraph or column breaks. It rejects different entity formatting on linked columns, which one MTEXT cannot preserve; normalize formatting explicitly first. Embedded-to-legacy conversion requires one non-null partition per column and exact preservation of the concatenated content. It does not split on `\N` automatically.

Manual dynamic columns use `Type = Dynamic`, `AutoHeight = false`, `DefinedHeight = 0`, and one height per column. Heights must be positive except for the final zero, which means remaining content. Newly converted manual legacy children omit standalone defined height; their heights live in the main column definition.

## Editing, cloning, and transforms

Scalar setters reject invalid enums, nonfinite dimensions, negative heights/gutters, and counts outside 1–32767. `Validate()` checks editable lists and cross-property consistency and runs before saving. `TotalWidth` is computed from count, width, and gutter. `TotalHeight` remains caller-supplied layout metadata and must be updated when layout changes.

Embedded duplicate direction, insertion, and reference width are retained independently in `EmbeddedTextDirection`, `EmbeddedInsertionPoint`, and `EmbeddedReferenceWidth`. Common entity fields, including explicit common rotation, have priority. Embedded fields provide fallback only when their common counterpart is absent. Applications may interpret inconsistent duplicate directions differently; repair conflicting source data explicitly before interoperability comparisons.

`StoredTotalWidth` retains the embedded extent even if it differs from computed width. Editing count, width, or gutter clears it. `ResetEmbeddedPlacement()` requests current primary values for duplicate placement fields on the next save. The reader rejects duplicate embedded scalars, missing required column fields, nonfinite values, nonintegral inferred automatic counts, partial embedded vectors, and an all-zero embedded text direction. Optional embedded vectors may be absent; when present, all three components are required. A zero insertion point remains valid.

`Clone()` copies metadata and legacy child entities independently. A standalone cloned first column has detached children, which must also be added to the document. `Block.Clone`, `Block.Create`, `Block.Save`, and `Insert.Clone` remap links to the actual copied block entities.

Embedded/direct column MTEXT supports translation, rotation, and positive uniform scaling. Dimensions scale and duplicate placement resets. Shear, reflection, singular and nonuniform transforms fail before mutation. Transforming a legacy first column with links, including `Insert.Explode`, fails because otherwise linked children would remain behind. Convert to an embedded entity explicitly or remove the linking definition before individually transforming detached columns.

## Validation

`MTextColumnTests.cs` covers all six versions in text and binary, authored direct/legacy/embedded fixtures, linked ordering, following entity/XDATA boundaries, malformed input, version gates, conversion, clone/block ownership, saved duplicate fields, and transform guards. It creates exactly 36 `mtext-columns-AutoCad*-{False,True}-{0,1,2}.dxf` fixtures audited with `python tools/verify_mtext_columns.py artifacts/conformance`. Ten committed independent ezdxf input fixtures run in normal CI, with a SHA-256 provenance manifest in `tests/fixtures/mtext-columns`. A separate conflicting-direction source verifies the documented duplicate-field contract. `DXF_INDEPENDENT_FIXTURES` can select another corpus of ten `independent-mtext-*.dxf` files.

References: [Autodesk MTEXT DXF](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm), [ezdxf MTEXT internals](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mtext.html), and the independent [ezdxf MTEXT implementation](https://github.com/mozman/ezdxf/blob/master/src/ezdxf/entities/mtext.py). Linked and embedded interoperability is qualified against ezdxf 1.4.4, not a licensed AutoCAD process.
