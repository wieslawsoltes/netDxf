// Port of pinned NumericHandleTests.cs. Original identities and assertions retained.
import { DxfDocument, DxfVersion, DxfDictionary, DxfXRecord, DxfTag, DxfTagValueType,
  ApplicationRegistry, XData, XDataRecord, XDataCode, MemoryStream } from '../../node-entry.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { OleSingle } from './Ole2FrameTests.js';
const A = value => Array.from(value);
const codes = [330,339,340,349,350,359,360,369];
const app = 'NUMERIC_HANDLES';
const alias = value => value.toLowerCase().padStart(16,'0');
export function RegisterNumericHandleTests() {
  for (const version of SupportedVersions) for (const binary of [false,true])
    Run(`numeric-handles/roundtrip-clone-erasure/${VersionName(version)}/${BooleanName(binary)}`,()=>NumericHandleGraph(version,binary));
  Run('numeric-handles/lookup/case-and-leading-zeroes',NumericHandleLookupAliases);
  Run('numeric-handles/lookup/reject-invalid-tokens',NumericHandleLookupInvalid);
  Run('numeric-handles/lookup/document-zero-and-full-width-misses',NumericHandleLookupZero);
  Run('numeric-handles/lookup/erased-identity-is-not-reused',NumericHandleLookupErased);
}
function fixture(version=DxfVersion.AutoCad2018) {
  const doc=new DxfDocument(version);
  for(let i=0;i<16;i++) {
    const target=new DxfXRecord(),name='NUMERIC_EXTERNAL_'+i;
    target.Data.Add(new DxfTag(1,'external target'));doc.Objects.Root.Add(name,target);
    if(/[A-F]/.test(target.Handle))return {doc,target,name};
  }
  throw new Error('Fixture did not allocate a handle containing hexadecimal letters');
}
function NumericHandleLookupAliases() {
  const {doc,target}=fixture(),seed=doc.NumHandles,count=doc.Objects.Items.Count;
  for(const spelling of [target.Handle,target.Handle.toLowerCase(),'0'+target.Handle,alias(target.Handle)])
    Check(doc.GetObjectByHandle(spelling)===target,'Equivalent public lookup selected another identity: '+spelling);
  Equal(seed,doc.NumHandles,'Lookup allocated handles');Equal(count,doc.Objects.Items.Count,'Lookup changed registration');
}
function NumericHandleLookupInvalid() {
  const {doc,target}=fixture(),seed=doc.NumHandles,count=doc.Objects.Items.Count;
  const invalid=[null,'',' ','\t',' '+target.Handle,target.Handle+' ',target.Handle+'\n','+'+target.Handle,
    '-'+target.Handle,'0x'+target.Handle,'G','1.0','Ａ','\0','0'.repeat(17),'10000000000000000','0'+alias(target.Handle)];
  for(const spelling of invalid)Check(doc.GetObjectByHandle(spelling)===null,'Invalid public handle token resolved: '+spelling);
  Equal(seed,doc.NumHandles,'Invalid lookup allocated handles');Equal(count,doc.Objects.Items.Count,'Invalid lookup changed registration');
}
function NumericHandleLookupZero() {
  const {doc}=fixture();
  for(const spelling of ['0','0000','0'.repeat(16)])Check(doc.GetObjectByHandle(spelling)===doc,'Public document-zero identity changed');
  for(const spelling of ['7FFFFFFFFFFFFFFF','8000000000000000','FFFFFFFFFFFFFFFF','ffffffffffffffff'])
    Check(doc.GetObjectByHandle(spelling)===null,'Valid missing unsigned handle resolved unexpectedly');
}
function NumericHandleLookupErased() {
  const {doc,target}=fixture(),handle=target.Handle,spelling=alias(handle),seed=doc.NumHandles;
  doc.Objects.EraseOwnedTree(target);
  Check(target.IsErased&&doc.GetObjectByHandle(handle)===null&&doc.GetObjectByHandle(spelling)===null,'Erased public identity remains visible');
  Equal(seed,doc.NumHandles,'Erasure changed allocation seed');
  const next=new DxfXRecord();doc.Objects.Root.Add('NUMERIC_AFTER_ERASE',next);
  Check(BigInt('0x'+next.Handle)>BigInt('0x'+handle),'Erased numeric identity was reused');
  Check(doc.GetObjectByHandle(spelling)===null,'New registration resurrected an erased alias');
}
function assertLinks(links,target,external,arbitraryTarget,arbitraryExternal,canonicalNull=false) {
  const zero=canonicalNull?'0':'0000',expected=[];
  for(const code of codes)expected.push([code,target],[code,zero]);
  expected.push([330,external],[320,arbitraryTarget],[329,arbitraryExternal],[320,zero]);
  Equal(expected,A(links.Data).map(t=>[t.Code,t.Value]),'Numeric/null/arbitrary XRECORD sequence differs');
  Equal([target,external,zero,canonicalNull?'0':'0'.repeat(16)],A(links.XData.get_Item(app).XDataRecord).map(t=>t.Value),'Exact numeric/null XData sequence differs');
}
function roundTrip(document,binary) {
  Equal(0,document.Objects.Validate().Count,'Numeric graph pre-save validation');
  const stream=new MemoryStream();
  try {
    Check(document.Save(stream,binary),'Numeric graph save failed');const bytes=stream.ToArray();
    Equal(binary,new TextDecoder('ascii').decode(bytes.slice(0,18))==='AutoCAD Binary DXF','Numeric graph actual transport differs');
    for(const record of A(document.Objects.Items).filter(r=>r instanceof DxfXRecord)) {
      for(const tag of A(record.Data).filter(t=>t.ValueType===DxfTagValueType.Handle))wireSpelling(bytes,binary,tag.Code,tag.Value);
      for(const data of record.XData.Values)for(const tag of A(data.XDataRecord).filter(t=>t.Code===XDataCode.DatabaseHandle))wireSpelling(bytes,binary,1005,tag.Value);
    }
    stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'Numeric graph reload failed');
    Equal(document.DrawingVariables.AcadVer,loaded.DrawingVariables.AcadVer,'Numeric graph profile changed');
    Equal(0,loaded.Objects.Validate().Count,'Numeric graph post-load validation');return loaded;
  }finally{stream.Dispose();}
}
function wireSpelling(bytes,binary,code,value) {
  if(binary) {
    const needle=new Uint8Array(3+value.length);needle[0]=code&255;needle[1]=code>>8;needle.set(new TextEncoder().encode(value),2);
    Check(Buffer.from(bytes).indexOf(needle)>=0,'Binary writer changed handle spelling '+code+':'+value);
  }else {
    const lines=new TextDecoder('ascii').decode(bytes).replaceAll('\r\n','\n').split('\n');let found=false;
    for(let i=0;i+1<lines.length;i+=2)if(Number(lines[i].trim())===code&&lines[i+1]===value){found=true;break;}
    Check(found,'ASCII writer changed handle spelling '+code+':'+value);
  }
}
function eraseRejected(document,graph) {
  const members=[graph,graph.get_Item('TARGET'),graph.get_Item('LINKS')],seed=document.NumHandles,count=document.Objects.Items.Count;
  Throws(InvalidOperationException,()=>document.Objects.EraseOwnedTree(graph));
  Equal(seed,document.NumHandles,'Numeric incoming guard allocated handles');Equal(count,document.Objects.Items.Count,'Numeric incoming guard changed registration count');
  for(const member of members)Check(!member.IsErased&&document.GetObjectByHandle(alias(member.Handle))===member,'Rejected numeric erasure changed member identity');
  Check(document.Objects.Root.get_Item('NUMERIC_COPY')===graph,'Rejected numeric erasure detached owning alias');
}
function NumericHandleGraph(version,binary) {
  const f=fixture(version);let source=f.doc,external=f.target,graph=new DxfDictionary(),target=new DxfXRecord(),links=new DxfXRecord();
  target.Data.Add(new DxfTag(1,'owned target'));graph.Add('TARGET',target);graph.Add('LINKS',links);source.Objects.Root.Add('NUMERIC_GRAPH',graph);
  const targetAlias=alias(target.Handle),externalAlias=alias(external.Handle);Check(/[a-f]/.test(externalAlias),'Semantic lowercase control has no letters');
  for(const code of codes){links.Data.Add(new DxfTag(code,targetAlias));links.Data.Add(new DxfTag(code,'0000'));}
  for(const [c,h]of [[330,externalAlias],[320,targetAlias],[329,externalAlias],[320,'0000']])links.Data.Add(new DxfTag(c,h));
  const data=new XData(new ApplicationRegistry(app));
  for(const handle of [targetAlias,externalAlias,'0000','0'.repeat(16)])data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,handle));
  links.XData.Add(data);Equal(0,source.Objects.Validate().Count,'Numeric references failed source validation');
  const authoredTags=A(links.Data),authoredXData=A(links.XData.get_Item(app).XDataRecord);
  source=roundTrip(source,binary);graph=source.Objects.Root.get_Item('NUMERIC_GRAPH');target=graph.get_Item('TARGET');links=graph.get_Item('LINKS');external=source.Objects.Root.get_Item(f.name);
  assertLinks(links,target.Handle,external.Handle,target.Handle,external.Handle,true);
  Check(source.GetObjectByHandle(targetAlias)===target&&source.GetObjectByHandle(externalAlias)===external,'Loaded numeric spelling selected another object');
  links.Data.Clear();for(const tag of authoredTags)links.Data.Add(tag);
  links.XData.get_Item(app).XDataRecord.Clear();for(const tag of authoredXData)links.XData.get_Item(app).XDataRecord.Add(tag);
  assertLinks(links,targetAlias,externalAlias,targetAlias,externalAlias);
  let destination=new DxfDocument(version),mappedExternal=new DxfXRecord();mappedExternal.Data.Add(new DxfTag(1,'mapped external'));destination.Objects.Root.Add('NUMERIC_EXTERNAL',mappedExternal);
  const failedSeed=destination.NumHandles,failedCount=destination.Objects.Items.Count;
  Throws(InvalidOperationException,()=>destination.Objects.Clone(graph,destination.Objects.Root,'NUMERIC_COPY'));
  Equal(failedSeed,destination.NumHandles,'Unmapped clone allocated handles');Equal(failedCount,destination.Objects.Items.Count,'Unmapped clone registered children');Check(!destination.Objects.Root.Contains('NUMERIC_COPY'),'Unmapped clone attached root');
  let copy=destination.Objects.Clone(graph,destination.Objects.Root,'NUMERIC_COPY',new Map([[external,mappedExternal]])),copyTarget=copy.get_Item('TARGET'),copyLinks=copy.get_Item('LINKS');
  assertLinks(copyLinks,copyTarget.Handle,mappedExternal.Handle,targetAlias,externalAlias);
  Check(copyTarget.Owner===copy&&copyLinks.Owner===copy,'Mapped clone ownership changed');assertLinks(links,targetAlias,externalAlias,targetAlias,externalAlias);
  destination=roundTrip(destination,binary);copy=destination.Objects.Root.get_Item('NUMERIC_COPY');copyTarget=copy.get_Item('TARGET');copyLinks=copy.get_Item('LINKS');mappedExternal=destination.Objects.Root.get_Item('NUMERIC_EXTERNAL');
  assertLinks(copyLinks,copyTarget.Handle,mappedExternal.Handle,target.Handle,external.Handle,true);
  for(const code of codes){mappedExternal.Data.Add(new DxfTag(code,alias(copyTarget.Handle)));eraseRejected(destination,copy);mappedExternal.Data.RemoveAt(mappedExternal.Data.Count-1);}
  const incoming=new XData(new ApplicationRegistry(app));incoming.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,alias(copyTarget.Handle)));mappedExternal.XData.Add(incoming);eraseRejected(destination,copy);mappedExternal.XData.Remove(app);
  const erasedAlias=alias(copyTarget.Handle);mappedExternal.Data.Add(new DxfTag(320,erasedAlias));mappedExternal.Data.Add(new DxfTag(329,erasedAlias));
  for(const code of codes)mappedExternal.Data.Add(new DxfTag(code,'0000'));
  const nullData=new XData(new ApplicationRegistry(app));nullData.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,'0000'));mappedExternal.XData.Add(nullData);
  const erased=[copy,copyTarget,copyLinks],handles=erased.map(x=>x.Handle),seed=destination.NumHandles;destination.Objects.EraseOwnedTree(copy);
  Equal(seed,destination.NumHandles,'Numeric erasure allocated handles');Check(!destination.Objects.Root.Contains('NUMERIC_COPY'),'Erased numeric root alias survived');
  for(const item of erased)Check(item.IsErased&&item.Database===null&&destination.GetObjectByHandle(alias(item.Handle))===null,'Numeric erased identity survived');
  Check(source.GetObjectByHandle(targetAlias)===target&&!target.IsErased,'Destination erasure changed source identity');
  const externalHandle=mappedExternal.Handle;destination=roundTrip(destination,binary);mappedExternal=destination.GetObjectByHandle(externalHandle);
  for(const handle of handles)Check(destination.GetObjectByHandle(alias(handle))===null,'Erased numeric identity returned on reload');
  Equal([copyTarget.Handle,copyTarget.Handle],A(mappedExternal.Data).filter(t=>t.Code===320||t.Code===329).map(t=>t.Value),'Reload changed arbitrary numeric values');
  Check(A(mappedExternal.Data).filter(t=>codes.includes(t.Code)).every(t=>t.Value==='0'),'Reload changed numeric-null semantic values');
  Equal('0',OleSingle(mappedExternal.XData.get_Item(app).XDataRecord).Value,'Reload changed numeric-null XData value');
}
