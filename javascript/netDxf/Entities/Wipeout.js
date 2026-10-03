// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ClippingBoundary } from '../ClippingBoundary.js';
import { ClippingBoundaryType } from '../ClippingBoundaryType.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
export class Wipeout extends EntityObject {
  #boundary; #elevation = new DataView(new ArrayBuffer(8));
  constructor(...args) {
    super(EntityType.Wipeout, DxfObjectCode.Wipeout);
    if (args.length === 1 && (args[0] === null || args[0] instanceof ClippingBoundary)) {
      if (args[0] === null) throw new ArgumentNullException('clippingBoundary'); this.#boundary = args[0];
    } else this.#boundary = new ClippingBoundary(...args);
  }
  /** Select the ambiguous null enumerable overload without changing its original exception. */
  static CreateOverload(signature, ...args) {
    if (signature === 'System.Collections.Generic.IEnumerable<netDxf.Vector2>') return new Wipeout(new ClippingBoundary(args[0]));
    if (['netDxf.ClippingBoundary','netDxf.Vector2,netDxf.Vector2','double,double,double,double'].includes(signature)) return new Wipeout(...args);
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get ClippingBoundary() { return this.#boundary; }
  set ClippingBoundary(value) { if (value == null) throw new ArgumentNullException('value'); this.#boundary = value; }
  get Elevation() { return this.#elevation.getFloat64(0); } set Elevation(value) { this.#elevation.setFloat64(0, value); }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    let elevation = this.Elevation;
    const normal = TransformedNormal(transformation, this.Normal);
    const ow = MathHelper.ArbitraryAxis(this.Normal), wo = MathHelper.ArbitraryAxis(normal).Transpose(), vertexes = [];
    for (const vertex of this.#boundary.Vertexes) {
      let v = Matrix3.Multiply(ow, new Vector3(vertex.X, vertex.Y, this.Elevation));
      v = Vector3.Add(Matrix3.Multiply(transformation, v), translation); v = Matrix3.Multiply(wo, v);
      vertexes.push(new Vector2(v.X, v.Y)); elevation = v.Z;
    }
    const boundary = this.#boundary.Type === ClippingBoundaryType.Rectangular ? new ClippingBoundary(vertexes[0], vertexes[1]) : new ClippingBoundary(vertexes);
    this.Normal = normal; this.Elevation = elevation; this.ClippingBoundary = boundary;
  }
  Clone() {
    const copy = this.$copyEntityAttributes(new Wipeout(this.#boundary.Clone()));
    copy.Elevation = this.Elevation; return this.$finishEntityClone(copy);
  }
}
