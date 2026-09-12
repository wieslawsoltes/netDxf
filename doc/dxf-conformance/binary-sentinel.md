# Binary DXF sentinel validation

Applies to the existing AutoCAD 2000, 2004, 2007, 2010, 2013 and 2018 binary readers. This does not enable pre-2000 document support.

The reader now validates all 22 bytes of `AutoCAD Binary DXF\r\n\x1A\0`, not just the 18 printable bytes. Every incomplete prefix raises `EndOfStreamException`; every mismatched byte in a full-length signature raises `InvalidDataException`. Invalid null constructor arguments raise `ArgumentNullException`. Caller-owned streams remain open.

Regression coverage: every truncated length (0–21), a corruption at every byte offset (0–21), one-byte fragmented reads, exact sentinel consumption, null reader, and the existing valid sentinel/string case. All six document formats continue to be exercised in text and binary on Linux and Windows, Debug and Release.

The behavior change is intentional: corrupt binary signatures are no longer accepted, and truncation no longer produces an incidental indexing exception. It does not alter valid binary DXF bytes or public library signatures.

Primary reference: [Autodesk Binary DXF Files](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm).
