import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { DxfTransport as io } from '../../index.js';
import { EndSequence } from '../../netDxf/Entities/EndSequence.js';
import { Vertex } from '../../netDxf/Entities/Vertex.js';
import { Polyline } from '../../netDxf/Entities/Polyline.js';
import { ICodeValueReader } from '../../netDxf/IO/ICodeValueReader.js';
import { ICodeValueWriter } from '../../netDxf/IO/ICodeValueWriter.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { DatabaseIOContext } from '../../runtime/DatabaseIOContext.js';
import { SourceRecordIdentity } from '../../netDxf/IO/DxfReader.SourceIdentity.js';
import { Encoding } from '../../runtime/Encoding.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { ProbeTextReader } from '../../runtime/ProbeStreamReaders.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { ReadXDataRecord } from '../../runtime/DxfXDataIO.js';
import { InvalidDataException } from '../../runtime/Errors.js';
const output=()=>({tags:[],Write(code,value){this.tags.push([code,value]);}});
const textReader=pairs=>{const r=new TextCodeValueReader(pairs.map(p=>p.join('\n')).join('\n')+'\n');r.Next();return r;};
function roundtrip(write,transport){
  const stream=new MemoryStream(),w=transport==='text'?new TextCodeValueWriter(stream):new BinaryCodeValueWriter(stream,transport==='legacy');
  write(w);w.Write(0,'EOF');w.Flush();stream.Position=0;
  const r=transport==='text'?new TextCodeValueReader(new ProbeTextReader(stream)):new BinaryCodeValueReader(stream,Encoding.UTF8,transport==='legacy');r.Next();return r;
}
function source(context,item){
  const id=new SourceRecordIdentity();id.Handle=BigInt('0x'+item.Handle);id.IdentitySeen=true;
  context.sourceObjectIdentities.add(id.Handle);context.RecordSourceObject(item,id);
}

test('internal sequence DTOs retain source defaults, value copies and reference aliases',()=>{
  const end=new EndSequence(),v=new Vertex(),p=new Polyline();
  assert.equal(end.CodeName,'SEQEND');assert.equal(end.Owner,null);assert.equal(end.Handle,null);
  assert.equal(v.CodeName,'VERTEX');assert.equal(v.Flags,api.VertexTypeFlags.Polyline2DVertex);
  assert.equal(v.Layer,null);assert.equal(v.VertexIndexes,null);assert.equal(v.StartWidth,0);
  const position=new api.Vector3(1,2,3);v.Position=position;position.X=9;assert.equal(v.Position.X,1);
  const copy=v.Position;copy.Y=8;assert.equal(v.Position.Y,2);
  const indexes=[1,-2,3];v.VertexIndexes=indexes;indexes[0]=-1;assert.equal(v.VertexIndexes,indexes);
  assert.equal(p.CodeName,'POLYLINE');assert.equal(p.Vertexes,null);assert.equal(p.Normal.Z,0);
  p.Normal=position;position.Z=9;assert.equal(p.Normal.Z,3);assert.equal(p.StoredSource,null);
  p.Vertexes=new ReferenceList([v]);p.EndSequence=end;assert.equal(p.Vertexes.get_Item(0),v);
});
test('document CLASS collection remains per-document, readonly, stable and handle-free',()=>{
  const a=new api.DxfDocument(),b=new api.DxfDocument(),seed=a.NumHandles,c=a.Classes;
  c.Add(new api.DxfClass('Custom','CustomClass','Vendor'));assert.equal(a.Classes,c);assert.equal(b.Classes.Count,0);assert.equal(a.NumHandles,seed);
  assert.throws(()=>{a.Classes=b.Classes;},TypeError);
});
for(const transport of ['text','binary','legacy'])test('structural codec contracts cover real '+transport+' codecs without invoking getters',()=>{
  const stream=new MemoryStream(),w=transport==='text'?new TextCodeValueWriter(stream):new BinaryCodeValueWriter(stream,transport==='legacy');
  assert.equal(w instanceof ICodeValueWriter,true);w.Write(0,'EOF');w.Flush();stream.Position=0;
  const r=transport==='text'?new TextCodeValueReader(new ProbeTextReader(stream)):new BinaryCodeValueReader(stream,Encoding.UTF8,transport==='legacy');
  assert.equal(r instanceof ICodeValueReader,true);assert.equal(r instanceof ICodeValueWriter,false);
  assert.equal(ICodeValueReader.IsImplementedBy(null),false);
  assert.equal(Object.create(r) instanceof ICodeValueReader,true); // No access to private-state getters.
});
test('HATCH public origin editing preserves nested tuples and original records',()=>{
  const record=(c,v)=>new api.XDataRecord(c,v),nested=[record(1002,'{'),record(1010,1),record(1020,2),record(1030,3),record(1002,'}')];
  const records=new ReferenceList([...nested,record(1000,'private'),record(1010,4),record(1020,5),record(1030,6),record(1070,7)]);
  const result=io.HatchPatternXData.WithOrigin(records,new api.Vector2(8,9));
  assert.equal(io.HatchPatternXData.FindOrigin(records),6);assert.equal(result.Count,records.Count);
  assert.equal(result.get_Item(0),records.get_Item(0));assert.equal(result.get_Item(6).Value,8);assert.equal(result.get_Item(8).Value,0);
  assert.equal(records.get_Item(6).Value,4);assert.equal(result.get_Item(9),records.get_Item(9));
  const prepended=io.HatchPatternXData.WithOrigin(new ReferenceList(nested),new api.Vector2());assert.equal(prepended.Count,8);assert.equal(prepended.get_Item(3),nested[0]);
  assert.throws(()=>io.HatchPatternXData.FindOrigin(null),{name:'NullReferenceException'});
});
test('HATCH color editing replaces only a leading Int16 record',()=>{
  const original=new ReferenceList([new api.XDataRecord(1070,7),new api.XDataRecord(1000,'keep')]);
  const changed=io.HatchPatternXData.WithColorIndex(original,12);assert.equal(changed.Count,2);assert.equal(changed.get_Item(0).Value,12);assert.equal(original.get_Item(0).Value,7);
  assert.equal(changed.get_Item(1),original.get_Item(1));assert.equal(io.HatchPatternXData.WithColorIndex(null,1).Count,1);
});
for(const version of [13,14,15,18])test('STYLE encoding preserves literal escapes and UTF-16 at version '+version,()=>{
  const text='\\U+0041\0\r\nΩ𐐀';io.CheckStyleUnicode(text);const encoded=io.EncodeStyleString(text,version);
  assert.equal(/[\0\r\n]/.test(encoded),false);assert.equal(DecodeDxfText(encoded),text);
  for(const bad of ['\ud800','\udc00','x\ud800z','\ud800\ud800'])assert.throws(()=>io.CheckStyleUnicode(bad),{name:'InvalidDataException'});
});
test('STYLE preflight visits registered unreferenced text and shape styles',()=>{
  const doc=new api.DxfDocument();io.ValidateTextStyleStrings(doc);
  const style=doc.TextStyles.Add(new api.TextStyle('Unreferenced','txt.shx'));
  const app=new api.ApplicationRegistry('APP'),data=new api.XData(app);data.XDataRecord.Add(new api.XDataRecord(1000,'\ud800'));style.XData.Add(data);
  assert.throws(()=>io.ValidateTextStyleStrings(doc),{name:'InvalidDataException'});
});
test('STYLE XData encodes only documented string fields and retains valid binary packet order',()=>{
  const data=new api.XData(new api.ApplicationRegistry('ΩAPP'));data.XDataRecord.Add(new api.XDataRecord(1000,'\\U+0041\r\n'));
  for(const count of [127,127,1])data.XDataRecord.Add(new api.XDataRecord(1004,new Uint8Array(count)));const owner=new api.Line();owner.XData.Add(data);
  const out=output();io.WriteStyleXData(out,13,owner.XData);assert.equal(out.tags[0][1],'\\U+03A9APP');
  assert.equal(DecodeDxfText(out.tags[1][1]),'\\U+0041\r\n');assert.deepEqual(out.tags.slice(2).map(t=>t[1].length),[127,127,1]);
});
test('stored dimension headers retain explicit numeric boxing and validation rules',()=>{
  const h=(name,code,value)=>new api.HeaderVariable(name,code,value);
  assert.equal(io.IsStoredDimensionHeader('$dimtsz'),true);assert.equal(io.IsStoredDimensionHeader(null),false);
  io.ValidateStoredDimensionHeader(h('$DIMUPT',70,new BoxedScalar('Int16',1)));
  for(const value of [1,true,new BoxedScalar('Int32',1),new BoxedScalar('Int16',2)])assert.throws(()=>io.ValidateStoredDimensionHeader(h('$DIMUPT',70,value)),{name:'FormatException'});
  for(const value of [NaN,Infinity,-1])assert.throws(()=>io.ValidateStoredDimensionHeader(h('$DIMTSZ',40,value)),{name:'ArgumentOutOfRangeException',ParamName:'variable'});
  io.ValidateStoredDimensionHeader(h('$DIMTVP',40,-1));assert.throws(()=>io.ValidateStoredDimensionHeader(h('$DIMTVP',70,1)),{name:'FormatException'});
  const doc=new api.DxfDocument(),out=output(),style=api.DimensionStyle.Default;style.TickSize=3;style.UserPositionedText=true;
  io.WriteStoredDimensionHeaders(out,doc,style);assert.deepEqual(out.tags.map(t=>t[0]),[9,40,9,40,9,70]);
  assert.equal(out.tags[1][1],3);
});
function hatchDoc(edge=null){const doc=new api.DxfDocument(18),block=doc.Blocks.Add(new api.Block('Unused'));
  const hatch=new api.Hatch(api.HatchPattern.Solid,false);if(edge)hatch.BoundaryPaths.Add(api.HatchBoundaryPath.FromEdges([edge]));block.Entities.Add(hatch);return {doc,block,hatch};}
