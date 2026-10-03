// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Exception } from '../../runtime/Errors.js';
export class DxfVersionNotSupportedException extends Exception {
  constructor(message, version) { if (version === undefined) { version = message; message = ""; } super(message); this.Version = version; }
}
