// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Private partial-reader/writer methods have explicit chunk/version/document arguments.
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { DecodeDxfText, EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { Insert } from '../Entities/Insert.js';
import { DxfVersionNotSupportedException } from './DxfVersionNotSupportedException.js';
import { InvalidDataException, NullReferenceException } from '../../runtime/Errors.js';
const maximum = EntityObject.MaximumProxyGraphicsBytes;
export class EntityCommonDataReader {
  ColorName = null; ShadowMode = null; ColorNameSeen = false;
  DeclaredLength = null; Payload = null; ActualLength = 0; ProxyGraphics = null;
  Complete() {
    if (this.DeclaredLength === null) {
      if (this.Payload !== null) throw new InvalidDataException('AcDbEntity proxy graphics chunks require a byte count.');
      return;
    }
    if (this.ActualLength !== this.DeclaredLength) throw new InvalidDataException('AcDbEntity proxy graphics byte count does not match the payload.');
    if (this.Payload === null) throw new NullReferenceException();
    this.ProxyGraphics = this.Payload.ToArray();
    this.Payload.Dispose();
  }
}
/** Call only for the AcDbEntity subclass; 92/310 mean other things elsewhere. */
export function ReadEntityCommonData(chunk, version, data, decode = DecodeDxfText) {
  switch (chunk.Code) {
    case 430:
      if (version < 14 || data.ColorNameSeen) throw new InvalidDataException('AcDbEntity color name requires DXF 2004 or later and one group 430.');
      data.ColorNameSeen = true;
      data.ColorName = decode(chunk.ReadString());
      break;
    case 284: {
      const mode = chunk.ReadShort();
      if (version < 15 || data.ShadowMode !== null || mode < 0 || mode > 3) throw new InvalidDataException('AcDbEntity shadow mode requires DXF 2007 or later and one value from 0 to 3.');
      data.ShadowMode = mode;
      break;
    }
    case 92: case 160: {
      if (data.DeclaredLength !== null || chunk.Code === 160 && version < 16) throw new InvalidDataException('AcDbEntity proxy graphics require one byte count; group 160 requires DXF 2010 or later.');
      const length = chunk.Code === 160 ? chunk.ReadLong() : chunk.ReadInt();
      if (length < data.ActualLength || length > maximum) throw new InvalidDataException('AcDbEntity proxy graphics byte count exceeds the allowed 0 to 16 MiB range.');
      data.DeclaredLength = Number(length);
      data.Payload ??= new MemoryStream();
      break;
    }
    case 310: {
      const bytes = chunk.ReadBytes();
      if (bytes == null) throw new NullReferenceException();
      if (bytes.length > 128 || data.ActualLength + bytes.length > maximum || data.DeclaredLength !== null && data.ActualLength + bytes.length > data.DeclaredLength)
        throw new InvalidDataException('AcDbEntity proxy graphics chunks exceed their byte count or 128-byte packet limit.');
      data.Payload ??= new MemoryStream();
      data.Payload.Write(bytes, 0, bytes.length); data.ActualLength += bytes.length;
      break;
    }
  }
}
export function ValidateEntityCommonDataVersion(data, version) {
  if (data.ColorName !== null && version < 14) throw new DxfVersionNotSupportedException('Entity color names require DXF 2004 or later; clear ColorName before downgrade.', version);
  if (data.ShadowMode !== null && version < 15) throw new DxfVersionNotSupportedException('Entity shadow modes require DXF 2007 or later; clear ShadowMode before downgrade.', version);
}
export function ValidateEntityCommonDataVersions(document) {
  for (const block of document.Blocks) {
    for (const definition of block.AttributeDefinitions.Values) ValidateEntityCommonDataVersion(definition.CommonData, document.DrawingVariables.AcadVer);
    for (const entity of block.Entities) {
      ValidateEntityCommonDataVersion(entity.CommonData, document.DrawingVariables.AcadVer);
      if (entity instanceof Insert) for (const attribute of entity.Attributes) ValidateEntityCommonDataVersion(attribute.CommonData, document.DrawingVariables.AcadVer);
    }
  }
}
export function WriteEntityCommonData(chunk, version, entity, encode = value => EncodeDxfDatabaseText(value, version)) {
  if (entity.ColorName !== null) chunk.Write(430, encode(entity.ColorName).replaceAll('\0', '\\U+0000').replaceAll('\r', '\\U+000D').replaceAll('\n', '\\U+000A'));
  if (entity.ShadowMode !== null) chunk.Write(284, entity.ShadowMode);
  const bytes = entity.ProxyGraphics;
  if (bytes === null) return;
  if (version < 17) chunk.Write(92, bytes.length);
  else chunk.Write(160, BigInt(bytes.length));
  for (let offset = 0; offset < bytes.length;) {
    const size = Math.min(127, bytes.length - offset);
    chunk.Write(310, bytes.slice(offset, offset + size)); offset += size;
  }
}
