// Original codec/value cases only. Twelve document roundtrips still require typed Load/Save;
// those cases remain absent rather than being shortened or marked skipped/passing.
import { DxfDocument, DxfThumbnailImage, MemoryStream } from '../../index.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
import { ArgumentNullException, InvalidDataException, EndOfStreamException } from '../../runtime/Errors.js';
const Tag=(code,value)=>[code,value];
export const PreviewBytes=count=>Uint8Array.from({length:count},(_,i)=>i*73+19);
function writer(stream,binary){return binary?new BinaryCodeValueWriter(stream):new TextCodeValueWriter(stream);}
function reader(stream,binary){return binary?new BinaryCodeValueReader(stream):new TextCodeValueReader(new TextDecoder().decode(stream.ToArray()));}
export function ThumbnailCopies(){const d=new DxfDocument();Equal(0,d.ThumbnailImage.length,'default preview');const source=Uint8Array.of(1,2,3);d.ThumbnailImage=source;source[0]=99;const copy=d.ThumbnailImage;Equal(1,copy[0],'setter must copy');copy[1]=88;Equal(2,d.ThumbnailImage[1],'getter must copy');Throws(ArgumentNullException,()=>{d.ThumbnailImage=null;});d.ThumbnailImage=new Uint8Array();Equal(0,d.ThumbnailImage.length,'clear preview');}
export function ThumbnailCodecRoundTrip(binary){
  for(const length of [1,126,127,128,254,255,256,257,4097]){
    const expected=PreviewBytes(length),stream=new MemoryStream(),output=writer(stream,binary);
    DxfThumbnailImage.Write(output,expected);output.Write(0,'EOF');output.Flush();stream.Position=0;const input=reader(stream,binary);
    input.Next();Equal('SECTION',input.ReadString(),'preview section start');input.Next();Equal('THUMBNAILIMAGE',input.ReadString(),'preview section name');
    input.Next();Equal(90,input.Code,'preview length group code');Equal(length,input.ReadInt(),'preview declared bytes');const actual=[];let chunks=0;input.Next();
    while(input.Code===310){const part=input.ReadBytes();Check(part.length>0&&part.length<=127,'Nonconforming preview chunk size.');actual.push(...part);chunks++;input.Next();}
    Equal(Math.floor((length+126)/127),chunks,'preview chunk count');Equal('ENDSEC',input.ReadString(),'preview section end');Equal(expected,Uint8Array.from(actual),'Preview chunk data changed.');
    input.Next();Equal('EOF',input.ReadString(),'preview writer boundary');stream.Dispose();
  }
}
export function ReadThumbnailFixture(binary,tags){const stream=new MemoryStream();try{const output=writer(stream,binary);output.Write(2,'THUMBNAILIMAGE');for(const [code,value] of tags)output.Write(code,value);output.Flush();stream.Position=0;const input=reader(stream,binary);input.Next();return DxfThumbnailImage.Read(input);}finally{stream.Dispose();}}
const ThumbnailValid=(binary,tags,expected)=>Equal(expected,ReadThumbnailFixture(binary,tags),'Preview parser changed valid data.');
const ThumbnailInvalid=(type,binary,...tags)=>Throws(type,()=>ReadThumbnailFixture(binary,tags));
export function RegisterThumbnailImageTests(){
  Run('thumbnail/defensive-copy-and-clear',ThumbnailCopies);
  for(const b of [false,true]){
    const prefix='thumbnail/codec/'+(b?'binary':'text')+'/';
    Run(prefix+'chunk-boundaries-and-count',()=>ThumbnailCodecRoundTrip(b));
    Run(prefix+'128-byte-input-chunk',()=>ThumbnailValid(b,[Tag(90,128),Tag(310,PreviewBytes(128)),Tag(0,'ENDSEC')],PreviewBytes(128)));
    Run(prefix+'zero-length-input-chunk',()=>ThumbnailValid(b,[Tag(90,0),Tag(310,new Uint8Array()),Tag(0,'ENDSEC')],new Uint8Array()));
    Run(prefix+'count-after-data',()=>ThumbnailValid(b,[Tag(310,Uint8Array.of(1,2)),Tag(90,2),Tag(0,'ENDSEC')],Uint8Array.of(1,2)));
    Run(prefix+'negative-count',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,-1),Tag(0,'ENDSEC')));
    Run(prefix+'duplicate-count',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,0),Tag(90,0),Tag(0,'ENDSEC')));
    Run(prefix+'missing-count',()=>ThumbnailInvalid(InvalidDataException,b,Tag(310,Uint8Array.of(1)),Tag(0,'ENDSEC')));
    Run(prefix+'count-too-small',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,1),Tag(310,Uint8Array.of(1,2)),Tag(0,'ENDSEC')));
    Run(prefix+'count-too-large',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,3),Tag(310,Uint8Array.of(1,2)),Tag(0,'ENDSEC')));
    Run(prefix+'untrusted-large-count',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,2147483647),Tag(0,'ENDSEC')));
    Run(prefix+'physical-eof',()=>ThumbnailInvalid(EndOfStreamException,b,Tag(90,0)));
    Run(prefix+'explicit-eof',()=>ThumbnailInvalid(EndOfStreamException,b,Tag(90,0),Tag(0,'EOF')));
    Run(prefix+'unexpected-record',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,0),Tag(0,'SECTION')));
    Run(prefix+'unexpected-value-code',()=>ThumbnailInvalid(InvalidDataException,b,Tag(90,0),Tag(1,'not preview data'),Tag(0,'ENDSEC')));
    Run(prefix+'empty-section',()=>ThumbnailValid(b,[Tag(90,0),Tag(0,'ENDSEC')],new Uint8Array()));
  }
  Run('thumbnail/text/comment',()=>ThumbnailValid(false,[Tag(999,'comment'),Tag(90,2),Tag(310,Uint8Array.of(7,9)),Tag(0,'ENDSEC')],Uint8Array.of(7,9)));
}
