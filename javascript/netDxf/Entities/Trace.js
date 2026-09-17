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
export class Trace extends EntityObject {
  #vertices; Elevation=0; Thickness=0;
  constructor(...args){
    super(EntityType.Trace,DxfObjectCode.Trace);
    if(args.length===0)this.#vertices=Array.from({length:4},()=>Vector2.Zero);
    else if((args.length===3||args.length===4)&&args.every(v=>v instanceof Vector2)){
      // The three-vertex overload reconstructs all four vectors; the four-vertex overload copies them.
      this.#vertices=args.length===3?[...args,args[2]].map(v=>new Vector2(v.X,v.Y)):args.map(Copy);
    }else throw new ArgumentException('No matching Trace constructor.');
  }
  get FirstVertex(){return Copy(this.#vertices[0]);}set FirstVertex(v){this.#vertices[0]=Copy(v);}
  get SecondVertex(){return Copy(this.#vertices[1]);}set SecondVertex(v){this.#vertices[1]=Copy(v);}
  get ThirdVertex(){return Copy(this.#vertices[2]);}set ThirdVertex(v){this.#vertices[2]=Copy(v);}
  get FourthVertex(){return Copy(this.#vertices[3]);}set FourthVertex(v){this.#vertices[3]=Copy(v);}
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    const normal=TransformedNormal(matrix,this.Normal),world=MathHelper.ArbitraryAxis(this.Normal),local=MathHelper.ArbitraryAxis(normal).Transpose();
    let v;
    for(let i=0;i<4;i++){
      v=Matrix3.Multiply(world,new Vector3(this.#vertices[i].X,this.#vertices[i].Y,this.Elevation));
      v=Vector3.Add(Matrix3.Multiply(matrix,v),translation);v=Matrix3.Multiply(local,v);this.#vertices[i]=new Vector2(v.X,v.Y);
    }
    this.Normal=normal;this.Elevation=v.Z;
  }
  Clone(){
    const copy=this.$copyEntityAttributes(new Trace());copy.#vertices=this.#vertices.map(Copy);copy.Thickness=this.Thickness;
    // Deliberately retain the pinned C# clone's omission of Elevation; this is a compatibility port.
    return this.$finishEntityClone(copy);
  }
}