test('HATCH boundary preflight includes unreferenced block definitions',()=>{
  const {doc,hatch}=hatchDoc();assert.throws(()=>io.ValidateHatchBoundaryPresence(doc),{name:'InvalidDataException'});
  hatch.BoundaryPaths.Add(api.HatchBoundaryPath.FromEdges([new api.HatchBoundaryPath.Line()]));io.ValidateHatchBoundaryPresence(doc);io.ValidateHatchSourceRelations(doc);
  hatch.BoundaryPaths.get_Item(0).AddContour(new api.Line());assert.throws(()=>io.ValidateHatchSourceRelations(doc),{name:'InvalidOperationException'});
});
test('HATCH spline preflight visits retained edges and enforces fit metadata versions',()=>{
  const edge=new api.HatchBoundaryPath.Spline();edge.Degree=1;edge.ControlPoints=[new api.Vector3(0,0,1),new api.Vector3(1,1,1)];edge.Knots=[0,0,1,1];
  const {doc}=hatchDoc(edge);io.ValidateHatchSplineData(doc);edge.FitPoints.Add(new api.Vector2(1,2));
  doc.DrawingVariables.AcadVer=15;assert.throws(()=>io.ValidateHatchSplineFitVersions(doc),{name:'NotSupportedException'});
  doc.DrawingVariables.AcadVer=16;io.ValidateHatchSplineFitVersions(doc);edge.Knots=[1,0,1,1];assert.throws(()=>io.ValidateHatchSplineData(doc),{name:'ArgumentException'});
});
function scalarReader(pairs){const chunk=textReader([...pairs,[0,'NEXT']]);return {chunk,
  InvalidHatchPolylineData:m=>new InvalidDataException(m),InvalidHatchEdgeData:m=>new InvalidDataException(m),
  ReadHatchPolylineCount:()=>chunk.ReadInt(),ReadHatchPolylineFlag:()=>chunk.ReadShort()!==0,ReadNextHatchPolylineTag:()=>chunk.Next(),
  ReadNextHatchEdgeTag:()=>chunk.Next(),ReadHatchEdgeDouble:()=>{const v=chunk.ReadDouble();chunk.Next();return v;},
  ReadHatchEdgeFlag:()=>{const v=chunk.ReadShort();chunk.Next();return v!==0;},ReadHatchEdgeCount:()=>{const v=chunk.ReadInt();chunk.Next();return v;}};}
for(const [type,pairs,property,value] of [[1,[[21,4],[10,1],[11,3],[20,2]],'Start',1],[2,[[73,1],[51,90],[40,2],[20,0],[50,0],[10,3]],'Center',3],[3,[[73,1],[51,90],[40,.5],[20,0],[50,0],[10,3],[11,2],[21,0]],'Center',3]])
 test('unordered HATCH scalar edge packet '+type,()=>{
  const reader=scalarReader(pairs),edge=io.ReadHatchScalarEdge(reader,type);assert.equal(edge.Type,type);assert.equal(edge[property].X,value);assert.equal(reader.chunk.ReadString(),'NEXT');
  assert.throws(()=>io.ReadHatchScalarEdge(scalarReader([...pairs,pairs[0]]),type),{name:'InvalidDataException'});
  assert.throws(()=>io.ReadHatchScalarEdge(scalarReader(pairs.slice(1)),type),{name:'InvalidDataException'});
 });
