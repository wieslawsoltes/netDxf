// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
/** Mirror of the internal C# reactor model; not registered-document ownership. */
export class ImageDefinitionReactor extends DxfObject {
  #imageHandle;
  constructor(imageHandle) { super(DxfObjectCode.ImageDefReactor); this.#imageHandle = imageHandle; }
  get ImageHandle() { return this.#imageHandle; }
}
