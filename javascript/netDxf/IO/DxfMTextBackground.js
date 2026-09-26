// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Private partial-reader/writer methods have explicit chunk/version/document arguments.
import { MTextBackgroundFill } from '../Entities/MTextBackgroundFill.js';
import { MText } from '../Entities/MText.js';
import { DecodeDxfText, EncodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { ArgumentException, InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
/** background is an explicit ref cell: { value: MTextBackgroundFill | null }. */
export function TryReadMTextBackground(chunk, background, decode = DecodeDxfText) {
  const code = chunk.Code;
  if (![90, 45, 63, 421, 431, 441].includes(code)) return false;
  if (background.value === null) {
    const value = new MTextBackgroundFill(); value.Flags = 0; value.ScaleFactor = null; value.ColorIndex = null;
    background.value = value;
  }
  try {
    switch (code) {
      case 90: background.value.Flags = chunk.ReadInt(); break;
      case 45: background.value.ScaleFactor = chunk.ReadDouble(); break;
      case 63: background.value.ColorIndex = chunk.ReadShort(); break;
      case 421: background.value.TrueColor = chunk.ReadInt(); break;
      case 431: background.value.ColorName = decode(chunk.ReadString()); break;
      case 441: background.value.Transparency = chunk.ReadInt(); break;
    }
  } catch (error) {
    if (!(error instanceof ArgumentException)) throw error;
    const outer = new InvalidDataException(`Invalid MTEXT background value for group code ${code} at position ${chunk.CurrentPosition}.`, {cause: error});
    outer.InnerException = error; throw outer;
  }
  return true;
}
export function ValidateMTextBackgroundVersion(background, version) {
  if (background === null) return;
  if (version < 15) throw new NotSupportedException('MTEXT background data requires the AutoCAD 2007 or later DXF writer profile. Remove BackgroundFill explicitly before down-saving.');
  if ((background.Flags & 16) !== 0 && version < 18) throw new NotSupportedException('MTEXT text frames require the AutoCAD 2018 DXF writer profile. Remove the frame flag explicitly before down-saving.');
}
export function ValidateMTextBackgroundVersions(document) {
  const version = document.DrawingVariables.AcadVer;
  if (version >= 18) return;
  for (const block of document.Blocks) for (const entity of block.Entities)
    if (entity instanceof MText) ValidateMTextBackgroundVersion(entity.BackgroundFill, version);
}
export function WriteMTextBackground(chunk, version, background, encode = value => EncodeDxfText(value, version)) {
  if (background === null) return;
  ValidateMTextBackgroundVersion(background, version);
  chunk.Write(90, background.Flags);
  const active = (background.Flags & 3) !== 0;
  if (active || background.ScaleFactor !== null) chunk.Write(45, background.ScaleFactor ?? 1.5);
  if (active || background.ColorIndex !== null) chunk.Write(63, background.ColorIndex ?? 7);
  if (background.TrueColor !== null) chunk.Write(421, background.TrueColor);
  if (background.ColorName !== null) chunk.Write(431, encode(background.ColorName));
  if (background.Transparency !== null) chunk.Write(441, background.Transparency);
}