test('HATCH scalar header parser updates out parameters in source order',()=>{
  const closed={},count={};io.ReadHatchPolylineHeader(scalarReader([[93,2],[73,1],[72,0]]),closed,count);assert.equal(closed.value,true);assert.equal(count.value,2);
  const boxes=Array.from({length:5},()=>({}));io.ReadHatchSplineHeader(scalarReader([[96,4],[74,0],[95,8],[73,1],[94,3]]),...boxes);
  assert.deepEqual(boxes.map(v=>v.value),[3,true,false,8,4]);
  const bad=Array.from({length:5},()=>({}));assert.throws(()=>io.ReadHatchSplineHeader(scalarReader([[94,32768]]),...bad),{name:'InvalidDataException'});assert.equal(bad[0].value,32768);
});
for(const transport of ['text','binary','legacy'])for(const kind of ['view','vport'])test('complete '+kind+' record body roundtrip through '+transport,()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc),view=kind==='view'?doc.Views.Add(new api.View('CameraΩ')):doc.VPorts.AddRecord(new api.VPort('ViewportΩ'));
  view.ViewCenter=new api.Vector2(2,3);view.ViewDirection=new api.Vector3(1,2,3);view.LensLength=25;
  const data=new api.XData(new api.ApplicationRegistry('APP_TEST'));data.XDataRecord.Add(new api.XDataRecord(1000,'Ω'));view.XData.Add(data);
  const r=roundtrip(w=>kind==='view'?io.WriteView(w,doc,view):io.WriteVPort(w,doc,view),transport),marker=kind==='view'?api.SubclassMarker.View:api.SubclassMarker.VPort;
  while(!(r.Code===100&&r.ReadString()===marker))r.Next();
  const copy=kind==='view'?io.ReadView(r,context):io.ReadVPort(r,context);
  assert.equal(copy.Name,view.Name);assert.equal(copy.ViewCenter.Y,3);assert.equal(copy.ViewDirection.X,1);assert.equal(copy.LensLength,25);assert.equal(r.ReadString(),'EOF');
  assert.equal(context.tableEntryXData.get_Item(copy).Count,1);assert.equal(copy.XData.Count,0);
  const detached=context.tableEntryXData.get_Item(copy).get_Item(0).ApplicationRegistry;assert.equal(detached.Owner,null);
});
for(const code of [10,11,12,13,14,15,16,17,110,111,112])test('VPORT rejects incomplete vector '+code,()=>{
  const context=new DatabaseIOContext(new api.DxfDocument());
  const reader=textReader([[100,api.SubclassMarker.VPort],[2,'Camera'],[code,2],[0,'EOF']]);
  assert.throws(()=>io.ReadVPort(reader,context),{name:'InvalidDataException'});
});
for(const code of [73,75,76,65])test('VPORT rejects nonbinary Boolean '+code,()=>{
  const reader=textReader([[100,api.SubclassMarker.VPort],[2,'Camera'],[code,2],[0,'EOF']]);
  assert.throws(()=>io.ReadVPort(reader,new DatabaseIOContext(new api.DxfDocument())),{name:'InvalidDataException'});
});
test('VIEW UCS parser requires explicit enable flag and complete vectors',()=>{
  const context=new DatabaseIOContext(new api.DxfDocument());
  for(const pairs of [[[110,2]],[[72,1],[110,2]],[[72,2]],[[72,1],[72,1]]]){
    const reader=textReader([[100,api.SubclassMarker.View],[2,'Camera'],...pairs,[0,'EOF']]);
    assert.throws(()=>io.ReadView(reader,context),{name:'InvalidDataException'});
  }
});
test('VIEW plottable-camera preflight precedes writes and honors per-call version changes',()=>{
  const doc=new api.DxfDocument(13),view=doc.Views.Add(new api.View('Camera'));view.IsCameraPlottable=true;const out=output();
  assert.throws(()=>io.WriteView(out,doc,view),{name:'NotSupportedException'});assert.equal(out.tags.length,0);
  view.IsCameraPlottable=false;const write=out.Write.bind(out);out.Write=(code,value)=>{write(code,value);if(code===281)doc.DrawingVariables.AcadVer=15;};
  io.WriteView(out,doc,view);assert.equal(out.tags.some(t=>t[0]===73),true);
});
test('VIEW/UCS relationships bind real registered targets, and unresolved targets fail',()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc),target=doc.UCSs.Add(new api.UCS('Target'));
  const view=doc.Views.Add(new api.View('Camera'));view.Ucs=new api.ViewUcs();
  io.AddUcsReference(context,view,345,target.Handle);io.ResolveUcsReferences(context);assert.equal(view.Ucs.NamedUcs,target);
  io.AddUcsReference(context,view,345,'ABCDE');assert.throws(()=>io.ResolveUcsReferences(context),{name:'InvalidDataException'});
});
test('UCS base resolution requires exact accepted physical identities',()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc),owner=doc.UCSs.Add(new api.UCS('Owner')),target=doc.UCSs.Add(new api.UCS('Target'));
  io.CompleteUcsBase(context,owner,1,target.Handle);
  assert.throws(()=>io.ResolveUcsBaseReferences(context),{name:'InvalidDataException'});source(context,owner);
  assert.throws(()=>io.ResolveUcsBaseReferences(context),{name:'InvalidDataException'});source(context,target);
  io.ResolveUcsBaseReferences(context);assert.equal(owner.BaseUcs,target);
});
test('UCS subclass context distinguishes nested controls from outer XData',()=>{
  const context=new io.UcsBaseContext();assert.equal(context.IsPublic,true);context.Observe(102,'{private');context.Observe(1001,'APP');assert.equal(context.IsPublic,false);
  context.Observe(102,'}');assert.equal(context.IsPublic,true);context.Observe(100,'Private');assert.equal(context.IsPublic,false);
  context.Observe(100,api.SubclassMarker.Ucs);assert.equal(context.IsPublic,true);context.Observe(1001,'APP');assert.equal(context.IsPublic,false);
});
test('VIEW live section resolver rejects generated same-handle referrers',()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc),view=doc.Views.Add(new api.View('Camera'));
  context.loadedViewSections.push([view,'0']);assert.throws(()=>io.ResolveViewSections(context),{name:'InvalidDataException'});
  source(context,view);io.ResolveViewSections(context);assert.equal(view.HasStoredLiveSection,true);assert.equal(view.LiveSection,null);
});
test('reader defaults preserve collection identity and existing records',()=>{
  const doc=new api.DxfDocument(),layers=doc.Layers,views=doc.Views;views.Add(new api.View('Keep'));io.EnsureTableCollections(doc);
  assert.equal(doc.Layers,layers);assert.equal(doc.Views,views);assert.equal(doc.Views.Contains('Keep'),true);
});

