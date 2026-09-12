# Exact-length binary chunk decoding

The binary decoder now verifies that a chunk contains every byte advertised by its one-byte length prefix. A short read raises `EndOfStreamException` including the group code and expected/received counts, rather than returning a silently truncated byte array.

Applies to every existing binary chunk dispatch: groups 310–319 and 1004. Valid payloads and following-record alignment are unchanged. Caller-owned streams are not closed by the codec.

The regression suite covers all eleven dispatch codes at payload lengths 0, 1, 2, 127, 128, 254 and 255; every shorter prefix of an advertised 255-byte payload; missing length bytes; fragmented one-byte reads; and alignment of the next record. These are low-level framing tests, not a relaxation of entity-specific or XData payload-size rules.

There are 110 additional named test cases (including 2,805 truncation assertions). Together with the previous tests the runner executes 170 cases per OS/configuration. All six supported document versions remain covered in text and binary.

Primary reference: [Autodesk Binary DXF Files](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm).
