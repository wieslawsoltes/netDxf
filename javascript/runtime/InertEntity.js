import { NotSupportedException } from './Errors.js';
/** Exact equality, not Matrix.IsIdentity's tolerance. ACIS also checks the homogeneous row. */
export function RequireInertIdentity(entity, transformation, translation, homogeneous = false) {
  if (homogeneous && translation === undefined &&
      (transformation.M41 !== 0 || transformation.M42 !== 0 || transformation.M43 !== 0 || transformation.M44 !== 1))
    throw new NotSupportedException('Changing this entity requires rewriting its private payload.');
  const [matrix, offset] = entity.$transformArguments(transformation, translation);
  for (let row = 1; row <= 3; row++) for (let column = 1; column <= 3; column++)
    if (matrix[`M${row}${column}`] !== (row === column ? 1 : 0))
      throw new NotSupportedException('Changing this entity requires rewriting its private payload.');
  if (offset.X !== 0 || offset.Y !== 0 || offset.Z !== 0)
    throw new NotSupportedException('Changing this entity requires rewriting its private payload.');
}
/** Source-ordered common metadata copy; never copies document identity or activates a payload. */
export function CopyInertEntity(source, target) {
  target.Layer = source.Layer.Clone(); target.Linetype = source.Linetype.Clone();
  target.Color = source.Color.Clone(); target.Lineweight = source.Lineweight;
  target.Transparency = source.Transparency.Clone(); target.LinetypeScale = source.LinetypeScale;
  target.IsVisible = source.IsVisible; target.Normal = source.Normal;
  for (const data of source.XData.Values) target.XData.Add(data.Clone());
  source.CopyCommonDataTo(target); return target;
}
