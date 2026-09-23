// Test-only constructor and observation adapter. No registration or topology logic.
// Synthetic retained records exercise the unchanged internal constructors in C#;
// this does not stand in for a typed DXF reader.
import * as api from '../index.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { doubleBits } from './wire.mjs';
const fields = new Set(['Handle','SourceVersion','HasPrivateData','HasPrivateHeader','IsRemoved','NumHandles','XDataStart','UsesBlockRecordOwner']);
export function retainedSet(target, name, value) {
  if (!fields.has(name)) throw new Error('Unapproved retained fixture field: ' + name);
  target[name] = value;
}
export function createRetainedParent(options) {
  const family=options.family, count=options.count ?? 4;
  if (!['Polyline3D','PolygonMesh','PolyfaceMesh','Polyline2D'].includes(family)) throw new Error('Unknown retained family.');
  const points=Array.from({length:count},(_,i)=>new api.Vector3(i,i%2,family==='Polyline2D'?0:i/4));
  let parent;
  if(family==='Polyline3D')parent=new api.Polyline3D(points);
  else if(family==='PolygonMesh')parent=new api.PolygonMesh(2,count/2,points);
  else if(family==='PolyfaceMesh')parent=new api.PolyfaceMesh(points,[[1,2,3,4]]);
  else parent=new api.Polyline2D(points.map(p=>new api.Vector2(p.X,p.Y)));
  const Record=api[family==='Polyline3D'?'Polyline3DRecord':family+'Record'];
  const layer=new api.Layer(options.layer ?? 'Stored'),linetype=new api.Linetype(options.linetype ?? 'StoredLine');
  const records=[];
  const make=(index,end=false,face=false)=>{
    const position=points[Math.min(index,count-1)];
    const sourceHandle=(0xc0+index).toString(16).toUpperCase();
    const tags=new ReferenceList([
      new api.DxfTag(5,sourceHandle),new api.DxfTag(330,'A0'),new api.DxfTag(100,'AcDbEntity'),
      new api.DxfTag(8,layer.Name),new api.DxfTag(6,linetype.Name),new api.DxfTag(100,end?'AcDbSequenceEnd':'AcDbVertex'),
      ...(!end?[new api.DxfTag(10,position.X),new api.DxfTag(20,position.Y),new api.DxfTag(30,position.Z),new api.DxfTag(70,32)]:[])
    ]);
    const record=new Record(end?'SEQEND':'VERTEX',tags);
    Object.assign(record,{SourceOwner:'A0',SourceVersion:options.version??18,CommonEnd:2,IdentityIndex:0,OwnerIndex:1,
      Position:position,UsesBlockRecordOwner:!!options.blockOwner});
    if(options.preserveHandles)record.Handle=sourceHandle;
    record.Resources.set(3,layer);record.Resources.set(4,linetype);
    record.OriginalResourceNames.set(3,layer.Name);record.OriginalResourceNames.set(4,linetype.Name);
    if(!end){record.Coordinates.set(10,6);record.Coordinates.set(20,7);record.Coordinates.set(30,8);}
    if(family==='Polyline2D'&&!end)record.StoredVertex=parent.Vertexes.get_Item(index);
    if(family==='PolyfaceMesh'){
      record.CoordinateIndex=end||face?-1:index;
      if(face){record.IsFaceRecord=true;record.FaceIndex=0;record.StoredFace=parent.Faces.get_Item(0);
        record.OriginalIndexes=Array.from(record.Face.VertexIndexes);record.FaceLayerIndex=3;
        if(options.faceLayer!==null)record.Face.Layer=new api.Layer(options.faceLayer??'FaceLayer');}
    }
    records.push(record);return record;
  };
  const vertices=points.map((_,index)=>make(index));
  if(family==='PolyfaceMesh')make(count,false,true);
  const end=make(records.length,true);
  if(family==='PolyfaceMesh')parent.SetStoredRecords(null,records);
  else parent.SetStoredRecords(null,vertices,end);
  if(family==='Polyline2D'||family==='PolyfaceMesh') {
    parent.StoredHeaderTags=new ReferenceList([new api.DxfTag(100,family==='Polyline2D'?'AcDb2dPolyline':'AcDbPolyFaceMesh')]);
    parent.StoredHeaderPublicEnd=1;
  }
  return parent;
}
const ref=item=>item==null?null:{type:item.constructor.name,handle:item.Handle,owner:item.Owner?.Handle??null,code:item.CodeName};
const point=p=>[doubleBits(p.X),doubleBits(p.Y),doubleBits(p.Z??0)];
function recordState(record){
  return {common:ref(record),sourceVersion:record.SourceVersion,storedOwner:ref(record.StoredOwner),sourceDocument:ref(record.SourceDocument),
    layer:ref(record.Layer),linetype:ref(record.Linetype),sequenceEnd:record.IsSequenceEnd,blockOwner:record.UsesBlockRecordOwner,
    authored:record instanceof api.Polyline3DRecord?record.IsAuthored:null,removed:record instanceof api.Polyline3DRecord?record.IsRemoved:null,
    privateData:record.HasPrivateData,position:point(record.Position),tagCount:record.TopologyTagCount(),
    tags:Array.from(record.Tags,t=>({code:t.Code,type:t.ValueType,value:t.ValueType===api.DxfTagValueType.Double?doubleBits(t.Value):t.Value})),
    resources:Array.from(record.Resources,([key,value])=>({key,value:ref(value)})),
    coordinates:Array.from(record.Coordinates,([key,value])=>({key,value})),
    references:Array.from(record.References,ref),xdata:Array.from(record.XData.Values,data=>({registry:ref(data.ApplicationRegistry),count:data.XDataRecord.Count}))};
}
export function retainedSnapshot(target){
  if(target instanceof api.Polyline3DRecord||target instanceof api.PolygonMeshRecord||target instanceof api.PolyfaceMeshRecord||target instanceof api.Polyline2DRecord)return recordState(target);
  return {common:ref(target),points:Array.from(target.Vertexes,v=>point(v.Position??v)),records:Array.from(target.StoredRecords,recordState)};
}
