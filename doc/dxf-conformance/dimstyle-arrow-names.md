# Context-qualified DIMSTYLE group 5 names

Baseline: merged PR #32, commit `2c6bfbc980ab8e57a0b2b9b5525e2da3a7b0e9bc`.

## Defect and corrected contract

The raw parser treated every group 5 as an object identity. In a DIMSTYLE table entry,
obsolete DIMBLK is instead an arrow-block **name**; the entry's identity is group 105.
Empty/nonhexadecimal names were rejected, and hexadecimal-looking names lost case and
leading zeros. Retaining original bytes alone did not prevent corruption during tag
inspection, edits or transport conversion.

`DxfRawDocument` now recognizes this exception in a DIMSTYLE record in the DIMSTYLE
table of TABLES, outside group 102 control data and XData. A bounded lexical context
tracker is shared by parsing and snapshot validation. Codecs receive an internal,
explicit context flag; standalone codecs and the typed `DxfDocument` reader retain
their existing strict defaults. Group 105 and group 5 outside this exception remain
validated handles. Record, table, section and control-group boundaries reset the
exception correctly; structural comparison is case-insensitive, names are not.

The ordinary `DxfTag(5, value)` constructor remains a handle constructor. To explicitly
author the exceptional field, use:

```csharp
DxfTag name = DxfTag.CreateDimensionStyleArrowName("00aB");
// Code == 5; ValueType == String; HandleKind == None; Value == "00aB".
```

The factory permits empty strings, preserves spelling and rejects null/NUL. Complete
raw snapshots reject a name tag outside its admitted context and a handle-typed tag
in the name slot. Scoped immutable edits perform the same validation and preserve
outside-range tags. Binary names containing CR/LF remain representable; normalized
text conversion fails before writing the destination, as for all multiline raw strings.

## Version coverage and executed evidence

| Contract | 2000 AC1015 | 2004 AC1018 | 2007 AC1021 | 2010 AC1024 | 2013 AC1027 | 2018 AC1032 |
|---|---|---|---|---|---|---|
| Raw DIMBLK name, byte retention, normalized save, transport conversion | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Typed DIMBLK name-to-block resolution | Not added | Not added | Not added | Not added | Not added | Not added |

The 60 behavioral cases added first failed against the unchanged baseline in both
Debug and Release: **5,446 passed / 60 failed**. They use a separate byte encoder,
not the production tag factory or code-value writers. Eight additional registered
cases exercise factory validation, malformed placement, nested control groups, XData,
scoped edits, structural casing and preflight failure. Corrected signed-library .NET 8
runs: **5,514 passed / 0 failed**, Debug and Release. Linux/Windows SDK CI and existing
netstandard2.0/source-audit gates are required before merge; compilation is not older
runtime execution evidence.

`tools/verify_dimstyle_arrow_names.py` independently verifies all 12 retained normalized
fixtures with ezdxf 1.4.4's low-level tag readers: exact DIMBLK `00aB`, identity `11`
at group 105 and unchanged LINE end coordinates. This is tag evidence, not whole-file
AUDIT success: synthetic names need not resolve to blocks. The fix was prompted by
pre-existing declared-R13/R14 fixtures, but admitting those raw profiles is a separate PR.

## Scope boundaries

No historical version admission, semantic block-name resolution, handle remapping,
modern 342/343/344 substitution, ACIS support or full-standard certification is implied.
This tracker is not a complete validator for application data or table schemas.

## Primary references

- Autodesk DIMSTYLE lists obsolete group 5 DIMBLK separately from modern group 342:
  https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm
- ezdxf's handle documentation identifies DIMSTYLE's group 105 identity exception:
  https://ezdxf.readthedocs.io/en/stable/dxfinternals/handles.html
