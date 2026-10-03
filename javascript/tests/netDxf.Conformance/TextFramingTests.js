// Port of pinned TextFramingTests.cs; TextReader/Stream overrides use the host adapters.
import { DxfDocument, MemoryStream } from '../../index.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { ArgumentNullException, EndOfStreamException, FormatException, IOException } from '../../runtime/Errors.js';
import { Culture } from '../../runtime/DisplayFormatting.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Near, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';

class FailingTextSource { ReadLine(){throw new IOException('Source failed.');} }
export class EofGuardStream extends MemoryStream {
  emptyReads=0; GuardTripped=false;
  constructor(bytes){super(bytes,false);}
  Read(buffer,offset,count){
    if(count>0&&this.Position===this.Length&&++this.emptyReads>8){this.GuardTripped=true;throw new IOException('Regression guard stopped repeated EOF reads.');}
    return super.Read(buffer,offset,count);
  }
}
export function RegisterTextFramingTests(){
  Run('text/framing/null-reader',()=>Throws(ArgumentNullException,()=>new TextCodeValueReader(null)));
  Run('text/framing/empty-input',()=>FramingError(EndOfStreamException,'',1));
  Run('text/framing/invalid-group',()=>{for(const code of ['','abc','32768','-32769','1.5','0x10','١٠'])FramingError(FormatException,code+'\nvalue\n',1);});
  Run('text/framing/empty-string-is-not-truncation',()=>{
    const reader=new TextCodeValueReader('1\n\n0\nEOF');reader.Next();Equal('',reader.ReadString(),'empty string value');reader.Next();Equal('EOF',reader.ReadString(),'explicit end marker');Throws(EndOfStreamException,()=>reader.Next());
  });
  Run('text/framing/group-code-whitespace',()=>{const reader=new TextCodeValueReader('  +10  \n1.25\n0\nEOF\n');reader.Next();Equal(10,reader.Code,'space-padded group code');Near(1.25,reader.ReadDouble(),'space-padded group value');});
  Run('text/framing/source-error',()=>{const reader=new TextCodeValueReader(new FailingTextSource());Throws(IOException,()=>reader.Next());});
  for(const newline of ['\n','\r\n','\r'])Run('text/framing/newline-'+Buffer.from(newline,'ascii').toString('hex').toUpperCase(),()=>{
    const reader=new TextCodeValueReader('999'+newline+'comment'+newline+'0'+newline+'EOF');reader.Next();Equal(2,reader.CurrentPosition,'comment line count');reader.Next();Equal(0,reader.Code,'end-marker group code');Equal('EOF',reader.ReadString(),'end marker without final newline');Equal(4,reader.CurrentPosition,'EOF line count');Throws(EndOfStreamException,()=>reader.Next());
  });
  for(const code of [0,1,9,10,40,60,90,100,101,102,105,110,140,160,170,210,270,280,290,300,310,320,330,370,380,390,400,410,420,430,440,450,460,470,480,999,1000,1004,1005,1010,1060,1071])
    Run(`text/framing/missing-value/${code}`,()=>{FramingError(EndOfStreamException,code+'\n',2,code);FramingError(EndOfStreamException,'999\ncomment\n'+code+'\n',4,code);});
  for(const version of SupportedVersions)for(const binary of [false,true]){
    const suffix=`${VersionName(version)}/${BooleanName(binary)}`;
    Run('framing/document/no-end-marker/'+suffix,()=>MissingDocumentEndMarker(version,binary));
    Run('framing/document/unterminated-header/'+suffix,()=>UnterminatedSection(version,binary,'HEADER'));
    Run('framing/document/unterminated-classes/'+suffix,()=>UnterminatedSection(version,binary,'CLASSES'));
  }
}
export function FramingError(Type,input,line,group=null){
  const reader=new TextCodeValueReader(input);if(line===4)reader.Next();let failure;try{reader.Next();}catch(error){failure=error;}
  Check(failure instanceof Type,`Expected ${Type.name}; truncated record was accepted.`);
  Check(failure.message.includes('line '+line),'Framing error has no physical line.');if(group!==null)Check(failure.message.includes('group code '+group),'Missing-value error has no group code.');
}
export function MissingDocumentEndMarker(version,binary){
  const complete=new MemoryStream();try{
    Check(new DxfDocument(version).Save(complete,binary),'Complete document failed to save.');const data=complete.ToArray(),marker=binary?Uint8Array.of(0,0,69,79,70,0):new TextEncoder().encode('0'+Culture.NewLine+'EOF'+Culture.NewLine);
    Equal(marker,data.slice(-marker.length),'Unexpected writer end-marker framing.');ExpectFiniteLoadFailure(data.slice(0,-marker.length));
  }finally{complete.Dispose();}
}
export function UnterminatedSection(version,binary,section){
  const output=new MemoryStream(),writer=binary?new BinaryCodeValueWriter(output):new TextCodeValueWriter(output),tag=(c,v)=>writer.Write(c,v);
  try{tag(0,'SECTION');tag(2,'HEADER');tag(9,'$ACADVER');tag(1,HeaderVersion(version));tag(9,'$DWGCODEPAGE');tag(3,'ANSI_1252');
    if(section!=='HEADER'){tag(0,'ENDSEC');tag(0,'SECTION');tag(2,section);}tag(0,'EOF');writer.Flush();ExpectFiniteLoadFailure(output.ToArray());
  }finally{output.Dispose();}
}
export function ExpectFiniteLoadFailure(bytes){
  const input=new EofGuardStream(bytes);
  try{if(GetTypedIOConfiguration()==='Debug')Throws(EndOfStreamException,()=>DxfDocument.Load(input));else Check(DxfDocument.Load(input)===null,'A truncated document was accepted.');}
  finally{try{Check(!input.GuardTripped,'Reader kept consuming physical EOF instead of reporting truncation.');Check(input.CanRead,'Failure closed a caller-owned stream.');}finally{input.Dispose();}}
}
