// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { SourceHandle,SourceRecordIdentity } from './DxfReader.SourceIdentity.js';
import { TextCodeValueReader } from './TextCodeValueReader.js';
import { FormatException } from '../../runtime/Errors.js';
// Observe common headers only; XRECORD subclass payload is never common metadata.
export class DatabaseMetadataReader {
  #inner;#records;#sourceIdentities;#sourceDeclarations=new Map();
  #current={Owner:null,Extension:null,Reactors:[]};#handle=null;#group=null;#groupDepth=0;
  #common=false;#recordType=null;#section=null;#sectionHeader=false;
  SourceRecord=new SourceRecordIdentity();
  constructor(inner,records,sourceIdentities){this.#inner=inner;this.#records=records;this.#sourceIdentities=sourceIdentities;}
  get Code(){return this.#inner.Code;}get Value(){return this.#inner.Value;}get CurrentPosition(){return this.#inner.CurrentPosition;}
  get Code5IsString(){return this.#inner.Code5IsString;}set Code5IsString(value){this.#inner.Code5IsString=value;}
  SkipComments(){this.SetSkipComments(true);}
  SetSkipComments(value){if(this.#inner instanceof TextCodeValueReader)this.#inner.SkipComments=value;}
  #physical(){return ['TABLES','BLOCKS','ENTITIES','OBJECTS'].includes(this.#section)&&this.#recordType!==null&&!this.#sectionHeader&&!['ENDSEC','EOF','ENDTAB','CLASS'].includes(this.#recordType);}
  #flush(){
    const value=SourceHandle(this.#handle);
    if(this.#physical()&&value!==null&&value!==0n){this.#sourceIdentities.add(value);this.SourceRecord.Handle=value;}
    if(this.#handle!==null&&(this.#current.Extension!==null||this.#current.Reactors.length>0))this.#records.set(this.#handle,this.#current);
  }
  Next(){
    this.#inner.Next();
    if(this.Code===0){
      if(this.#group!==null)throw new FormatException('Unterminated common object control group.');
      this.#flush();this.#current={Owner:null,Extension:null,Reactors:[]};this.#handle=null;this.#common=true;this.#recordType=this.ReadString();this.SourceRecord=new SourceRecordIdentity();
      this.#sectionHeader=this.#recordType==='SECTION'&&this.#section===null;
      if(this.#recordType==='ENDSEC')this.#section=null;return;
    }
    if(this.#sectionHeader&&this.#section===null&&this.Code===2)this.#section=this.ReadString();
    if(!this.#common)return;
    if(this.Code===102){
      const value=this.ReadString();
      if(value==='}') {if(this.#groupDepth>0)this.#groupDepth--;if(this.#groupDepth===0)this.#group=null;}
      else if(value.startsWith('{')){if(this.#groupDepth===0)this.#group=value;this.#groupDepth++;}return;
    }
    if(this.#group!==null){
      if(this.#groupDepth===1&&this.#group==='{ACAD_XDICTIONARY'&&this.Code===360)this.#current.Extension=this.ReadHex();
      else if(this.#groupDepth===1&&this.#group==='{ACAD_REACTORS'&&this.Code===330)this.#current.Reactors.push(this.ReadHex());return;
    }
    if(this.Code===100||this.Code===1001){this.#flush();this.#common=false;return;}
    if(this.Code===5&&this.#recordType!=='DIMSTYLE'||this.Code===105&&this.#recordType==='DIMSTYLE'){
      this.#handle=this.ReadHex();if(this.SourceRecord.IdentitySeen)this.SourceRecord.Ambiguous=true;this.SourceRecord.IdentitySeen=true;
      const value=SourceHandle(this.#handle);
      if(this.#physical()&&value!==null&&value!==0n){const previous=this.#sourceDeclarations.get(value);
        if(previous!==undefined&&previous!==this.SourceRecord){previous.Ambiguous=true;this.SourceRecord.Ambiguous=true;}
        else this.#sourceDeclarations.set(value,this.SourceRecord);
      }
    }
  }
  ReadByte(){return this.#inner.ReadByte();}ReadBytes(){return this.#inner.ReadBytes();}ReadShort(){return this.#inner.ReadShort();}
  ReadInt(){return this.#inner.ReadInt();}ReadLong(){return this.#inner.ReadLong();}ReadBool(){return this.#inner.ReadBool();}
  ReadDouble(){return this.#inner.ReadDouble();}ReadString(){return this.#inner.ReadString();}ReadHex(){return this.#inner.ReadHex();}
}
