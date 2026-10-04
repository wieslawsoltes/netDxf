// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { WriteXDataRecords } from '../../runtime/DxfXDataIO.js';
import { InvalidDataException, NullReferenceException } from '../../runtime/Errors.js';
export function CheckStyleUnicode(value) {
  if (value === null) throw new NullReferenceException();
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code < 0xd800 || code > 0xdfff) continue;
    if (code <= 0xdbff && index + 1 < value.length) {
      const low = value.charCodeAt(index + 1);
      if (low >= 0xdc00 && low <= 0xdfff) { index++; continue; }
    }
    throw new InvalidDataException('STYLE strings cannot contain unpaired UTF-16 surrogates.');
  }
}
export function CheckStyleXDataUnicode(data) {
  for (const item of data.Values) {
    CheckStyleUnicode(item.ApplicationRegistry.Name);
    for (const record of item.XDataRecord) if (typeof record.Value === 'string') CheckStyleUnicode(record.Value);
  }
}
export function ValidateTextStyleStrings(document) {
  for (const style of document.TextStyles.Items) {
    CheckStyleUnicode(style.Name); CheckStyleUnicode(style.FontFile); CheckStyleUnicode(style.BigFont);
    CheckStyleXDataUnicode(style.XData);
  }
  for (const style of document.ShapeStyles.Items) {
    CheckStyleUnicode(style.Name); CheckStyleUnicode(style.File); CheckStyleXDataUnicode(style.XData);
  }
}
export function EncodeStyleString(value, version) {
  return EncodeDxfDatabaseText(value, version).replaceAll('\0', '\\U+0000')
    .replaceAll('\r', '\\U+000D').replaceAll('\n', '\\U+000A');
}
export function WriteStyleXData(chunk, version, data) {
  for (const appId of data.AppIds) WriteXDataRecords(chunk, version, appId, data.get_Item(appId).XDataRecord, true);
}
