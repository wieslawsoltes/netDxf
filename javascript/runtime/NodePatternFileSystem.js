import fs from 'node:fs';
import { FullPath, FileError } from './NodeFileStream.js';
import { Encoding } from './Encoding.js';

/** StreamReader's BOM-detected Unicode input with replacement fallback. */
export function DecodePatternText(bytes) {
  if (bytes.length >= 4 && ((bytes[0] === 0xff && bytes[1] === 0xfe && bytes[2] === 0 && bytes[3] === 0) ||
      (bytes[0] === 0 && bytes[1] === 0 && bytes[2] === 0xfe && bytes[3] === 0xff))) {
    const little = bytes[0] === 0xff, view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength), pieces = [];
    for (let i = 4; i < bytes.length; i += 4) {
      const cp = i + 4 <= bytes.length ? view.getUint32(i, little) : 0xfffd;
      pieces.push(String.fromCodePoint(cp > 0x10ffff || (cp >= 0xd800 && cp <= 0xdfff) ? 0xfffd : cp));
    }
    return pieces.join('');
  }
  let encoding = 'utf-8', offset = 0;
  if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf) offset = 3;
  else if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe) { encoding = 'utf-16le'; offset = 2; }
  else if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff) { encoding = 'utf-16be'; offset = 2; }
  return new TextDecoder(encoding, { ignoreBOM: true }).decode(bytes.subarray(offset));
}
export const NodePatternFileSystem = Object.freeze({
  NewLine: process.platform === 'win32' ? '\r\n' : '\n',
  ReadAllText(file) {
    const filename = FullPath(file);
    try { return DecodePatternText(fs.readFileSync(filename)); } catch (error) { throw FileError(error, filename); }
  },
  AppendAllText(file, text) {
    const filename = FullPath(file);
    // AppendAllText uses UTF-8 without a BOM and rejects invalid UTF-16 input.
    const bytes = Encoding.UTF8.GetBytes(text);
    try { fs.appendFileSync(filename, bytes); } catch (error) { throw FileError(error, filename); }
  }
});
