// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { Copy, List } from '../../runtime/GeometryRuntime.js';
import { ReadOnlyValueMap } from '../../runtime/ReadOnlyValueMap.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
import { InstallOrthographicBase } from './UcsOrthographicBase.js';
import { InstallUcsRelationships } from './UcsRelationships.js';
const kind = value => RequireInteger(value, 1, 6, 'type');
export class UCS extends TableObject {
  #origin = Vector3.Zero; #x = Vector3.UnitX; #y = Vector3.UnitY; #z = Vector3.UnitZ; #elevation = 0;
  #origins = new ReadOnlyValueMap(() => Vector3.Zero);
  constructor(name, ...args) {
    const axes = args.length === 3 || args.length === 4;
    super(name, DxfObjectCode.Ucs, axes ? (args[3] ?? true) : (args[0] ?? true));
    if (![0,1,3,4].includes(args.length)) throw new ArgumentException('No matching UCS constructor.');
    if (axes) { this.SetAxis(args[1], args[2]); this.#origin = Copy(args[0]); }
    else if (name == null || name === '') throw new ArgumentNullException('name');
  }
  get Origin() { return Copy(this.#origin); } set Origin(value) { this.#origin = Copy(value); }
  get XAxis() { return Copy(this.#x); } get YAxis() { return Copy(this.#y); } get ZAxis() { return Copy(this.#z); }
  get Elevation() { return this.#elevation; }
  set Elevation(value) { if (!Number.isFinite(value)) throw new ArgumentOutOfRangeException('value', value); this.#elevation = value; }
  get OrthographicOrigins() { return this.#origins; }
  SetOrthographicOrigin(type, origin) {
    kind(type);
    if (![origin.X,origin.Y,origin.Z].every(Number.isFinite)) throw new ArgumentOutOfRangeException('origin', origin);
    this.#origins.$set(type, origin);
  }
  TryGetOrthographicOrigin(type, origin) { kind(type); return this.#origins.TryGetValue(type, origin); }
  RemoveOrthographicOrigin(type) { kind(type); return this.#origins.$remove(type); }
  SetAxis(xDirection, yDirection) {
    if (!Vector3.ArePerpendicular(xDirection, yDirection)) throw new ArgumentException('X-axis direction and Y-axis direction must be perpendicular.');
    this.#x = Copy(xDirection); this.#x.Normalize(); this.#y = Copy(yDirection); this.#y.Normalize();
    this.#z = Vector3.CrossProduct(this.#x, this.#y);
  }
  static FromXAxisAndPointOnXYplane(name, origin, xDirection, pointOnPlaneXY) {
    const ucs = new UCS(name); ucs.#origin = Copy(origin); ucs.#x = Copy(xDirection); ucs.#x.Normalize();
    ucs.#z = Vector3.CrossProduct(xDirection, pointOnPlaneXY); ucs.#z.Normalize(); ucs.#y = Vector3.CrossProduct(ucs.#z, ucs.#x); return ucs;
  }
  static FromNormal(name, origin, normal, rotation) {
    let matrix = MathHelper.ArbitraryAxis(normal);
    if (arguments.length === 4) matrix = Matrix3.Multiply(matrix, Matrix3.RotationZ(rotation));
    const ucs = new UCS(name); ucs.#origin = Copy(origin);
    ucs.#x = new Vector3(matrix.M11,matrix.M21,matrix.M31); ucs.#y = new Vector3(matrix.M12,matrix.M22,matrix.M32); ucs.#z = new Vector3(matrix.M13,matrix.M23,matrix.M33); return ucs;
  }
  GetTransformation() { return new Matrix3(this.#x.X,this.#y.X,this.#z.X,this.#x.Y,this.#y.Y,this.#z.Y,this.#x.Z,this.#y.Z,this.#z.Z); }
  Transform(points, from, to) {
    if (points == null) throw new ArgumentNullException('points');
    let matrix = this.GetTransformation(); const origin = Copy(this.#origin);
    const inverse = from === CoordinateSystem.World && to === CoordinateSystem.Object;
    const forward = from === CoordinateSystem.Object && to === CoordinateSystem.World;
    if (inverse) matrix = matrix.Transpose();
    const transform = p => inverse ? Matrix3.Multiply(matrix, Vector3.Subtract(p, origin)) : forward ? Vector3.Add(Matrix3.Multiply(matrix,p),origin) : Copy(p);
    if (points instanceof Vector3) return transform(points);
    const result = new List(); for (const point of points) result.Add(transform(point)); return result;
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new UCS(newName); copy.Origin = this.#origin; copy.Elevation = this.#elevation; copy.Flags = this.Flags;
    copy.#x = Copy(this.#x); copy.#y = Copy(this.#y); copy.#z = Copy(this.#z); this.CopyOrthographicBaseTo(copy);
    for (const pair of this.#origins) copy.#origins.$set(pair.Key,pair.Value);
    for (const data of this.XData.Values) copy.XData.Add(data.Clone()); return copy;
  }
}
InstallOrthographicBase(UCS); InstallUcsRelationships(UCS);
