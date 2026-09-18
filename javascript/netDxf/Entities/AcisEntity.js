// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { AcisSatChunk } from './AcisSatChunk.js';
import { ReadOnlyArrayView } from '../../runtime/StoredRecord.js';
import { RequireInertIdentity, CopyInertEntity } from '../../runtime/InertEntity.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NotSupportedException } from '../../runtime/Errors.js';
function snapshot(values) {
  const view = ReadOnlyArrayView(values);
  // .NET's read-only IList adapter is explicit; ordinary iteration remains allocation-free.
  return Object.freeze({ ...view, set_Item() { throw new NotSupportedException('Read-only SAT snapshot.'); } });
}
// DXF SAT substitution as independently documented by ezdxf's MIT-licensed tools/crypt.py
// (Copyright (c) 2014-2018 Manfred Moitzi). This is an envelope codec, not an ACIS parser.
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify,
// merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to the following
// conditions: The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS",
// WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
// OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
function encode(text) {
  const result = [];
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c === 32) result.push(' ');
    else if (c === 95) result.push('@');
    else if (c === 64) result.push('_');
    else if (c >= 65 && c <= 94) { result.push(String.fromCharCode(159-c)); if (c === 65) result.push(' '); }
    else result.push(String.fromCharCode(c ^ 0x5f));
  }
  return result.join('');
}
function decode(text) {
  const result = [];
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c === 32) result.push(' ');
    else if (c === 64) result.push('_');
    else if (c === 95) result.push('@');
    else if (c >= 65 && c <= 94) {
      result.push(String.fromCharCode(159-c));
      if (c === 94 && (++i >= text.length || text[i] !== ' ')) throw new ArgumentException('Malformed SAT encoded A escape.');
    } else result.push(String.fromCharCode(c ^ 0x5f));
  }
  return result.join('');
}
export class AcisEntity extends EntityObject {
  static get MaximumSatChunks() { return 65536; }
  static get MaximumSatCharacters() { return 16 * 1024 * 1024; }
  static get MaximumSatLineCharacters() { return 1024 * 1024; }
  #chunks = snapshot([]); #lines = snapshot([]);
  constructor(type, code) {
    super(type, code);
    if (new.target === AcisEntity) throw new NotSupportedException('AcisEntity is abstract.');
  }
  get ModelerFormatVersion() { return 1; }
  get EncodedSatChunks() { return this.#chunks; }
  get SatLines() { return this.#lines; }
  static ValidateAscii(text, parameter) {
    for (let i = 0; i < text.length; i++) if (text.charCodeAt(i) < 32 || text.charCodeAt(i) > 126)
      throw new ArgumentException('SAT text must contain printable ASCII without newline delimiters.', parameter);
  }
  SetSatLines(value) {
    if (value == null) throw new ArgumentNullException('value');
    const result = []; let total = 0;
    for (const line of value) {
      if (line == null) throw new ArgumentException('SAT lines cannot be null.', 'value');
      if (line.length > AcisEntity.MaximumSatLineCharacters) throw new ArgumentOutOfRangeException('value');
      AcisEntity.ValidateAscii(line, 'value');
      const encoded = encode(line);
      if (encoded.length > AcisEntity.MaximumSatLineCharacters || encoded.length > AcisEntity.MaximumSatCharacters - total)
        throw new ArgumentOutOfRangeException('value');
      total += encoded.length;
      for (let offset = 0; offset < encoded.length || offset === 0; offset += 255) {
        if (result.length === AcisEntity.MaximumSatChunks) throw new ArgumentOutOfRangeException('value');
        result.push(new AcisSatChunk(offset === 0 ? 1 : 3, encoded.slice(offset, offset+255)));
      }
    }
    this.SetEncodedSatChunks(result);
  }
  SetEncodedSatChunks(value) {
    if (value == null) throw new ArgumentNullException('value');
    const result = [], decoded = []; let line = null, lineLength = 0, total = 0;
    for (const part of value) {
      if (part == null) throw new ArgumentException('SAT chunks cannot be null.', 'value');
      if (result.length === AcisEntity.MaximumSatChunks || part.Text.length > AcisEntity.MaximumSatCharacters - total)
        throw new ArgumentOutOfRangeException('value');
      total += part.Text.length;
      if (part.GroupCode === 1) { if (line !== null) decoded.push(decode(line.join(''))); line = []; lineLength = 0; }
      if (line === null) throw new ArgumentException('A SAT continuation requires a preceding group 1.', 'value');
      if (part.Text.length > AcisEntity.MaximumSatLineCharacters - lineLength) throw new ArgumentOutOfRangeException('value');
      line.push(part.Text); lineLength += part.Text.length; result.push(part);
    }
    if (line !== null) decoded.push(decode(line.join('')));
    this.#chunks = snapshot(result); this.#lines = snapshot(decoded);
  }
  TransformBy(transformation, translation) { RequireInertIdentity(this, transformation, translation, true); }
  CopyTo(copy) { copy.SetEncodedSatChunks(this.#chunks); CopyInertEntity(this, copy); }
}
