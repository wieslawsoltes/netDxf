# Reject unrepresentable binary chunk lengths before writing

Baseline: `36a7bb7b8734f75d910c61b16f9a3ea333213e2d`, after merged PR #27.

## Defect and correction

The binary writer cast an arbitrary array length to byte, then wrote the entire array. A 256-byte value therefore advertised zero bytes and left 256 payload bytes to be misread as subsequent records. Null/wrong-type values also failed after the group code had already been written.

Validate the payload before changing the current tag or writing its group code. The direct WriteBytes helper validates before writing its prefix. Null data, non-byte-array values and lengths above 255 raise deterministic argument exceptions. A rejected call leaves stream bytes/position and the writer's current code/value unchanged; the caller may then write a valid tag. No automatic splitting changes the meaning of a repeated-code record.

This is a **one-byte framing limit**, not an expansion of the standard's semantic chunk limits. Autodesk specifies 127-byte chunks for groups 310–319 and 1004. Existing low-level support for 128–255-byte producer data is left unchanged; the XDataRecord constructor and thumbnail serializer continue applying their smaller semantic limits. General writer preflight, non-finite values, partial IO failures and transactional file replacement remain separate work.

## Verified coverage

95 new registered cases. All eleven binary-data codes exercise every length 0–255, exact wire-prefix/content checks and the following EOF boundary. Rejected 256/257/511/512/65535-byte arrays, nulls and wrong types are tested against nonempty streams, with state preservation and successful subsequent writes. The direct helper and valid 0/1/126/127-byte XData in all six admitted DXF families are covered.

Old production writer: **4,943 passed / 78 failed**. Corrected signed assembly: **5,021 passed / 0 failed**, local Debug and Release. Final-head Linux/Windows SDK, netstandard2.0 and source-audit CI must pass before merge. The shared modern binary codec applies to AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032; no historical binary dialect is added.

The tests inspect the emitted length byte directly, rather than relying only on a matching reader. Public DxfDocument Save behavior and stream ownership remain unchanged; other document data may have been emitted before a later invalid tag is encountered.

## Primary references

- Autodesk binary framing and single-byte unsigned chunk length: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- Autodesk groups 310–319 and 1004 semantic limits: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
