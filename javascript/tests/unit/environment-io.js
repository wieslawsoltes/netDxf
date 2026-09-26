import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import * as io from '../../runtime/DatabasePayloadIO.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { plotPacket,geoPacket,sunPacket,environmentIOCorpus } from '../../tools/environment-io-corpus.mjs';
import { ValidateEnvironmentObservation } from '../../tools/environment-io-observation.mjs';
const doc=version=>new api.DxfDocument(version??18);
const tags=(packet,d)=>packet.map(([c,v])=>new api.DxfTag(c,v&&typeof v==='object'&&'h'in v?d.Blocks.get_Item('*Model_Space').Record.Handle:v));
const capture=()=>({tags:[],Write(c,v){this.tags.push(new api.DxfTag(c,v));}});
const state=d=>{void d.Objects;return [d.DrawingVariables.HandleSeed,d.AddedObjects.Count,d.Objects.Items.Count];};
function loadPlot(d=doc(),packet=plotPacket){const context=new io.DatabaseIOContext(d),record=new io.DatabaseRecord();assert.equal(io.ReadOutputSettingsPayload(context,record,'PLOTSETTINGS',tags(packet,d),0),true);return {d,context,record,plot:record.Object.Settings};}
function loadGeo(d=doc(),packet=geoPacket()) {
  const context=new io.DatabaseIOContext(d),record=new io.DatabaseRecord();assert.equal(io.ReadGeoDataPayload(context,record,'GEODATA',tags(packet,d),0),true);
  const host=d.Blocks.get_Item('*Model_Space').Record,extension=new api.DxfDictionary();d.Objects.SetExtensionDictionary(host,extension);
  record.Object.Owner=extension;d.Objects.Register(record.Object,false);extension.AddLoaded('ACAD_GEOGRAPHICDATA',record.Object,true);io.ResolveGeoDataHosts(context);
  return {d,context,record,data:record.Object,host,extension};
}
function accept(context,item){const source=new io.SourceRecordIdentity();source.Handle=BigInt('0x'+item.Handle);source.IdentitySeen=true;context.sourceObjectIdentities.add(source.Handle);context.RecordSourceObject(item,source);}
function loadSun(d=doc(),packet=sunPacket) {
  const context=new io.DatabaseIOContext(d);accept(context,d.Viewport);
  const record=io.ReadSunRecord(context,[new api.DxfTag(5,'F00'),new api.DxfTag(330,d.Viewport.Handle),...tags(packet,d)]);
  record.Object.Owner=d.Viewport;d.Objects.Register(record.Object,true);accept(context,record.Object);io.AddSunReference(context,d.Viewport,'F00');io.ResolveSunReferences(context);
  return {d,context,record,sun:record.Object};
}
for(const [file,names]of [
  ['DxfReader.OutputSettings',['ReadOutputSettingsPayload','ParsePlotSettings','ResolveOutputSettingsReferences']],['DxfWriter.OutputSettings',['WriteOutputSettingsPayload','WritePlotSettingsPayload','ValidateOutputSettings']],
  ['DxfReader.GeoData',['ReadGeoDataPayload','ReadGeoTag','ResolveGeoDataHosts']],['DxfWriter.GeoData',['WriteGeoDataPayload','WriteGeoVector','SplitGeoDefinition','PrepareGeoDataClass']],
  ['DxfReader.Sun',['ReadSunRecord','ReadSunPayload','SunOwnerContext','AddSunReference','ResolveSunReferences','IsNullSourceHandle']],['DxfWriter.Sun',['WriteSunPayload','WriteSunReference','PrepareSunClass']]
])test(file+' standalone imports share the runtime barrel identities',async()=>{
  const module=await import('../../netDxf/IO/'+file+'.js');for(const name of names)assert.equal(module[name],io[name]);
});
test('plot parser keeps signed zero and omitted scale-factor presence without mutating defaults',()=>{
  const {plot}=loadPlot();assert.equal(Object.is(plot.PaperMargin.Left,-0),true);assert.equal(Object.is(plot.PaperImageOrigin.X,-0),true);
  const minimal=loadPlot(doc(),[[100,'AcDbPlotSettings']]);assert.equal(minimal.plot.StandardScaleFactor,null);
  const out=capture();io.WritePlotSettingsPayload(out,18,minimal.plot);assert.equal(out.tags.find(t=>t.Code===147).Value,1);assert.equal(minimal.plot.StandardScaleFactor,null);
});
test('plot settings cannot use the document as a shade reference through padded zero handles',()=>{
  for(const handle of ['00','0000','0000000000000000']) {
    const {d,context,plot}=loadPlot(doc(),plotPacket.map(([c,v])=>[c,c===333?handle:v]));
    const before=state(d);
    assert.throws(()=>io.ResolveOutputSettingsReferences(context),e=>e.name==='FormatException'&&e.InnerException?.name==='ArgumentException'&&e.InnerException.ParamName==='value');
    assert.equal(plot.ShadePlotObject,null);assert.deepEqual(state(d),before);
  }
});
test('plot document-type rejection also applies to standalone property assignments',()=>{
  const plot=new api.PlotSettings();assert.throws(()=>{plot.ShadePlotObject=doc();},{name:'ArgumentException',ParamName:'value'});assert.equal(plot.ShadePlotObject,null);
});
test('shade resolution distinguishes literal zero from unresolved values and graphical identities',()=>{
  const d=doc(),line=new api.Line();d.Entities.Add(line);
  for(const [handle,expected]of [['0',null],['FFFF','Unresolved plot-settings shade reference: FFFF'],[line.Handle,'Invalid plot-settings shade object.']]) {
    const loaded=loadPlot(d,plotPacket.map(([c,v])=>[c,c===333?handle:v]));
    if(expected===null)io.ResolveOutputSettingsReferences(loaded.context);else assert.throws(()=>io.ResolveOutputSettingsReferences(loaded.context),e=>e.message===expected);
  }
});
test('plot shade resolution intentionally uses live document identity rather than accepted-source filtering',()=>{
  const d=doc(),leaf=new api.DxfPlaceholder();d.NamedObjects.Add('shade',leaf);const loaded=loadPlot(d,plotPacket.map(([c,v])=>[c,c===333?leaf.Handle:v]));
  assert.equal(loaded.context.acceptedSourceObjects.size,0);io.ResolveOutputSettingsReferences(loaded.context);assert.equal(loaded.plot.ShadePlotObject,leaf);
});
test('plot duplicate public fields throw while unknown private fields cause whole-payload fallback',()=>{
  const d=doc(),context=new io.DatabaseIOContext(d),record=new io.DatabaseRecord();
  assert.throws(()=>io.ReadOutputSettingsPayload(context,record,'PLOTSETTINGS',tags([[100,'AcDbPlotSettings'],[72,1],[72,1]],d),0),{name:'FormatException'});
  assert.equal(record.Object,null);assert.equal(context.outputShadeReferences.length,0);
  assert.equal(io.ReadOutputSettingsPayload(context,record,'PLOTSETTINGS',tags([...plotPacket,[300,'private']],d),0),false);assert.equal(record.Object,null);
});
test('plot XData failure retains the already-parsed object and pending shade',()=>{
  const d=doc(),context=new io.DatabaseIOContext(d),record=new io.DatabaseRecord();
  assert.throws(()=>io.ReadOutputSettingsPayload(context,record,'PLOTSETTINGS',tags([...plotPacket,[1001,'AFTER_PARSE'],[90,1]],d),0),{name:'FormatException'});
  assert.ok(record.Object instanceof api.DxfPlotSettingsObject);assert.equal(context.outputShadeReferences.length,1);assert.equal(d.ApplicationRegistries.Contains('AFTER_PARSE'),true);
});
test('WIPEOUTVARIABLES defaults and framing follow the original public payload grammar',()=>{
  for(const flag of [null,0,1]) {
    const d=doc(),record=new io.DatabaseRecord(),input=tags([[100,'AcDbWipeoutVariables'],...(flag===null?[]:[[70,flag]])],d);
    assert.equal(io.ReadOutputSettingsPayload(new io.DatabaseIOContext(d),record,'WIPEOUTVARIABLES',input,0),true);assert.equal(record.Object.DisplayFrame,flag===1);
  }
  const d=doc();assert.throws(()=>io.ReadOutputSettingsPayload(new io.DatabaseIOContext(d),new io.DatabaseRecord(),'WIPEOUTVARIABLES',tags([[100,'AcDbWipeoutVariables'],[70,2]],d),0),{name:'FormatException'});
});
test('plot output preflight visits inactive layouts and rejects foreign registered shades',()=>{
  const d=doc(14),foreign=doc(),leaf=new api.DxfPlaceholder();foreign.NamedObjects.Add('shade',leaf);const layout=d.Layouts.Add(new api.Layout('Inactive'));layout.PlotSettings.ShadePlotObject=leaf;
  assert.throws(()=>io.ValidateOutputSettings(d),e=>e.name==='InvalidOperationException'&&e.message.includes('Layout shade references require')&&e.message.includes('not registered in this document'));
});
test('plot output callbacks observe live properties and preserve only completed output',()=>{
  const {plot}=loadPlot(),out=[];io.WritePlotSettingsPayload({Write(c,v){out.push([c,v]);if(c===1)plot.PlotterName='changed';}},18,plot);
  assert.equal(out.find(([c])=>c===2)[1],'changed');const error=new Error('writer'),prefix=[];
  assert.throws(()=>io.WritePlotSettingsPayload({Write(c,v){if(c===44)throw error;prefix.push([c,v]);}},18,plot),e=>e===error);assert.equal(prefix.at(-1)[0],43);
});
test('GEODATA resolves only a BLOCK_RECORD in its reciprocal geographic dictionary',()=>{
  const {d,data,host,extension}=loadGeo();assert.equal(data.HostBlock,host);assert.equal(extension.get_Item('ACAD_GEOGRAPHICDATA'),data);assert.equal(d.Objects.Validate().Count,0);
});
test('GEODATA malformed point counts are bounded by remaining input before allocating',()=>{
  const d=doc(),context=new io.DatabaseIOContext(d),record=new io.DatabaseRecord();
  assert.throws(()=>io.ReadGeoDataPayload(context,record,'GEODATA',tags(geoPacket().map(([c,v])=>[c,c===93?2147483647:v]),d),0),e=>e.message==='GEODATA mesh point count exceeds the available data.');
  assert.equal(record.Object,null);assert.equal(context.geoDataHosts.length,0);
});
test('GEODATA incorrect mesh index cannot install a partial model',()=>{
  const d=doc(),context=new io.DatabaseIOContext(d),record=new io.DatabaseRecord();
  assert.throws(()=>io.ReadGeoDataPayload(context,record,'GEODATA',tags(geoPacket().map(([c,v])=>[c,c===99?3:v]),d),0),e=>e.message==='GEODATA face index is outside the point array.');
  assert.equal(record.Object,null);
});
test('GEODATA missing public fields remain errors while unknown versions stay opaque',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),r=new io.DatabaseRecord();
  assert.equal(io.ReadGeoDataPayload(ctx,r,'GEODATA',tags(geoPacket().map(([c,v])=>[c,c===90?7:v]),d),0),false);
  assert.throws(()=>io.ReadGeoDataPayload(ctx,r,'GEODATA',tags(geoPacket().filter(([c])=>c!==10),d),0),e=>e.message==='Missing GEODATA scalar group: 10');
});
test('GEODATA coordinate chunks join before one-pass DXF decoding and newline expansion',()=>{
  const d=doc(),packet=geoPacket().flatMap(([c,v])=>c===301?[[303,'\\U+'],[301,'03A9^Jtail']]:[[c,v]]),{data}=loadGeo(d,packet);
  assert.equal(data.CoordinateSystemDefinition,'Ω\ntail');
  assert.throws(()=>loadGeo(doc(),geoPacket().flatMap(([c,v])=>c===301?[[301,'end'],[303,'extra']]:[[c,v]])),{name:'FormatException'});
});
test('GEODATA output never splits a UTF-16 pair or seven-character escape token',()=>{
  for(const token of ['😀','\\U+03A9'])for(const prefix of [248,249,250,251,252,253,254,255]) {
    const text='x'.repeat(prefix)+token+'tail',parts=io.SplitGeoDefinition(text);assert.equal(parts.join(''),text);assert.ok(parts.every(p=>p.length<=255));assert.ok(parts.some(p=>p.includes(token)));
  }
  assert.deepEqual(io.SplitGeoDefinition(''),['']);assert.throws(()=>io.SplitGeoDefinition(null),{name:'NullReferenceException'});
});
test('GEODATA validates Unicode controls without normalizing its field semantics',()=>{
  assert.throws(()=>loadGeo(doc(),geoPacket({definition:'\\U+0000'})),e=>e.name==='FormatException'&&e.InnerException?.ParamName==='value');
  const {data}=loadGeo(doc(),geoPacket({definition:'literal\\U+005CU+0041'}));assert.equal(data.CoordinateSystemDefinition,'literal\\U+0041');
});
test('GEODATA XData failure leaves no pending host but retains original registry effects',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),r=new io.DatabaseRecord();
  assert.throws(()=>io.ReadGeoDataPayload(ctx,r,'GEODATA',tags([...geoPacket(),[1001,'GEOTEST'],[90,1]],d),0),{name:'FormatException'});
  assert.equal(r.Object,null);assert.equal(ctx.geoDataHosts.length,0);assert.equal(d.ApplicationRegistries.Contains('GEOTEST'),true);
});
test('GEODATA host validation retains the bound host even when later ownership fails',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),r=new io.DatabaseRecord();io.ReadGeoDataPayload(ctx,r,'GEODATA',tags(geoPacket(),d),0);
  assert.throws(()=>io.ResolveGeoDataHosts(ctx),{name:'FormatException'});assert.equal(r.Object.HostBlock,d.Blocks.get_Item('*Model_Space').Record);
});
test('GEODATA vector writer takes a value-copy before invoking callbacks',()=>{
  const value=new api.Vector3(1,2,3),out=[];io.WriteGeoVector({Write(c,v){out.push([c,v]);value.Y=99;}},10,value);assert.deepEqual(out,[[10,1],[20,2],[30,3]]);
});
test('GEODATA writer uses versioned iterators so a mesh mutation fails after the completed prefix',()=>{
  const {data}=loadGeo(),out=[];
  assert.throws(()=>io.WriteGeoDataPayload({Write(c,v){out.push([c,v]);if(c===13)data.MeshPoints.Clear();}},18,data),{name:'InvalidOperationException'});
  assert.equal(out.at(-1)[0],24);assert.equal(data.MeshPoints.Count,0);
});
test('GEODATA CLASS preparation counts matching opaque instances and preserves compatible metadata',()=>{
  const {d}=loadGeo(),opaque=new api.DxfOpaqueObject('GEODATA',[]);d.NamedObjects.Add('Opaque',opaque);
  const entry=new api.DxfClass('GEODATA','AcDbGeoData','Caller');entry.ProxyFlags=7;d.Classes.Add(entry);io.PrepareGeoDataClass(d,d.Classes);
  assert.equal(d.Classes.get_Item('GEODATA'),entry);assert.equal(entry.InstanceCount,2);assert.equal(entry.ApplicationName,'Caller');assert.equal(entry.ProxyFlags,7);
});
test('GEODATA CLASS preparation leaves declarations alone without a typed object',()=>{
  const d=doc(),entry=new api.DxfClass('GEODATA','Private','Caller');entry.InstanceCount=9;d.Classes.Add(entry);io.PrepareGeoDataClass(d,d.Classes);assert.equal(entry.InstanceCount,9);
});
test('GEODATA binary public payload can be decoded without changing mesh scalars',()=>{
  const {data,d}=loadGeo(),stream=new api.MemoryStream(),writer=new BinaryCodeValueWriter(stream);io.WriteGeoDataPayload(writer,18,data);writer.Write(0,'ENDSEC');writer.Flush();stream.Position=0;
  const reader=new BinaryCodeValueReader(stream),packet=[];reader.Next();while(reader.Code!==0){packet.push(new api.DxfTag(reader.Code,reader.Value));reader.Next();}
  const record=new io.DatabaseRecord(),ctx=new io.DatabaseIOContext(d);assert.equal(io.ReadGeoDataPayload(ctx,record,'GEODATA',packet,0),true);assert.equal(record.Object.MeshPoints.Count,3);assert.equal(Object.is(record.Object.DesignPoint.X,-0),true);
});
test('SUN resolves reciprocal accepted-source owner slots with original identity checks',()=>{
  const {d,sun}=loadSun();assert.equal(d.Viewport.Sun,sun);assert.equal(d.Viewport.SunHandlePresent,true);assert.equal(d.Objects.Validate().Count,0);
});
test('SUN source ownership cannot be satisfied by a generated or ambiguous default',()=>{
  const {d,context}=loadSun();context.acceptedSourceRecords.get(BigInt('0x'+d.Viewport.Handle)).Ambiguous=true;
  assert.throws(()=>io.ResolveSunReferences(context),e=>e.message==='SUN owner was not retained from its source record.');
});
test('SUN semantic source null accepts padded zero while invalid syntax remains nonnull',()=>{
  for(const value of ['0','000000000000000000000','0\0','00\0\0'])assert.equal(io.IsNullSourceHandle(value),true);
  for(const value of [null,'',' 0','0 ','0x0','G','10000000000000000'])assert.equal(io.IsNullSourceHandle(value),false);
});
test('SUN distinguishes repeated ownership declarations by object identity',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d);io.AddSunReference(ctx,d.Viewport,'0');assert.throws(()=>io.AddSunReference(ctx,d.Viewport,'F00'),{name:'FormatException'});assert.equal(ctx.sunReferences.length,1);
});
test('SUN private headers and unknown payload versions remain wholly opaque',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),header=[[5,'F00'],[102,'{PRIVATE'],[1,'kept'],[102,'}']];
  const r=io.ReadSunRecord(ctx,tags([...header,...sunPacket],d));assert.ok(r.Object instanceof api.DxfOpaqueObject);assert.equal(r.Object.Tags.get_Item(0).Value,'{PRIVATE');
  const v=io.ReadSunRecord(ctx,tags([[5,'F01'],...sunPacket.map(([c,x])=>[c,c===90?2:x])],d));assert.ok(v.Object instanceof api.DxfOpaqueObject);
});
test('SUN common controls preserve repeated reactors and exact extension slot',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),r=io.ReadSunRecord(ctx,tags([[5,'F00'],[330,'A'],[102,'{ACAD_REACTORS'],[330,'B'],[330,'B'],[102,'}'],[102,'{ACAD_XDICTIONARY'],[360,'C'],[102,'}'],...sunPacket],d));
  assert.deepEqual(r.Metadata,{Owner:'A',Extension:'C',Reactors:['B','B']});assert.ok(r.Object instanceof api.DxfSun);
});
test('SUN record preservation has no spread-argument limit for long private headers',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),input=[new api.DxfTag(5,'F00'),...Array.from({length:70000},()=>new api.DxfTag(1,'private')),...tags(sunPacket,d)];
  const record=io.ReadSunRecord(ctx,input);assert.ok(record.Object instanceof api.DxfOpaqueObject);assert.equal(record.Object.Tags.Count,70000+sunPacket.length);
});
test('SUN public grammar distinguishes duplicate marker, unknown version, and ordered fields',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),r=new io.DatabaseRecord();
  assert.throws(()=>io.ReadSunPayload(ctx,r,tags([...sunPacket,[100,'AcDbSun']],d),0),e=>e.message==='SUN repeats its public subclass marker.');
  assert.equal(io.ReadSunPayload(ctx,r,tags(sunPacket.map(([c,v])=>[c,c===90?99:v]),d),0),false);
  assert.throws(()=>io.ReadSunPayload(ctx,r,tags(sunPacket.filter(([c])=>c!==290),d),0),e=>e.message==='SUN requires ordered group 290.');
});
test('SUN setter failures preserve their native wrapped error before registration',()=>{
  const d=doc(),ctx=new io.DatabaseIOContext(d),r=new io.DatabaseRecord();
  assert.throws(()=>io.ReadSunPayload(ctx,r,tags(sunPacket.map(([c,v])=>[c,c===71?65:v]),d),0),e=>e.name==='FormatException'&&e.InnerException?.name==='ArgumentOutOfRangeException'&&e.InnerException.ParamName==='value');assert.equal(r.Object,null);
});
test('SUN public-owner context ignores private control scopes and never leaves XData scope',()=>{
  const c=new io.SunOwnerContext('AcDbViewport');assert.equal(c.IsPublic,true);c.Observe(102,'{A');c.Observe(100,'Private');assert.equal(c.IsPublic,false);c.Observe(102,'}');assert.equal(c.IsPublic,true);
  c.Observe(1001,'APP');c.Observe(100,'AcDbViewport');assert.equal(c.IsPublic,false);
});
test('SUN owner version restrictions are checked before writing any owner reference',()=>{
  const d=doc(),view=d.Views.Add(new api.View('Named'));api.SunReferences.Set(view,null);const out=capture();
  assert.throws(()=>io.WriteSunReference(out,15,view),{name:'NotSupportedException'});assert.equal(out.tags.length,0);io.WriteSunReference(out,16,view);assert.equal(out.tags[0].Value,'0');
});
test('SUN output preserves missing true-color instead of synthesizing its model default',()=>{
  const {sun}=loadSun(doc(),sunPacket.filter(([c])=>c!==421)),out=capture();assert.equal(sun.TrueColor,null);io.WriteSunPayload(out,18,sun);assert.equal(out.tags.some(t=>t.Code===421),false);
});
test('SUN writer callbacks can change later fields without corrupting already-delivered values',()=>{
  const {sun}=loadSun(),out=[];io.WriteSunPayload({Write(c,v){out.push([c,v]);if(c===63)sun.TrueColor=null;}},18,sun);assert.equal(out.some(([c])=>c===421),false);assert.equal(out.find(([c])=>c===63)[1],7);
});
test('environment corpus counts and complete observation envelopes are enforced',()=>{
  const c=environmentIOCorpus();assert.equal(c.length,1760);assert.equal(c.reduce((n,p)=>n+p.request.steps.length,0),10173);assert.deepEqual(c,environmentIOCorpus());assert.equal(new Set(c.map(p=>p.name)).size,1760);assert.ok(c.every(p=>!Object.hasOwn(p,'expected')));
});
for(const invalid of [null,[],[{}],[{ok:true,value:{}}]])test('environment observation refuses malformed result '+JSON.stringify(invalid),()=>assert.throws(()=>ValidateEnvironmentObservation(invalid,1)));
