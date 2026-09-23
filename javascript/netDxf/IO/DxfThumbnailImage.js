// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { ArgumentNullException, InvalidDataException, EndOfStreamException, NullReferenceException } from '../../runtime/Errors.js';
const invalid = (chunk, reason) => new InvalidDataException(`Invalid THUMBNAILIMAGE at position ${chunk.CurrentPosition}, group code ${chunk.Code}: ${reason}`);
/** Opaque preview-section codec; does not interpret BMP/PNG or render a preview. */
export class DxfThumbnailImage {
  static Read(chunk) {
    if (chunk == null) throw new ArgumentNullException('chunk');
    let declaredLength = null;
    const data = new MemoryStream();
    try {
      chunk.Next();
      while (chunk.Code !== 0) {
        switch (chunk.Code) {
          case 90:
            if (declaredLength !== null) throw invalid(chunk, 'Duplicate byte count (group code 90).');
            declaredLength = chunk.ReadInt();
            if (declaredLength < 0) throw invalid(chunk, 'The byte count must not be negative.');
            break;
          case 310: {
            const bytes = chunk.ReadBytes();
            if (bytes == null) throw new NullReferenceException();
            // Grow from received bytes, never from an untrusted declared length.
            if (data.Length + bytes.length > 2147483647) throw invalid(chunk, 'The preview exceeds the supported byte count.');
            data.Write(bytes, 0, bytes.length);
            break;
          }
          case 999: break;
          default: throw invalid(chunk, 'Unexpected group code in preview data.');
        }
        if (declaredLength !== null && data.Length > declaredLength) throw invalid(chunk, 'The preview contains more bytes than its declared count.');
        chunk.Next();
      }
      if (chunk.ReadString() === 'EOF') throw new EndOfStreamException('Unexpected end of DXF while reading THUMBNAILIMAGE; ENDSEC is required.');
      if (chunk.ReadString() !== 'ENDSEC') throw invalid(chunk, 'Expected ENDSEC after preview data.');
      if (declaredLength === null || data.Length !== declaredLength) throw invalid(chunk, 'The preview byte count is missing or does not match its data.');
      return data.ToArray(); // ENDSEC stays current for the section dispatcher.
    } finally { data.Dispose(); }
  }
  static Write(chunk, data) {
    if (chunk == null) throw new ArgumentNullException('chunk');
    if (data == null) throw new ArgumentNullException('data');
    if (data.length === 0) return;
    chunk.Write(0, 'SECTION');
    chunk.Write(2, 'THUMBNAILIMAGE');
    chunk.Write(90, data.length);
    for (let offset = 0; offset < data.length;) {
      const count = Math.min(127, data.length - offset);
      // Each packet owns its bytes, including across synchronous writer callbacks.
      const part = data.slice(offset, offset + count);
      chunk.Write(310, part); offset += count;
    }
    chunk.Write(0, 'ENDSEC');
  }
}
