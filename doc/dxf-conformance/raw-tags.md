# Immutable typed tags and handle metadata

Baseline: `c6a7d18add0dfdafc47c44d700e7859414467aaf`, after merged PR #25.

## Delivered API

`netDxf.IO.DxfTag` stores a validated group-code/value pair with an exact CLR type. `DxfGroupCode.TryGetValueType` and `GetValueType` expose the encoding recognized by the modern codecs; `GetHandleKind` distinguishes object identities, arbitrary handles, soft/hard pointers, soft/hard owners, and XData handles. Negative application-only codes and unrecognized ranges are rejected.

Strings and handles are not interchangeable semantically even though both use CLR strings. Group 330 in an ACAD_REACTORS control group is not automatically the containing object's owner: record and control-group context is still required. The API classifies codes but does not resolve, allocate, remap or validate a complete database graph.

```csharp
using netDxf.IO;

var owner = new DxfTag(360, "FFFFFFFFFFFFFFFF");
var count = new DxfTag(90, 12);       // Int32
var flags = new DxfTag(70, (short)3); // Int16, no implicit narrowing
var data = new DxfTag(310, new byte[] { 0, 255, 127 });
byte[] copy = (byte[])data.Value;
copy[0] = 99; // cannot modify the stored tag
```

Binary input and output arrays are copied. Strings retain ordinary .NET string contents and handle spelling; no Unicode escape decoding or handle normalization is performed by the constructor. Doubles retain their bits, including negative zero and subnormals; NaN/infinity are rejected by the existing library finite-number policy. Handles must contain 1–16 ASCII hexadecimal digits; full unsigned 64-bit values are admitted without signed overflow. NUL is rejected in string values. The raw-document continuation permits physical CR/LF in binary string values; raw text export rejects those values before writing. This supersedes the initial transport-independent newline rejection.

## Boundaries

This is the foundation for ordered record preservation, not an integration of unknown records into `DxfDocument`. It does not yet read or write a document itself. The existing semantic reader/writer behavior is unchanged.

Type recognition is not a record schema or a historical version capability. The broad 1000–1071 ranges accepted by the existing codecs are mirrored for compatibility; not every code in those ranges is a standard XData field. In particular, this API is not a historical-version permission table. Groups 450–459 use the existing 32-bit DXF long representation, distinct from 160–169 Int64.

Binary tags are immutable arbitrary byte sequences at this layer. The transport writer and record schema must enforce their respective chunk limits; tag creation alone does not guarantee that a large byte array can be emitted as one binary DXF chunk. Invalid complete tags are rejected before they can enter a future raw-record pipeline. Comments, original whitespace, hexadecimal spelling normalized by existing readers, section grammar, and unknown dependency closures need their own preservation APIs.

## Verification

943 new registered cases, including every recognized code through the actual signed production text/binary codecs (except binary comments, which the binary codec excludes). The tests exhaustively inspect admission across all 65,536 `short` values, validate handle categories, test exact CLR types, array aliasing, integer endpoints, double bit patterns, malformed values and four cultures. The compiler/runtime are the retained Roslyn/.NET 8 workbench; no production dependency or target framework was changed.

Local Debug and Release suites: **4,549 passed / 0 failed**. Final-head Linux/Windows SDK, netstandard2.0 and source-audit checks are merge gates. This is a new API, so no failing old-API test result is invented. Existing 3,606 regressions still pass.

## Primary sources

- Autodesk primitive value types and string encoding: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-2553CF98-44F6-4828-82DD-FE3BC7448113.htm
- Autodesk numerical group codes and reference semantics: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
- Autodesk object ownership and application classes: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-CC1D6F91-42BF-4946-8D4B-6FC6A39100C5.htm
