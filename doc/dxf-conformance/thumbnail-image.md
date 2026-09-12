# THUMBNAILIMAGE preservation

`DxfDocument.ThumbnailImage` exposes the opaque preview bytes carried by the DXF `THUMBNAILIMAGE` section. Reading no longer discards this section; saving writes its declared byte count and ordered binary chunks in both text and binary DXF.

```csharp
DxfDocument document = DxfDocument.Load("input.dxf");
byte[] preview = document.ThumbnailImage;
// The payload is DXF preview data, not necessarily a standalone image file.
document.ThumbnailImage = preview;
document.Save("preserved.dxf", true);
// Clear a stale preview after modifying drawing entities:
document.ThumbnailImage = Array.Empty<byte>();
```

## Contract

The property defaults to an empty array and defensively copies both assigned and returned arrays. Null assignments throw `ArgumentNullException`. An absent or zero-byte preview saves without a thumbnail section. The library does not decode, render, regenerate, or automatically invalidate preview pixels. Callers replacing geometry should clear or replace a stale preview explicitly.

The reader accepts the byte count before or after data, preserves chunk order, and ignores text comments. It rejects missing, negative, duplicate, or mismatched byte counts, unexpected section records, and unexpected value codes. Physical or explicit EOF before ENDSEC terminates with an end-of-stream error rather than looping. It does not preallocate from an untrusted declared count. This is not a substitute for a document-wide resource-quota policy.

The writer emits chunks of at most 127 bytes (254 hexadecimal characters in text DXF), satisfying both the general binary-chunk limit and the thumbnail-specific 256-character maximum. The reader also accepts 128-byte thumbnail input chunks. Invalid numeric-token behavior remains governed by the existing low-level text codec and is a separate audit item.

## Evidence

Source baseline: `411b9a5cd8843e7a91d6674191aeec5a350dfb76` after the merged UCS XData fix.

The new tests cover every admitted version (2000/2004/2007/2010/2013/2018), both transports, empty and nonempty previews, byte values, boundary lengths through 4,097 bytes, exact group codes and chunk sizes, malformed lengths/terminators, code ordering, comments, stream ownership and defensive copying.

With the new API/helper but the old document read/write paths, 193 tests passed and all twelve preview document round-trip cases failed. With the integration, local signed-assembly Debug and Release runs both pass all 205 tests. PR CI must also pass the Linux/Windows matrix and netstandard2.0 build before merge. Self-round-trip tests do not constitute independent AutoCAD visual validation.

## Primary references

- Autodesk 2026 THUMBNAILIMAGE group codes: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-F0369984-9699-40D5-8F9A-139491A14231.htm
- Autodesk 2018 THUMBNAILIMAGE group codes: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F0369984-9699-40D5-8F9A-139491A14231.htm
- General binary-chunk group codes: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
