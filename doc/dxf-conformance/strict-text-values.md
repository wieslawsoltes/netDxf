# Strict text DXF numeric and handle decoding

Continuation baseline: `12cede7f741832465f66223b50c0566283c710e3`, after merged PR #14. Audit date: 13 September 2026.

## Defect and behavior

The text codec previously converted malformed integers, floating-point values, booleans and handles to zero, false or an empty string after a Debug assertion. This could return a successfully loaded but altered drawing in Release. Handle groups 5 and 1005 also bypassed handle validation entirely.

The codec now reports invalid values as `FormatException` with the group code and physical value-line number. It validates signed 16/32/64-bit ranges, invariant floating-point syntax, boolean values 0/1, and unsigned hexadecimal handles with 1–16 ASCII digits. Handle values are normalized to uppercase without redundant leading zeros; `0` remains a valid null handle. Sign/prefix/embedded whitespace/Unicode-lookalike/overlong handle strings are rejected. High-bit 64-bit handles are not mistaken for negative numbers.

The numeric input contract requires finite doubles: NaN, infinity and overflow-to-infinity are rejected rather than admitted into geometry. Ordinary string whitespace is preserved. Padded numbers, explicit signs, exponent notation, subnormal doubles and full signed-integer endpoints remain supported. Numeric terminal NULs accepted by some framework parsers are explicitly rejected. Diagnostics do not echo potentially large untrusted values.

## Version and transport scope

| Scope | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 |
|---|---|---|---|---|---|---|
| Text numeric/boolean/handle validation | Tested | Tested | Tested | Tested | Tested | Tested |
| Binary codec changes | None | None | None | None | None | None |

All numeric group ranges already recognized by the codec are exercised. Recognition of reserved/unassigned codes is not expanded. Binary validation, writer preflight, lenient recovery and database handle-allocation exhaustion are separate work. This is not full-version certification.

The existing public loader conventions are retained: Debug exposes parsing exceptions; Release returns null on failure. Neither path closes caller-owned streams. There is no new recovery mode that silently fabricates replacement values.

## Measured regression evidence

826 new registered cases, including boundary/malformed values for each applicable numeric/handle code, four current cultures, ordinary-string control cases, record alignment, diagnostic positions and independently authored documents in all six formats.

Local Roslyn compilation of the actual signed production library and executable harness:

- Unchanged production codec plus new tests: **1,093 passed / 424 failed** in Release.
- Corrected production codec: **1,517 passed / 0 failed**, Debug and Release.

Repository CI additionally uses the normal SDK projects on Linux and Windows, Debug and Release, with netstandard2.0 builds and Roslyn audit checks. The temporary branch-only preparation workflow must be absent from the final merged diff.

## Primary reference

Autodesk group-code types and handle descriptions:
https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm

The source-pinned 113-row matrix predates this correction; its strict-value row is superseded for the text codec by this measured entry, not for binary input or writers.
