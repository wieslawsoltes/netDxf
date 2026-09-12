# Binary DXF value validation

Baseline: `518ca4fa4d296b637fff7585f5679793b0163990`, after PR #15. Scope: all six admitted formats, AC1015 through AC1032.

## Correction

Bring the binary reader into agreement with the text reader's validation contract. Reject boolean bytes other than 0 and 1, malformed unsigned hexadecimal handles (including groups 5 and 1005), and non-finite doubles. Previously invalid flags became true and malformed handles could become empty strings; groups 5/1005 bypassed validation. Errors are `InvalidDataException` with the group code and absolute starting byte address of the value. For a standalone nonseekable codec input, the diagnostic reports an unknown address rather than requesting an unsupported stream position.

Finite doubles retain their exact IEEE bits, including negative zero and subnormals. Signed integer endpoints retain their widths and values. UTF-8 ordinary strings, zero-terminated framing, record boundaries, truncated-scalar EOF errors and caller stream ownership are preserved. Handle normalization and accepted surrounding whitespace match the text codec. As in PR #15, requiring finite numbers is the library's input contract, not a claim that the IEEE encoding cannot represent other bit patterns.

## Evidence

667 added registered regression cases. Independently written binary fields exercise every recognized double, integer and handle range; all 254 invalid boolean byte values for each boolean code; signaling/quiet/signed NaNs and both infinities; unsigned high-bit handles; fragmented reads; nonzero file offsets; truncation; nonseekable diagnostics; string controls; and malformed document fixtures for all six versions.

- Unchanged binary decoder plus new tests: **1,914 passed / 270 failed**, Release.
- Corrected signed production library: **2,184 passed / 0 failed**, Debug and Release.
- Normal SDK Linux/Windows Debug/Release, netstandard2.0 and source-audit checks are required before merge.

The tests use the actual signed library. Temporary source preparation is removed from the final diff. This does not add old binary dialects, writer preflight, recovery, resource quotas, or full unsigned database-handle allocation. It does not certify full AutoCAD interoperability.

## Primary references

- Autodesk numerical group-code definitions and 16-digit handles: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
- Autodesk binary framing and byte-address diagnostics: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
