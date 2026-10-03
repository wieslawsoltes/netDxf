// Observation only. All geometry, mutation and generation run in production modules.
import { Dimension, DimensionBlock } from '../index.js';
import { dimensionProperties, dimensionBuildOverloads } from './concrete-dimension-schema.mjs';
const base = ['TextPositionManuallySet', 'TextReferencePoint', 'Style', 'StyleOverrides', 'DimensionType',
  'AttachmentPoint', 'LineSpacingStyle', 'LineSpacingFactor', 'Block', 'TextRotation', 'UserText', 'Elevation', 'Measurement'];
export function concreteDimensionWire(value, common, wire) {
  if (!(value instanceof Dimension)) return undefined;
  const fields = {};
  for (const name of new Set(base.concat(dimensionProperties[value.constructor.name] ?? []))) fields[name] = wire(value[name]);
  fields.DefinitionPoint = wire(value.DefinitionPoint);
  return { common, fields };
}
export function concreteDimensionInvoke(type, signature, args) {
  if (type !== DimensionBlock || signature === undefined) return { handled: false };
  const method = dimensionBuildOverloads.find(item => item.signature === signature);
  if (!method) throw new Error('Unmapped exact DimensionBlock.Build signature: ' + signature);
  return { handled: true, value: DimensionBlock[method.implementation](...args) };
}
