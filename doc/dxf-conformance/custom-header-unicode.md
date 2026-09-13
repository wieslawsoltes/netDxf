# Unicode in custom HEADER values

Baseline: `fe15343d93622bf40a84f6176284bf67d1ef125d`, after merged PR #20.

## Defects and scope

Unmodeled/custom HEADER strings bypassed the document string codec. On input, escaped Unicode remained literal `\U+nnnn` text. On output, Unicode was written as raw UTF-8 even for AC1015/AC1018 files declaring a legacy code page; independent and netDxf readers then interpreted those bytes incorrectly. Actual failures reproduced corrupted Polish and Japanese project names and hyperlink paths.

Decode string values when adding custom HEADER variables, and encode string values on custom-header output using the same helpers as modeled strings. Do not mutate the stored value or change numeric/vector types, names, tag order, duplicate-name policy, handle-reference behavior or exception conventions. Values supplied to HeaderVariable are ordinary decoded .NET strings, not pre-encoded DXF text.

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2000);
document.DrawingVariables.AddCustomVariable(new HeaderVariable("$PROJECTNAME", 1, "Zażółć 東京"));
document.DrawingVariables.AddCustomVariable(new HeaderVariable("$HYPERLINKBASE", 1, @"C:\Próby\東京\rysunki"));
document.Save("unicode-header.dxf");
```

## Version and fidelity matrix

| Path | AC1015 / 2000 | AC1018 / 2004 | AC1021 / 2007 | AC1024 / 2010 | AC1027 / 2013 | AC1032 / 2018 |
|---|---|---|---|---|---|---|
| Read encountered Unicode escapes | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary | Tested text/binary |
| Write non-ASCII string values | Unicode escapes | Unicode escapes | UTF-8 | UTF-8 | UTF-8 | UTF-8 |
| Repeated cross-transport round trips | Tested | Tested | Tested | Tested | Tested | Tested |
| Scalar/vector types unchanged | Tested | Tested | Tested | Tested | Tested | Tested |

Independent fixtures cover PROJECTNAME, HYPERLINKBASE, MENU and UCS name/base strings, including Polish, Japanese, Cyrillic, Greek, ordinary backslashes, empty strings and ASCII controls. The tests establish string transport and retention, not resolution of a named UCS or external path. General unmodeled multi-tag header records, `$DIM*` overrides, literal-escape ambiguity, preservation of comments/order, legacy releases before 2000 and complete referenced-object graphs remain separate work.

## Regression and interoperability evidence

60 new registered cases. Old production code: **2,739 passed / 20 failed**. Corrected signed production library: **2,759 passed / 0 failed**, Debug and Release. The cases distinguish independently authored escaped input, ordinary input, exact wire representation, source-model immutability, editing, four alternating-transport cycles, and numeric/Vector2/Vector3 controls.

Independent ezdxf 1.4.4 verification loaded all 12 generated version/transport fixtures, checked the raw wire representation of all seven strings and their values using its `decode_dxf_unicode` helper, and reported **0 errors / 0 repairs**. Its HEADER accessor retains Unicode escapes rather than automatically decoding them, so the explicit decoder step is material; direct accessor equality is not claimed. This is independent wire/escape verification for these fixtures, not AutoCAD execution or complete HEADER support. Cross-platform SDK, netstandard2.0 and source-audit checks remain required before merge.

A separate audit fixture also exposed rejection of interleaved HEADER comments. It is deliberately excluded from this Unicode-only correction and is recorded for its own parser PR.

## Primary references

- Autodesk HEADER variable definitions and group codes: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm
- Independent DXF encoding description by the ezdxf author: https://ezdxf.readthedocs.io/en/stable/dxfinternals/fileencoding.html
