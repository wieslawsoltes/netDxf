// Port of pinned OpaqueEntityTests.cs. The fixture is a declared test schema,
// not an independent native/private application-produced entity.
import { DxfDocument, DxfVersion, DxfRawDocument, DxfClass, DxfTag, MemoryStream,
  Line, Vector3, Matrix3, Matrix4, Block, Layout, Layer, ApplicationRegistry,
  AciColor, Lineweight, XDataRecord, XDataCode } from '../../node-entry.js';
import { NotSupportedException, InvalidOperationException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
import { CompatibilityState, CompatibilityAnalyze } from './VersionCompatibilityTests.js';
const A=Array.from, T=(code,value)=>new DxfTag(code,value);
export const OpaqueName='QUALIFIED_FUTURE_CURVE';
export function RegisterOpaqueEntityTests() {
  for(const v of SupportedVersions)for(const i of [false,true]) {
    for(const o of [false,true])Run(`opaque-entity/packet/${VersionName(v)}/${BooleanName(i)}/${BooleanName(o)}`,()=>OpaquePacket(v,i,o));
    Run(`opaque-entity/lifecycle/${VersionName(v)}/${BooleanName(i)}`,()=>OpaqueLifecycle(v,i));
    Run(`opaque-entity/common-edits/${VersionName(v)}/${BooleanName(i)}`,()=>OpaqueCommonEdits(v,i));
  }
  for(const b of [false,true])for(const d of ['missing-owner','missing-handle','duplicate-handle','duplicate-owner','missing-layer','missing-target','missing-appid','duplicate-layer','duplicate-common','children','embedded','proxy-name','proxy-subclass','vertex-name','vertex-subclass','attribute-subclass','block-subclass','dimension-subclass','modeler-subclass','surface-subclass','bad-scale','bad-xdata','class-field','class-duplicate','class-object'])
    Run(`opaque-entity/reject/${d}/${BooleanName(b)}`,()=>OpaqueRejected(d,b));
}
export function OpaqueFixture(version,defect='') {
  const document=new DxfDocument(version),line=new Line(Vector3.Zero,Vector3.UnitX);
  if(defect==='unused-block'){const block=new Block('OPAQUE_OWNER');block.Entities.Add(line);document.Blocks.Add(block);}
  else {if(defect==='paper'){document.Layouts.Add(new Layout('OpaquePaper'));document.Entities.ActiveLayout='OpaquePaper';}document.Entities.Add(line);}
  if(defect==='xdata-layer')document.Layers.Add(new Layer('SOURCE_XDATA_LAYER'));
  document.ApplicationRegistries.Add(new ApplicationRegistry('OPAQUE_TEST'));
  const definition=new DxfClass(OpaqueName,'AcDbQualifiedFutureCurve','declared-schema');
  definition.IsEntity=defect!=='class-object';definition.InstanceCount=version===DxfVersion.AutoCad2000?null:1;document.Classes.Add(definition);
  const original=new MemoryStream();
  try {
    Check(document.Save(original),'Opaque scaffold save');original.Position=0;
    const raw=DxfRawDocument.Create(A(DxfRawDocument.Load(original).Tags).filter(t=>t.Code!==999));
    const tags=[
      [0,defect==='proxy-name'?'ACAD_PROXY_ENTITY':defect==='vertex-name'?'VERTEX':OpaqueName],
      [5,'F001'],[330,line.Owner.Record.Handle],[102,'{UNINTERPRETED'],[5,'F123'],[330,'DEAD'],[66,1],[101,'Embedded Object'],[102,'}'],
      [100,'AcDbEntity'],[8,'0'],[6,'BYLAYER'],[62,3],[48,1.25],[300,'unknown common field'],
      [100,'AcDbQualifiedFutureCurve'],[10,9.5],[20,-4.5],[30,2],[5,'BEEF'],[340,line.Handle],[320,'DEAD'],[329,'BEEF'],
      [102,'{PRIVATE'],[360,'DEAD'],[102,'{NESTED'],[100,'AcDbVertex'],[102,'}'],[102,'}'],
      [310,new Uint8Array([0,255,37,10,13])],[300,String.raw`private \U+0041 text`],[62,191],[48,-8],
      [1001,'OPAQUE_TEST'],[1000,'source xdata'],[1005,line.Handle],[1070,4]
    ].map(([c,v])=>T(c,v));
    if(defect==='paper')tags.splice(14,0,T(67,1));if(defect==='xdata-layer')tags.push(T(1003,'SOURCE_XDATA_LAYER'));
    const first=code=>tags.findIndex(t=>t.Code===code),body=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbQualifiedFutureCurve');
    if(defect==='missing-owner')tags.splice(first(330),1);if(defect==='missing-handle')tags.splice(first(5),1);
    if(defect==='duplicate-owner')tags.splice(3,0,T(330,line.Owner.Record.Handle));if(defect==='duplicate-handle')tags.splice(2,0,T(5,'F001'));
    if(defect==='missing-layer')tags[first(8)]=T(8,'ABSENT_LAYER');if(defect==='missing-target')tags[first(340)]=T(340,'D00D');
    if(defect==='missing-appid')tags[first(1001)]=T(1001,'ABSENT_APPID');
    if(defect==='duplicate-layer')tags.splice(body,0,T(8,'0'));if(defect==='duplicate-common')tags.splice(body,0,T(100,'AcDbEntity'));
    if(defect==='children')tags.splice(body,0,T(66,1));if(defect==='embedded')tags.splice(body,0,T(101,'Embedded Object'));
    const subclasses={'proxy-subclass':'AcDbProxyEntity','vertex-subclass':'AcDbVertex','attribute-subclass':'AcDbAttribute','block-subclass':'AcDbBlockReference','dimension-subclass':'AcDbDimension','modeler-subclass':'AcDbModelerGeometry','surface-subclass':'AcDbSurface'};
    if(Object.hasOwn(subclasses,defect))tags[body]=T(100,subclasses[defect]);
    if(defect==='bad-scale')tags[first(48)]=T(48,0);if(defect==='bad-xdata')tags.push(T(1002,'{'));
    const all=A(raw.Tags),at=OleSingle(A(raw.Sections).flatMap(s=>A(s.Records)).filter(r=>r.Name==='LINE'&&A(r.Tags).some(t=>t.Code===5&&t.Value===line.Handle))).EndTagIndex;
    all.splice(at,0,...tags);
    if(['class-field','class-duplicate'].includes(defect)) {
      const modified=DxfRawDocument.Create(all),declaration=OleSingle(A(OleSingle(A(modified.Sections).filter(s=>s.Name==='CLASSES')).Records).filter(r=>A(r.Tags).some(t=>t.Code===1&&t.Value===OpaqueName)));
      all.splice(declaration.EndTagIndex,0,defect==='class-field'?T(301,'discarded field'):T(90,0));
    }
    return DxfRawDocument.Create(all);
  }finally{original.Dispose();}
}
export function OpaqueLoad(raw,binary) {
  const input=new MemoryStream();try{raw.Save(input,binary);input.Position=0;const doc=DxfDocument.Load(input);Check(doc!==null,'Declared opaque fixture failed to load');return doc;}finally{input.Dispose();}
}
export function OpaqueSameTags(expected,actual,context) {
  expected=A(expected);actual=A(actual);Equal(expected.length,actual.length,context+' count');
  for(let i=0;i<expected.length;i++){Equal(expected[i].Code,actual[i].Code,context+' code '+i);Equal(expected[i].Value,actual[i].Value,context+' value '+i);}
}
function OpaquePacket(version,input,output) {
  const source=OpaqueFixture(version),ss=new MemoryStream();
  try{source.Save(ss,input);OleWriteArtifact(`opaque-entity-source-${VersionName(version)}-${BooleanName(input)}.dxf`,ss.ToArray());}finally{ss.Dispose();}
  const document=OpaqueLoad(source,input),entity=OleSingle(document.Entities.OpaqueEntities),original=OleSingle(A(OleSingle(A(source.Sections).filter(s=>s.Name==='ENTITIES')).Records).filter(r=>r.Name===OpaqueName));
  OpaqueSameTags(original.Tags,entity.SourceTags,'Original source snapshot');Check(document.GetObjectByHandle('F001')===entity,'Actual registered unknown identity');Check(entity.References.Contains(OleSingle(document.Entities.Lines)),'Exact known source pointer');Equal(Vector3.UnitZ,entity.Normal,'Normal compatibility placeholder');
  const stream=new MemoryStream();try{Check(document.Save(stream,output),'Unknown packet save');OleWriteArtifact(`opaque-entity-packet-${VersionName(version)}-${BooleanName(input)}-${BooleanName(output)}.dxf`,stream.ToArray());stream.Position=0;
    const written=OleSingle(A(OleSingle(A(DxfRawDocument.Load(stream).Sections).filter(s=>s.Name==='ENTITIES')).Records).filter(r=>r.Name===OpaqueName));OpaqueSameTags(original.Tags,written.Tags,'Unchanged complete output packet');stream.Position=0;
    const reloaded=DxfDocument.Load(stream);Check(reloaded!==null,'Opaque packet reload');OpaqueSameTags(entity.SourceTags,OleSingle(reloaded.Entities.OpaqueEntities).SourceTags,'Second typed load');
  }finally{stream.Dispose();}
}
function OpaqueLifecycle(version,binary) {
  const document=OpaqueLoad(OpaqueFixture(version),binary),entity=OleSingle(document.Entities.OpaqueEntities),state=new CompatibilityState(document);
  for(const action of [()=>entity.Clone(),()=>entity.TransformBy(Matrix3.Identity,Vector3.UnitX),()=>entity.TransformBy(new Matrix4(2,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1)),()=>{entity.Normal=Vector3.UnitX;},()=>entity.Owner.Clone('opaque-copy')]){
    let rejected=false;try{action();}catch(e){if(!(e instanceof NotSupportedException))throw e;rejected=true;}Check(rejected,'Unknown geometry or cloning must reject');state.CheckUnchanged();
  }
  entity.TransformBy(Matrix3.Identity,Vector3.Zero);entity.TransformBy(Matrix4.Identity);state.CheckUnchanged();
  Check(!document.Entities.Remove(OleSingle(document.Entities.Lines)),'Opaque pointer prevents dependency removal');
  const target=SupportedVersions.find(v=>v!==version),report=CompatibilityAnalyze(document,target);
  Check(A(report.Diagnostics).some(t=>t.Code==='STORED_SOURCE_PROFILE'&&t.SourceObject===entity),'Opaque target profile diagnostic');
  document.DrawingVariables.AcadVer=target;const changed=new CompatibilityState(document),output=new MemoryStream();
  try{let saved;try{saved=document.Save(output,binary);}catch(e){if(!(e instanceof NotSupportedException))throw e;saved=false;}Check(!saved&&output.Length===0,'Profile rejection before output');changed.CheckUnchanged();}finally{output.Dispose();}
  document.DrawingVariables.AcadVer=version;Check(document.Entities.Remove(entity),'Explicit unreferenced unknown removal');Equal('F001',entity.SourceHandle,'Retired source identity snapshot');
  let rejected=false;try{document.Entities.Add(entity);}catch(e){if(!(e instanceof InvalidOperationException))throw e;rejected=true;}Check(rejected,'Retired unknown cannot reattach');
  Check(!A(document.AnalyzeVersionCompatibility(target).Diagnostics).some(t=>t.SourceObject===entity),'Fresh report omits retired unknown');
}
function OpaqueCommonEdits(version,binary) {
  const document=OpaqueLoad(OpaqueFixture(version),binary),entity=OleSingle(document.Entities.OpaqueEntities),snapshot=A(entity.SourceTags);
  entity.Color=AciColor.Red;entity.LinetypeScale=4;entity.IsVisible=false;entity.Layer=new Layer('OPAQUE_EDIT');entity.Lineweight=Lineweight.W50;
  entity.XData.get_Item('OPAQUE_TEST').XDataRecord.Add(new XDataRecord(XDataCode.String,'edited'));
  const stream=new MemoryStream();try{Check(document.Save(stream,!binary),'Common appearance edit save');stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'Common appearance edit reload');const other=OleSingle(loaded.Entities.OpaqueEntities);
    Equal(1,other.Color.Index,'Edited ACI');Equal(4,other.LinetypeScale,'Edited scale');Check(!other.IsVisible,'Edited visibility');Equal('OPAQUE_EDIT',other.Layer.Name,'Edited registered layer');Equal(Lineweight.W50,other.Lineweight,'Edited lineweight');
    OpaqueSameTags(snapshot,entity.SourceTags,'Edit preserves original snapshot');
    const payload=values=>{const t=A(values),body=t.findIndex(v=>v.Code===100&&v.Value==='AcDbQualifiedFutureCurve'),end=t.findIndex((v,i)=>i>=body&&v.Code===1001);return t.slice(body,end<0?undefined:end);};
    OpaqueSameTags(payload(snapshot),payload(other.SourceTags),'Common edit preserves private body');
  }finally{stream.Dispose();}
}
function OpaqueRejected(defect,binary) {
  const raw=OpaqueFixture(DxfVersion.AutoCad2018,defect),input=new MemoryStream();
  try{raw.Save(input,binary);input.Position=0;let rejected=false;try{rejected=DxfDocument.Load(input)===null;}catch{rejected=true;}Check(rejected,'Unsupported opaque envelope must reject: '+defect);}finally{input.Dispose();}
}
