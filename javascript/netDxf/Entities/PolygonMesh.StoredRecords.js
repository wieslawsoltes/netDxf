// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import {ReadOnlyArrayView,HasNonzeroXDataReference} from '../../runtime/StoredRecord.js';
import {NotSupportedException} from '../../runtime/Errors.js';
const storage=new WeakMap();
function state(p){if(!storage.has(p))storage.set(p,{vertices:null,view:ReadOnlyArrayView([]),end:null,document:null});return storage.get(p);}
export function InstallPolygonMeshStoredRecords(Type){
 Object.defineProperties(Type.prototype,{
  VertexRecords:{get(){return state(this).view;}},EndSequenceRecord:{get(){return state(this).end;}},
  HasStoredRecords:{get(){return state(this).end!==null;}},StoredRecords:{get(){return this.HasStoredRecords?[...state(this).vertices,state(this).end]:[];}}
 });
 Type.prototype.SetStoredRecords=function(document,vertices,end){const s=state(this);s.document=document;s.vertices=vertices;s.view=ReadOnlyArrayView(vertices);s.end=end;for(const r of this.StoredRecords)r.Owner=this;};
 Type.prototype.BindStoredRecordDocument=function(document){state(this).document=document;};
 Type.prototype.ValidateStoredRecords=function(document,registered){
  if(!this.HasStoredRecords)return;const s=state(this);
  if(s.document!==null&&s.document!==document)throw new NotSupportedException('Retained polygon mesh records cannot change documents.');
  if(this.SmoothType!==0||this.Vertexes.length!==s.vertices.length)throw new NotSupportedException('Retained polygon mesh topology requires regeneration.');
  this.ValidateSurface();for(const r of this.StoredRecords)r.Validate(document,this,registered);
 };
 Type.prototype.RejectStoredRecordClone=function(){
  if(!this.HasStoredRecords)return;
  if(this.SmoothType!==0)throw new NotSupportedException('A retained polygon mesh cannot be cloned after a smoothing change.');
  this.ValidateSurface();
  if(this.ExtensionDictionary!==null||this.PersistentReactors.Count!==0||HasNonzeroXDataReference(this)||this.StoredRecords.some(r=>!r.CanClone()))throw new NotSupportedException('Cloning retained dependencies requires complete graph mapping.');
 };
 Type.prototype.CopyStoredRecordsTo=function(clone){if(!this.HasStoredRecords)return;this.RejectStoredRecordClone();clone.SetStoredRecords(null,Array.from(state(this).vertices,r=>r.CopyForClone()),state(this).end.CopyForClone());};
}
