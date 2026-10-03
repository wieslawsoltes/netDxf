// Synchronous string adapters for the original reader/writer private methods.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { NullReferenceException } from './Errors.js';
export function DecodeDxfText(text) {
  if (text == null || text === '') return text;
  let result = '';
  for (let i = 0; i < text.length; i++) {
    let value = text[i];
    if (value === '\\' && i + 6 < text.length && (text[i + 1] === 'U' || text[i + 1] === 'u') && text[i + 2] === '+') {
      const raw = text.slice(i + 3, i + 7);
      if (/^[\x09-\x0d\x20]*[0-9a-f]+[\x09-\x0d\x20]*$/i.test(raw)) { value = String.fromCharCode(parseInt(raw.trim(), 16)); i += 6; }
    }
    result += value;
  }
  return result;
}
export function EncodeDxfText(text, version) {
  if (version >= 15) return text;
  if (text == null || text === '') return '';
  let result = '';
  for (let i = 0; i < text.length; i++) {
    const value = text.charCodeAt(i);
    result += value > 127 ? '\\U+' + value.toString(16).toUpperCase().padStart(4, '0') : text[i];
  }
  return result;
}
export function EncodeDxfDatabaseText(text, version) {
  if (text == null) throw new NullReferenceException();
  return EncodeDxfText(text.replaceAll('\\', '\\U+005C'), version);
}
