import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceList } from '../../runtime/EntityRuntime.js';
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ReadOnlyArrayView, HasNonzeroXDataReference } from '../../runtime/StoredRecord.js';
import { InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const states=new WeakMap();
export function Stored3D(p){if(!states.has(p))states.set(p,{vertices:null,view:ReadOnlyArrayView([]),end:null,document:null});return states.get(p);}
export function InstallPolyline3DStoredRecords(Type){
  Object.defineProperties(Type.prototype,{
    VertexRecords:{get(){return Stored3D(this).view;}},EndSequenceRecord:{get(){return Stored3D(this).end;}},
    HasStoredRecords:{get(){return Stored3D(this).end!==null;}},StoredRecords:{get(){return this.HasStoredRecords?[...Stored3D(this).vertices,Stored3D(this).end]:[];}}
  });
  Type.prototype.SetStoredRecords=function(document,vertices,end){const s=Stored3D(this);s.document=document;s.vertices=vertices instanceof ReferenceList?vertices:new ReferenceList(vertices);s.view=ReadOnlyReferenceList(s.vertices);s.end=end;for(const r of this.StoredRecords)r.Owner=this;};
  Type.prototype.BindStoredRecordDocument=function(document){Stored3D(this).document=document;};
  Type.prototype.ValidateStoredRecords=function(document,registered){if(!this.HasStoredRecords)return;const s=Stored3D(this);if(s.document!==null&&s.document!==document)throw new NotSupportedException('Retained records cannot change documents.');this.ValidateStoredRecordGeometry();for(const r of this.StoredRecords)r.Validate(document,this,registered);};
  Type.prototype.ValidateStoredRecordGeometry=function(){
    if(this.Vertexes.Count!==Stored3D(this).vertices.length||this.SmoothType!==0)throw new NotSupportedException('Changing retained count or smoothing requires topology mapping.');
    for(const p of this.Vertexes)if(![p.X,p.Y,p.Z].every(Number.isFinite))throw new InvalidOperationException('Retained polyline coordinates must be finite.');
  };
  Type.prototype.RejectStoredRecordClone=function(){if(this.HasStoredRecords&&(this.ExtensionDictionary!==null||this.PersistentReactors.Count!==0||HasNonzeroXDataReference(this)||this.StoredRecords.some(r=>!r.CanClone())))throw new NotSupportedException('Cloning retained dependencies requires complete graph mapping.');};
  Type.prototype.CopyStoredRecordsTo=function(clone){if(!this.HasStoredRecords)return;const s=Stored3D(this),vertices=Array.from(s.vertices,r=>r.CopyForClone()),end=s.end.CopyForClone();this.RejectStoredRecordClone();clone.SetStoredRecords(null,vertices,end);};
  // Structural adapters for the not-yet-ported Block/Insert/Dimension graph. No registration is invented.
  Type.RejectStoredRecordBlockClone=function(root){const seen=new Set();function visit(block){if(block==null||seen.has(block))return;seen.add(block);for(const entity of block.Entities){const name=entity.constructor.name;if(name==='DxfOpaqueEntity')throw new NotSupportedException('Unknown entities require a complete schema.');if(['Polyline3D','PolygonMesh','PolyfaceMesh','Polyline2D'].includes(name))entity.RejectStoredRecordClone();if(name==='Insert'||'Block' in entity&&entity.Type>=0&&name.endsWith('Dimension'))visit(entity.Block);}}visit(root);};
  Type.RejectStoredRecordOwnershipClone=function(source){const seen=new Set();for(let current=source;current!=null;current=current.Owner){if(seen.has(current))throw new InvalidOperationException('The clone source has cyclic ownership.');seen.add(current);if(['DxfOpaqueEntity','Polyline3DRecord','PolygonMeshRecord','PolyfaceMeshRecord','Polyline2DRecord'].includes(current.constructor.name))throw new NotSupportedException('Retained metadata requires its complete source graph.');}};
}
