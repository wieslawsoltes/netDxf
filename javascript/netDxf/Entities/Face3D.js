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
export class Face3D extends EntityObject {
  #vertices; EdgeFlags=0;
  constructor(...args){super(EntityType.Face3D,DxfObjectCode.Face3d);this.#vertices=EntityVertices3(args,4);}
  get FirstVertex(){return Copy(this.#vertices[0]);}set FirstVertex(v){this.#vertices[0]=Copy(v);}
  get SecondVertex(){return Copy(this.#vertices[1]);}set SecondVertex(v){this.#vertices[1]=Copy(v);}
  get ThirdVertex(){return Copy(this.#vertices[2]);}set ThirdVertex(v){this.#vertices[2]=Copy(v);}
  get FourthVertex(){return Copy(this.#vertices[3]);}set FourthVertex(v){this.#vertices[3]=Copy(v);}
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    for(let i=0;i<4;i++)this.#vertices[i]=Vector3.Add(Matrix3.Multiply(matrix,this.#vertices[i]),translation);
    this.Normal=TransformedNormal(matrix,this.Normal);
  }
  Clone(){const copy=this.$copyEntityAttributes(new Face3D());copy.#vertices=this.#vertices.map(Copy);copy.EdgeFlags=this.EdgeFlags;return this.$finishEntityClone(copy);}
}
