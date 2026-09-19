// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { DxfTag } from '../IO/DxfTag.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyArrayView, HasNonzeroXDataReference, IsStoredReference } from '../../runtime/StoredRecord.js';
import { MultiplyDouble as mul, Copy } from '../../runtime/GeometryRuntime.js';
import { InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const data=new WeakMap();
function state(p){if(!data.has(p))data.set(p,{vertices:[],view:ReadOnlyArrayView([]),end:null,document:null,start:null,finish:null});return data.get(p);}
const finite=v=>[v.X,v.Y,v.Z].every(Number.isFinite);
const tagAt=(list,i)=>list.get_Item?list.get_Item(i):list[i];
export function InstallPolyline2DStoredRecords(Type){
  Object.defineProperties(Type.prototype,{
    VertexRecords:{get(){return state(this).view;}},EndSequenceRecord:{get(){return state(this).end;}},
    LegacyDefaultStartWidth:{get(){return state(this).start;},set(v){state(this).start=v;}},
    LegacyDefaultEndWidth:{get(){return state(this).finish;},set(v){state(this).finish=v;}},
    HasStoredRecords:{get(){return state(this).end!==null;}},
    StoredRecords:{get(){return this.HasStoredRecords?[...state(this).vertices,state(this).end]:[];}},
    StoredHeaderReferences:{get(){return this.StoredHeaderTags===null?[]:Array.from(this.StoredHeaderTags).filter(IsStoredReference);}}
  });
  Type.prototype.SetStoredRecords=function(document,vertices,end){const s=state(this);s.document=document;s.vertices=vertices;s.view=ReadOnlyArrayView(vertices);s.end=end;this.CodeName=DxfObjectCode.Polyline;for(const r of this.StoredRecords)r.Owner=this;};
  Type.prototype.BindStoredRecordDocument=function(document){state(this).document=document;};
  Type.prototype.LegacyHeaderValues=function(){
    const result=new Map(),indices=this.StoredHeaderIndices,add=(code,value)=>result.set(code,new DxfTag(code,value));
    if(indices.has(70)||(this.Flags<<16>>16)!==0)add(70,this.Flags<<16>>16);
    if(indices.has(30)||this.Elevation!==0){for(const c of [10,20])result.set(c,indices.has(c)?tagAt(this.StoredHeaderTags,indices.get(c)):new DxfTag(c,0));add(30,this.Elevation);}
    if(indices.has(39)||this.Thickness!==0)add(39,this.Thickness);
    if(this.LegacyDefaultStartWidth!==null)add(40,this.LegacyDefaultStartWidth);if(this.LegacyDefaultEndWidth!==null)add(41,this.LegacyDefaultEndWidth);
    if(!Vector3.Equals(this.Normal,this.StoredNormal)){add(210,this.Normal.X);add(220,this.Normal.Y);add(230,this.Normal.Z);}
    else for(const c of [210,220,230])if(indices.has(c))result.set(c,tagAt(this.StoredHeaderTags,indices.get(c)));return result;
  };
  Type.prototype.ValidateStoredRecords=function(document,registered){
    if(!this.HasStoredRecords)return;if(state(this).document!==null&&state(this).document!==document)throw new NotSupportedException('Retained records cannot change documents.');
    this.ValidateStoredRecordGeometry();for(const r of this.StoredRecords)r.Validate(document,this,registered);
  };
  Type.prototype.ValidateStoredRecordGeometry=function(){
    if(!this.HasStoredRecords)return;
    if(this.SmoothType!==0||this.ConstantWidth!==null||(this.Flags&~129)!==0||this.Vertexes.Count!==state(this).vertices.length)throw new NotSupportedException('Retained legacy records require unchanged ordinary topology and width representation.');
    this.ValidateVertexFidelity();let retained=0;
    for(const r of this.StoredRecords){const count=r.TopologyTagCount();if(count>4096)throw new NotSupportedException('A retained child exceeds its tag budget.');retained+=count;}
    if(retained>1048576||this.StoredHeaderTags.Count+this.LegacyHeaderValues().size-this.StoredHeaderIndices.size>4096)throw new NotSupportedException('The retained chain exceeds its tag budget.');
    if(!Number.isFinite(this.Elevation)||!Number.isFinite(this.Thickness)||!finite(this.Normal)||Vector3.IsZero(this.Normal))throw new InvalidOperationException('Retained plane, thickness and nonzero normal must be finite.');
    if(this.LegacyDefaultStartWidth!==null)Type.ValidateWidth(this.LegacyDefaultStartWidth,'LegacyDefaultStartWidth');if(this.LegacyDefaultEndWidth!==null)Type.ValidateWidth(this.LegacyDefaultEndWidth,'LegacyDefaultEndWidth');
    for(let i=0;i<this.Vertexes.Count;i++){
      const v=this.Vertexes.get_Item(i);if(v!==state(this).vertices[i].Vertex)throw new NotSupportedException('Retained vertex replacement or reordering requires topology mapping.');
      if(![v.Position.X,v.Position.Y,v.Bulge].every(Number.isFinite))throw new InvalidOperationException('Retained coordinates and bulges must be finite.');
    }
  };
  Type.prototype.RejectStoredRecordClone=function(){
    if(!this.HasStoredRecords)return;this.ValidateStoredRecordGeometry();
    if(this.HasPrivateHeader||this.ExtensionDictionary!==null||this.PersistentReactors.Count!==0||HasNonzeroXDataReference(this)||this.StoredRecords.some(r=>!r.CanClone()))throw new NotSupportedException('Private, owned and external retained dependencies require complete graph mapping.');
  };
  Type.prototype.CopyStoredRecordsTo=function(clone){
    if(!this.HasStoredRecords)return;this.RejectStoredRecordClone();const s=state(this);
    clone.SetStoredRecords(null,s.vertices.map((r,i)=>r.CopyForClone(clone.Vertexes.get_Item(i))),s.end.CopyForClone());clone.StoredHeaderTags=new ReferenceList(this.StoredHeaderTags);
    for(const [key,value] of this.StoredHeaderIndices)clone.StoredHeaderIndices.set(key,value);
    clone.StoredNormal=Copy(this.StoredNormal);clone.StoredHeaderPublicEnd=this.StoredHeaderPublicEnd;clone.LegacyDefaultStartWidth=this.LegacyDefaultStartWidth;clone.LegacyDefaultEndWidth=this.LegacyDefaultEndWidth;
  };
  Type.prototype.TransformStoredRecords=function(matrix,translation){
    this.ValidateStoredRecordGeometry();const widthScale=this.GetWidthTransformScale(matrix),normal=Matrix3.Multiply(matrix,this.Normal),normalScale=normal.Modulus();
    if(!Number.isFinite(normalScale)||normalScale<=0)throw new NotSupportedException('A retained transform requires a finite nonzero normal.');
    const ow=MathHelper.ArbitraryAxis(this.Normal),wo=MathHelper.ArbitraryAxis(normal).Transpose(),x=Matrix3.Multiply(matrix,Matrix3.Multiply(ow,Vector3.UnitX)),y=Matrix3.Multiply(matrix,Matrix3.Multiply(ow,Vector3.UnitY));
    if(!Number.isFinite(x.Modulus())||!Number.isFinite(y.Modulus())||x.Modulus()<=0||y.Modulus()<=0||Math.abs(Vector3.DotProduct(Vector3.Divide(x,x.Modulus()),Vector3.Divide(normal,normalScale)))>MathHelper.Epsilon||Math.abs(Vector3.DotProduct(Vector3.Divide(y,y.Modulus()),Vector3.Divide(normal,normalScale)))>MathHelper.Epsilon)
      throw new NotSupportedException('Retained transform must preserve the plane perpendicular to its normal.');
    const transform=p=>Matrix3.Multiply(wo,Vector3.Add(Matrix3.Multiply(matrix,Matrix3.Multiply(ow,new Vector3(p.X,p.Y,this.Elevation))),translation));
    const origin=transform(Vector2.Zero);if(!finite(origin))throw new InvalidOperationException('The transformed retained plane must be finite.');
    let elevation=origin.Z;const positions=[];
    for(const v of this.Vertexes){const p=transform(v.Position);if(!finite(p))throw new InvalidOperationException('Transformed retained coordinates must be finite.');positions.push(new Vector2(p.X,p.Y));elevation=p.Z;}
    const thickness=mul(this.Thickness,normalScale);if(!Number.isFinite(thickness))throw new InvalidOperationException('Transformed retained thickness must be finite.');
    for(let i=0;i<positions.length;i++)this.Vertexes.get_Item(i).Position=positions[i];this.Elevation=elevation;this.Normal=normal;this.Thickness=thickness;
    if(this.LegacyDefaultStartWidth!==null)this.LegacyDefaultStartWidth=mul(this.LegacyDefaultStartWidth,widthScale);if(this.LegacyDefaultEndWidth!==null)this.LegacyDefaultEndWidth=mul(this.LegacyDefaultEndWidth,widthScale);
    for(const v of this.Vertexes){if(v.StartWidthOverride!==null)v.StartWidthOverride=mul(v.StartWidthOverride,widthScale);if(v.EndWidthOverride!==null)v.EndWidthOverride=mul(v.EndWidthOverride,widthScale);}
  };
  Type.prototype.ReverseStoredRecords=function(){if(!this.HasStoredRecords)return;state(this).vertices.reverse();const start=this.LegacyDefaultStartWidth;this.LegacyDefaultStartWidth=this.LegacyDefaultEndWidth;this.LegacyDefaultEndWidth=start;};
}
