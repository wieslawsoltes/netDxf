// Complete port of pinned ClassDefinitionTests.cs, including typed document integration.
import { DxfClass } from '../../netDxf/DxfClass.js';
import { DxfClassCollection } from '../../netDxf/Collections/DxfClassCollection.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
import { DxfDocument, MemoryStream, ImageDefinition, ImageResolutionUnits, Image, Vector3, Block, Insert } from '../../index.js';
import { InvalidDataException, EndOfStreamException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { HeaderEscape } from './CustomHeaderUnicodeTests.js';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
export function RegisterClassDefinitionTests() {
  Run('classes/model',ClassDefinitionModel);Run('classes/collection',ClassDefinitionCollection);
  for(const v of SupportedVersions)for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    Run(`classes/independent-input/${id}`,()=>ClassDefinitionInput(v,b));Run(`classes/defaults/${id}`,()=>ClassDefinitionDefaults(v,b));
    Run(`classes/edit-clone-roundtrip/${id}`,()=>ClassDefinitionRoundTrip(v,b));Run(`classes/generated-raster/${id}`,()=>ClassDefinitionGenerated(v,b));Run(`classes/unused-declarations/${id}`,()=>ClassDefinitionUnused(v,b));
    for(let n=0;n<9;n++)Run(`classes/malformed/${id}/${n}`,()=>ClassDefinitionMalformed(v,b,n));
    for(let n=0;n<3;n++)Run(`classes/conflict-preflight/${id}/${n}`,()=>ClassDefinitionConflict(v,b,n));
  }
}
export function CheckClass(expected,actual) {
  for(const key of ['Name','CppClassName','ApplicationName','ProxyFlags','InstanceCount','WasProxy','IsEntity'])Equal(expected[key],actual[key],key);
}
export function ClassDefinitionModel() {
  const c=new DxfClass('CUSTOM','AcDbCustom','');Equal(0,c.ProxyFlags);Check(c.InstanceCount===null&&!c.WasProxy&&!c.IsEntity);
  for(const value of ['',' ','bad\0name','bad\rname','bad\nname']) {
    Throws(ArgumentException,()=>new DxfClass(value,'Cpp','App'));Throws(ArgumentException,()=>new DxfClass('Name',value,'App'));
  }
  Throws(ArgumentNullException,()=>new DxfClass(null,'Cpp','App'));Throws(ArgumentNullException,()=>new DxfClass('Name',null,'App'));
  Throws(ArgumentNullException,()=>{c.ApplicationName=null;});Throws(ArgumentException,()=>{c.ApplicationName='bad\0app';});
  for(const value of [-1,-2147483648])Throws(ArgumentOutOfRangeException,()=>{c.InstanceCount=value;});
  c.InstanceCount=2147483647;c.ProxyFlags=-2147483648;c.WasProxy=true;c.IsEntity=true;
  const clone=c.Clone();CheckClass(c,clone);clone.ApplicationName='changed';clone.InstanceCount=null;clone.ProxyFlags=-1;clone.WasProxy=false;
  Equal('',c.ApplicationName);Equal(2147483647,c.InstanceCount);Check(c.WasProxy);Equal(-2147483648,c.ProxyFlags);
  // CLASS remains an independent metadata object, with no handle/owner inheritance.
  Equal(Object.prototype,Object.getPrototypeOf(DxfClass.prototype));Check(!('Handle' in c));
}
export function ClassDefinitionCollection() {
  const classes=new DxfClassCollection(),first=new DxfClass('FIRST','CppFirst','App'),second=new DxfClass('SECOND','CppSecond','App');
  classes.Add(first);classes.Add(second);Check(first===classes.get_Item('FIRST'));
  Throws(ArgumentException,()=>classes.Add(new DxfClass('FIRST','DifferentCpp','App')));
  Throws(ArgumentException,()=>classes.Add(new DxfClass('Different','CppFirst','App')));
  Throws(ArgumentNullException,()=>classes.Add(null));
  Throws(ArgumentException,()=>classes.set_Item(1,new DxfClass('THIRD','CppFirst','App')));
  Throws(ArgumentException,()=>classes.set_Item(1,new DxfClass('FIRST','CppThird','App')));
  Equal(2,classes.Count);Check(second===classes.get_Item(1));
  classes.set_Item(0,new DxfClass('REPLACED','CppFirst','New app'));Check(!classes.Contains('FIRST')&&classes.Contains('REPLACED'));
  Check(classes.Remove('REPLACED'));classes.Add(first);Check(first===classes.get_Item(1));classes.Clear();
  classes.Add(new DxfClass('SECOND','CppSecond','Again'));Equal(1,classes.Count);
}
export function ClassSample(name='VENDOR_ENTITY',cpp='AcDbVendorEntity'){return Object.assign(new DxfClass(name,cpp,'Plugin Zażółć 東京'),{ProxyFlags:0x800083ff|0,InstanceCount:17,WasProxy:true,IsEntity:true});}
export function* ClassSampleTags(c){yield[281,c.IsEntity?1:0];yield[90,c.ProxyFlags];yield[3,c.ApplicationName];yield[280,c.WasProxy?1:0];if(c.InstanceCount!==null)yield[91,c.InstanceCount];yield[2,c.CppClassName];yield[1,c.Name];}
export function ClassFixture(v,b,tags,terminated=true){const stream=new MemoryStream(),writer=b?new BinaryCodeValueWriter(stream):new TextCodeValueWriter(stream),t=(c,x)=>writer.Write(c,x);t(0,'SECTION');t(2,'HEADER');t(9,'$ACADVER');t(1,HeaderVersion(v));t(9,'$DWGCODEPAGE');t(3,'ANSI_1252');t(0,'ENDSEC');t(0,'SECTION');t(2,'CLASSES');if(!b)t(999,'ENDSEC');t(0,'CLASS');for(const [code,value]of tags){t(code,v<15&&typeof value==='string'?HeaderEscape(value):value);if(!b)t(999,'EOF');}if(terminated){t(0,'ENDSEC');t(0,'SECTION');t(2,'ENTITIES');t(0,'ENDSEC');}t(0,'EOF');writer.Flush();stream.Position=0;return stream;}
const loaded=stream=>{const doc=DxfDocument.Load(stream);Check(doc!==null,'CLASS input failed');return doc;};
export function ClassDefinitionInput(v,b){for(const name of ['VENDOR_ENTITY','ENDSEC','EOF']){const expected=ClassSample(name),input=ClassFixture(v,b,ClassSampleTags(expected));try{const doc=loaded(input);Equal(1,doc.Classes.Count,'CLASS input discarded');CheckClass(expected,doc.Classes.get_Item(0));Check(input.CanRead,'CLASS input closed caller stream.');}finally{input.Dispose();}}}
export function ClassDefinitionDefaults(v,b){const input=ClassFixture(v,b,[[1,'MINIMAL'],[2,'CppMinimal']]);try{const doc=loaded(input);Equal(1,doc.Classes.Count,'Minimal CLASS missing');CheckClass(new DxfClass('MINIMAL','CppMinimal',''),doc.Classes.get_Item(0));}finally{input.Dispose();}}
export function RawClasses(bytes,b){const input=new MemoryStream(bytes),reader=b?new BinaryCodeValueReader(input):new TextCodeValueReader(new TextDecoder().decode(bytes)),records=[];let current=null,classes=false,section=false;try{while(true){reader.Next();const code=reader.Code,value=reader.Value;if(code===0){if(current!==null){records.push(current);current=null;}if(value==='EOF')return records;if(value==='SECTION'){section=true;continue;}if(value==='ENDSEC'){classes=false;continue;}if(classes){Equal('CLASS',value,'CLASSES record type');current=new Map();}}if(section&&code===2){classes=value==='CLASSES';section=false;}if(current!==null&&code!==999){Check(!current.has(code),'Duplicate physical CLASS group');current.set(code,value);}}}finally{input.Dispose();}}
export function ClassDefinitionRoundTrip(v,b){let doc=new DxfDocument(v);const first=ClassSample(),second=new DxfClass('VENDOR_OBJECT','CppVendorObject','').Clone();second.ProxyFlags=1024;second.InstanceCount=null;doc.Classes.Add(first);doc.Classes.Add(second);for(let cycle=0;cycle<3;cycle++){const transport=cycle===1?!b:b,out=new MemoryStream();try{Check(doc.Save(out,transport),'CLASS save failed.');const wire=RawClasses(out.ToArray(),transport);Equal(3,wire.length,'Custom and generated CLASS reconciliation');for(let i=0;i<2;i++){const c=doc.Classes.get_Item(i),t=wire[i];Equal(c.Name,t.get(1),'CLASS output ordering');Equal(c.CppClassName,t.get(2),'CLASS output C++');Equal(v<15?HeaderEscape(c.ApplicationName):c.ApplicationName,t.get(3),'CLASS wire encoding');Equal(c.ProxyFlags,t.get(90),'CLASS raw bit mask');Equal(c.WasProxy?1:0,t.get(280),'CLASS proxy flag type');Equal(c.IsEntity?1:0,t.get(281),'CLASS entity flag type');Equal(v>13&&c.InstanceCount!==null,t.has(91),'CLASS count version/absence');Check(!t.has(5)&&!t.has(330),'CLASS acquired handle/owner fields.');}CheckClass(first,doc.Classes.get_Item(0));if(cycle===0)OleWriteArtifact(`classes-${VersionName(v)}-${BooleanName(b)}.dxf`,out.ToArray());out.Position=0;doc=loaded(out);if(v===13)first.InstanceCount=null;Equal(3,doc.Classes.Count,'Reload lost CLASS records');CheckClass(first,doc.Classes.get_Item(0));CheckClass(second,doc.Classes.get_Item(1));doc.Classes.get_Item(0).ApplicationName='Edited Ł 東京';first.ApplicationName='Edited Ł 東京';}finally{out.Dispose();}}
  Check(doc.Classes.Remove('VENDOR_ENTITY'),'Remove custom definition');const removed=new MemoryStream();try{Check(doc.Save(removed,b),'Save after CLASS removal failed.');Check(RawClasses(removed.ToArray(),b).every(r=>r.get(1)!=='VENDOR_ENTITY'),'Removed CLASS returned.');}finally{removed.Dispose();}}
