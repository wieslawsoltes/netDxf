// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, MultiplyDouble } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { EntityVector3, EntityVertices3, TransformedNormal } from '../../runtime/EntityGeometry.js';
export class Point extends EntityObject {
  #position; #rotation=0; Thickness=0;
  constructor(...args) {
    super(EntityType.Point,DxfObjectCode.Point);
    if(args.length===0)this.#position=Vector3.Zero;
    else if(args.length===1)this.#position=EntityVector3(args[0]);
    else if(args.length===3&&args.every(value=>typeof value==='number'))this.#position=new Vector3(...args);
    else throw new ArgumentException('No matching Point constructor.');
  }
  get Position(){return Copy(this.#position);}set Position(value){this.#position=Copy(value);}
  get Rotation(){return this.#rotation;}set Rotation(value){this.#rotation=MathHelper.NormalizeAngle(value);}
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    const position=Vector3.Add(Matrix3.Multiply(matrix,this.Position),translation),normal=TransformedNormal(matrix,this.Normal);
    const world=MathHelper.ArbitraryAxis(this.Normal),local=MathHelper.ArbitraryAxis(normal).Transpose();
    const ref=Vector2.Rotate(Vector2.UnitX,MultiplyDouble(this.Rotation,MathHelper.DegToRad));
    let v=Matrix3.Multiply(world,new Vector3(ref.X,ref.Y,0));v=Matrix3.Multiply(matrix,v);v=Matrix3.Multiply(local,v);
    const rotation=MultiplyDouble(Vector2.Angle(new Vector2(v.X,v.Y)),MathHelper.RadToDeg);
    this.Position=position;this.Rotation=rotation;this.Normal=normal;
  }
  Clone(){const copy=this.$copyEntityAttributes(new Point());copy.Position=this.#position;copy.Rotation=this.#rotation;copy.Thickness=this.Thickness;return this.$finishEntityClone(copy);}
}
