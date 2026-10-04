// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDictionary } from '../Objects/DxfDatabaseObject.js';
import { FormatException } from '../../runtime/Errors.js';
// UInt64.TryParse(AllowHexSpecifier): leading zeros do not impose a digit-count bound.
export function SourceHandle(value){
  if(typeof value!=='string'||!(/^[0-9a-f]+\0*$/i).test(value))return null;
  const number=BigInt('0x'+value.replace(/\0+$/,''));return number<=0xffffffffffffffffn?number:null;
}
export class SourceRecordIdentity {Handle=0n;IdentitySeen=false;Ambiguous=false;}
export class SourceIdentityContext {
  sourceObjectIdentities=new Set();acceptedSourceObjects=new Map();acceptedSourceRecords=new Map();
  constructor(document){this.Document=document;this.Chunk=null;}
  get CurrentSourceRecord(){return this.Chunk.SourceRecord;}
  IsAcceptedSourceDictionary(handle){
    const value=SourceHandle(handle),source=this.acceptedSourceRecords.get(value);
    return value!==null&&this.sourceObjectIdentities.has(value)&&source!==undefined&&!source.Ambiguous&&this.acceptedSourceObjects.get(value) instanceof DxfDictionary;
  }
  RecordSourceObject(item,source){
    if(item==null||source==null||source.Handle===0n)return;
    const value=SourceHandle(item.Handle);
    if(value!==null&&value===source.Handle){this.acceptedSourceObjects.set(value,item);this.acceptedSourceRecords.set(value,source);}
  }
  ValidateSourceIdentityDeclarations(){
    for(const [handle,source]of this.acceptedSourceRecords)if(source.Ambiguous)
      throw new FormatException('A retained DXF object has an ambiguous physical source identity: '+handle.toString(16).toUpperCase());
  }
  GetObjectBySourceHandle(handle,includeMetadata=false){
    const value=SourceHandle(handle);
    if(value===null||value===0n||!this.sourceObjectIdentities.has(value)||!this.acceptedSourceObjects.has(value))return null;
    const source=this.acceptedSourceRecords.get(value);if(source===undefined||source.Ambiguous)return null;
    const canonical=value.toString(16).toUpperCase();
    const current=includeMetadata?this.Document.StoredTableHandleTarget(canonical):this.Document.GetObjectByHandle(canonical);
    return this.acceptedSourceObjects.get(value)===current?current:null;
  }
}
