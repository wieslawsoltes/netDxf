// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { PolyfaceMesh } from '../Entities/PolyfaceMesh.js';
import { ArgumentException, InvalidOperationException } from '../../runtime/Errors.js';
export function ValidatePolyfaceMeshOutput(document) {
  for (const block of document.Blocks) for (const entity of block.Entities) {
    if (!(entity instanceof PolyfaceMesh)) continue;
    try { entity.ValidateFaceIndexes(); }
    catch (cause) {
      if (!(cause instanceof ArgumentException)) throw cause;
      const error = new InvalidOperationException("Invalid POLYFACE face indices in block '" + block.Name + "'.", { cause });
      error.InnerException = cause; throw error;
    }
  }
}
