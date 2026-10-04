// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfClass } from '../DxfClass.js';
import { DxfClassCollection } from '../Collections/DxfClassCollection.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { Image } from '../Entities/Image.js';
import { PrepareHelixClass } from './DxfHelix.js';
import { PrepareDatabaseClasses } from './DxfWriter.Objects.js';
import { IsOpaqueEntityClass } from './DxfWriter.OpaqueEntity.js';
import { DecodeDxfText, EncodeDxfText, EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { ArgumentException, InvalidDataException, EndOfStreamException, OverflowException } from '../../runtime/Errors.js';
export function ReadNextClassTag(chunk){do{chunk.Next();}while(chunk.Code===999);}
export function ReadClassFlag(chunk) {
  const value=chunk.ReadShort();
  if(value!==0&&value!==1)throw new InvalidDataException('CLASS group '+chunk.Code+' must be 0 or 1 at position '+chunk.CurrentPosition+'.');
  return value===1;
}
/** Enter at the CLASSES section-name tag; leave the ENDSEC record unconsumed. */
export function ReadClassDefinitions(context) {
  const chunk=context.Chunk,document=context.Document;ReadNextClassTag(chunk);
  while(true) {
    if(chunk.Code!==0)throw new InvalidDataException('Expected a CLASS record boundary.');
    const type=chunk.ReadString();
    if(type===DxfObjectCode.EndSection)return;
    if(type===DxfObjectCode.EndOfFile)throw new EndOfStreamException('CLASSES ended without ENDSEC.');
    if(type!==DxfObjectCode.Class)throw new InvalidDataException('Unexpected record in CLASSES: '+type);
    let name=null,cppName=null,application='',flags=0,count=null,wasProxy=false,isEntity=false,qualified=true;
    const seen=new Set();ReadNextClassTag(chunk);
    while(chunk.Code!==0) {
      if(seen.has(chunk.Code))qualified=false;seen.add(chunk.Code);
      switch(chunk.Code) {
        case 1:name=DecodeDxfText(chunk.ReadString());break;
        case 2:cppName=DecodeDxfText(chunk.ReadString());break;
        case 3:application=DecodeDxfText(chunk.ReadString());break;
        case 90:flags=chunk.ReadInt();break;
        case 91:count=chunk.ReadInt();break;
        case 280:wasProxy=ReadClassFlag(chunk);break;
        case 281:isEntity=ReadClassFlag(chunk);break;
        default:qualified=false;break;
      }
      ReadNextClassTag(chunk);
    }
    if(!qualified&&name!==null)context.unqualifiedOpaqueClasses.add(name);
    try {
      const definition=new DxfClass(name,cppName,application);
      definition.ProxyFlags=flags;definition.InstanceCount=count;definition.WasProxy=wasProxy;definition.IsEntity=isEntity;
      document.Classes.Add(definition);
    }catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid or duplicate CLASS definition: '+(name??''),error);throw error;}
  }
}
export function AddGeneratedClass(definitions,name,cppName,flags,entity,count) {
  if(definitions.Contains(name)) {
    const existing=definitions.get_Item(name);
    if(existing.CppClassName!==cppName||existing.IsEntity!==entity)throw new InvalidDataException('CLASS conflicts with a generated raster definition: '+name);
    existing.InstanceCount=count;
  }else {
    const definition=new DxfClass(name,cppName,'ISM');definition.ProxyFlags=flags;definition.IsEntity=entity;definition.InstanceCount=count;definitions.Add(definition);
  }
}
export function PrepareClassDefinitions(document) {
  const definitions=new DxfClassCollection();for(const definition of document.Classes)definitions.Add(definition.Clone());
  AddGeneratedClass(definitions,DxfObjectCode.RasterVariables,SubclassMarker.RasterVariables,0,false,1);
  if(document.ImageDefinitions.Count>0) {
    let images=0;for(const block of document.Blocks)for(const entity of block.Entities)if(entity instanceof Image){if(images===2147483647)throw new OverflowException();images++;}
    AddGeneratedClass(definitions,DxfObjectCode.ImageDef,SubclassMarker.RasterImageDef,0,false,document.ImageDefinitions.Count);
    AddGeneratedClass(definitions,DxfObjectCode.ImageDefReactor,SubclassMarker.RasterImageDefReactor,1,false,images);
    AddGeneratedClass(definitions,DxfObjectCode.Image,SubclassMarker.RasterImage,127,true,images);
  }else for(const name of [DxfObjectCode.ImageDef,DxfObjectCode.ImageDefReactor,DxfObjectCode.Image])if(definitions.Contains(name))definitions.get_Item(name).InstanceCount=0;
  PrepareHelixClass(document,definitions);PrepareDatabaseClasses(document,definitions);return definitions;
}
export function WriteClassDefinition(chunk,document,definition) {
  // Select the encoder once, but read the profile at every string call like the native delegate.
  const encode=IsOpaqueEntityClass(document,definition.Name)?value=>EncodeDxfDatabaseText(value,document.DrawingVariables.AcadVer):value=>EncodeDxfText(value,document.DrawingVariables.AcadVer);
  chunk.Write(0,DxfObjectCode.Class);chunk.Write(1,encode(definition.Name));chunk.Write(2,encode(definition.CppClassName));chunk.Write(3,encode(definition.ApplicationName));chunk.Write(90,definition.ProxyFlags);
  if(document.DrawingVariables.AcadVer>13&&definition.InstanceCount!==null)chunk.Write(91,definition.InstanceCount);
  chunk.Write(280,definition.WasProxy?1:0);chunk.Write(281,definition.IsEntity?1:0);
}
