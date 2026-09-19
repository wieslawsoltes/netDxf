// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Line } from './Line.js';
import { Polyline2D } from './Polyline2D.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { ValueList } from '../../runtime/ValueList.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { NurbsEvaluator } from '../../runtime/NurbsEvaluator.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { CopyCurveAppearance } from '../../runtime/CurveGeometry.js';
import { ArgumentException, ArgumentNullException, RequireInteger } from '../../runtime/Errors.js';
import { InstallPolyline3DStoredRecords, Stored3D } from './Polyline3D.StoredRecords.js';
import { InstallPolyline3DTopology } from './Polyline3D.Topology.js';
export class Polyline3D extends EntityObject {
  #vertices; #flags; #smooth=0; static #segments=8;
  constructor(vertexes=[],isClosed=false){
    super(EntityType.Polyline3D,DxfObjectCode.Polyline);
    if(arguments.length>2)throw new ArgumentException('No matching Polyline3D constructor.');
    if(vertexes==null)throw new ArgumentNullException('vertexes');
    this.#vertices=new ValueList(vertexes);this.#flags=isClosed?9:8;
  }
  static get DefaultSplineSegs(){return this.#segments;}static set DefaultSplineSegs(v){this.#segments=RequireInteger(v,1,32767);}
  get Vertexes(){return this.#vertices;}
  get Flags(){return this.#flags;}set Flags(v){this.#flags=v;}
  get IsClosed(){return (this.#flags&1)!==0;}set IsClosed(v){this.#flags=v?this.#flags|1:this.#flags&~1;}
  get LinetypeGeneration(){return (this.#flags&128)!==0;}set LinetypeGeneration(v){this.#flags=v?this.#flags|128:this.#flags&~128;}
  get SmoothType(){return this.#smooth;}set SmoothType(v){this.#flags=v===0?this.#flags&~4:this.#flags|4;this.#smooth=v;}
  Reverse(){if(this.Vertexes.Count<2)return;if(this.HasStoredRecords)this.ValidateStoredRecordGeometry();this.Vertexes.Reverse();if(this.HasStoredRecords)Stored3D(this).vertices.Reverse();}
  #line(start,end){const line=CopyCurveAppearance(this,new Line());line.StartPoint=start;line.EndPoint=end;return line;}
  Explode(){
    const lines=new ReferenceList();
    if(this.SmoothType===0){let i=0;for(const v of this.Vertexes){if(i===this.Vertexes.Count-1&&!this.IsClosed)break;lines.Add(this.#line(v,this.Vertexes.get_Item((i+1)%this.Vertexes.Count)));i++;}return lines;}
    const degree=this.SmoothType===5?2:3,segs=this.Owner===null?Polyline3D.DefaultSplineSegs:this.Owner.Record.Owner.Owner.DrawingVariables.SplineSegs;
    const precision=(segs*(this.IsClosed?this.Vertexes.Count:this.Vertexes.Count-1))|0;
    const points=NurbsEvaluator(this.Vertexes.ToArray(),null,null,degree,false,this.IsClosed,precision);
    for(let i=1;i<points.Count;i++)lines.Add(this.#line(points.get_Item(i-1),points.get_Item(i)));
    if(this.IsClosed)lines.Add(this.#line(points.get_Item(points.Count-1),points.get_Item(0)));return lines;
  }
  PolygonalVertexes(precision){
    RequireInteger(precision,0,2147483647,'precision');
    if(this.SmoothType!==5&&this.SmoothType!==6)return new ValueList(this.Vertexes);
    return NurbsEvaluator(this.Vertexes.ToArray(),null,null,this.SmoothType===5?2:3,false,this.IsClosed,Math.max(2,precision));
  }
  ToPolyline2D(precision){const points=MathHelper.Transform(this.PolygonalVertexes(precision),this.Normal,{value:0});const p=CopyCurveAppearance(this,new Polyline2D(points));p.IsClosed=this.IsClosed;return p;}
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    for(let i=0;i<this.Vertexes.Count;i++)this.Vertexes.set_Item(i,Vector3.Add(Matrix3.Multiply(matrix,this.Vertexes.get_Item(i)),translation));
    this.Normal=TransformedNormal(matrix,this.Normal);
  }
  Clone(){
    this.RejectStoredRecordClone();const p=CopyCurveAppearance(this,new Polyline3D(this.Vertexes));p.IsVisible=this.IsVisible;p.Flags=this.Flags;
    // The pinned Clone copies Flags but does not copy the separate SmoothType field.
    for(const d of this.XData.Values)p.XData.Add(d.Clone());this.CopyCommonDataTo(p);this.CopyStoredRecordsTo(p);return p;
  }
}
InstallPolyline3DStoredRecords(Polyline3D);InstallPolyline3DTopology(Polyline3D);
