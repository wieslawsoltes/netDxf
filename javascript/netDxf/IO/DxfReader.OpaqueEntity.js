// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { DxfOpaqueEntity } from '../Entities/DxfOpaqueEntity.js';
import { DxfTag } from './DxfTag.js';
import { DxfHandleKind } from './DxfGroupCode.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { TrimDotNet } from '../../runtime/InvariantFloat.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { InvalidDataException, FormatException, NotSupportedException } from '../../runtime/Errors.js';
const ordinary=new Set(['3DFACE','3DSOLID','ACAD_TABLE','ARC','ARC_DIMENSION','ATTDEF','BODY','CIRCLE','DGNUNDERLAY','DIMENSION','DWFUNDERLAY','ELLIPSE','HATCH','HELIX','IMAGE','INSERT','LEADER','LIGHT','LINE','LWPOLYLINE','MESH','MLEADER','MLINE','MTEXT','MULTILEADER','OLE2FRAME','OLEFRAME','PDFUNDERLAY','POINT','POLYLINE','RAY','REGION','SECTION','SECTIONOBJECT','SHAPE','SOLID','SPLINE','TEXT','TOLERANCE','TRACE','VIEWPORT','WIPEOUT','XLINE']);
const excludedSubclasses=['AcDbSequenceEnd','AcDbVertex','AcDb2dVertex','AcDb3dPolylineVertex','AcDbPolygonMeshVertex','AcDbPolyFaceMeshVertex','AcDbFaceRecord','AcDbAttribute','AcDbAttributeDefinition','AcDbBlockBegin','AcDbBlockEnd','AcDbBlockReference','AcDbMInsertBlock','AcDbDimension','AcDbModelerGeometry','AcDbSurface','AcDbProxyEntity'];
const excludedNames=['VERTEX','ATTRIB','SEQEND','BLOCK','ENDBLK','SECTION','ENDSEC','EOF','TABLE','ENDTAB','CLASS','ACAD_PROXY_ENTITY','ACAD_PROXY_OBJECT','ACAD_ZOMBIE_ENTITY','SURFACE','PLANESURFACE','EXTRUDEDSURFACE','LOFTEDSURFACE','REVOLVEDSURFACE','SWEPTSURFACE'];
const pointer=kind=>kind===DxfHandleKind.SoftPointer||kind===DxfHandleKind.HardPointer;
const ownership=kind=>kind===DxfHandleKind.SoftOwner||kind===DxfHandleKind.HardOwner;
export function IsOpaqueEntityCandidate(name) {
  if(ordinary.has(name))return false;
  if(Array.from(ordinary).some(known=>OrdinalIgnoreCaseEquals(known,name)))throw new InvalidDataException('Unsupported casing of a known entity name.');return true;
}
export function OpaqueExcludedSubclass(name){return excludedSubclasses.some(value=>OrdinalIgnoreCaseEquals(value,name));}
export function OpaqueEntityGroupEnd(tags,start) {
  if(!tags[start].Value.startsWith('{'))throw new InvalidDataException('Invalid unknown entity control group.');
  let depth=1;
  for(let i=start+1;i<tags.length;i++) {
    if(tags[i].Code!==102)continue;const value=tags[i].Value;
    if(value==='}'){if(--depth===0)return i;}
    else if(value.startsWith('{')){if(++depth>32)throw new InvalidDataException('Unknown entity control groups exceed their depth budget.');}
    else throw new InvalidDataException('Invalid unknown entity control delimiter.');
  }
  throw new InvalidDataException('Unterminated unknown entity control group.');
}
export function ReadOpaqueCommon(context,entity,tags,indices) {
  const document=context.Document;let trueColor=false,graphicsSeen=false,length=null;const graphics=[];
  for(const index of indices) {
    const tag=tags[index],value=tag.Value;
    switch(tag.Code) {
      case 8:case 6: {
        const layer=tag.Code===8,table=layer?document.Layers:document.Linetypes,result={};
        if(!table.TryGetValue(DecodeDxfText(value),result)||context.GetObjectBySourceHandle(result.value.Handle)!==result.value)
          throw new InvalidDataException('Unknown entity requires its actual source '+(layer?'LAYER.':'LTYPE.'));
        entity[layer?'Layer':'Linetype']=result.value;break;
      }
      case 62:if(value< -256||value>256)throw new InvalidDataException('Invalid common ACI color.');if(!trueColor)entity.Color=api.AciColor.FromCadIndex(value);break;
      case 420:entity.Color=api.AciColor.FromTrueColor(value);trueColor=true;break;
      case 370:if(!Object.values(api.Lineweight).includes(value))throw new InvalidDataException('Invalid common lineweight.');entity.Lineweight=value;break;
      case 48:entity.LinetypeScale=value;break;
      case 60:if(value!==0&&value!==1)throw new InvalidDataException('Invalid common visibility.');entity.IsVisible=value===0;break;
      case 67:if(value!==0&&value!==1)throw new InvalidDataException('Invalid common space flag.');break;
      case 440:entity.Transparency=api.Transparency.FromAlphaValue(value);break;
      case 430:if(entity.SourceVersion<14)throw new InvalidDataException('Common color names require DXF 2004.');entity.ColorName=DecodeDxfText(value);break;
      case 284:if(entity.SourceVersion<15)throw new InvalidDataException('Common shadow mode requires DXF 2007.');entity.ShadowMode=value;break;
      case 92:case 160:
        if(length!==null||tag.Code===160&&entity.SourceVersion<16)throw new InvalidDataException('Invalid common graphics length.');
        length=BigInt(value);if(length<0n||length>BigInt(api.EntityObject.MaximumProxyGraphicsBytes))throw new InvalidDataException('Common graphics length exceeds its budget.');break;
      case 310:
        graphicsSeen=true;if(value.length>128||graphics.length+value.length>api.EntityObject.MaximumProxyGraphicsBytes)throw new InvalidDataException('Common graphics exceed their budget.');
        for(const byte of value)graphics.push(byte);break;
    }
  }
  if(length!==null&&length!==BigInt(graphics.length)||length===null&&graphicsSeen)throw new InvalidDataException('Common graphics length mismatch.');
  if(length!==null)entity.ProxyGraphics=Uint8Array.from(graphics);
}
/** Requires a DatabaseMetadataReader: identities must come from physical observation. */
export function ReadOpaqueEntity(context,isBlockEntity) {
  const document=context.Document,chunk=context.Chunk,name=chunk.ReadString();
  if(excludedNames.some(value=>OrdinalIgnoreCaseEquals(value,name)))throw new NotSupportedException('Unsupported standalone, aggregate or proxy entity: '+name);
  const source=context.CurrentSourceRecord,tags=[new DxfTag(0,name)];chunk.SetSkipComments(false);
  try {
    chunk.Next();while(chunk.Code!==0) {
      if(tags.length>=65536||++context.opaqueEntityTags>1048576)throw new InvalidDataException('Unknown entity tags exceed the admission budget.');
      tags.push(new DxfTag(chunk.Code,chunk.Value));chunk.Next();
    }
  }finally{chunk.SetSkipComments(true);}
  let handle=null,owner=null,common=-1,body=-1,xdata=tags.length;
  const commonFields=[],references=[],owners=[],reactors=[],singleton=new Set(),metadata=new Set();
  for(let i=1;i<tags.length;i++) {
    const tag=tags[i];if(tag.Code===999)continue;
    if(tag.Code===102) {
      const end=OpaqueEntityGroupEnd(tags,i),group=tag.Value;
      if(common<0&&(group==='{ACAD_XDICTIONARY'||group==='{ACAD_REACTORS')) {
        if(metadata.has(group))throw new InvalidDataException('Repeated unknown entity metadata group.');metadata.add(group);let count=0;
        for(let at=i+1;at<end;at++) {
          if(tags[at].Code===999)continue;
          if(tags[at].Code!==(group==='{ACAD_XDICTIONARY'?360:330))throw new InvalidDataException('Unsupported unknown entity metadata group framing.');
          references.push(at);count++;if(group==='{ACAD_XDICTIONARY')owners.push(at);else reactors.push(at);
        }
        if(group==='{ACAD_XDICTIONARY'&&count!==1)throw new InvalidDataException('Extension metadata requires one target.');
      }
      i=end;continue;
    }
    if(tag.Code===66||tag.Code===101)throw new NotSupportedException('Unknown aggregate or embedded entity framing is unsupported.');
    if(tag.Code===1001){if(body<0)throw new InvalidDataException('Unknown entity XData must follow private subclasses.');xdata=i;break;}
    if(tag.Code>=1000)throw new InvalidDataException('Unknown entity XData requires an APPID marker.');
    if(tag.Code===100) {
      const subclass=tag.Value;
      if(TrimDotNet(subclass).length===0)throw new InvalidDataException('Empty unknown entity subclass marker.');
      if(OpaqueExcludedSubclass(subclass))throw new NotSupportedException('Unsupported unknown entity subclass: '+subclass);
      if(common<0){if(subclass!=='AcDbEntity')throw new InvalidDataException('Unknown entity requires AcDbEntity first.');common=i;}
      else {if(OrdinalIgnoreCaseEquals(subclass,'AcDbEntity'))throw new InvalidDataException('Repeated AcDbEntity frame.');if(body<0)body=i;}
      continue;
    }
    if(common<0) {
      if(tag.Code===5){if(handle!==null)throw new InvalidDataException('Repeated unknown entity identity.');handle=DxfOpaqueEntity.CanonicalHandle(tag.Value);}
      else if(tag.Code===330){if(owner!==null)throw new InvalidDataException('Repeated unknown entity block owner.');owner=DxfOpaqueEntity.CanonicalHandle(tag.Value);references.push(i);}
      else if(pointer(tag.HandleKind))references.push(i);
      else if(ownership(tag.HandleKind))throw new NotSupportedException('Unsupported unknown entity header ownership.');
    }else {
      if(body<0) {
        if([8,6,62,420,370,48,60,440,430,284,67,410,347,390].includes(tag.Code)) {
          if(singleton.has(tag.Code))throw new InvalidDataException('Repeated unknown entity common singleton.');singleton.add(tag.Code);
        }
        commonFields.push(i);
      }
      if(pointer(tag.HandleKind)||ownership(tag.HandleKind)){references.push(i);if(ownership(tag.HandleKind))owners.push(i);}
    }
  }
  if(handle===null||handle==='0'||owner===null||owner==='0'||common<0||body<0||!singleton.has(8)||source===null||source.Handle!==BigInt('0x'+handle))
    throw new InvalidDataException('Unknown entity requires one physical identity, owner, layer and complete subclass envelope.');
  if(source.Ambiguous)throw new FormatException('A retained DXF object has an ambiguous physical source identity: '+handle);
  const entity=new DxfOpaqueEntity(document,name,tags,handle);entity.SourceOwnerHandle=owner;entity.CommonEnd=body;entity.XDataStart=xdata;
  for(const index of commonFields)entity.CommonFields.add(index);entity.ReferenceIndices.AddRange(references);entity.OwnerIndices.AddRange(owners);
  for(const index of reactors)entity.QualifiedReactorIndices.add(index);
  ReadOpaqueCommon(context,entity,tags,commonFields);
  if(xdata<tags.length)context.ReadDatabaseXData(entity,tags.filter((tag,index)=>index>=xdata&&tag.Code!==999),0);
  context.RecordSourceObject(entity,source);
  const identity=BigInt.asIntN(64,BigInt('0x'+handle));if(identity>=document.NumHandles&&identity<9223372036854775807n)document.NumHandles=identity+1n;
  context.opaqueEntities.push(entity);if(!isBlockEntity)context.entityList.Add(entity,owner);return entity;
}
export function ResolveOpaqueEntities(context) {
  if(context.opaqueEntities.length!==0&&context.hasDiscardedAcdsData)throw new NotSupportedException('Unknown entities with discarded ACDSDATA require raw preservation.');
  for(const entity of context.opaqueEntities) {
    if(context.unqualifiedOpaqueClasses.has(entity.CodeName))throw new NotSupportedException('Unknown entity CLASS has unqualified source fields.');
    const definition=context.Document.Classes.Contains(entity.CodeName)?context.Document.Classes.get_Item(entity.CodeName):null;
    if(definition!==null&&!definition.IsEntity)throw new InvalidDataException('Unknown entity CLASS must declare an entity.');
    entity.Resolve(handle=>context.GetObjectBySourceHandle(handle,true),definition);
  }
}
