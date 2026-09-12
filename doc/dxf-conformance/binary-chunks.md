# Exact-length binary DXF chunks

The binary transport reader now rejects incomplete payloads for group codes 310–319 and 1004. Previously `BinaryReader.ReadBytes(length)` could return fewer bytes than requested, which the DXF reader accepted as a complete payload. The fix raises `EndOfStreamException` with the group code and declared/actual lengths. Missing length bytes already raise `EndOfStreamException`.

The change applies to the shared modern binary codec used by all six admitted DXF formats. It does not introduce historical document-dialect support, impose new record-specific length policies, or change the public document loader's existing Debug/Release exception conventions.

## Evidence

On top of production baseline `e79da92bbdbb1b4db84797954dff74b1479fc000`, the unchanged codec with the new tests produced **304 passed / 11 failed**. Every new truncated-payload test failed as expected, one for each affected group code. The corrected signed library produced **315 passed / 0 failed** in both local Debug and Release runs.

The 110 added registered cases cover all eleven group codes, zero-length data, lengths 1/2/127/128/254/255, every truncated prefix of a declared 255-byte payload, missing length bytes, fragmented stream reads, exact payload bytes, and following-record alignment. These are low-level transport tests: accepting the one-byte length range is not a claim that every record family permits a 255-byte chunk. Existing six-version text/binary document and XData round trips remain enabled.

The earlier unmerged binary-chunk branch is incorporated without overwriting the subsequently merged text-hex, UCS, thumbnail or audit work. No temporary source-preparation workflow is part of the final diff. Final-head Linux/Windows Debug/Release CI and netstandard2.0 compilation must pass before merge.

## Primary specification

Autodesk, Binary DXF framing and byte-counted payloads:
https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm

This closes the short-binary-chunk finding in the source-pinned version/feature audit. It is not a full-standard or independent AutoCAD-interoperability certificate.
