// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Shared source-identical portions of the four retained VERTEX/SEQEND partials.
import * as api from '../index.js';
import { DxfTag } from '../netDxf/IO/DxfTag.js';
import { DxfTagValueType } from '../netDxf/IO/DxfGroupCode.js';
import { DecodeDxfText, EncodeDxfText } from './DxfStringEncoding.js';
import { InvariantIgnoreCaseEquals } from './InvariantFloat.js';
import { ReferenceList } from './ReferenceList.js';
import { SameSequence } from './StoredRecord.js';
import { Copy } from './GeometryRuntime.js';
import { WriteXData } from './DxfXDataIO.js';
import { WriteDatabaseTag, CheckDatabaseText } from '../netDxf/IO/DxfWriter.Objects.js';
import { FormatException, OverflowException, ArgumentException, NullReferenceException } from './Errors.js';
export const At=(list,index)=>list.get_Item?list.get_Item(index):list[index];
export const Count=list=>list.Count??list.length;
const append=(map,key,value)=>{if(map.has(key))throw new ArgumentException('An item with the same key has already been added.');map.set(key,value);};
export function PolylineRecordHandle(tag) {
  const text=tag.Value;if(text===null)throw new NullReferenceException();
  if(!/^[0-9a-f]+\0*$/i.test(text))throw new FormatException();const value=BigInt('0x'+text.replace(/\0+$/,''));
  if(value>0xffffffffffffffffn)throw new OverflowException();return value.toString(16).toUpperCase();
}
export function StoredPolylineGroupEnd(tags,start) {
  if(!At(tags,start).Value.startsWith('{'))throw new FormatException('Invalid polyline control group.');let depth=1;
  for(let i=start+1;i<Count(tags);i++) {
    if(At(tags,i).Code!==102)continue;const text=At(tags,i).Value;
    if(text==='}'&&--depth===0)return i;
    if(text.startsWith('{')&&++depth>32)throw new FormatException('Polyline control groups are too deeply nested.');
  }
  throw new FormatException('Unterminated polyline control group.');
}
export function GetRecordResource(context,code,name) {
  const layer=code===8,Type=layer?api.Layer:api.Linetype,table=layer?context.Document.Layers:context.Document.Linetypes;
  if(!api.TableObject.IsValidName(name))name=Type.DefaultName;
  const found={};if(table.TryGetValue(name,found))return found.value;
  if(!layer)for(const item of context.complexLinetypes)if(InvariantIgnoreCaseEquals(name,item.Name))return item;
  return table.Add(new Type(name));
}
const specs={
  polyline:{Record:()=>api.Polyline3DRecord,name:'polyline',subclass:'AcDb3dPolylineVertex',flags:32,
    flagError:'The retained VERTEX slice requires ordinary 3D vertex flags 32.',incomplete:'Incomplete ordinary 3D VERTEX/SEQEND record.',list:'loadedPolylineRecords'},
  polygon:{Record:()=>api.PolygonMeshRecord,name:'polygon mesh',subclass:'AcDbPolygonMeshVertex',flags:64,
    flagError:'The retained VERTEX slice requires ordinary polygon mesh vertex flags 64.',incomplete:'Incomplete ordinary polygon mesh VERTEX/SEQEND record.',list:'loadedPolygonMeshRecords'},
  polyface:{Record:()=>api.PolyfaceMeshRecord,name:'polyface mesh',subclass:'AcDbPolyFaceMeshVertex',flags:192,
    flagError:'A retained POLYFACE VERTEX requires flags 192 for coordinates or 128 for faces.',incomplete:'Incomplete ordinary polyface mesh VERTEX/SEQEND record.',list:'loadedPolyfaceMeshRecords'},
  legacy:{Record:()=>api.Polyline2DRecord,name:'legacy 2D polyline',subclass:'AcDb2dVertex',flags:0,
    flagError:'Retained legacy VERTEX requires ordinary flags zero or absent.',incomplete:'Incomplete ordinary legacy VERTEX/SEQEND record.',list:'loadedPolyline2DRecords'}
};
export function ReadStoredRecord(context,end,kind) {
  const spec=specs[kind],polyface=kind==='polyface',legacy=kind==='legacy',source=context.CurrentSourceRecord,chunk=context.Chunk,document=context.Document,tags=[];
  chunk.Next();while(chunk.Code!==0) {
    if(tags.length>=4096||++context.polylineRecordTags>1048576)throw new FormatException('Retained '+spec.name+' records exceed their tag admission budget.');
    if(chunk.Code!==999)tags.push(new DxfTag(chunk.Code,chunk.Value));chunk.Next();
  }
  const Type=spec.Record(),record=new Type(end?'SEQEND':'VERTEX',new ReferenceList(tags));record.SourceDocument=document;record.SourceVersion=document.DrawingVariables.AcadVer;
  let i=0;
  for(;i<tags.length&&tags[i].Code!==100&&tags[i].Code!==1001;i++) {
    const tag=tags[i];
    if(tag.Code===102) {
      const name=tag.Value,first=i,last=StoredPolylineGroupEnd(tags,i);
      if(name==='{ACAD_REACTORS'||name==='{ACAD_XDICTIONARY') {
        if(Array.from(record.MetadataGroups.keys()).some(key=>tags[key].Value===name))throw new FormatException('Duplicate '+spec.name+' metadata control group.');
        record.MetadataGroups.set(first,last);
        for(let at=first+1;at<last;at++) {
          if(name==='{ACAD_REACTORS'&&tags[at].Code===330)record.ReactorHandles.Add(PolylineRecordHandle(tags[at]));
          else if(name==='{ACAD_XDICTIONARY'&&tags[at].Code===360&&record.ExtensionHandle===null)record.ExtensionHandle=PolylineRecordHandle(tags[at]);
          else throw new FormatException('Invalid '+spec.name+' metadata control group.');
        }
      }else record.HasPrivateData=true;i=last;
    }else if(tag.Code===5) {
      if(record.Handle!==null)throw new FormatException('Duplicate VERTEX/SEQEND identity.');record.Handle=PolylineRecordHandle(tag);record.IdentityIndex=i;
    }else if(tag.Code===330) {
      if(record.SourceOwner!==null)throw new FormatException('Duplicate VERTEX/SEQEND owner.');record.SourceOwner=PolylineRecordHandle(tag);record.OwnerIndex=i;
    }else record.HasPrivateData=true;
  }
  if(record.Handle===null||record.Handle==='0'||record.SourceOwner===null||record.SourceOwner==='0')throw new FormatException('Retained VERTEX/SEQEND requires a nonzero physical identity and owner.');
  if(source===null||source.Handle!==BigInt('0x'+record.Handle))throw new FormatException('VERTEX/SEQEND identity must belong to its own common header.');
  const identity=BigInt.asIntN(64,BigInt('0x'+record.Handle));if(identity>=document.NumHandles&&identity<9223372036854775807n)document.NumHandles=identity+1n;
  record.CommonEnd=i;let stage=0,privateSubclass=false,flagsSeen=false;if(legacy)record.GeometryEnd=tags.length;
  for(;i<tags.length;i++) {
    const tag=tags[i],code=tag.Code;
    if(code===1001){if(legacy&&record.GeometryEnd===tags.length)record.GeometryEnd=i;record.XDataStart=i;context.ReadDatabaseXData(record,tags,i);break;}
    if(code===102){record.HasPrivateData=true;i=StoredPolylineGroupEnd(tags,i);continue;}
    if(code===100) {
      const name=tag.Value;
      if(polyface) {
        if(stage===1&&record.FaceCommonEnd<0)record.FaceCommonEnd=i;
        if(stage===0&&name==='AcDbEntity'){stage=1;privateSubclass=false;}
        else if(!end&&stage===1&&name==='AcDbVertex'){stage=2;privateSubclass=false;}
        else if(!end&&stage===1&&name==='AcDbFaceRecord'){stage=3;privateSubclass=false;record.IsFaceRecord=true;}
        else if(!end&&stage===2&&name===spec.subclass){stage=3;privateSubclass=false;}
        else {if(['AcDbEntity','AcDbVertex',spec.subclass,'AcDbFaceRecord'].includes(name))throw new FormatException('Invalid retained POLYFACE subclass sequence.');privateSubclass=true;record.HasPrivateData=true;}
      }else {
        const expected=stage===0?'AcDbEntity':stage===1?'AcDbVertex':spec.subclass;
        if(!privateSubclass&&stage<(end?1:3)&&name===expected)stage++;
        else {
          if(stage<(end?1:3)||['AcDbEntity','AcDbVertex',spec.subclass].includes(name))throw new FormatException('Invalid retained '+spec.name+' subclass sequence.');
          if(legacy&&!privateSubclass)record.GeometryEnd=i;privateSubclass=true;record.HasPrivateData=true;
        }
      }
      continue;
    }
    if(privateSubclass)continue;
    if(stage===1) {
      if(code===8||code===6) {
        const layer=code===8;if(record[layer?'Layer':'Linetype']!==null)throw new FormatException('Duplicate '+spec.name+' record '+(layer?'layer.':'linetype.'));
        if(polyface&&layer)record.FaceLayerIndex=i;
        record.Resources.set(i,GetRecordResource(context,code,DecodeDxfText(tag.Value)));record.OriginalResourceNames.set(i,record[layer?'Layer':'Linetype'].Name);
      }
      if(polyface) {
        if([62,420,430].includes(code))record.FaceColorIndices.add(i);
        if(code===62&&(record.OriginalColor===null||!record.OriginalColor.UseTrueColor))record.OriginalColor=api.AciColor.FromCadIndex(tag.Value);
        if(code===420)record.OriginalColor=api.AciColor.FromTrueColor(tag.Value);
      }
      if(tag.ValueType===DxfTagValueType.Handle){if(code!==347&&code!==390)record.HasPrivateData=true;}
      else if(![8,6,62,420,430,440,370,48,60,67,410,284].includes(code))record.HasPrivateData=true;
    }
    if(!end&&stage===2)record.HasPrivateData=true;
    if(!end&&stage===3) {
      if(legacy) {
        if([10,20,30,40,41,42,91].includes(code)) {
          if(record.GeometryIndices.has(code))throw new FormatException('Duplicate retained legacy vertex geometry field.');record.GeometryIndices.set(code,i);
          if([10,20,30].includes(code))record.Coordinates.set(code,i);
        }else if(code===70) {
          if(flagsSeen||tag.Value!==0)throw new FormatException(spec.flagError);flagsSeen=true;
        }else if(code===50)throw new FormatException('Fitted legacy VERTEX tangents require an unsupported retained schema.');
        else record.HasPrivateData=true;
      }else {
        if([10,20,30].includes(code)) {
          if(record.Coordinates.has(code))throw new FormatException('Duplicate retained vertex coordinate.');record.Coordinates.set(code,i);record.Position[code===10?'X':code===20?'Y':'Z']=tag.Value;
        }
        if(code===70){if(flagsSeen||tag.Value!==(polyface&&record.IsFaceRecord?128:spec.flags))throw new FormatException(spec.flagError);flagsSeen=true;}
        if(polyface&&code>=71&&code<=74){if(!record.IsFaceRecord||record.FaceSlots.has(code))throw new FormatException('Invalid or duplicate POLYFACE face slot.');record.FaceSlots.set(code,i);}
        if(!(polyface?[10,20,30,70,40,41,42,50,91,71,72,73,74]:[10,20,30,70,40,41,42,50,91]).includes(code))record.HasPrivateData=true;
      }
    }
  }
  if(stage!==(end?1:3)||!end&&((!legacy&&!flagsSeen)||record.Coordinates.size!==3))throw new FormatException(spec.incomplete);
  if(legacy&&!end) {
    const value=code=>tags[record.GeometryIndices.get(code)].Value;
    if(value(30)!==0)throw new FormatException('The retained legacy 2D slice requires planar child Z=0; elevation belongs to the parent.');
    const vertex=new api.Polyline2DVertex(value(10),value(20));
    for(const [code,key] of [[40,'StartWidthOverride'],[41,'EndWidthOverride'],[42,'Bulge'],[91,'VertexIdentifier']])if(record.GeometryIndices.has(code))vertex[key]=value(code);record.StoredVertex=vertex;
  }
  context.RecordSourceObject(record,source);context[spec.list].push(record);return record;
}
export function ResolveStoredRecords(context,kind,Parent) {
  const spec=specs[kind];
  for(const record of context[spec.list]) {
    const owner=context.GetObjectBySourceHandle(record.SourceOwner);
    if(owner!==record.Owner) {
      const block=record.Owner instanceof Parent&&record.Owner.Owner instanceof api.Block?record.Owner.Owner:null;
      if(record.IsSequenceEnd||block===null||owner!==block.Record)throw new FormatException('A VERTEX owner must be its actual source POLYLINE or containing BLOCK_RECORD; SEQEND requires POLYLINE.');record.UsesBlockRecordOwner=true;
    }
    record.PersistentReactors.Clear();
    for(const handle of record.ReactorHandles) {
      if(handle==='0')continue;const target=context.GetObjectBySourceHandle(handle);
      if(target===null)throw new FormatException('Unresolved retained '+spec.name+' reactor: '+handle);record.PersistentReactors.Add(target);record.OriginalReactors.Add(target);
    }
    if(record.ExtensionHandle!==null&&record.ExtensionHandle!=='0') {
      const extension=context.GetObjectBySourceHandle(record.ExtensionHandle);
      if(!(extension instanceof api.DxfDictionary)||extension.Owner!==record)throw new FormatException('Invalid retained '+spec.name+' extension dictionary.');record.ExtensionDictionary=extension;
    }
    record.OriginalExtension=record.ExtensionDictionary;let entity=false;
    for(let i=record.CommonEnd;i<record.XDataStart;i++) {
      const tag=At(record.Tags,i);if(tag.Code===102){i=StoredPolylineGroupEnd(record.Tags,i);continue;}
      if(tag.Code===100){entity=tag.Value==='AcDbEntity';continue;}
      if(!entity||tag.Code!==347&&tag.Code!==390||PolylineRecordHandle(tag)==='0')continue;
      const target=context.GetObjectBySourceHandle(PolylineRecordHandle(tag));if(target===null)throw new FormatException('Unresolved retained '+spec.name+' common reference: '+tag.Value);append(record.Resources,i,target);
    }
  }
}
export function CheckRecordText(records){for(const record of records){for(const tag of record.Tags)if(typeof tag.Value==='string')CheckDatabaseText(tag.Value);for(const data of record.XData.Values)for(const tag of data.XDataRecord)if(typeof tag.Value==='string')CheckDatabaseText(tag.Value);}}
export function WriteRecordExtension(chunk,record){if(record.ExtensionDictionary===null)return;chunk.Write(102,'{ACAD_XDICTIONARY');chunk.Write(360,record.ExtensionDictionary.Handle);chunk.Write(102,'}');}
export function WriteRecordReactors(chunk,record){if(record.PersistentReactors.Count===0)return;chunk.Write(102,'{ACAD_REACTORS');for(const target of record.PersistentReactors)chunk.Write(330,target.Handle);chunk.Write(102,'}');}
export function WriteStoredRecord(chunk,document,record,point,kind) {
  point=Copy(point);const legacy=kind==='legacy',polyface=kind==='polyface',raw=tag=>WriteDatabaseTag(chunk,document.DrawingVariables.AcadVer,tag,false),encode=value=>EncodeDxfText(value,document.DrawingVariables.AcadVer);
  chunk.Write(0,record.CodeName);const geometry=legacy?record.GeometryTags():null;let extensionWritten=false,reactorsWritten=false;
  const appendGeometry=()=>{for(const [code,tag] of geometry)if(!record.GeometryIndices.has(code))raw(tag);};
  for(let i=0;i<record.XDataStart;i++) {
    if(i===record.CommonEnd){if(!extensionWritten&&record.ExtensionDictionary!==null)WriteRecordExtension(chunk,record);if(!reactorsWritten&&record.PersistentReactors.Count>0)WriteRecordReactors(chunk,record);}
    if(legacy&&i===record.GeometryEnd)appendGeometry();
    if(polyface&&record.Face!==null&&i===record.FaceCommonEnd) {
      if(record.FaceLayerIndex<0&&record.Face.Layer!==null)chunk.Write(8,encode(record.Face.Layer.Name));
      if(!record.FaceColorUnchanged&&record.Face.Color!==null){chunk.Write(62,record.Face.Color.Index);if(record.Face.Color.UseTrueColor)chunk.Write(420,api.AciColor.ToTrueColor(record.Face.Color));}
    }
    if(polyface&&record.Face!==null&&!record.FaceColorUnchanged&&record.FaceColorIndices.has(i))continue;
    const tag=At(record.Tags,i);
    if(record.MetadataGroups.has(i)) {
      const last=record.MetadataGroups.get(i),extension=tag.Value==='{ACAD_XDICTIONARY';
      const unchanged=extension?record.ExtensionDictionary===record.OriginalExtension:SameSequence(record.PersistentReactors,record.OriginalReactors)&&SameSequence(Array.from(record.ReactorHandles).filter(h=>h!=='0'),Array.from(record.PersistentReactors,t=>t.Handle));
      if(unchanged)for(let at=i;at<=last;at++)raw(At(record.Tags,at));else if(extension)WriteRecordExtension(chunk,record);else WriteRecordReactors(chunk,record);
      if(extension)extensionWritten=true;else reactorsWritten=true;i=last;continue;
    }
    if(i===record.IdentityIndex)chunk.Write(5,record.Handle);
    else if(i===record.OwnerIndex)chunk.Write(330,record.StoredOwner.Handle);
    else if(polyface&&record.Face!==null&&i===record.FaceLayerIndex) {
      if(record.Face.Layer!==null){if(record.OriginalResourceNames.has(i)&&record.OriginalResourceNames.get(i)===record.Face.Layer.Name)raw(tag);else chunk.Write(8,encode(record.Face.Layer.Name));}
    }else if(polyface&&record.Face!==null&&!record.FaceIndexesUnchanged&&record.FaceSlots.has(tag.Code)&&record.FaceSlots.get(tag.Code)===i) {
      const slot=tag.Code-71;if(slot<record.Face.VertexIndexes.length)chunk.Write(tag.Code,record.Face.VertexIndexes[slot]);else raw(tag);
    }else if(legacy&&record.GeometryIndices.has(tag.Code)&&record.GeometryIndices.get(tag.Code)===i) {if(geometry.has(tag.Code))raw(geometry.get(tag.Code));}
    else if(!legacy&&(!polyface||!record.IsFaceRecord)&&record.Coordinates.has(tag.Code)&&record.Coordinates.get(tag.Code)===i)chunk.Write(tag.Code,point[tag.Code===10?'X':tag.Code===20?'Y':'Z']);
    else if(record.Resources.has(i)) {
      const resource=record.Resources.get(i);
      if(resource instanceof api.TableObject&&record.OriginalResourceNames.has(i)) {
        if((kind!=='polyline'||!record.IsAuthored)&&record.OriginalResourceNames.get(i)===resource.Name)raw(tag);else chunk.Write(tag.Code,encode(resource.Name));
      }else chunk.Write(tag.Code,resource.Handle);
    }else raw(tag);
  }
  if(legacy&&record.GeometryEnd===record.XDataStart)appendGeometry();
  WriteXData(chunk,()=>document.DrawingVariables.AcadVer,record.XData);
}
