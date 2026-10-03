// Port of pinned PrivateXRecordTests.cs; preserve opaque/public boundaries and assertions.
import { DxfDocument, DxfVersion, DxfDictionary, DxfXRecord, DxfPlaceholder, DxfDictionaryVariable,
  DxfOpaqueObject, DxfRawDocument, DxfTag, MemoryStream, ApplicationRegistry, XData, XDataRecord, XDataCode } from '../../node-entry.js';
import { ArgumentException, FormatException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { CompositeTableRecord, CompositeTableSource, TableContentSourceBytes } from './CompositeTableOwnershipTests.js';
import { OwnershipTagValues } from './SunTests.js';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
const A=Array.from;
export function RegisterPrivateXRecordTests() {
  for(const v of SupportedVersions)for(const b of [false,true])Run(`private-xrecord/graph/${VersionName(v)}/${BooleanName(b)}`,()=>PrivateXRecordGraph(v,b));
  for(const b of [false,true]) {
    Run('private-xrecord/native/'+BooleanName(b),()=>PrivateXRecordNative(b));
    for(const v of ['valid-xdata','nested-appid','private-subclass','malformed-xdata','missing-subclass'])
      Run(`private-xrecord/boundary/${v}/${BooleanName(b)}`,()=>PrivateXRecordBoundary(v,b));
  }
  Run('private-xrecord/authored-boundary',()=>{
    const record=new DxfXRecord();
    for(const [c,v]of [[1000,'private'],[1001,'APP'],[1004,new Uint8Array([1,2])],[1005,'0'],[1070,1],[1071,1]])
      Throws(ArgumentException,()=>record.Data.Add(new DxfTag(c,v)));
    Equal(0,record.Data.Count,'rejected authored data changed record');
  });
}
function PrivateXRecordFixture(version,variant) {
  const doc=new DxfDocument(version),parent=new DxfDictionary(),record=new DxfXRecord();
  doc.Objects.Root.Add('PRIVATE_OWNER',parent);parent.Add('PRIVATE_RECORD',record);
  const reactor=new DxfPlaceholder();doc.Objects.Root.Add('PRIVATE_REACTOR',reactor);record.PersistentReactors.Add(reactor);
  const extension=new DxfDictionary();doc.Objects.SetExtensionDictionary(record,extension);const note=new DxfDictionaryVariable();note.Value='retained extension';extension.Add('NOTE',note);
  const app=doc.ApplicationRegistries.Add(new ApplicationRegistry('PRIVATE_XDATA')),xdata=new XData(app);
  xdata.XDataRecord.Add(new XDataRecord(XDataCode.String,'actual XData'));xdata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,reactor.Handle));record.XData.Add(xdata);
  const bytes=new MemoryStream();
  try {
    Check(doc.Save(bytes),'private XRECORD fixture save');bytes.Position=0;const raw=DxfRawDocument.Load(bytes);
    const packet=OleSingle(A(OleSingle(A(raw.Sections).filter(s=>s.Name==='OBJECTS')).Records).filter(r=>A(r.Tags).some(t=>t.Code===5&&t.Value===record.Handle))),tags=A(packet.Tags);
    const start=tags.findIndex(t=>t.Code===100),end=tags.findIndex(t=>t.Code===1001),T=(c,v)=>new DxfTag(c,v);
    let body=[[100,'AcDbXrecord'],[280,1],[1070,0],[1070,1],[1070,1]].map(([c,v])=>T(c,v));
    if(variant==='valid-xdata')body=[[100,'AcDbXrecord'],[280,1],[1,'ordinary data'],[369,'0']].map(([c,v])=>T(c,v));
    if(variant==='nested-appid')body=[[100,'AcDbXrecord'],[280,1],[102,'{PRIVATE'],[102,'{NESTED'],[1001,'not a registry'],[1000,'nested private'],[102,'}'],[102,'}'],[1,'after private group']].map(([c,v])=>T(c,v));
    if(variant==='private-subclass')body.splice(2,0,T(100,'PrivateData'));
    if(variant==='missing-subclass')body.shift();
    const tail=tags.slice(end);if(variant==='malformed-xdata')tail.push(T(1,'not XData'));
    return raw.WithRecord(packet,[...tags.slice(0,start),...body,...tail]);
  }finally{bytes.Dispose();}
}
function PrivateXRecordLoad(raw,binary) {
  const bytes=new MemoryStream();
  try{raw.WithTags(A(raw.Tags).filter(t=>t.Code!==999)).Save(bytes,binary);bytes.Position=0;return DxfDocument.Load(bytes);}
  finally{bytes.Dispose();}
}
function PrivateXRecordGraph(version,binary) {
  const raw=PrivateXRecordFixture(version,'direct'),doc=PrivateXRecordLoad(raw,binary);
  Check(doc!==null,'Private XRECORD load');const parent=doc.Objects.Root.get_Item('PRIVATE_OWNER'),record=parent.get_Item('PRIVATE_RECORD');
  Check(record instanceof DxfOpaqueObject,'private native-shaped XRECORD acquired an authored projection');
  Equal('XRECORD',record.CodeName,'private record class');Check(doc.GetObjectByHandle(record.Handle)===record,'private source identity');Check(record.Owner===parent,'private owning dictionary');
  Equal(1,record.PersistentReactors.Count,'persistent reactors');Equal('retained extension',record.ExtensionDictionary.get_Item('NOTE').Value,'private extension');
  Equal('actual XData',record.XData.get_Item('PRIVATE_XDATA').XDataRecord.get_Item(0).Value,'real XData boundary');Equal(0,doc.Objects.Validate().Count,'private graph validation');
  const seed=doc.NumHandles,count=doc.Objects.Items.Count;
  Throws(NotSupportedException,()=>record.Tags.Clear());Throws(NotSupportedException,()=>doc.Objects.EraseOwnedTree(record));Throws(NotSupportedException,()=>doc.Objects.EraseOwnedTree(parent));
  const target=new DxfDocument(version),before=target.Objects.Items.Count,allocation=target.NumHandles;
  Throws(NotSupportedException,()=>target.Objects.Clone(parent,target.Objects.Root,'COPY'));Throws(ArgumentException,()=>target.Objects.Root.Add('FOREIGN',record));
  Equal(before,target.Objects.Items.Count,'private clone registration');Equal(allocation,target.NumHandles,'private clone allocation');Equal(seed,doc.NumHandles,'private source allocation');Equal(count,doc.Objects.Items.Count,'private source objects');
  const output=new MemoryStream();try {
    Check(doc.Save(output,binary),'private graph save');OleWriteArtifact(`private-xrecord-${VersionName(version)}-${BooleanName(binary)}.dxf`,output.ToArray());output.Position=0;
    const saved=DxfRawDocument.Load(output);Equal(OwnershipTagValues(CompositeTableRecord(raw,record.Handle).Tags),OwnershipTagValues(CompositeTableRecord(saved,record.Handle).Tags),'complete private packet/common metadata differs');
    output.Position=0;Check(DxfDocument.Load(output).GetObjectByHandle(record.Handle) instanceof DxfOpaqueObject,'private reload classification');
  }finally{output.Dispose();}
}
function PrivateXRecordBoundary(variant,binary) {
  const raw=PrivateXRecordFixture(DxfVersion.AutoCad2018,variant);
  if(['malformed-xdata','missing-subclass'].includes(variant)) {
    let rejected=false;try{rejected=PrivateXRecordLoad(raw,binary)===null;}catch(e){if(!(e instanceof FormatException))throw e;rejected=true;}
    Check(rejected,'invalid XRECORD boundary admitted');return;
  }
  const doc=PrivateXRecordLoad(raw,binary);Check(doc!==null,'private boundary load');const record=doc.Objects.Root.get_Item('PRIVATE_OWNER').get_Item('PRIVATE_RECORD');
  Check(variant==='valid-xdata'?record instanceof DxfXRecord:record instanceof DxfOpaqueObject,'public/private boundary classification');Equal(1,record.XData.Count,'private APPID-looking text projected as real XData');
  const output=new MemoryStream();try{Check(doc.Save(output,binary),'private boundary save');output.Position=0;const saved=DxfRawDocument.Load(output);
    Equal(OwnershipTagValues(CompositeTableRecord(raw,record.Handle).Tags),OwnershipTagValues(CompositeTableRecord(saved,record.Handle).Tags),'private boundary packet changed');
  }finally{output.Dispose();}
}
function PrivateXRecordNative(binary) {
  const bytes=new MemoryStream(TableContentSourceBytes('sample_AC1018_ascii.dxf'));
  try{
    const raw=DxfRawDocument.Load(bytes),doc=PrivateXRecordLoad(raw,binary);Check(doc!==null,'native carrier load');const record=doc.GetObjectByHandle('145A');
    Check(record instanceof DxfOpaqueObject,'native145A must remain opaque');Equal('1459',record.Owner.Handle,'native145A owner');Equal(0,doc.Objects.Validate().Count,'complete native carrier validation');
    const output=new MemoryStream();try{Check(doc.Save(output,binary),'native145A save');OleWriteArtifact(`private-xrecord-native-${BooleanName(binary)}.dxf`,output.ToArray());output.Position=0;
      Equal(OwnershipTagValues(CompositeTableRecord(CompositeTableSource(),'145A').Tags),OwnershipTagValues(CompositeTableRecord(DxfRawDocument.Load(output),'145A').Tags),'native145A complete source packet differs');
    }finally{output.Dispose();}
  }finally{bytes.Dispose();}
}
