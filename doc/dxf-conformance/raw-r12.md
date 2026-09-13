# R11/R12 raw DXF preservation and binary framing

Baseline: `312667df82292cc669b9487bc97e8ea7115a74a3`, after merged PR #34. Audit date: 13 September 2026.

## Implemented contract

`DxfRawDocument` admits the AC1009 database family (R11/R12, represented by `DxfVersion.AutoCad12`) in text and binary. This is an ordered preservation/editing pipeline, not admission of R12 into the typed `DxfDocument` geometry model. No implicit version conversion, handle repair, or historical entity downgrade is performed.

The pre-R13 binary framing is implemented in explicit internal codec overloads. Ordinary group codes occupy one byte; larger codes use byte 255 followed by the actual little-endian 16-bit group code. XData Int32, Int16, strings, handles, doubles and length-prefixed binary chunks retain their primitive encodings. Existing constructor signatures keep their modern two-byte behavior; the raw-document loader selects the dialect. Noncanonical escaped codes below 255 are rejected; truncation never synthesizes an EOF.

The loader distinguishes framing at the initial group-0 structural string, then checks the real HEADER version against that framing. It does not search payload text for a version marker, nor retry failed decoding with a different dialect. Modern two-byte AC1009 files and legacy-framed AC1012+ files are rejected as inconsistent. A HEADER following another section is supported. Headerless drawings remain outside this explicit-profile API.

## Version/pipeline comparison

| Capability | AC1009 / R11-R12 | AC1012 / R13 | AC1014 / R14 | AC1015-AC1032 / 2000-2018 families |
|---|---|---|---|---|
| Raw text load, edit, normalized save | Implemented here | Existing | Existing | Existing |
| Raw binary load, edit, normalized save | One-byte + escaped codes, implemented here | Existing two-byte codes | Existing two-byte codes | Existing two-byte codes |
| Exact unedited same-transport bytes | Tested against pinned files | Existing | Existing | Existing |
| Cross-transport ordered values | Tested | Existing | Existing | Existing |
| Typed `DxfDocument` admission | Still rejected | Still rejected | Still rejected | Existing partial typed model |
| Full historical schema/evaluation | Not claimed | Not claimed | Not claimed | Not claimed |

Strings retain the selected code page or raw Unicode escapes without silently rewriting semantic data. ANSI and DOS aliases use the established legacy resolver. Text comments are retained; binary export requires their explicit removal. Transport tolerance for 128-255 byte chunks remains distinct from the standard 127-byte XData chunk limit. Unknown-record retention does not certify that a field exists in R12 merely because its primitive encoding is recognized.

```csharp
using System.IO;
using System.Linq;
using netDxf.IO;

using var input = File.OpenRead("drawing-r12.dxf");
DxfRawDocument original = DxfRawDocument.Load(input);
var line = original.Sections.Single(s => s.Name == "ENTITIES")
    .Records.First(r => r.Name == "LINE");
DxfRawDocument edited = original.WithRecord(line,
    line.Tags.Select(t => t.Code == 10 ? new DxfTag(10, 12.345678901234567) : t));
// Remove comments explicitly only when choosing binary output.
edited = edited.WithTags(edited.Tags.Where(t => t.Code != 999));
using var output = File.Create("edited-r12-binary.dxf");
edited.Save(output, binary: true);
```

## Reproducible evidence

46 new registered cases. Final tests against unchanged PR #34 production: **5,553 passed / 45 failed**. Corrected signed production assembly: **5,598 passed / 0 failed** in local Debug and Release on .NET 8. Existing unsupported-version controls now use AC1006, which remains unsupported, instead of the newly admitted AC1009; the explicit binary mismatch tests cover the old two-byte AC1009 form.

The two unchanged external fixtures are `ASCII_R12.dxf` (595 tags) and `bin_dxf_r12.dxf` (499 tags), from the same pinned ezdxf commit used by PR #34. SHA-256, Git blob identities, source URLs and the upstream MIT license are retained under `tests/fixtures/legacy`. The existing `-text` attributes preserve their bytes on Windows. Tests use no network.

Independent ezdxf 1.4.4 checks compare all 1,094 input tags with each normalized text and binary output, including exact binary64 bits; scoped LINE edits change only group 10. It also loads the authored LINE/XData fixture in both transports and verifies actual XData primitive values. No AutoCAD process or complete drawing-graph AUDIT is claimed. The independently encoded byte fixture checks emitted one-byte and escaped-code framing directly, not merely a matching round trip.

Other cases exercise all standard XData value families, chunk boundaries, DIMSTYLE arrow names, code pages, comments, mismatched declarations, truncated/noncanonical escapes, late HEADER, fragmented nonseekable input, offsets, budgets, cancellation, stream ownership and unchanged typed admission. Final-head Linux/Windows Debug/Release CI and netstandard2.0 compilation remain merge gates.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_r12_raw_profiles.py artifacts/conformance
```

## Primary reference and remaining work

Autodesk's current binary DXF definition explicitly locates the one-/two-byte transition at R13 and describes the 255 escape, little-endian values and exclusion of binary comments:
https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm

An older 2015 page said R14 for the transition; the current definition and independently pinned R13/R14 binary fixtures agree on R13. This implementation does not use that obsolete wording as an alternate dialect rule.

R10/earlier profiles, headerless input, full typed legacy read/write, schema-aware down-save and dependency-closed edits remain separate features. The R12 raw path is not a lossy substitute for down-saving a modern typed document.
