// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { ProbeBinaryCursor, ProbeTextReader, ProbeAscii, ReadStreamBytes } from '../../runtime/ProbeStreamReaders.js';
import { BinaryCodeValueReader } from './BinaryCodeValueReader.js';
import { TextCodeValueReader } from './TextCodeValueReader.js';
import { StringEnum, StringComparison } from '../StringEnum.js';
import { DxfVersion } from '../Header/DxfVersion.js';
import { ArgumentException, ArgumentNullException, EndOfStreamException, FormatException, NotSupportedException } from '../../runtime/Errors.js';
let openFile=null;
export function SetHeaderProbeFileHost(open){const previous=openFile;openFile=open;return previous;}
const isTag=(reader,code,value)=>reader.Code===code&&typeof reader.Value==='string'&&reader.Value===value;
const next=reader=>{do{reader.Next();}while(reader.Code===999);};
/** The static source partial. This does not claim the unfinished full DxfReader. */
export class DxfReader {
  static CheckHeaderVariable(stream,headerVariable,isBinary={value:false}){
    isBinary.value=false;
    if(stream==null)throw new ArgumentNullException('stream');
    if(headerVariable==null||headerVariable==='')throw new ArgumentNullException('headerVariable');
    if(!stream.CanRead||!stream.CanSeek)throw new ArgumentException('A readable, seekable stream is required for a DXF header probe.','stream');
    let start;
    try{start=stream.Position;}catch(error){if(error instanceof NotSupportedException)throw new ArgumentException('Streams with an inaccessible Position property are not supported.','stream');throw error;}
    try{
      const prefix=ReadStreamBytes(stream,BinarySentinel.length);
      isBinary.value=prefix.length===BinarySentinel.length&&prefix.every((b,i)=>b===BinarySentinel[i]);stream.Position=start;
      const reader=isBinary.value?new BinaryCodeValueReader(new ProbeBinaryCursor(stream),ProbeAscii):new TextCodeValueReader(new ProbeTextReader(stream));
      return DxfReader.ProbeStringHeaderVariable(reader,headerVariable);
    }finally{stream.Position=start;}
  }
  static ProbeStringHeaderVariable(reader,headerVariable){
    next(reader);
    while(!isTag(reader,0,'EOF')){
      if(!isTag(reader,0,'SECTION'))throw new FormatException('Expected a DXF SECTION or EOF record while probing the header.');
      next(reader);if(reader.Code!==2)throw new FormatException('A DXF SECTION name must use group code 2.');
      const header=reader.ReadString()==='HEADER';next(reader);
      while(!isTag(reader,0,'ENDSEC')){
        if(isTag(reader,0,'EOF'))throw new EndOfStreamException('The DXF section ended without its ENDSEC record.');
        if(header){
          if(reader.Code===0)throw new FormatException('Unexpected control record in the DXF HEADER section.');
          if(isTag(reader,9,headerVariable)){
            next(reader);
            if(reader.Code===0||reader.Code===9||headerVariable==='$ACADVER'&&reader.Code!==1||headerVariable==='$DWGCODEPAGE'&&reader.Code!==3)
              throw new FormatException('The requested DXF header variable has a missing or incorrectly typed value.');
            if(typeof reader.Value!=='string')throw new FormatException('The requested DXF header variable must have a string value.');
            return reader.Value;
          }
        }
        next(reader);
      }
      if(header)return '';next(reader);
    }
    return '';
  }
}
export function InstallHeaderProbe(Type){
  const probe=(stream,binary)=>{binary.value=false;let value;
    try{value=DxfReader.CheckHeaderVariable(stream,'$ACADVER',binary);}catch{return DxfVersion.Unknown;}
    return StringEnum.Parse(DxfVersion,value,StringComparison.OrdinalIgnoreCase);
  };
  Type.CheckDxfFileVersion=function(input,binary={value:false}){
    if(typeof input!=='string')return probe(input,binary);
    if(openFile===null)throw new NotSupportedException('Filename probing requires the Node entry or an explicit header-probe file host.');
    const stream=openFile(input);try{return probe(stream,binary);}finally{stream.Close();}
  };
}