function leaderModel(doc,kind='text') {
  const style=new api.DxfMLeaderStyle();style.Properties.TextStyle=doc.TextStyles.get_Item('Standard');doc.Objects.AddMLeaderStyle('Style',style);
  const leader=new api.MultiLeader();leader.Properties.Style=style;leader.Properties.TextStyle=style.Properties.TextStyle;leader.Properties.LeaderLinetype=doc.Linetypes.get_Item('Continuous');
  if(kind==='text') {
    const content=new api.MLeaderMTextContent();content.Style=style.Properties.TextStyle;content.Text='literal\\U+0041 Ω';content.ColumnHeights.Add(2);content.ColumnHeights.Add(3);leader.Context.MText=content;
  }else {
    leader.Properties.ContentType=1;const content=new api.MLeaderBlockContent();content.Block=doc.Blocks.Add(new api.Block('Content')).Record;
    for(let i=0;i<16;i++)content.TransformationMatrix.Add(i%5===0?1:0);leader.Context.Block=content;
  }
  const node=new api.MLeaderNode(),line=new api.MLeaderLine(),breaks=new api.MLeaderLineBreaks();breaks.Index=7;
  const pair=new api.MLeaderBreak(new api.Vector3(1,2,3),new api.Vector3(4,5,6));breaks.Breaks.Add(pair);node.Breaks.Add(pair);line.Breaks.Add(breaks);line.Vertices.Add(new api.Vector3(-0,2,3));line.Vertices.Add(new api.Vector3(4,5,6));node.Lines.Add(line);leader.Context.Leaders.Add(node);
  return leader;
}
for(const transport of ['text','binary','legacy'])for(const kind of ['text','block'])test('MULTILEADER nested '+kind+' packet roundtrip through '+transport,()=>{
  const doc=new api.DxfDocument(18),leader=leaderModel(doc,kind),context=new DatabaseIOContext(doc);
  context.Chunk=roundtrip(w=>io.WriteMultiLeader(w,doc,leader),transport);
  const copy=io.ReadMultiLeader(context);assert.equal(context.loadedMLeaders[0],copy);assert.equal(copy.PendingInputReferences,true);
  io.ResolveMultiLeaderReferences(context);assert.equal(copy.PendingInputReferences,false);copy.Validate(doc,18);
  assert.equal(copy.Properties.Style,leader.Properties.Style);assert.equal(copy.Properties.TextStyle,leader.Properties.TextStyle);
  assert.equal(copy.Context.Leaders.get_Item(0).Lines.get_Item(0).Breaks.get_Item(0).Index,7);
  assert.equal(copy.Context.Leaders.get_Item(0).Breaks.get_Item(0).End.Z,6);
  if(kind==='text'){assert.equal(copy.Context.MText.Text,leader.Context.MText.Text);assert.deepEqual([...copy.Context.MText.ColumnHeights],[2,3]);}
  else{assert.equal(copy.Context.Block.Block,leader.Context.Block.Block);assert.equal(copy.Context.Block.TransformationMatrix.Count,16);}
  assert.equal(context.Chunk.ReadString(),'EOF');
});
const minimalLeader=[[100,'AcDbMLeader'],[270,2],[300,'CONTEXT_DATA{'],[290,0],[296,0],[301,'}']];
for(const [name,pairs] of [
  ['missing context',[[100,'AcDbMLeader'],[270,2]]],
  ['unknown version',[[100,'AcDbMLeader'],[270,3]]],
  ['missing flags',[[100,'AcDbMLeader'],[300,'CONTEXT_DATA{'],[301,'}']]],
  ['unclosed context',minimalLeader.slice(0,-1)],
  ['partial vector',[...minimalLeader.slice(0,-1),[110,1],[301,'}']]],
  ['duplicate flag',[...minimalLeader.slice(0,4),[290,0],...minimalLeader.slice(4)]],
  ['unknown property',[...minimalLeader,[99,1]]],
  ['incomplete arrow',[...minimalLeader,[94,1]]],
  ['incomplete attribute',[...minimalLeader,[330,'0'],[177,0]]],
  ['break without pair',[...minimalLeader.slice(0,-1),[302,'LEADER{'],[304,'LEADER_LINE{'],[90,0],[305,'}'],[303,'}'],[301,'}']]]
])test('MULTILEADER rejects '+name+' without admitting a completed entity',()=>{
  const context=new DatabaseIOContext(new api.DxfDocument(18));context.Chunk=textReader([...pairs,[0,'EOF']]);assert.throws(()=>io.ReadMultiLeader(context),{name:'InvalidDataException'});assert.equal(context.loadedMLeaders.length,0);
});
test('MULTILEADER failed reference resolution preserves completed earlier fixups',()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc),a=new api.MLeaderMTextContent(),b=new api.MLeaderMTextContent();
  context.mleaderReferences.push([a,340,doc.TextStyles.get_Item('Standard').Handle],[b,340,'FFFF']);
  assert.throws(()=>io.ResolveMultiLeaderReferences(context),{name:'InvalidDataException'});assert.equal(a.Style,doc.TextStyles.get_Item('Standard'));assert.equal(b.Style,null);
});
for(const transport of ['text','binary','legacy'])for(const name of ['SECTION','SECTIONOBJECT'])test(name+' independent indicators and vertices through '+transport,()=>{
  const doc=new api.DxfDocument(18),section=new api.Section(name),context=new DatabaseIOContext(doc);section.Name='Cut Ω';section.StoredIndicatorColor=17;section.StoredNativeIndicatorColor=256;section.IndicatorColorName='Book';section.State=-2147483648;section.Flags=2147483647;
  section.Vertices.Add(new api.Vector3(1,2,3));section.BackLineVertices.Add(new api.Vector3(4,5,6));
  context.Chunk=roundtrip(w=>io.WriteSection(w,doc,section),transport);const copy=io.ReadSection(context,name);assert.equal(copy.PendingInputReferences,true);io.ResolveSections(context);
  assert.equal(copy.Name,section.Name);assert.equal(copy.State,section.State);assert.equal(copy.Flags,section.Flags);assert.equal(copy.StoredIndicatorColor,17);assert.equal(copy.StoredNativeIndicatorColor,256);assert.equal(copy.Vertices.get_Item(0).Y,2);assert.equal(copy.BackLineVertices.get_Item(0).Z,6);assert.equal(copy.HasSettingsField,true);assert.equal(copy.GeometrySettings,null);assert.equal(context.Chunk.ReadString(),'EOF');
});
function sectionPairs(){const out=output();io.WriteSection(out,new api.DxfDocument(18),new api.Section());return out.tags;}
for(const fault of ['duplicate-color','missing-vector','huge-count','negative-count','extra-payload','settings-target'])test('SECTION packet rejects '+fault,()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc),pairs=sectionPairs();
  if(fault==='duplicate-color')pairs.splice(pairs.findIndex(p=>p[0]===92),0,[62,1],[62,2]);
  if(fault==='missing-vector')pairs.splice(pairs.findIndex(p=>p[0]===20),1);
  if(fault==='huge-count')pairs[pairs.findIndex(p=>p[0]===92)][1]=2147483647;
  if(fault==='negative-count')pairs[pairs.findIndex(p=>p[0]===92)][1]=-1;
  if(fault==='extra-payload')pairs.push([90,0]);
  if(fault==='settings-target')pairs[pairs.findIndex(p=>p[0]===360)][1]=doc.Layers.get_Item('0').Handle;
  context.Chunk=textReader([...pairs,[0,'EOF']]);
  assert.throws(()=>{io.ReadSection(context,'SECTION');io.ResolveSections(context);},{name:'InvalidDataException'});
});
for(const transport of ['text','binary','legacy'])test('CLASSES strict section parsing and cloned preparation through '+transport,()=>{
  const doc=new api.DxfDocument(18),definition=new api.DxfClass('CUSTOM','VendorClass','Ω Vendor');definition.IsEntity=true;definition.ProxyFlags=1025;definition.InstanceCount=9;definition.WasProxy=true;
  const context=new DatabaseIOContext(doc);context.Chunk=roundtrip(w=>{w.Write(2,'CLASSES');io.WriteClassDefinition(w,doc,definition);w.Write(0,'ENDSEC');},transport);
  io.ReadClassDefinitions(context);assert.equal(context.Chunk.ReadString(),'ENDSEC');const restored=doc.Classes.get_Item('CUSTOM');assert.equal(restored.InstanceCount,9);assert.equal(restored.ApplicationName,'Ω Vendor');assert.equal(restored.WasProxy,true);
  const prepared=io.PrepareClassDefinitions(doc);assert.notEqual(prepared.get_Item('CUSTOM'),restored);assert.equal(prepared.Contains('RASTERVARIABLES'),true);assert.equal(doc.Classes.Contains('RASTERVARIABLES'),false);assert.equal(context.unqualifiedOpaqueClasses.size,0);
});
test('CLASSES source qualification preserves repeated/unknown fields without pretending opaque fidelity',()=>{
  const doc=new api.DxfDocument(18),context=new DatabaseIOContext(doc);context.Chunk=textReader([[2,'CLASSES'],[0,'CLASS'],[1,'C'],[1,'C'],[2,'CPP'],[999,'comment'],[70,3],[0,'ENDSEC']]);
  io.ReadClassDefinitions(context);assert.equal(doc.Classes.Count,1);assert.equal(context.unqualifiedOpaqueClasses.has('C'),true);
  const plain=new DatabaseIOContext(new api.DxfDocument(18));plain.Chunk=textReader([[2,'CLASSES'],[0,'CLASS'],[1,'C'],[2,'CPP'],[999,'comment'],[0,'ENDSEC']]);io.ReadClassDefinitions(plain);assert.equal(plain.unqualifiedOpaqueClasses.size,0);
});
for(const [name,pairs,error] of [['flag',[[0,'CLASS'],[1,'C'],[2,'CPP'],[281,2],[0,'ENDSEC']],'InvalidDataException'],['early EOF',[[0,'EOF']],'EndOfStreamException'],['record',[[0,'LINE']],'InvalidDataException'],['missing name',[[0,'CLASS'],[2,'CPP'],[0,'ENDSEC']],'InvalidDataException']])test('CLASSES rejects '+name,()=>{
  const context=new DatabaseIOContext(new api.DxfDocument(18));context.Chunk=textReader([[2,'CLASSES'],...pairs]);assert.throws(()=>io.ReadClassDefinitions(context),{name:error});
});
test('CLASS raster count preparation updates cloned declarations and rejects conflicts',()=>{
  const doc=new api.DxfDocument(18),definition=new api.DxfClass('IMAGE','AcDbRasterImage','Vendor');definition.IsEntity=true;definition.InstanceCount=9;doc.Classes.Add(definition);
  assert.equal(io.PrepareClassDefinitions(doc).get_Item('IMAGE').InstanceCount,0);assert.equal(definition.InstanceCount,9);
  const wrong=new api.DxfClassCollection();wrong.Add(new api.DxfClass('IMAGE','Different','Vendor'));assert.throws(()=>io.AddGeneratedClass(wrong,'IMAGE','AcDbRasterImage',0,true,1),{name:'InvalidDataException'});
});
for(const version of [13,14,15,18])test('CLASS instance-count emission follows current version '+version,()=>{
  const doc=new api.DxfDocument(version),definition=new api.DxfClass('C','CPP','APP'),out=output();definition.InstanceCount=3;io.WriteClassDefinition(out,doc,definition);assert.equal(out.tags.some(p=>p[0]===91),version>13);
});
test('unknown entity candidate classification rejects case variants of typed/aggregate names',()=>{
  assert.equal(io.IsOpaqueEntityCandidate('LINE'),false);assert.equal(io.IsOpaqueEntityCandidate('VendorThing'),true);assert.throws(()=>io.IsOpaqueEntityCandidate('line'),{name:'InvalidDataException'});
  assert.equal(io.OpaqueExcludedSubclass('acdbvertex'),true);assert.equal(io.OpaqueExcludedSubclass('VendorClass'),false);
  const context=new DatabaseIOContext(new api.DxfDocument());context.Chunk=textReader([[0,'acad_proxy_entity'],[0,'EOF']]);assert.throws(()=>io.ReadOpaqueEntity(context,false),{name:'NotSupportedException'});
});
test('unknown entity control groups enforce framing and depth budget',()=>{
  const tags=pairs=>pairs.map(([c,v])=>new api.DxfTag(c,v));
  assert.equal(io.OpaqueEntityGroupEnd(tags([[102,'{A'],[999,'comment'],[102,'{B'],[102,'}'],[102,'}']]),0),4);
  for(const packet of [[[102,'x']],[[102,'{A']],[[102,'{A'],[102,'x']],Array.from({length:33},()=>[102,'{A'])])assert.throws(()=>io.OpaqueEntityGroupEnd(tags(packet),0),{name:'InvalidDataException'});
});
for(const binary of [false,true])test('unknown entity transport text preflight '+binary,()=>{
  assert.throws(()=>io.ValidateOpaqueText('x\0y',binary),{name:'InvalidOperationException'});assert.throws(()=>io.ValidateOpaqueText('\ud800',binary),{name:'InvalidDataException'});
  if(binary)io.ValidateOpaqueText('x\ny',true);else assert.throws(()=>io.ValidateOpaqueText('x\ny',false),{name:'InvalidOperationException'});
});

