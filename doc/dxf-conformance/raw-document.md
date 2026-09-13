# Ordered raw DXF preservation

Baseline: `4b97e5c64bef5c0a86bb68c9da3ef17a20872989`, after merged PR #28.
The interrupted implementation was recovered as PR #29; its new tests were not
registered at the recovery checkpoint. This entry records the completed feature.

## API and fidelity contract

`DxfRawDocument` is an immutable ordered-tag document, separate from the existing
`DxfDocument` semantic geometry model. It retains unfamiliar records, subclass and
application data, repeated group codes, handle values, section order, duplicate
non-HEADER sections and unknown section names. `DxfRawSection` indexes each section
without copying its body tags. Comments between SECTION and its name are retained
in the complete `Tags` sequence. Section-name recognition tolerates case differences
without rewriting their original spelling. One unambiguous HEADER is required.

Unedited same-transport `Save` writes the original bytes, including BOM, line
endings, numeric spelling, handle padding/case and supported trailing whitespace.
`WithTags` snapshots explicit edits without changing the source; edited and
cross-transport saves normalize lexical formatting but preserve ordered primitive
values. Existing codec handle normalization is visible in `Tags`; only the retained
original bytes promise exact lexical fidelity. The raw API does not decode or
insert DXF Unicode escapes, recalculate class counts, resolve references, repair
ownership, load external resources or evaluate geometry/application payloads.

```csharp
using netDxf.IO;

using var input = File.OpenRead("drawing.dxf");
var raw = DxfRawDocument.Load(input, new DxfRawOptions(
    maximumBytes: 128 * 1024 * 1024,
    maximumTags: 2_000_000,
    maximumStringLength: 1_048_576));

// Copy without passing unfamiliar data through the typed geometry reader.
using var exact = File.Create("unchanged.dxf");
raw.Save(exact);

// Explicitly remove comments before requesting binary conversion.
// Other unsupported conversions still fail rather than dropping data.
var withoutComments = raw.WithTags(raw.Tags.Where(tag => tag.Code != 999));
using var binary = File.Create("without-comments-binary.dxf");
withoutComments.Save(binary, binary: true);
```

`DxfTag` now permits CR/LF in ordinary strings for binary retention. NUL remains
invalid, and handles still require 1–16 hexadecimal digits. This deliberately
relaxes the earlier transport-independent newline rejection. Raw text export
rejects multiline values before touching its destination; it never silently
substitutes MTEXT escapes or changes unknown record semantics.

## Version and transport matrix

| Contract | AC1015 / 2000 | AC1018 / 2004 | AC1021 / 2007 | AC1024 / 2010 | AC1027 / 2013 | AC1032 / 2018 |
|---|---|---|---|---|---|---|
| Text and modern binary raw load | Tested | Tested | Tested | Tested | Tested | Tested |
| Exact unedited same-transport copy | Tested | Tested | Tested | Tested | Tested | Tested |
| Ordered normalized/cross-transport save | Tested | Tested | Tested | Tested | Tested | Tested |
| Unfamiliar records, sections and dependencies as tags | Preserved | Preserved | Preserved | Preserved | Preserved | Preserved |
| Character decoding/encoding | Declared ANSI code page | Declared ANSI code page | Strict UTF-8 | Strict UTF-8 | Strict UTF-8 | Strict UTF-8 |
| Typed semantic evaluation of unfamiliar records | Not added | Not added | Not added | Not added | Not added | Not added |

The version declaration must remain unchanged in `WithTags`; there is no implicit
schema downgrade or code-page conversion. Pre-2000 files, UTF-16/UTF-32 transport,
ambiguous/missing profiles and unknown primitive encodings are rejected. Legacy
ANSI_1250/1251/1252/932 fixtures are tested; absent legacy code-page declarations
use 1252. A UTF-8 BOM conflicting with a legacy profile is rejected. On runtimes
using the netstandard target, legacy encodings may require the host's usual
CodePagesEncodingProvider registration. These limits do not imply that the DXF
standard itself excludes other profiles or record variants.

## Validation, budgets and ownership

The loader consumes from the caller's current position through physical EOF and
supports fragmented/nonseekable input. It never closes caller streams. Encoded
input/output bytes, tag count and decoded string length have explicit limits.
Input is buffered; limits are not a total-heap or streaming guarantee. String
length is checked after decoding within the byte bound, and the ASCII bootstrap
uses its own byte-bounded limit to avoid miscounting multibyte UTF-8 characters.

Missing/truncated EOF, invalid primitive values/encodings, duplicate profile
variables, incorrect section framing and trailing non-whitespace are rejected.
A SECTION entity inside ENTITIES is retained as a record, not treated as a nested
file-section opener. Arbitrary record bodies are not certified against a schema.

Normalized output is fully staged and bounded before destination writes. Binary
comments, over-255-byte physical chunks and unencodable strings are rejected;
comments/chunks are never automatically dropped or split. The 255-byte physical
bound does not replace smaller semantic chunk limits. Cancellation is checked
while reading, parsing, validating, serializing and copying. Final destination IO
failure or cancellation can leave partial bytes; existing destination suffixes
are not truncated. File replacement/transaction policy belongs to the caller.
Exceptions in this new API are consistent between Debug and Release.

## Verification

The recovered raw tests, once registered, produced **5,163 passed / 6 failed**;
all six failures exposed the contradictory binary multiline-string contract.
The completed implementation adds **223 registered cases** beyond the 5,021-test
baseline. Signed production assemblies compiled with the retained Roslyn/.NET 8
workbench pass **5,244 / 0 failed** in both Debug and Release. Normal SDK Linux and
Windows builds, netstandard2.0 compilation and source audit remain PR merge gates.

Tests cover independent text/binary fixture encoders, exact bytes and primitive
bits, unknown sections/records, repeated fields, application/reactor groups,
SECTION entities, reordered/duplicate/case-variant sections, handles, comments,
Unicode/code pages, explicit edits, malformed profiles, byte/tag/string budget
boundaries, fragmented/nonseekable/offset streams, cancellation and IO failures.

`tools/verify_raw_preservation.py` supplies repeatable independent ezdxf 1.4.4
validation. All **12 raw-preservation files** match the independently decoded
ordered primitive tags and double bits. These deliberately unfamiliar fixtures
are not claimed to have valid semantic CAD graphs. All **12 known LINE files**
retain untouched/edited coordinate bits and have **zero audit errors and zero
repairs**. The text verifier uses universal-newline handling, as a file reader
would; it does not include CR from CRLF in the logical string value. No AutoCAD
process was executed.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_preservation.py artifacts/conformance --output artifacts/raw-independent.json
```

## Next layers, not claimed by this feature

Record indexes, context-aware dependency indexing/remapping, surgical high-level
edits and integration of unknown data into `DxfDocument` remain separate work.
Keeping ACIS/ASM/ACDSDATA/proxy bytes as tags is not solid modeling, dynamic-block
evaluation or compatibility certification. This does not change the semantic
reader's existing unsupported-record behavior.

## Primary references

- Autodesk DXF format and contextual group-code meaning: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-235B22E0-A567-4CF6-92D3-38A2306D73F3.htm
- Autodesk value types and versioned string encoding: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-2553CF98-44F6-4828-82DD-FE3BC7448113.htm
- Autodesk binary framing: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- Independent ezdxf primitive decoders: https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/lldxf/tagger.py
