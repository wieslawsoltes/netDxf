// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { EntityVertices3, TransformedNormal } from '../../runtime/EntityGeometry.js';
export class Ray extends EntityObject {
  #origin; #direction;
  constructor(...args){
    super(EntityType.Ray,DxfObjectCode.Ray);
    const [origin,direction]=args.length===0?[Vector3.Zero,Vector3.UnitX]:EntityVertices3(args,2);
    this.#origin=origin;this.#direction=Vector3.Normalize(direction);
    if(Vector3.IsZero(this.#direction))throw new ArgumentException('The direction can not be the zero vector.','direction');
  }
  get Origin(){return Copy(this.#origin);}set Origin(value){this.#origin=Copy(value);}
  get Direction(){return Copy(this.#direction);}set Direction(value){
    this.#direction=Vector3.Normalize(value);if(Vector3.IsZero(this.#direction))throw new ArgumentException('The direction can not be the zero vector.','value');
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    this.Origin=Vector3.Add(Matrix3.Multiply(matrix,this.Origin),translation);
    let direction=Matrix3.Multiply(matrix,this.Direction);if(Vector3.Equals(Vector3.Zero,direction))direction=this.Direction;
    this.Direction=direction;this.Normal=TransformedNormal(matrix,this.Normal);
  }
  Clone(){const copy=this.$copyEntityAttributes(new Ray());copy.Origin=this.#origin;copy.Direction=this.#direction;return this.$finishEntityClone(copy);}
}