import { DatabaseMetadataReader } from '../../netDxf/IO/DxfReader.DatabaseMetadata.js';
import { ReadVertex, ReadPolyline2D } from '../../runtime/FittedPolylineIO.js';
// Packet-level fixtures use actual transport codecs, physical identity observation,
// typed models and registration. They do not call a replacement whole-file loader.
function physicalContext(pairs,transport='text',doc=new api.DxfDocument(18)) {
  const context=new DatabaseIOContext(doc),stream=new MemoryStream();
  const writer=transport==='text'?new TextCodeValueWriter(stream):new BinaryCodeValueWriter(stream,transport==='legacy');
  for(const pair of [[0,'SECTION'],[2,'ENTITIES'],...pairs,[0,'ENDSEC'],[0,'EOF']])writer.Write(...pair);
  writer.Flush();stream.Position=0;
  const inner=transport==='text'?new TextCodeValueReader(new ProbeTextReader(stream)):new BinaryCodeValueReader(stream,Encoding.UTF8,transport==='legacy');
  context.Chunk=new DatabaseMetadataReader(inner,context.entityDatabaseMetadata,context.sourceObjectIdentities);context.Chunk.Next();return context;
}
const seek=(context,code,value)=>{const chunk=context.Chunk;while(!(chunk.Code===code&&chunk.Value===value)){if(chunk.Code===0&&chunk.Value==='EOF')throw new Error('Test fixture has no requested packet');chunk.Next();}};
const childPacket=(handle,subclass,point,flags,extras=[],owner='A0')=>[[0,'VERTEX'],[5,handle],[330,owner],[100,'AcDbEntity'],[8,'0'],...(subclass==='AcDbFaceRecord'?[]:[[100,'AcDbVertex']]),[100,subclass],[10,point[0]],[20,point[1]],[30,point[2]],[70,flags],...extras];
const endPacket=(handle='AF',owner='A0')=>[[0,'SEQEND'],[5,handle],[330,owner],[100,'AcDbEntity']];
function retainedFixture(kind,transport='text',edit=pairs=>pairs){
  const doc=new api.DxfDocument(18),block=doc.Layouts.get_Item('Model').AssociatedBlock;
  let subclass,flags,body,header=[];
  if(kind==='polyline'){subclass='AcDb3dPolyline';flags=8;body=[...childPacket('A1','AcDb3dPolylineVertex',[1,2,3],32),...childPacket('A2','AcDb3dPolylineVertex',[4,5,6],32)];}
  else if(kind==='polygon'){subclass='AcDbPolygonMesh';flags=16;body=Array.from({length:6},(_,i)=>childPacket((0xA1+i).toString(16),'AcDbPolygonMeshVertex',[i,10+i,20+i],64)).flat();}
  else if(kind==='legacy'){subclass='AcDb2dPolyline';flags=1;header=[[10,0],[20,0],[30,5],[40,2],[41,3]];body=[...childPacket('A1','AcDb2dVertex',[1,2,0],0,[[42,0.5],[91,19]]),...childPacket('A2','AcDb2dVertex',[4,5,0],0,[[40,7]])];}
  else{subclass='AcDbPolyFaceMesh';flags=64;header=[[71,123],[72,99]];body=[...childPacket('A1','AcDbPolyFaceMeshVertex',[0,0,0],192),...childPacket('A2','AcDbPolyFaceMeshVertex',[1,0,0],192),...childPacket('A3','AcDbPolyFaceMeshVertex',[1,1,0],192),...childPacket('A4','AcDbFaceRecord',[0,0,0],128,[[71,1],[72,-2],[73,3],[74,0]])];}
  const pairs=edit([[0,'POLYLINE'],[5,'A0'],[330,block.Record.Handle],[100,'AcDbEntity'],[100,subclass],[70,flags],...header,...body,...endPacket()]);
  const context=physicalContext(pairs,transport,doc);seek(context,0,'POLYLINE');const identity=context.Chunk.SourceRecord;
  if(kind==='polyline'||kind==='polygon')seek(context,0,'VERTEX');else seek(context,100,subclass);
  const read=kind==='polyline'?()=>io.ReadStoredPolylineSequence(context,flags,api.Vector3.UnitZ,[]):kind==='polygon'?()=>io.ReadStoredPolygonMeshSequence(context,flags,api.Vector3.UnitZ,[],2,3):kind==='legacy'?()=>io.ReadStoredPolyline2D(context):()=>io.ReadStoredPolyfaceMesh(context);
  const parent=read();parent.Handle='A0';doc.AddEntityToDocument(parent,false);parent.Owner=block;context.RecordSourceObject(parent,identity);
  const name={polyline:'Polyline',polygon:'PolygonMesh',legacy:'Polyline2D',polyface:'PolyfaceMesh'}[kind];io['ResolveStored'+name+'Records'](context);io['ValidateStored'+name+'Records'](doc,false);
  return {doc,context,parent,name,pairs};
}
for(const kind of ['polyline','polygon','legacy','polyface'])for(const transport of ['text','binary','legacy'])test('retained '+kind+' physical identity/registration and '+transport+' output roundtrip',()=>{
  const {doc,parent,name,pairs}=retainedFixture(kind,transport);
  assert.equal(parent.HasStoredRecords,true);for(const r of parent.StoredRecords){assert.equal(doc.GetObjectByHandle(r.Handle),r);assert.equal(r.Owner,parent);}
  const r=roundtrip(w=>io['WriteStored'+name+'Records'](w,doc,parent),transport),out=[];while(!(r.Code===0&&r.Value==='EOF')){out.push([r.Code,r.Value]);r.Next();}
  assert.equal(out.filter(t=>t[0]===0&&t[1]==='SEQEND').length,1);
  const first=pairs.findIndex(t=>t[0]===0&&t[1]==='VERTEX'),expected=pairs.slice(first).map(([c,v])=>[c,c===5||c===330?String(v).toUpperCase():v]);
  const actual=out.slice(out.findIndex(t=>t[0]===0&&t[1]==='VERTEX'));
  assert.deepEqual(actual,expected);
  if(kind==='polygon'){assert.deepEqual(Array.from(parent.Vertexes,v=>v.X),[0,3,1,4,2,5]);assert.deepEqual(Array.from(parent.VertexRecords,r=>r.Handle),['A1','A4','A2','A5','A3','A6']);}
  if(kind==='legacy'){assert.equal(parent.Elevation,5);assert.equal(parent.Vertexes.get_Item(0).VertexIdentifier,19);assert.equal(parent.GetEffectiveStartWidth(0),2);}
  if(kind==='polyface'){assert.equal(parent.DeclaredVertexCount,123);assert.equal(parent.DeclaredFaceCount,99);assert.deepEqual(Array.from(parent.Faces.get_Item(0).VertexIndexes),[1,-2,3]);}
});
for(const kind of ['polyline','polygon','legacy','polyface'])test('retained '+kind+' rejects a child identity borrowed from another header',()=>{
  assert.throws(()=>retainedFixture(kind,'text',pairs=>{const index=pairs.findIndex(t=>t[0]===0&&t[1]==='VERTEX');const result=pairs.slice();const [identity]=result.splice(index+1,1);result.splice(index+4,0,identity);return result;}),{name:'FormatException'});
});
for(const kind of ['polyline','polygon','legacy','polyface'])test('retained '+kind+' rejects incorrect physical SEQEND ownership',()=>{
  assert.throws(()=>retainedFixture(kind,'text',pairs=>{const index=pairs.findIndex(t=>t[0]===0&&t[1]==='SEQEND');const result=pairs.slice();result[index+2]=[330,'FFFF'];return result;}),{name:'FormatException'});
});
for(const kind of ['polyline','polygon','legacy','polyface'])test('retained '+kind+' preserves private subclass payload across output',()=>{
  const {doc,parent,name}=retainedFixture(kind,'text',pairs=>{const index=pairs.findIndex((t,i)=>i>0&&t[0]===0&&t[1]==='SEQEND');return [...pairs.slice(0,index),[100,'VendorPrivate'],[1,'private-data'],[70,123],...pairs.slice(index)];});
  const out=output();io['WriteStored'+name+'Records'](out,doc,parent);assert.deepEqual(out.tags.filter(t=>t[0]===1),[[1,'private-data']]);assert.ok(Array.from(parent.StoredRecords).some(r=>r.HasPrivateData));
});
test('legacy retained edits rewrite geometry and append missing fields before private data',()=>{
  const {doc,parent}=retainedFixture('legacy','text',pairs=>{const i=pairs.findIndex(t=>t[0]===0&&t[1]==='VERTEX');return [...pairs.slice(0,i),[100,'PrivateParent'],[40,901],...pairs.slice(i)];});
  parent.Elevation=12;parent.LegacyDefaultStartWidth=6;parent.Vertexes.get_Item(0).Position=new api.Vector2(7,8);parent.Vertexes.get_Item(0).EndWidthOverride=4;
  const out=output();io.WriteStoredPolyline2DRecords(out,doc,parent);
  assert.equal(out.tags.find(t=>t[0]===30)[1],12);assert.deepEqual(out.tags.filter(t=>t[0]===40).map(t=>t[1]),[6,901,7]);
  assert.ok(out.tags.some(t=>t[0]===10&&t[1]===7));assert.ok(out.tags.some(t=>t[0]===41&&t[1]===4));
});
test('polyface retained edits preserve advisory counts and regenerate face appearance',()=>{
  const {doc,parent}=retainedFixture('polyface');parent.Normal=new api.Vector3(0,1,0);
  const face=parent.Faces.get_Item(0);face.Color=api.AciColor.FromTrueColor(0x123456);face.Layer=doc.Layers.Add(new api.Layer('EditedFace'));
  const header=output(),records=output();io.WriteStoredPolyfaceMeshHeader(header,doc,parent);io.WriteStoredPolyfaceMeshRecords(records,doc,parent);
  assert.ok(header.tags.some(t=>t[0]===71&&t[1]===123));assert.deepEqual(header.tags.filter(t=>[210,220,230].includes(t[0])),[[210,0],[220,1],[230,0]]);
  assert.ok(records.tags.some(t=>t[0]===8&&t[1]==='EditedFace'));assert.ok(records.tags.some(t=>t[0]===420&&t[1]===-1038994346));
});
test('fitted legacy path retains spline controls rather than fitted output vertices',()=>{
  const context=physicalContext([[100,'AcDb2dPolyline'],[70,4],[75,5],...childPacket('A1','AcDb2dVertex',[1,2,0],16,[[40,-1],[42,0.75]]),...childPacket('A2','AcDb2dVertex',[9,9,0],8),...childPacket('A3','AcDb2dVertex',[3,4,0],16),...endPacket()]);
  seek(context,100,'AcDb2dPolyline');const p=io.ReadStoredPolyline2D(context);assert.equal(p.Vertexes.Count,2);assert.equal(p.Vertexes.get_Item(0).Bulge,0);assert.equal(p.Vertexes.get_Item(0).StartWidth,0);assert.equal(p.SmoothType,5);assert.equal(p.HasStoredRecords,false);
});
test('fitted legacy reader requires SEQEND and preserves source read errors',()=>{
  const context=physicalContext([[100,'AcDb2dPolyline'],[70,4],...childPacket('A1','AcDb2dVertex',[1,2,0],16)]);seek(context,100,'AcDb2dPolyline');assert.throws(()=>io.ReadStoredPolyline2D(context),{name:'FormatException'});
});
test('MULTILEADER vector output snapshots the by-value input before callbacks',()=>{
  const v=new api.Vector3(1,2,3),out=output(),write=out.Write;out.Write=function(c,x){write.call(this,c,x);v.Y=99;v.Z=99;};io.WriteMLeaderVector(out,10,v);assert.deepEqual(out.tags,[[10,1],[20,2],[30,3]]);
});

