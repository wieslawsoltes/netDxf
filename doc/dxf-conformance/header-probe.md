# Non-consuming, structurally scoped DXF header probing

`DxfDocument.CheckDxfFileVersion` now restores a readable, seekable stream's entry position on success, malformed input and read failures, provided the stream still permits repositioning. Its internal temporary readers are disposed without closing the caller's stream. Nonseekable input is rejected without consuming bytes; the public method keeps its existing Unknown-on-failure contract.

The binary classifier checks all 22 sentinel bytes rather than just the text prefix. Short or corrupted signatures are not reported as valid binary DXF. This is independent of the already fixed binary-reader constructor.

Header lookup now follows group-code structure: 0/SECTION, 2/HEADER, 9/variable-name, and the correct string value code for `$ACADVER` or `$DWGCODEPAGE`. Comments containing HEADER/EOF/ENDSEC or variable names are not structural records. Other sections and numeric/multivalue header data are skipped without unsafe string casts. Lookup stops at the end of HEADER or when the requested variable is found.

## Regression evidence

80 new registered cases include every shortened sentinel length and corrupted byte position; all six version/transport pairs; repeated probing at offsets 0, 7 and 31; normal and one-byte fragmented reads; injected IO failures; malformed value types; headerless short inputs; misleading comment/payload strings; multitype sections; and probing immediately followed by semantic document loading.

Before the correction: **642 passed / 49 failed**. The corrected signed-library suite reports **691 passed / 0 failed** in both Debug and Release. Tested source hashes are verified before publication; cross-platform final-head CI, netstandard2.0 builds and audit tooling remain the merge gate.

## Deliberate limits

This is declaration probing, not full document validation. A readable `$ACADVER` can be returned before malformed later content is encountered. Headerless input returns Unknown; no historical version inference or pre-modern binary dialect is added. The full semantic reader's separate handling of interspersed comments, codepage fallback and other malformed numeric values is not claimed fixed by the probe change.

## Primary references

- Binary sentinel and framing: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- Section/code-value structure: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-D939EA11-0CEC-4636-91A8-756640A031D3.htm
- Header variable group codes: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm
