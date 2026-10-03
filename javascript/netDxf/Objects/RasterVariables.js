// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ImageDisplayQuality } from './ImageDisplayQuality.js';
import { ImageUnits } from '../Units/ImageUnits.js';
/** Internal C# construction/ownership adapter; does not create a document. */
export class RasterVariables extends DxfObject {
  DisplayFrame = true; DisplayQuality = ImageDisplayQuality.High; Units = ImageUnits.Unitless;
  constructor(document) { super(DxfObjectCode.RasterVariables); this.Owner = document; }
}
