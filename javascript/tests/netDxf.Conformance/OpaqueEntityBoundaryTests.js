// Port of pinned OpaqueEntityBoundaryTests.cs. The separate graph module is not
// registered here; its outstanding identities stay in the original-test backlog.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfVersion, DxfRawDocument, DxfTag, MemoryStream, DxfDictionary,
  AciColor, Layer, Linetype, Lineweight, Transparency, EntityShadowMode, XDataRecord,
  XDataCode, Block, Insert } from '../../node-entry.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { OleSingle } from './Ole2FrameTests.js';
import { CompatibilityState } from './VersionCompatibilityTests.js';
import { OpaqueFixture, OpaqueName, OpaqueLoad, OpaqueSameTags } from './OpaqueEntityTests.js';
const A=Array.from,T=(c,v)=>new DxfTag(c,v);
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
export function RegisterOpaqueEntityBoundaryTests() {
  for(const v of SupportedVersions)for(const b of [false,true]) {
    for(const f of ['aci','true-color','layer','linetype','weight','scale','visibility','transparency','color-name','shadow','graphics','graphics-empty','graphics-null','xdata','app-rename','layer-rename'])
      Run(`opaque-entity/field/${f}/${VersionName(v)}/${BooleanName(b)}`,()=>OpaqueField(v,b,f));
    for(const f of ['class-change','class-replace','class-remove','profile','reactor','extension','weight','scale','xdata-target'])
      Run(`opaque-entity/preflight/${f}/${VersionName(v)}/${BooleanName(b)}`,()=>OpaquePreflight(v,b,f));
  }
  for(const b of [false,true]) {
    for(const d of ['common-graphics-empty','common-graphics-count','control-value','control-depth','space-mismatch','layout-mismatch','actual-layer-missing','target-duplicate','self-owner','self-pointer','null-pointer','no-class','acds-data'])
      Run(`opaque-entity/boundary/${d}/${BooleanName(b)}`,()=>OpaqueBoundary(d,b));
    for(const [name,fn]of [['comments',OpaqueComments],['text-transport',OpaqueTextTransport],['tag-budget',OpaqueTagBudget],['block-atomicity',OpaqueBlockAtomicity],['class-retry',OpaqueClassRetry]])
      Run(`opaque-entity/${name}/${BooleanName(b)}`,()=>fn(b));
  }
}
export function OpaqueModify(raw,edit) {
  const record=OleSingle(A(OleSingle(A(raw.Sections).filter(s=>s.Name==='ENTITIES')).Records).filter(r=>r.Name===OpaqueName)),packet=A(record.Tags);edit(packet);
  return DxfRawDocument.Create([...A(raw.Tags).slice(0,record.StartTagIndex),...packet,...A(raw.Tags).slice(record.EndTagIndex)]);
}
const body=tags=>{tags=A(tags);const start=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbQualifiedFutureCurve');let end=tags.findIndex((t,i)=>i>=start&&t.Code===1001);return tags.slice(start,end<0?undefined:end);};
function OpaqueField(version,binary,field) {
  const document=OpaqueLoad(OpaqueFixture(version),binary),entity=OleSingle(document.Entities.OpaqueEntities),original=A(entity.SourceTags);
  switch(field) {
    case 'aci':entity.Color=new AciColor(17);break;
    case 'true-color':entity.Color=new AciColor(25,50,75);break;
    case 'layer':entity.Layer=new Layer('changed-layer');break;
    case 'linetype':entity.Linetype=new Linetype('changed-line');break;
    case 'weight':entity.Lineweight=Lineweight.W70;break;
    case 'scale':entity.LinetypeScale=4.125;break;
    case 'visibility':entity.IsVisible=false;break;
    case 'transparency':entity.Transparency=new Transparency(42);break;
    case 'color-name':entity.ColorName='Book$Blue';break;
    case 'shadow':entity.ShadowMode=EntityShadowMode.Ignore;break;
    case 'graphics':entity.ProxyGraphics=Uint8Array.from({length:300},(_,i)=>i&255);break;
    case 'graphics-empty':entity.ProxyGraphics=new Uint8Array();break;
    case 'graphics-null':entity.ProxyGraphics=new Uint8Array([1,2]);entity.ClearProxyGraphics();break;
    case 'xdata':entity.XData.get_Item('OPAQUE_TEST').XDataRecord.Add(new XDataRecord(XDataCode.String,String.raw`literal \U+0041 π`));break;
    case 'app-rename':document.ApplicationRegistries.get_Item('OPAQUE_TEST').Name='RENAMED_APP';break;
    case 'layer-rename':entity.Layer=new Layer('before-rename');entity.Layer.Name='after-rename';break;
  }
  const supported=(field!=='color-name'||version>=DxfVersion.AutoCad2004)&&(field!=='shadow'||version>=DxfVersion.AutoCad2007);
  if(!supported){OpaqueExpectPreflight(document,!binary,field);return;}
  const output=new MemoryStream();
  try{Check(document.Save(output,!binary),'Individual common edit save');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Individual common edit reload');const other=OleSingle(loaded.Entities.OpaqueEntities);
    Equal(entity.Color.Index,other.Color.Index,'ACI projection');Equal(entity.Color.UseTrueColor,other.Color.UseTrueColor,'True-color presence');if(entity.Color.UseTrueColor)Equal(AciColor.ToTrueColor(entity.Color),AciColor.ToTrueColor(other.Color),'True-color value');
    for(const [expected,actual,name]of [[entity.Layer.Name,other.Layer.Name,'Layer'],[entity.Linetype.Name,other.Linetype.Name,'Linetype'],[entity.Lineweight,other.Lineweight,'Lineweight'],[entity.LinetypeScale,other.LinetypeScale,'Scale'],[entity.IsVisible,other.IsVisible,'Visibility'],[entity.Transparency.Value,other.Transparency.Value,'Transparency'],[entity.ColorName,other.ColorName,'Color-name'],[entity.ShadowMode,other.ShadowMode,'Shadow']])Equal(expected,actual,name+' value');
    Equal(entity.ProxyGraphics,other.ProxyGraphics,'Proxy-cache value');Equal(OleSingle(entity.XData.Values).ApplicationRegistry.Name,OleSingle(other.XData.Values).ApplicationRegistry.Name,'XData app name');Equal(A(OleSingle(entity.XData.Values).XDataRecord).at(-1).Value,A(OleSingle(other.XData.Values).XDataRecord).at(-1).Value,'XData final record');
    OpaqueSameTags(original,entity.SourceTags,'Immutable original snapshot after edit');OpaqueSameTags(body(original),body(other.SourceTags),'Private payload after common edit');
    const bytes=A(entity.SourceTags).find(t=>t.Code===310).Value;bytes[0]^=255;OpaqueSameTags(original,entity.SourceTags,'Binary snapshot getter isolation');
  }finally{output.Dispose();}
}
export function OpaqueExpectPreflight(document,binary,label) {
  const state=new CompatibilityState(document),sentinel=new Uint8Array([13,37,42,91]),output=new MemoryStream();
  try{output.Write(sentinel);output.Position=2;let saved;try{saved=document.Save(output,binary);}catch{saved=false;}
    Check(!saved,'Expected opaque refusal: '+label);Equal(sentinel,output.ToArray(),'Failed stream save bytes');Equal(2,output.Position,'Failed stream save position');state.CheckUnchanged();
    fs.mkdirSync(artifactDirectory,{recursive:true});const file=path.join(artifactDirectory,`opaque-sentinel-${label}-${BooleanName(binary)}.bin`);fs.writeFileSync(file,sentinel);
    const name=document.Name,working=document.SupportFolders.WorkingFolder;try{saved=document.Save(file,binary);}catch{saved=false;}
    Check(!saved,'Failed opaque file save accepted');Equal(sentinel,new Uint8Array(fs.readFileSync(file)),'Failed opaque file save changed destination');Equal(name,document.Name,'Failed file save name');Equal(working,document.SupportFolders.WorkingFolder,'Failed file save folder');state.CheckUnchanged();
  }finally{output.Dispose();}
}
function OpaquePreflight(version,binary,field) {
  const document=OpaqueLoad(OpaqueFixture(version),binary),entity=OleSingle(document.Entities.OpaqueEntities);
  switch(field){
    case 'class-change':document.Classes.get_Item(OpaqueName).ApplicationName='changed';break;
    case 'class-replace':{const definition=document.Classes.get_Item(OpaqueName);document.Classes.set_Item(document.Classes.IndexOf(definition),definition.Clone());break;}
    case 'class-remove':document.Classes.Remove(OpaqueName);break;
    case 'profile':document.DrawingVariables.AcadVer=SupportedVersions.find(v=>v!==version);break;
    case 'reactor':entity.PersistentReactors.Add(OleSingle(document.Entities.Lines));break;
    case 'extension':document.Objects.SetExtensionDictionary(entity,new DxfDictionary());break;
    case 'weight':entity.Lineweight=3;break;
    case 'scale':entity.LinetypeScale=NaN;break;
    case 'xdata-target':OleSingle(entity.XData.Values).XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,'D00D'));break;
  }
  OpaqueExpectPreflight(document,binary,field);
}
function OpaqueBoundary(defect,binary) {
  let raw=OpaqueModify(OpaqueFixture(DxfVersion.AutoCad2018),p=>{
    const body=p.findIndex(t=>t.Code===100&&t.Value==='AcDbQualifiedFutureCurve');
    if(defect==='common-graphics-empty')p.splice(body,0,T(310,new Uint8Array()));
    if(defect==='common-graphics-count')p.splice(body,0,T(92,1),T(310,new Uint8Array([1,2])));
    if(defect==='control-value')p.splice(5,0,T(102,'unframed'));
    if(defect==='control-depth')p.splice(body,0,...Array(33).fill(T(102,'{TOO_DEEP')),...Array(33).fill(T(102,'}')));
    if(defect==='space-mismatch')p.splice(body,0,T(67,1));if(defect==='layout-mismatch')p.splice(body,0,T(410,'AbsentLayout'));
    if(defect==='self-owner')p.splice(body+1,0,T(360,'F001'));if(defect==='self-pointer')p.splice(body+1,0,T(340,'F001'));if(defect==='null-pointer')p.splice(body+1,0,T(340,'0'));
  });const tags=A(raw.Tags),records=name=>A(OleSingle(A(raw.Sections).filter(s=>s.Name===name)).Records);
  if(defect==='actual-layer-missing'){const r=OleSingle(records('TABLES').filter(r=>r.Name==='LAYER'&&A(r.Tags).some(t=>t.Code===2&&t.Value==='0')));tags.splice(r.StartTagIndex,r.Tags.Count);}
  if(defect==='target-duplicate'){const r=OleSingle(records('ENTITIES').filter(r=>r.Name==='LINE'));tags.splice(r.EndTagIndex,0,...r.Tags);}
  if(defect==='no-class'){const r=OleSingle(records('CLASSES').filter(r=>A(r.Tags).some(t=>t.Code===1&&t.Value===OpaqueName)));tags.splice(r.StartTagIndex,r.Tags.Count);}
  if(defect==='acds-data')tags.splice(tags.length-1,0,T(0,'SECTION'),T(2,'ACDSDATA'),T(0,'ACDSSCHEMA'),T(90,1),T(0,'ENDSEC'));
  raw=DxfRawDocument.Create(tags);const input=new MemoryStream();try{raw.Save(input,binary);input.Position=0;const allowed=['self-pointer','null-pointer','no-class'].includes(defect);let document=null,error=null;
    try{document=DxfDocument.Load(input);}catch(e){error=e;}
    if(!allowed){Check(document===null,'Malformed or unsupported boundary accepted: '+defect);return;}
    Check(document!==null,'Valid opaque boundary rejected: '+defect+' '+error);const output=new MemoryStream();try{Check(document.Save(output,!binary),'Valid boundary save');}finally{output.Dispose();}
  }finally{input.Dispose();}
}
function OpaqueComments(binary) {
  const raw=OpaqueModify(OpaqueFixture(DxfVersion.AutoCad2018),p=>{p.splice(3,0,T(999,'source header comment'));p.splice(p.length-4,0,T(999,'private comment'));}),document=OpaqueLoad(raw,false);
  if(binary){OpaqueExpectPreflight(document,true,'comments');return;}
  const output=new MemoryStream();try{Check(document.Save(output),'ASCII comment save');output.Position=0;const other=OleSingle(A(OleSingle(A(DxfRawDocument.Load(output).Sections).filter(s=>s.Name==='ENTITIES')).Records).filter(r=>r.Name===OpaqueName));OpaqueSameTags(OleSingle(document.Entities.OpaqueEntities).SourceTags,other.Tags,'ASCII comments preserved');}finally{output.Dispose();}
}
function OpaqueTextTransport(binary) {
  const raw=OpaqueModify(OpaqueFixture(DxfVersion.AutoCad2018),p=>p.splice(p.length-4,0,T(300,'private\r\ntext'))),document=OpaqueLoad(raw,true);
  if(!binary){OpaqueExpectPreflight(document,false,'private-crlf');return;}
  const output=new MemoryStream();try{Check(document.Save(output,true),'Binary CRLF packet save');output.Position=0;const other=DxfDocument.Load(output);Check(other!==null,'Binary CRLF reload');OpaqueSameTags(OleSingle(document.Entities.OpaqueEntities).SourceTags,OleSingle(other.Entities.OpaqueEntities).SourceTags,'Binary CRLF unchanged');}finally{output.Dispose();}
}
function OpaqueTagBudget(binary) {
  const document=OpaqueLoad(OpaqueFixture(DxfVersion.AutoCad2018),binary),entity=OleSingle(document.Entities.OpaqueEntities),count=65536-entity.SourceTags.Count,data=OleSingle(entity.XData.Values);
  for(let i=0;i<count;i++)data.XDataRecord.Add(new XDataRecord(XDataCode.Int16,1));
  const output=new MemoryStream();try{Check(document.Save(output,binary),'Exact tag boundary save');output.Position=0;Check(DxfDocument.Load(output)!==null,'Exact tag boundary reload');}finally{output.Dispose();}
  data.XDataRecord.Add(new XDataRecord(XDataCode.Int16,1));OpaqueExpectPreflight(document,binary,'tag-budget');data.XDataRecord.RemoveAt(data.XDataRecord.Count-1);
  const retry=new MemoryStream();try{Check(document.Save(retry,binary),'Budget repair retry');}finally{retry.Dispose();}
}
function OpaqueBlockAtomicity(binary) {
  const document=OpaqueLoad(OpaqueFixture(DxfVersion.AutoCad2018),binary),entity=OleSingle(document.Entities.OpaqueEntities),state=new CompatibilityState(document);
  for(const action of [()=>Block.Create(document,'copy'),()=>entity.Owner.Save(path.join(artifactDirectory,'must-not-exist-opaque-block.dxf'),DxfVersion.AutoCad2018,binary),()=>new Insert(entity.Owner).Explode(),()=>new Insert(entity.Owner).ExplodeCell(0,0),()=>A(new Insert(entity.Owner).ExplodeEnumerable())]){
    let rejected=false;try{action();}catch(e){if(!(e instanceof NotSupportedException))throw e;rejected=true;}Check(rejected,'Block/insert geometry path must reject');state.CheckUnchanged();
  }
}
function OpaqueClassRetry(binary) {
  const document=OpaqueLoad(OpaqueFixture(DxfVersion.AutoCad2018),binary),definition=document.Classes.get_Item(OpaqueName),application=definition.ApplicationName;definition.ApplicationName='changed';OpaqueExpectPreflight(document,binary,'class-retry');definition.ApplicationName=application;
  const output=new MemoryStream();try{Check(document.Save(output,binary),'Repair only CLASS state permits retry');}finally{output.Dispose();}
}
