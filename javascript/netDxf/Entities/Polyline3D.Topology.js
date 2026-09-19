// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Stored3D } from './Polyline3D.StoredRecords.js';
import { ArgumentException, InvalidOperationException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
function finite(p,name){if(![p.X,p.Y,p.Z].every(Number.isFinite))throw new ArgumentException('Topology coordinates must be finite.',name);}
function validate(p){
  if(p.Vertexes.Count>65536)throw new NotSupportedException('A topology edit cannot exceed 65,536 vertices.');
  for(const v of p.Vertexes)finite(v,'Vertexes');if(!p.HasStoredRecords)return null;
  const doc=Stored3D(p).document;
  if(doc===null||p.Handle===null||doc.GetObjectByHandle(p.Handle)!==p||p.Owner===null||doc.GetObjectByHandle(p.Owner.Handle)!==p.Owner)throw new InvalidOperationException('Retained topology requires a registered source parent.');
  p.ValidateStoredRecords(doc,true);return doc;
}
export function InstallPolyline3DTopology(Type){
  Type.prototype.InsertVertex=function(index,position){
    RequireInteger(index,0,this.Vertexes.Count,'index');finite(position,'position');if(this.Vertexes.Count>=65536)throw new NotSupportedException('A topology edit cannot exceed 65,536 vertices.');
    const doc=validate(this);if(!this.HasStoredRecords){this.Vertexes.Insert(index,position);return;}
    const inserted=doc.PreparePolylineVertexInsertion(this,position);doc.RegisterPolylineVertexInsertion(inserted);this.Vertexes.Insert(index,position);Stored3D(this).vertices.Insert(index,inserted);
  };
  Type.prototype.RemoveVertexAt=function(index){
    RequireInteger(index,0,this.Vertexes.Count-1,'index');const doc=validate(this);if(!this.HasStoredRecords){this.Vertexes.RemoveAt(index);return;}
    if(this.Vertexes.Count<=2)throw new InvalidOperationException('A retained polyline must keep at least two vertices.');const r=Stored3D(this).vertices.get_Item(index);
    doc.ValidatePolylineVertexRemoval(r);doc.UnregisterPolylineVertexRemoval(r);this.Vertexes.RemoveAt(index);Stored3D(this).vertices.RemoveAt(index);r.Owner=null;r.IsRemoved=true;
  };
  Type.prototype.MoveVertex=function(fromIndex,toIndex){
    RequireInteger(fromIndex,0,this.Vertexes.Count-1,'fromIndex');RequireInteger(toIndex,0,this.Vertexes.Count-1,'toIndex');validate(this);if(fromIndex===toIndex)return;
    const p=this.Vertexes.get_Item(fromIndex);this.Vertexes.RemoveAt(fromIndex);this.Vertexes.Insert(toIndex,p);
    if(this.HasStoredRecords){const a=Stored3D(this).vertices;const r=a.get_Item(fromIndex);a.RemoveAt(fromIndex);a.Insert(toIndex,r);}
  };
}
