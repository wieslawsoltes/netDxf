# R13 and R14 raw-document profiles

Baseline: `61c512f8d13063cc16f0e19fdfcf60ba3388c3e0`, after merged PR #33.

## Capability and pipeline boundary

`DxfRawDocument` now admits the explicitly declared AC1012 (R13) and AC1014 (R14) database families in text and binary form. It supports bounded loading, immutable tag/section/record indexes, original-byte output, normalized output, same-profile text/binary conversion and scoped record edits. This is the preservation pipeline, not the typed `DxfDocument` geometry pipeline. Typed construction, loading and writing remain limited to the six AutoCAD 2000–2018 families; tests protect that boundary.

| Capability | R13 / AC1012 | R14 / AC1014 | 2000 / AC1015 | 2004 / AC1018 | 2007 / AC1021 | 2010 / AC1024 | 2013 / AC1027 | 2018 / AC1032 |
|---|---|---|---|---|---|---|---|---|
| Raw text and binary transport | Added | Added | Existing | Existing | Existing | Existing | Existing | Existing |
| Binary group-code width | 16-bit | 16-bit | 16-bit | 16-bit | 16-bit | 16-bit | 16-bit | 16-bit |
| Code-page selection | Header/default | Header/default | Header/default | Header/default | UTF-8 | UTF-8 | UTF-8 | UTF-8 |
| DOS numeric code-page aliases | Tested | Tested | Tested | Tested | Not used | Not used | Not used | Not used |
| Typed entity model | Not admitted | Not admitted | Existing partial model | Existing partial model | Existing partial model | Existing partial model | Existing partial model | Existing partial model |

A raw snapshot preserves records without claiming their historical schema is valid. Lexical record deletion does not delete dependencies; replacing a layer color does not rebuild a drawing database. The existing byte/tag/string limits, cancellation, complete EOF framing and immutable-profile rules remain in force. An edit cannot change `$ACADVER` into a different family: this API does not implement a schema downgrade by relabeling a file.

## Historical encoding correction

The external `small_r13.dxf` declares `dos932`. Previously the raw loader admitted only `ANSI_<number>` declarations, rejecting this real fixture independently of version admission. Pre-2007 profiles now also resolve case-insensitive `DOS<number>` aliases. The original declaration spelling is preserved. ASCII-compatible encodings are required, with exception fallbacks rather than replacement characters; arbitrary runtime encoding names, malformed numeric declarations and UTF-16/UTF-32 remain rejected. Missing declarations retain the existing code page 1252 default. Modern profiles remain UTF-8 regardless of the legacy header value.

Executed alias cases cover DOS437, DOS850 and DOS932 in R13, R14, 2000 and 2004, both transports, including accented and Japanese characters. This is not an exhaustive list of Autodesk code-page aliases or a claim that every runtime provides every numeric encoding. The existing target-specific encoding-provider behavior is unchanged.

## External corpus and independent evidence

Four pre-existing fixtures from `mozman/ezdxf`, pinned to commit `d6f2ac10caeddc712ed1824aaeb3b9c050de4a04`, are vendored in `tests/fixtures/legacy`. Their unmodified bytes, SHA-256 checks, Git blob identities, exact source URLs and MIT license are retained there. Tests do not need a network connection.

| Fixture | Declared profile | Transport | Encoding | Ordered tags |
|---|---|---|---|---:|
| small_r13.dxf | AC1012 | Text | DOS932 | 1,502 |
| small_r14.dxf | AC1014 | Text | ANSI_1252 | 789 |
| bin_dxf_r13.dxf | AC1012 | Binary | ANSI_1252 | 2,075 |
| bin_dxf_r14.dxf | AC1014 | Binary | ANSI_1252 | 2,083 |

The tests verify exact original-byte saves, all ordered tag values after normalization and cross-transport conversion, record partitions, and a first-LAYER color edit with unchanged outside-range tags. These fixtures exercise POLYLINE/VERTEX/SEQEND, XRECORD and opaque proxy records. Their declared versions do not prove they were produced by original R13/R14 applications or that every contained record is valid for those historical versions.

The development-only verifier `tools/verify_legacy_raw_profiles.py`, executed with ezdxf 1.4.4, compares **all 6,449 ordered tags** in the four original files against their normalized text outputs, then verifies the four edited outputs differ only at the selected layer-color tag. Handle spelling is canonicalized for handle comparisons, not for DIMSTYLE block names. This uses an independently implemented low-level tag reader; it does not invoke `ezdxf.audit`, certify dependency validity, or execute AutoCAD.

```sh
# Standard SDK harness, from the repository root:
dotnet run --project tests/netDxf.Conformance -c Debug
# Independent, optional development check after the fixture-producing tests:
python tools/verify_legacy_raw_profiles.py artifacts/conformance
```

Set the `DXF_TEST_ARTIFACTS` environment variable to select a different output directory. The Python verifier is not a runtime dependency of netDxf.

## Red/green and merge gates

With the final new tests and unchanged PR #33 production code: **5,515 passed / 37 failed**, both local Debug and Release. With the version and encoding changes: **5,552 passed / 0 failed**, both configurations. There are 38 new registered cases. Both green configurations were repeated after the external-edit fixture was corrected to select an existing LAYER color; the original test assumed a `$LTSCALE` variable that one source fixture does not contain. No production contract was relaxed to accommodate that test correction.

Local execution uses the signed production library on .NET 8. Final-head Linux/Windows Debug/Release SDK tests, netstandard2.0 compilation and source-audit evidence are additional merge gates. Compiling an older target is not execution on that older runtime.

## Remaining work

Pre-R13 binary uses a different group-code encoding and is not admitted by this change. Headerless legacy drawings, other historical code-page aliases, complete typed R13/R14 schemas, old-style block-name resolution in the typed pipeline, schema-aware upgrades/downgrades, ownership repair and AutoCAD-produced/AutoCAD-validated conformance corpora remain separate tasks. Full AutoCAD DXF capability is not claimed.

## Primary references

- Autodesk database version identifiers: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm
- Autodesk binary framing, 16-bit group codes from R13 and the older 255 escape: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- Exact external sources and license: [provenance](../../tests/fixtures/legacy/provenance.json) and [MIT license](../../tests/fixtures/legacy/LICENSE).
