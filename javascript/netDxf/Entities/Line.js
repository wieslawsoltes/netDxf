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
export class Line extends EntityObject {
  #start; #end; Thickness = 0;
  constructor(...args) { super(EntityType.Line,DxfObjectCode.Line); [this.#start,this.#end]=EntityVertices3(args,2); }
  get StartPoint() { return Copy(this.#start); } set StartPoint(value) { this.#start=Copy(value); }
  get EndPoint() { return Copy(this.#end); } set EndPoint(value) { this.#end=Copy(value); }
  get Direction() { return Vector3.Subtract(this.#end,this.#start); }
  Reverse() { [this.#start,this.#end]=[this.#end,this.#start]; }
  TransformBy(matrix,translation) {
    [matrix,translation]=this.$transformArguments(matrix,translation);
    const normal=TransformedNormal(matrix,this.Normal);
    this.StartPoint=Vector3.Add(Matrix3.Multiply(matrix,this.StartPoint),translation);
    this.EndPoint=Vector3.Add(Matrix3.Multiply(matrix,this.EndPoint),translation);this.Normal=normal;
  }
  Clone() {
    const copy=this.$copyEntityAttributes(new Line());copy.StartPoint=this.#start;copy.EndPoint=this.#end;copy.Thickness=this.Thickness;
    return this.$finishEntityClone(copy);
  }
}
