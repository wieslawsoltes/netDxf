// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Hatch } from '../Entities/Hatch.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
export function ValidateHatchSourceRelations(document) {
  for (const block of document.Blocks) for (const entity of block.Entities)
    if (entity instanceof Hatch) for (const path of entity.BoundaryPaths) for (const source of path.Entities)
      if (!entity.Associative || source === null || source === entity || source.Owner !== block
        || document.GetObjectByHandle(source.Handle) !== source)
        throw new InvalidOperationException('HATCH source boundaries must be registered entities in the same block before output.');
}
