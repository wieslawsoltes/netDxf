import { CodePages } from './CodePages.generated.js';
import { DecoderFallbackException, EncoderFallbackException, NotSupportedException } from './Errors.js';
const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true });
const cache = new Map();
function unpack(parts) {
  let text = '';
  for (const part of parts) {
    if (typeof part === 'string') text += part;
    else if (part[0] === 0) text += String.fromCharCode(part[1]).repeat(part[2]);
    else for (let i = 0; i < part[2]; i++) text += String.fromCharCode(part[1] + i);
  }
  if (text.length !== 256) throw new Error('Invalid generated code-page row.');
  return text;
}
function validateUnicode(text) {
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c >= 0xd800 && c <= 0xdbff) {
      const next = text.charCodeAt(++i);
      if (!(next >= 0xdc00 && next <= 0xdfff)) throw new EncoderFallbackException('Unpaired UTF-16 surrogate.');
    } else if (c >= 0xdc00 && c <= 0xdfff) throw new EncoderFallbackException('Unpaired UTF-16 surrogate.');
  }
}
export class Encoding {
  #single; #leads; #encode;
  constructor(codePage) {
    this.CodePage = codePage;
    if (codePage !== 65001) {
      const data = CodePages[codePage];
      if (!data) throw new NotSupportedException(`Code page ${codePage} has not been ported; no silent fallback is allowed.`);
      this.#single = unpack(data.single);
      this.#leads = new Map(Object.entries(data.leads).map(([k,v]) => [Number(k),unpack(v)]));
      this.#encode = new Map();
      for (let b = 0; b < 256; b++) if (this.#single[b] !== '\uffff') this.#encode.set(this.#single.charCodeAt(b),b);
      for (const [lead,row] of this.#leads) for (let b = 0; b < 256; b++)
        if (row[b] !== '\uffff') this.#encode.set(row.charCodeAt(b),(lead << 8) | b);
      for (const [c,value] of Object.entries(data.overrides)) this.#encode.set(Number(c),value);
    }
    Object.freeze(this);
  }
  static GetEncoding(codePage) {
    if (!cache.has(codePage)) cache.set(codePage,new Encoding(codePage));
    return cache.get(codePage);
  }
  static get UTF8() { return Encoding.GetEncoding(65001); }
  static get Latin1() { return Encoding.GetEncoding(28591); }
  GetString(bytes) {
    if (this.CodePage === 65001) {
      try { return decoder.decode(bytes); }
      catch (error) { throw new DecoderFallbackException('Invalid UTF-8 input.', null, { cause: error }); }
    }
    const chunks = []; let text = '';
    for (let i = 0; i < bytes.length; i++) {
      const first = bytes[i]; let c = this.#single[first];
      if (c === '\uffff') {
        const row = this.#leads.get(first);
        if (row && i + 1 < bytes.length) c = row[bytes[++i]];
      }
      if (c === '\uffff') throw new DecoderFallbackException(`Invalid code-page ${this.CodePage} sequence.`);
      text += c;
      if (text.length >= 8192) { chunks.push(text); text = ''; }
    }
    chunks.push(text); return chunks.join('');
  }
  GetBytes(text) {
    if (this.CodePage === 65001) { validateUnicode(text); return encoder.encode(text); }
    const bytes = new Uint8Array(text.length * (this.#leads.size === 0 ? 1 : 2)); let at = 0;
    for (let i = 0; i < text.length; i++) {
      const encoded = this.#encode.get(text.charCodeAt(i));
      if (encoded === undefined) throw new EncoderFallbackException(`Character is not encodable in code page ${this.CodePage}.`);
      if (encoded === -1) continue;
      if (encoded > 255) bytes[at++] = encoded >>> 8;
      bytes[at++] = encoded & 255;
    }
    return bytes.slice(0,at);
  }
  GetByteCount(text) { return this.GetBytes(text).length; }
}
