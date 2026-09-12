# UCS table XData isolation regression

The TABLE header for UCS incorrectly serialized `DxfDocument.Blocks.XData` instead of `DxfDocument.UCSs.XData`. This discarded UCS metadata and copied BLOCK_RECORD metadata into the wrong table.

The fix changes only the collection used by `DxfWriter.BeginTable` for UCS. The regression uses distinct APPID names and string markers on the two table collections and verifies that each retains only its own application and payload after save/load. It covers all six supported DXF versions in text and binary.

Before the correction, the local signed-assembly conformance run reported 149 passing existing tests and 12 failing isolation tests. After the correction, both local Debug and Release runs pass all 161 tests. The PR also requires the normal Linux/Windows CI matrix and netstandard2.0 build before merge.

Source baseline: `054026b78980dd40ea3660a1d5eed136f25d83ee`. This is metadata ownership preservation, not a change to UCS geometry or version admission.

Primary references: Autodesk common symbol table group codes and ordered APPID-scoped XData: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/index.htm and https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A2A628B0-3699-4740-A215-C560E7242F63.htm