function columnText(storage,type=1,auto=false){const text=new api.MText('column body'),c=new api.MTextColumns();c.Storage=storage;c.Type=type;c.Count=3;c.Width=12;c.Gutter=2;c.AutoHeight=auto;c.FlowReversed=true;c.TotalHeight=24;
  if(type===2&&!auto)c.Heights.AddRange([24,18,0]);else c.DefinedHeight=24;text.Columns=c;return text;}
for(const transport of ['text','binary','legacy'])for(const [type,auto] of [[1,false],[2,false],[2,true]])test('direct MTEXT columns decode '+type+'/'+auto+' through '+transport,()=>{
  const doc=new api.DxfDocument(17),text=columnText(0,type,auto),reader=roundtrip(w=>io.WriteMTextColumnDefinition(w,doc,text,api.Vector3.UnitX),transport),columns={value:null},height={value:null};
  while(io.TryReadMTextColumnTag(reader,columns,height));const c=columns.value;c.DefinedHeight=height.value??0;c.Validate();
  assert.equal(reader.Code,0);assert.equal(c.Storage,0);assert.equal(c.Type,type);assert.equal(c.Count,3);assert.equal(c.Width,12);assert.equal(c.Gutter,2);assert.equal(c.FlowReversed,true);assert.equal(c.AutoHeight,auto);
  assert.deepEqual(Array.from(c.Heights),type===2&&!auto?[24,18,0]:[]);
});
for(const transport of ['text','binary','legacy'])for(const [type,auto] of [[1,false],[2,false],[2,true]])test('embedded MTEXT columns decode '+type+'/'+auto+' through '+transport,()=>{
  const doc=new api.DxfDocument(18),text=columnText(2,type,auto);text.Position=new api.Vector3(3,4,5);text.Columns.EmbeddedTextDirection=new api.Vector3(0,1,0);text.Columns.EmbeddedReferenceWidth=17;
  const reader=roundtrip(w=>io.WriteMTextColumnDefinition(w,doc,text,api.Vector3.UnitX),transport);assert.equal(reader.Code,46);reader.Next();const c=io.ReadMTextEmbeddedColumns(reader);
  assert.equal(reader.Code,0);assert.equal(c.Storage,2);assert.equal(c.Type,type);assert.equal(c.Count,3);assert.equal(c.StoredTotalWidth,40);assert.equal(c.EmbeddedReferenceWidth,17);assert.deepEqual([c.EmbeddedTextDirection.X,c.EmbeddedTextDirection.Y,c.EmbeddedTextDirection.Z],[0,1,0]);assert.deepEqual([c.EmbeddedInsertionPoint.X,c.EmbeddedInsertionPoint.Y,c.EmbeddedInsertionPoint.Z],[3,4,5]);
  assert.deepEqual(Array.from(c.Heights),type===2&&!auto?[24,18,0]:[]);
});
for(const transport of ['text','binary','legacy'])test('legacy MTEXT column XDATA resolves actual same-block linked entities through '+transport,()=>{
  const doc=new api.DxfDocument(17),text=columnText(1),one=new api.MText('second'),two=new api.MText('third');doc.Entities.Add(one);doc.Entities.Add(two);text.Columns.LinkedColumns.AddRange([one,two]);
  const vendor=new api.XData(new api.ApplicationRegistry('VENDOR'));vendor.XDataRecord.Add(new api.XDataRecord(1000,'retain'));text.XData.Add(vendor);
  const acad=new api.XData(new api.ApplicationRegistry('ACAD'));acad.XDataRecord.Add(new api.XDataRecord(1070,42));text.XData.Add(acad);
  const reader=roundtrip(w=>io.WriteMTextColumnXData(w,doc,text),transport),copy=new api.MText('first');while(reader.Code===1001)copy.XData.Add(ReadXDataRecord(reader,doc));
  io.ReadMTextColumnXData(copy);assert.equal(copy.Columns.PendingHandles.Count,2);assert.equal(copy.Columns.LinkedColumns.Count,0);assert.equal(copy.XData.get_Item('ACAD').XDataRecord.Count,1);assert.equal(copy.XData.get_Item('VENDOR').XDataRecord.get_Item(0).Value,'retain');
  doc.Entities.Add(copy);io.ResolveMTextColumnLinks(doc);assert.equal(copy.Columns.PendingHandles.Count,0);assert.deepEqual(Array.from(copy.Columns.LinkedColumns),[one,two]);assert.equal(copy.Columns.Count,3);io.ValidateMTextColumns(doc);
});
test('legacy MTEXT links replace stale advisory count and ignore exactly zero padded slots',()=>{
  const doc=new api.DxfDocument(17),text=columnText(1),linked=new api.MText();doc.Entities.Add(linked);text.Columns.Count=7;text.Columns.PendingHandles.Add(linked.Handle);doc.Entities.Add(text);
  io.ResolveMTextColumnLinks(doc);assert.equal(text.Columns.Count,2);assert.equal(text.Columns.LinkedColumns.get_Item(0),linked);
  const copy=new api.MText(),data=new api.XData(new api.ApplicationRegistry('ACAD'));const r=(c,v)=>new api.XDataRecord(c,v);
  data.XDataRecord.AddRange([r(1000,'ACAD_MTEXT_COLUMN_INFO_BEGIN'),r(1070,76),r(1070,1),r(1000,'ACAD_MTEXT_COLUMN_INFO_END'),r(1000,'ACAD_MTEXT_COLUMNS_BEGIN'),r(1070,47),r(1070,1),r(1005,'0'),r(1005,'00'),r(1000,'ACAD_MTEXT_COLUMNS_END')]);copy.XData.Add(data);io.ReadMTextColumnXData(copy);
  assert.deepEqual(Array.from(copy.Columns.PendingHandles),['00']);assert.equal(copy.XData.ContainsAppId('ACAD'),false);
});
for(const [storage,version,allowed] of [[0,14,false],[0,15,true],[1,17,true],[1,18,false],[2,17,false],[2,18,true]])test('MTEXT column writer admission storage/version '+storage+'/'+version,()=>{
  const doc=new api.DxfDocument(version),text=columnText(storage);if(storage===1){for(let i=0;i<2;i++){const link=new api.MText();doc.Entities.Add(link);text.Columns.LinkedColumns.Add(link);}}
  doc.Entities.Add(text);if(allowed)io.ValidateMTextColumns(doc);else assert.throws(()=>io.ValidateMTextColumns(doc),{name:'NotSupportedException'});
});
test('MTEXT direct unrecognized tags leave cursor and out parameters unchanged',()=>{
  const reader=textReader([[50,33],[0,'EOF']]),c={value:null},h={value:7};assert.equal(io.TryReadMTextColumnTag(reader,c,h),false);assert.equal(reader.Code,50);assert.equal(c.value,null);assert.equal(h.value,7);
});
for(const value of [-1])test('MTEXT invalid defined height preserves the assigned ref before rejection '+value,()=>{
  const reader=textReader([[46,value],[0,'EOF']]),c={value:null},h={value:null};assert.throws(()=>io.TryReadMTextColumnTag(reader,c,h),{name:'ArgumentOutOfRangeException',ParamName:'DefinedHeight'});assert.equal(h.value,value);assert.equal(reader.Code,46);
});
test('MTEXT direct truncation preserves completed manual heights and current cursor',()=>{
  const reader=textReader([[50,3],[50,10],[40,4],[0,'EOF']]),c={value:columnText(0,2,false).Columns};c.value.Heights.Clear();assert.throws(()=>io.TryReadMTextColumnTag(reader,c,{value:null}),{name:'InvalidDataException'});assert.deepEqual(Array.from(c.value.Heights),[10]);assert.equal(reader.Code,40);
});
for(const value of [-1,2,32767])test('MTEXT boolean flag rejects '+value,()=>assert.throws(()=>io.ReadColumnFlag(value),{name:'InvalidDataException'}));
function embeddedPacket(){const doc=new api.DxfDocument(18),out=output();io.WriteMTextColumnDefinition(out,doc,columnText(2,2,true),api.Vector3.UnitX);return out.tags.slice(1);}
for(const field of [70,41,42,43,71,72,44,45,73,74,20,31])test('embedded MTEXT requires qualified field or complete vector '+field,()=>{
  const packet=embeddedPacket().filter(p=>p[0]!==field);assert.throws(()=>io.ReadMTextEmbeddedColumns(textReader([...packet,[0,'EOF']])),{name:'InvalidDataException'});
});
test('embedded MTEXT scalar duplicates and nonintegral automatic count are rejected',()=>{
  const packet=embeddedPacket();assert.throws(()=>io.ReadMTextEmbeddedColumns(textReader([...packet,[44,12],[0,'EOF']])),{name:'InvalidDataException'});
  assert.throws(()=>io.ReadMTextEmbeddedColumns(textReader([...packet.map(p=>p[0]===42?[42,41]:p),[0,'EOF']])),{name:'InvalidDataException'});
});
test('MTEXT column links preserve completed prefix when a later handle is unresolved',()=>{
  const doc=new api.DxfDocument(17),text=columnText(1),link=new api.MText();doc.Entities.Add(link);text.Columns.PendingHandles.AddRange([link.Handle,'FFFF']);doc.Entities.Add(text);
  assert.throws(()=>io.ResolveMTextColumnLinks(doc),{name:'InvalidDataException'});assert.equal(text.Columns.LinkedColumns.get_Item(0),link);assert.equal(text.Columns.PendingHandles.Count,2);
});
test('MTEXT mixed raw and typed column definitions are rejected before output',()=>{
  const doc=new api.DxfDocument(18),text=columnText(2),acad=new api.XData(new api.ApplicationRegistry('ACAD'));acad.XDataRecord.Add(new api.XDataRecord(1000,'ACAD_MTEXT_COLUMN_INFO_BEGIN'));text.XData.Add(acad);doc.Entities.Add(text);assert.throws(()=>io.ValidateMTextColumns(doc),{name:'InvalidOperationException'});
});
for(const markers of [['ACAD_MTEXT_COLUMN_INFO_BEGIN'],['ACAD_MTEXT_COLUMN_INFO_BEGIN','ACAD_MTEXT_COLUMNS_BEGIN'],['ACAD_MTEXT_COLUMN_INFO_BEGIN','ACAD_MTEXT_COLUMN_INFO_END','ACAD_MTEXT_COLUMN_INFO_BEGIN']])test('MTEXT malformed legacy XDATA markers preserve input '+markers.join('/'),()=>{
  const text=new api.MText(),acad=new api.XData(new api.ApplicationRegistry('ACAD'));acad.XDataRecord.AddRange(markers.map(s=>new api.XDataRecord(1000,s)));text.XData.Add(acad);assert.throws(()=>io.ReadMTextColumnXData(text),{name:'InvalidDataException'});assert.equal(acad.XDataRecord.Count,markers.length);assert.equal(text.Columns,null);
});
test('MTEXT field boxing preserves Int16 versus integral Double and partial append failures',()=>{
  const records=new ReferenceList();io.AddColumnField(records,75,new BoxedScalar('Int16',1));io.AddColumnField(records,48,12);io.AddColumnField(records,49,new BoxedScalar('Double',2));
  assert.deepEqual(Array.from(records,r=>[r.Code,r.Value]),[[1070,75],[1070,1],[1070,48],[1040,12],[1070,49],[1040,2]]);
  assert.throws(()=>io.AddColumnField(records,76,new BoxedScalar('Int32',3)),{name:'ArgumentException'});assert.equal(records.Count,7);assert.equal(records.get_Item(6).Value,76);
});
test('MTEXT embedded output snapshots value arguments and placement across callbacks',()=>{
  const doc=new api.DxfDocument(18),text=columnText(2),v=new api.Vector3(1,2,3),out=output(),write=out.Write;out.Write=function(c,x){write.call(this,c,x);v.Y=99;v.Z=99;if(c===10)text.Position=new api.Vector3(8,9,10);};
  io.WriteMTextColumnDefinition(out,doc,text,v);assert.deepEqual(out.tags.filter(t=>[10,20,30].includes(t[0])),[[10,1],[20,2],[30,3]]);assert.deepEqual(out.tags.filter(t=>[11,21,31].includes(t[0])),[[11,0],[21,0],[31,0]]);
});

