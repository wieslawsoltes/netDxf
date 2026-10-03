// Port of all original cases/assertions in pinned CustomHeaderUnicodeTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, HeaderVariable, Vector2, Vector3, MemoryStream } from '../../index.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
import { NewCodeWriter, NewCodeReader } from '../support/CodecFactory.js';
import { Run, Check, Equal, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
export const HeaderUnicodeValues=[
  ['$PROJECTNAME',1,'Zażółć 東京 Ω'], ['$HYPERLINKBASE',1,'C:\\Próby\\東京\\rysunki'],
  ['$MENU',1,'Меню'], ['$UCSBASE',2,'工程_ó'], ['$PUCSBASE',2,'papier_Ł'],
  ['$PUCSNAME',2,''], ['$UCSORTHOREF',2,'ordinary ASCII \\ path']
];
export function RegisterCustomHeaderUnicodeTests(){
  for(const version of SupportedVersions)for(const binary of [false,true]){
    const suffix=`${VersionName(version)}/${BooleanName(binary)}`;
    Run('header/custom-unicode/escaped-input/'+suffix,()=>CustomHeaderRead(version,binary,true));
    Run('header/custom-unicode/ordinary-input/'+suffix,()=>CustomHeaderRead(version,binary,false));
    Run('header/custom-unicode/exact-export/'+suffix,()=>CustomHeaderExport(version,binary));
    Run('header/custom-unicode/repeated-roundtrip/'+suffix,()=>CustomHeaderRoundTrip(version,binary));
    Run('header/custom-unicode/non-string-controls/'+suffix,()=>CustomHeaderNonStrings(version,binary));
  }
}
export function HeaderEscape(text){let result='';for(let i=0;i<text.length;i++){const c=text.charCodeAt(i);result+=c>127?'\\U+'+c.toString(16).toUpperCase().padStart(4,'0'):text[i];}return result;}
export function CustomHeaderFixture(version,binary,escaped){
  const stream=new MemoryStream(),writer=NewCodeWriter(stream,binary),T=(code,value)=>writer.Write(code,value);
  for(const [code,value]of [[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252']])T(code,value);
  for(const [name,code,value]of HeaderUnicodeValues){T(9,name);T(code,escaped?HeaderEscape(value):version<15?'ASCII '+name:value);}
  T(0,'ENDSEC');T(0,'EOF');writer.Flush();stream.Position=0;return stream;
}
export function CustomHeaderValue(document,name){const result={};Check(document.DrawingVariables.TryGetCustomVariable(name,result),'Missing custom header '+name);return result.value;}
function load(stream,message){const doc=DxfDocument.Load(stream);if(!doc)throw new InvalidOperationException(message);return doc;}
export function CustomHeaderRead(version,binary,escaped){const input=CustomHeaderFixture(version,binary,escaped);try{
  const document=load(input,'Custom header input failed.');
  for(const [name,code,text]of HeaderUnicodeValues){const variable=CustomHeaderValue(document,name);Equal(code,variable.GroupCode,'Custom string group code');Equal(escaped||version>=15?text:'ASCII '+name,variable.Value,'Custom header Unicode decoding '+name);}
  Check(input.CanRead,'Header reader closed caller stream.');
}finally{input.Dispose();}}
export function NewCustomHeaderDocument(version){const doc=new DxfDocument(version);for(const [name,code,value]of HeaderUnicodeValues)doc.DrawingVariables.AddCustomVariable(new HeaderVariable(name,code,value));return doc;}
export function ReadRawHeader(bytes,binary){const input=new MemoryStream(bytes);try{
  const reader=NewCodeReader(input,binary),result=new Map();let header=false,name=null;
  while(true){reader.Next();const code=reader.Code,value=reader.Value;if(code===0&&value==='EOF')break;if(code===2&&value==='HEADER'){header=true;continue;}if(header&&code===0&&value==='ENDSEC')break;if(!header||code===999)continue;
    if(code===9){name=value.toUpperCase();Check(!result.has(name),'Duplicate raw header');result.set(name,[]);}else if(name!==null)result.get(name).push({Code:code,Value:value});}
  return result;
}finally{input.Dispose();}}
export function CustomHeaderExport(version,binary){const document=NewCustomHeaderDocument(version),output=new MemoryStream();try{
  Check(document.Save(output,binary),'Custom header save failed.');const raw=ReadRawHeader(output.ToArray(),binary);
  for(const [name,code,text]of HeaderUnicodeValues){const rows=raw.get(name);Equal(1,rows.length,'Single exported header tag');Equal(code,rows[0].Code,'Export changed custom group code');Equal(version<15?HeaderEscape(text):text,rows[0].Value,'Custom string wire encoding '+name);Equal(text,CustomHeaderValue(document,name).Value,'Encoding mutated the source header');}
  fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`header-unicode-${VersionName(version)}-${BooleanName(binary)}.dxf`),output.ToArray());
}finally{output.Dispose();}}
export function CustomHeaderRoundTrip(version,binary){let doc=NewCustomHeaderDocument(version);
  for(let cycle=0;cycle<4;cycle++){const stream=new MemoryStream();try{Check(doc.Save(stream,(cycle&1)===0?binary:!binary),'Repeated custom header save failed.');stream.Position=0;doc=load(stream,'Repeated custom header load failed.');for(const [name,,text]of HeaderUnicodeValues)Equal(text,CustomHeaderValue(doc,name).Value,'Repeated custom Unicode round trip '+name);Check(stream.CanRead,'Custom header round trip closed caller stream.');}finally{stream.Dispose();}}
  CustomHeaderValue(doc,'$PROJECTNAME').Value='changed Żółć 東京';const edited=new MemoryStream();try{Check(doc.Save(edited,binary),'Edited custom header save failed.');edited.Position=0;const updated=load(edited,'Edited custom header load failed.');Equal('changed Żółć 東京',CustomHeaderValue(updated,'$PROJECTNAME').Value,'Custom header edit lost Unicode');}finally{edited.Dispose();}
}
export function CustomHeaderNonStrings(version,binary){
  const controls=[new HeaderVariable('$USERI1',70,new BoxedScalar('Int16',-123)),new HeaderVariable('$USERR1',40,1.23456789),new HeaderVariable('$LIMMIN',20,new Vector2(-1.25,2.5)),new HeaderVariable('$EXTMIN',30,new Vector3(-10,-20,-30))];
  const document=new DxfDocument(version);for(const variable of controls)document.DrawingVariables.AddCustomVariable(variable);const output=new MemoryStream();try{
    Check(document.Save(output,binary),'Non-string custom header save failed.');output.Position=0;const loaded=load(output,'Non-string custom header load failed.');
    for(const expected of controls){const actual=CustomHeaderValue(loaded,expected.Name);Equal(expected.GroupCode,actual.GroupCode,'Non-string group changed');Equal(expected.Value.constructor,actual.Value.constructor,'Non-string runtime type changed');
      if(expected.Value instanceof BoxedScalar){Equal(expected.Value.Type,actual.Value.Type,'Non-string boxed runtime type changed');Equal(expected.Value.Value,actual.Value.Value,'Non-string custom value changed');}
      else if(typeof expected.Value?.Equals==='function')Check(expected.Value.Equals(actual.Value),'Non-string custom value changed');else Equal(expected.Value,actual.Value,'Non-string custom value changed');}
  }finally{output.Dispose();}
}