export function ClassRasterDocument(v){const doc=new DxfDocument(v),definition=new ImageDefinition('ClassImage','not-opened.png',16,96,8,96,ImageResolutionUnits.Inches);doc.Entities.Add(new Image(definition,Vector3.Zero,16,8));const block=new Block('ImageBlock');block.Entities.Add(new Image(definition,Vector3.Zero,16,8));doc.Entities.Add(new Insert(block));doc.Entities.Add(new Insert(block,new Vector3(5,5,0)));return doc;}
export function ClassDefinitionGenerated(v,b){let doc=ClassRasterDocument(v);doc.Classes.Add(Object.assign(new DxfClass('IMAGE','AcDbRasterImage','Producer metadata'),{ProxyFlags:1023,WasProxy:true,IsEntity:true,InstanceCount:999}));for(let cycle=0;cycle<2;cycle++){const before=doc.Classes.Count,s=new MemoryStream();try{Check(doc.Save(s,b),'Raster CLASS reconciliation failed.');Equal(before,doc.Classes.Count,'Saving mutated source CLASS collection');const raw=RawClasses(s.ToArray(),b);Equal(4,raw.length,'Generated CLASS count');const image=OleSingle(raw.filter(r=>r.get(1)==='IMAGE'));Equal(1023,image.get(90),'Generated class overwrote source proxy permissions');Equal('Producer metadata',image.get(3),'Generated class overwrote application metadata');for(const[name,count]of[['IMAGE',2],['IMAGEDEF',1],['IMAGEDEF_REACTOR',2],['RASTERVARIABLES',1]]){const t=OleSingle(raw.filter(r=>r.get(1)===name));if(v>13)Equal(count,t.get(91),'Generated class instance count '+name);else Check(!t.has(91),'2000 generated CLASS emitted count tag.');}if(cycle===0)Equal(999,doc.Classes.get_Item('IMAGE').InstanceCount,'Count reconciliation mutated source metadata');s.Position=0;doc=loaded(s);}finally{s.Dispose();}}}
export function ClassDefinitionUnused(v,b){const doc=ClassRasterDocument(v),source=new MemoryStream(),out=new MemoryStream();try{Check(doc.Save(source,b),'Unused CLASS source failed.');source.Position=0;const old=loaded(source),empty=new DxfDocument(v);for(const c of old.Classes)empty.Classes.Add(c.Clone());Check(empty.Save(out,b),'Unused CLASS output failed.');const raw=RawClasses(out.ToArray(),b);Equal(4,raw.length,'Unused definitions discarded.');if(v>13)for(const name of ['IMAGE','IMAGEDEF','IMAGEDEF_REACTOR'])Equal(0,OleSingle(raw.filter(r=>r.get(1)===name)).get(91),'Unused generated type retained stale count');}finally{source.Dispose();out.Dispose();}}
export function ClassDefinitionMalformed(v,b,n){let t=Array.from(ClassSampleTags(ClassSample())),terminated=true;switch(n){case 0:t=t.filter(([c])=>c!==1);break;case 1:t=t.filter(([c])=>c!==2);break;case 2:t.push([280,2]);break;case 3:t.push([281,-1]);break;case 4:t.push([91,-1]);break;case 5:t.push([3,'bad\\U+0000name']);break;case 6:t.push([0,'CLASS'],...ClassSampleTags(ClassSample('VENDOR_ENTITY','OtherCpp')));break;case 7:t.push([0,'CLASS'],...ClassSampleTags(ClassSample('OTHER','AcDbVendorEntity')));break;default:terminated=false;}const input=ClassFixture(v,b,t,terminated);try{if(GetTypedIOConfiguration()==='Debug')Throws(terminated?InvalidDataException:EndOfStreamException,()=>DxfDocument.Load(input));else Check(DxfDocument.Load(input)===null,'Malformed CLASS was accepted.');Check(input.CanRead,'Malformed CLASS closed caller input.');}finally{input.Dispose();}}
export function ClassDefinitionConflict(v,b,n){const doc=new DxfDocument(v),definition=n===0?new DxfClass('RASTERVARIABLES','WrongCpp','App'):n===1?Object.assign(new DxfClass('RASTERVARIABLES','AcDbRasterVariables','App'),{IsEntity:true}):new DxfClass('CUSTOM','AcDbRasterVariables','App');doc.Classes.Add(definition);const layouts=doc.Layouts.Count,s=new MemoryStream(),sentinel=Uint8Array.of(7,8,9);try{s.Write(sentinel);const position=s.Position;if(GetTypedIOConfiguration()==='Debug')Throws(n===2?ArgumentException:InvalidDataException,()=>doc.Save(s,b));else Check(!doc.Save(s,b),'Conflicting CLASS allowed invalid export.');Equal(sentinel,s.ToArray(),'CLASS preflight wrote partial data.');Equal(position,s.Position,'CLASS preflight changed stream position');Equal(layouts,doc.Layouts.Count,'CLASS preflight changed layouts');Check(definition===OleSingle(doc.Classes),'CLASS preflight mutated source collection.');Check(s.CanWrite,'CLASS preflight closed caller stream.');}finally{s.Dispose();}}