for(const value of [NaN,Infinity])test('MTEXT nonfinite text height is rejected at codec admission '+value,()=>assert.throws(()=>textReader([[46,value],[0,'EOF']]),{name:'FormatException'}));

import { storedTablePacket } from '../../tools/stored-table-corpus.mjs';
const packetValue=value=>{if(value===null||typeof value!=='object')return value;if('int' in value)return value.int;if('short' in value)return value.short;if('double' in value){const d=new DataView(new ArrayBuffer(8));d.setBigUint64(0,BigInt('0x'+value.double));return d.getFloat64(0);}return value;};
for(const transport of ['text','binary','legacy'])test('stored TABLE packet, explicit resources and XData roundtrip through '+transport,()=>{
  const doc=new api.DxfDocument(18),display=doc.Blocks.Add(new api.Block('Display'));
  const input=storedTablePacket().map(([code,value])=>[code,packetValue(value)]),data=[[1001,'TABLE_APP'],[1000,'private-xdata']];
  const reader=roundtrip(w=>{for(const pair of [...input,...data])w.Write(...pair);},transport),tables=new ReferenceList();
  const table=io.ReadStoredTable(reader,doc,tables);assert.equal(reader.Code,0);assert.equal(tables.get_Item(0),table);doc.Entities.Add(table);io.ResolveStoredTables(tables);io.ValidateStoredTables(doc,false);
  const out=roundtrip(w=>io.WriteStoredTable(w,doc,table),transport),pairs=[];while(out.Code!==0){pairs.push([out.Code,out.Value]);out.Next();}assert.deepEqual(pairs,[...input,...data]);
  display.Name='ChangedDisplay';const changed=output();io.WriteStoredTable(changed,doc,table);assert.equal(changed.tags.find(pair=>pair[0]===2)[1],'ChangedDisplay');
});
test('stored TABLE rejects payload after XData without registering a partial table',()=>{
  const doc=new api.DxfDocument(18),tables=new ReferenceList(),input=storedTablePacket().map(([c,v])=>[c,packetValue(v)]);
  const reader=roundtrip(w=>{for(const pair of [...input,[1001,'APP'],[1000,'v'],[1,'late']])w.Write(...pair);},'text');
  assert.throws(()=>io.ReadStoredTable(reader,doc,tables),{name:'InvalidDataException'});assert.equal(tables.Count,0);assert.equal(reader.Code,1);
});
function opaqueFixture(transport='text',change=p=>p){
  const doc=new api.DxfDocument(18),block=doc.Layouts.get_Item('Model').AssociatedBlock,layer=doc.Layers.get_Item('0'),line=doc.Linetypes.get_Item('ByLayer');
  const pairs=change([[0,'VENDOR_ENTITY'],[5,'B0'],[330,block.Record.Handle],[100,'AcDbEntity'],[8,'0'],[6,'ByLayer'],[60,0],[100,'VendorData'],[1,'private-payload'],[90,12]]);
  const context=physicalContext(pairs,transport,doc);for(const item of [block.Record,layer,line])source(context,item);seek(context,0,'VENDOR_ENTITY');
  const entity=io.ReadOpaqueEntity(context,false);doc.AddEntityToDocument(entity,false);entity.Owner=block;io.ResolveOpaqueEntities(context);return {doc,context,entity,pairs,block};
}
for(const transport of ['text','binary','legacy'])test('unknown entity physical admission, deferred source resolution and '+transport+' roundtrip',()=>{
  const {doc,entity,pairs}=opaqueFixture(transport),reader=roundtrip(w=>io.WriteOpaqueEntity(w,doc,entity),transport),out=[];
  while(!(reader.Code===0&&reader.Value==='EOF')){out.push([reader.Code,reader.Value]);reader.Next();}assert.deepEqual(out,pairs);entity.Validate(doc);
  entity.IsVisible=false;const changed=output();io.WriteOpaqueEntity(changed,doc,entity);assert.equal(changed.tags.find(t=>t[0]===60)[1],1);assert.ok(changed.tags.some(t=>t[0]===1&&t[1]==='private-payload'));
});
test('unknown entity retains comments and private control groups in ASCII output',()=>{
  const {doc,entity}=opaqueFixture('text',pairs=>[...pairs,[999,'retained comment'],[102,'{VENDOR'],[90,123],[102,'}']]);const out=output();io.WriteOpaqueEntity(out,doc,entity);
  assert.ok(out.tags.some(t=>t[0]===999&&t[1]==='retained comment'));assert.deepEqual(out.tags.slice(-3),[[102,'{VENDOR'],[90,123],[102,'}']]);
});
test('unknown entity rejects unsourced fallback layer instead of treating defaults as physical source',()=>{
  const doc=new api.DxfDocument(18),block=doc.Layouts.get_Item('Model').AssociatedBlock,context=physicalContext([[0,'VENDOR_ENTITY'],[5,'B0'],[330,block.Record.Handle],[100,'AcDbEntity'],[8,'0'],[100,'VendorData'],[1,'payload']],'text',doc);seek(context,0,'VENDOR_ENTITY');
  assert.throws(()=>io.ReadOpaqueEntity(context,false),{name:'InvalidDataException'});assert.equal(context.opaqueEntities.length,0);
});
for(const edit of ['missing-layer','missing-owner','repeated-layer','aggregate'])test('unknown entity rejects '+edit+' framing',()=>{
  assert.throws(()=>opaqueFixture('text',pairs=>edit==='missing-layer'?pairs.filter(p=>p[0]!==8):edit==='missing-owner'?pairs.filter(p=>p[0]!==330):edit==='repeated-layer'?[...pairs.slice(0,7),[8,'0'],...pairs.slice(7)]:[...pairs,[66,1]]),{name:edit==='aggregate'?'NotSupportedException':'InvalidDataException'});
});
test('unknown entity rejects unqualified CLASS declarations and discarded ACDSDATA',()=>{
  const {context}=opaqueFixture();context.hasDiscardedAcdsData=true;assert.throws(()=>io.ResolveOpaqueEntities(context),{name:'NotSupportedException'});
  context.hasDiscardedAcdsData=false;context.unqualifiedOpaqueClasses.add('VENDOR_ENTITY');assert.throws(()=>io.ResolveOpaqueEntities(context),{name:'NotSupportedException'});
});
