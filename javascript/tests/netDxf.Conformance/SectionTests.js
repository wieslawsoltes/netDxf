// Port of pinned SectionTests.cs; original identities and assertions retained.
import { Section, Vector3, Matrix3, Matrix4, DxfDocument, DxfVersion, MemoryStream, DxfRawDocument, DxfTag, DxfSectionSettings } from '../../node-entry.js';
import { Run, Equal, Check, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
function SectionReject(action){let rejected=false;try{action();}catch{rejected=true;}Check(rejected,'SECTION input or mutation was not rejected');}
export function SectionExample(codeName='SECTION'){
  const section=new Section(codeName);Object.assign(section,{Name:String.raw`東京 Literal\U+0041`,State:4,Flags:17,VerticalDirection:new Vector3(1,2,3),TopHeight:5.25,BottomHeight:-15.5,IndicatorTransparency:70,StoredIndicatorColor:256,StoredNativeIndicatorColor:9,IndicatorColorName:'Book$Color'});
  section.Vertices.Add(new Vector3(1,2,3));section.Vertices.Add(new Vector3(-4,5,6));section.BackLineVertices.Add(new Vector3(9,8,7));return section;
}
function RegisterExistingSectionTests(){Run('section/api/stored-values',SectionValues);for(const version of SupportedVersions.filter(v=>v<DxfVersion.AutoCad2007))Run('section/unsupported-profile/'+VersionName(version),()=>{const doc=new DxfDocument(version);SectionReject(()=>doc.Entities.Add(new Section()));});}
export function SectionValues(){
  const section=SectionExample(),clone=section.Clone();Equal(section.Name,clone.Name,'Section clone text');Equal(section.VerticalDirection,clone.VerticalDirection,'Section clone independent vector');
  clone.Vertices.set_Item(0,Vector3.Zero);Check(!Vector3.Equals(section.Vertices.get_Item(0),clone.Vertices.get_Item(0)),'Section clone shared vertices');
  for(const value of [NaN,Infinity,-Infinity]){SectionReject(()=>{section.TopHeight=value;});SectionReject(()=>section.Vertices.Add(new Vector3(value,0,0)));}
  Equal(2,section.Vertices.Count,'Rejected vertex mutated collection');SectionReject(()=>{section.Name='bad\ud800';});SectionReject(()=>{section.IndicatorColorName='bad\nline';});
  SectionReject(()=>section.TransformBy(Matrix3.Identity,Vector3.UnitX));section.TransformBy(Matrix3.Identity,Vector3.Zero);section.TransformBy(Matrix4.Identity);
  const absent=new Section();Equal('SECTIONOBJECT',absent.CodeName,'Native default spelling');SectionReject(()=>new Section('section'));Check(absent.StoredIndicatorColor===null&&absent.StoredNativeIndicatorColor===null&&absent.IndicatorColorName===null,'New section invented indicator fields');
}

import fs from 'node:fs';
import path from 'node:path';
import { gunzipSync } from 'node:zlib';
import { sourceRoot } from '../../tools/dotnet.mjs';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
export function SectionBytes(doc,binary){const stream=new MemoryStream();try{Check(doc.Save(stream,binary),'SECTION save');return stream.ToArray();}finally{stream.Dispose();}}
export function SectionRaw(version){const doc=new DxfDocument(version);doc.Entities.Add(SectionExample());const stream=new MemoryStream(SectionBytes(doc,false));try{return DxfRawDocument.Load(stream);}finally{stream.Dispose();}}
export function SectionLoad(raw,binary){const stream=new MemoryStream();try{raw.WithTags([...raw.Tags].filter(t=>t.Code!==999)).Save(stream,binary);stream.Position=0;const doc=DxfDocument.Load(stream);Check(doc!==null,'SECTION load failed');return doc;}finally{stream.Dispose();}}
export function RegisterSectionTests(){
  RegisterExistingSectionTests();
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2007))for(const b of [false,true]){
    const suffix=`${VersionName(v)}/${BooleanName(b)}`;
    Run('section/authored/'+suffix,()=>SectionAuthored(v,b));Run('section/native-name/'+suffix,()=>SectionNativeName(v,b));
    for(const defect of ['count-negative','count-excessive','count-short','incomplete-vector','duplicate-state','duplicate-color','private-field','missing-subclass','unknown-subclass','missing-state','wrong-settings','private-after-xdata'])Run('section/malformed/'+suffix+'/'+defect,()=>SectionMalformed(v,b,defect));
  }
  for(const b of [false,true])Run('section/native/R2018/'+BooleanName(b),()=>SectionNative(b));
}
function SectionAuthored(v,b){
  const doc=SectionLoad(SectionRaw(v),b),section=OleSingle(doc.Entities.Sections);
  Equal('SECTION',section.CodeName,'Section documented spelling');Equal(String.raw`東京 Literal\U+0041`,section.Name,'Section Unicode and literal escape');
  Equal(256,section.StoredIndicatorColor,'Section indicator63');Equal(9,section.StoredNativeIndicatorColor,'Section indicator62');Equal('Book$Color',section.IndicatorColorName,'Section string411');Equal(2,section.Vertices.Count,'Section vertices');Equal(1,section.BackLineVertices.Count,'Section back-line vertices');Equal(new Vector3(1,2,3),section.VerticalDirection,'Section vector was normalized');Equal(-15.5,section.BottomHeight,'Section signed bottom height');
  const bytes=SectionBytes(doc,b);OleWriteArtifact(`section-authored-${VersionName(v)}-${BooleanName(b)}.dxf`,bytes);const stream=new MemoryStream(bytes);
  try{const raw=DxfRawDocument.Load(stream),record=OleSingle([...raw.Sections].flatMap(s=>[...s.Records]).filter(r=>r.Name==='SECTION'));Equal(1,[...record.Tags].filter(t=>t.Code===360&&t.Value==='0').length,'Explicit null settings presence');}finally{stream.Dispose();}
  section.StoredIndicatorColor=null;section.StoredNativeIndicatorColor=null;section.IndicatorColorName=null;
  const input=new MemoryStream(SectionBytes(doc,b));try{const cleared=OleSingle(DxfDocument.Load(input).Entities.Sections);Check(cleared.StoredIndicatorColor===null&&cleared.StoredNativeIndicatorColor===null&&cleared.IndicatorColorName===null,'Cleared color presence returned');}finally{input.Dispose();}
}
function SectionNativeName(v,b){const doc=new DxfDocument(v),section=SectionExample('SECTIONOBJECT');doc.Entities.Add(section);const bytes=SectionBytes(doc,b);OleWriteArtifact(`section-native-name-${VersionName(v)}-${BooleanName(b)}.dxf`,bytes);const input=new MemoryStream(bytes);try{const loaded=OleSingle(DxfDocument.Load(input).Entities.Sections);Equal('SECTIONOBJECT',loaded.CodeName,'Authored native spelling');Equal(section.Name,loaded.Name,'Native-name text');Equal(section.StoredNativeIndicatorColor,loaded.StoredNativeIndicatorColor,'Native-name indicator field');Equal('SECTIONOBJECT',loaded.Clone().CodeName,'Cloned native spelling');}finally{input.Dispose();}}
function SectionMalformed(v,b,defect){
  let raw=SectionRaw(v);const record=OleSingle([...raw.Sections].flatMap(s=>[...s.Records]).filter(r=>r.Name==='SECTION')),t=[...record.Tags],body=t.findIndex(x=>x.Code===100&&x.Value==='AcDbSection'),index=code=>t.findIndex((x,i)=>i>body&&x.Code===code),T=(c,x)=>new DxfTag(c,x);
  switch(defect){case 'count-negative':t[index(92)]=T(92,-1);break;case 'count-excessive':t[index(92)]=T(92,Section.MaximumVertices+1);break;case 'count-short':t[index(92)]=T(92,1);break;case 'incomplete-vector':t.splice(index(21),1);break;case 'duplicate-state':t.splice(index(90),0,T(90,1));break;case 'duplicate-color':t.splice(index(63),0,T(63,3));break;case 'private-field':t.splice(index(92),0,T(299,true));break;case 'missing-subclass':t.splice(body,1);break;case 'unknown-subclass':t[body]=T(100,'AcDbPrivateSection');break;case 'missing-state':t.splice(index(90),1);break;case 'wrong-settings':t[index(360)]=T(360,t.find(x=>x.Code===330).Value);break;case 'private-after-xdata':t.push(T(1001,'ACAD'),T(1000,'marker'),T(90,8));break;}
  raw=raw.WithRecord(record,t);SectionReject(()=>SectionLoad(raw,b));
}
function SectionNative(b){
  const bytes=gunzipSync(fs.readFileSync(path.join(sourceRoot,'tests/fixtures/section/LiveSection1.dxf.gz'))),input=new MemoryStream(new Uint8Array(bytes));
  try{const doc=DxfDocument.Load(input);Check(doc!==null,'Native section original load');const section=OleSingle(doc.Entities.Sections);Equal('SECTIONOBJECT',section.CodeName,'Native section spelling');Equal('Section Plane (1)',section.Name,'Native section name');Equal(9,section.StoredNativeIndicatorColor,'Native subclass62 color');Check(section.StoredIndicatorColor===null&&section.IndicatorColorName===null,'Native section invented color slots');Equal(188,section.ProxyGraphics.length,'Native proxy bytes');Check(section.GeometrySettings!==null&&section.GeometrySettings.Owner===section,'Native reciprocal owned settings');Equal('SECTION_SETTINGS',section.GeometrySettings.CodeName,'Native settings spelling');
  const output=SectionBytes(doc,b);OleWriteArtifact(`section-native-AutoCad2018-${BooleanName(b)}.dxf`,output);const again=new MemoryStream(output);try{const loaded=OleSingle(DxfDocument.Load(again).Entities.Sections);Equal(2,loaded.Vertices.Count,'Native reloaded vertices');Check(loaded.GeometrySettings.Owner===loaded,'Native reloaded settings owner');}finally{again.Dispose();}
  if(section.GeometrySettings instanceof DxfSectionSettings){const clone=doc.Objects.CloneSection(section,section.Owner);Equal('SECTIONOBJECT',clone.CodeName,'Native graph clone spelling');Check(clone.GeometrySettings!==section.GeometrySettings,'Native graph clone shared settings');OleWriteArtifact(`section-native-copy-AutoCad2018-${BooleanName(b)}.dxf`,SectionBytes(doc,b));doc.Objects.EraseSection(clone);OleWriteArtifact(`section-native-erased-AutoCad2018-${BooleanName(b)}.dxf`,SectionBytes(doc,b));}
  }finally{input.Dispose();}
}
