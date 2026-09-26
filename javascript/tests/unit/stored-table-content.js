import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { DecodeTableText } from '../../runtime/TablePayload.js';
import { tableContentPacket, tableContentCorpus } from '../../tools/table-content-corpus.mjs';
import { storedTablePacket, storedTableCorpus } from '../../tools/stored-table-corpus.mjs';
import { fromBits } from '../../tools/wire.mjs';
const input=value=>value&&typeof value==='object'?'int'in value?value.int:'short'in value?value.short:'double'in value?fromBits(value.double):value:value;
const tags=packet=>packet.map(([code,value])=>new api.DxfTag(code,input(value)));
const document=()=>new api.DxfDocument(api.DxfVersion.AutoCad2018);
function content(doc=document(),packet=tableContentPacket(),name='content') { const value=new api.DxfStoredTableContent(doc,tags(packet),DecodeTableText);doc.NamedObjects.Add(name,value);value.Resolve(h=>doc.StoredTableHandleTarget(h));return value; }
function table(doc=document(),packet=storedTablePacket(),resolve=true){const value=new api.StoredTable(doc,tags(packet),DecodeTableText);doc.Entities.Add(value);if(resolve)value.Resolve();return value;}
for(const [code,kind,value] of [[90,'Int32',17],[70,'Int16',2],[140,'Double',-0],[160,'Int64',123n]])test(`DxfTag accepts only the mapped explicit scalar box at group ${code}`,()=>{
  const tag=new api.DxfTag(code,new BoxedScalar(kind,value));assert.equal(tag.Value,value);
  const wrong=kind==='Int32'?'Double':'Int32';assert.throws(()=>new api.DxfTag(code,new BoxedScalar(wrong,17)),{name:'ArgumentException',ParamName:'value'});
});
test('TABLECONTENT barrel and original paths use the same public identities',async()=>{
  const standalone=await import('../../netDxf/Objects/DxfStoredTableContent.js'),scalar=await import('../../netDxf/Objects/DxfStoredTableContent.Value.js');
  assert.equal(api.DxfStoredTableContent,standalone.DxfStoredTableContent);assert.equal(api.DxfStoredTableContentValue,scalar.DxfStoredTableContentValue);
});
test('TABLECONTENT exposes the four scalar kinds without guessing cell addresses',()=>{
  const c=content();assert.deepEqual(Array.from(c.StoredValues,v=>v.Kind),[1,2,4,32]);assert.equal(c.ColumnCount,0);assert.equal(c.RowCount,1);
  assert.equal(c.Subclasses.Count,4);assert.equal(c.StoredValues.get_Item(1).Value,-0);assert.equal(c.StoredValues.get_Item(3).Value.X,-0);
});
test('TABLECONTENT edits are immutable and copy Vector3 values at every boundary',()=>{
  const c=content(),value=c.StoredValues.get_Item(3),p=new api.Vector3(4,5,6),e=value.WithValue(p,'display');p.X=99;e.Value.Y=99;value.Value.Z=99;
  assert.deepEqual(e.Value.ToArray(),[4,5,6]);assert.equal(value.Value.Z,3);assert.throws(()=>{e.FormattedText='changed';},TypeError);
});
test('TABLECONTENT rejects explicit wrong scalar boxes even when numeric values compare equal',()=>{
  const c=content();assert.throws(()=>c.StoredValues.get_Item(0).WithValue(new BoxedScalar('Double',1),'display'),{name:'ArgumentException',ParamName:'value'});
  assert.throws(()=>c.StoredValues.get_Item(1).WithValue(new BoxedScalar('Int32',1),'display'),{name:'ArgumentException',ParamName:'value'});
});
test('TABLECONTENT no-op preserves containers; accepted edit invalidates all old value identities',()=>{
  const c=content(),payload=c.Payload,values=c.StoredValues,subclasses=c.Subclasses;
  c.ReplaceContent(c.Name,c.Description,null,Array.from(values,v=>v.WithValue(v.Value,v.FormattedText)));
  assert.equal(c.Payload,payload);assert.equal(c.StoredValues,values);assert.equal(c.Subclasses,subclasses);
  c.ReplaceContent('changed',c.Description,null,[]);assert.notEqual(c.StoredValues,values);assert.equal(payload.get_Item(1).Value,'name');
  assert.throws(()=>c.ReplaceContent('changed',c.Description,null,[values.get_Item(0).WithValue(1,'display')]),{name:'ArgumentException',ParamName:'values'});
});
test('TABLECONTENT positive and negative double zero are distinct edits',()=>{
  const c=content(),payload=c.Payload,v=c.StoredValues.get_Item(1);c.ReplaceContent(c.Name,c.Description,null,[v.WithValue(0,'display')]);assert.notEqual(c.Payload,payload);assert.equal(Object.is(c.StoredValues.get_Item(1).Value,0),true);
});
test('TABLECONTENT header and scalar text escaping retain literal escape sequences',()=>{
  const c=content(),e=c.StoredValues.get_Item(2).WithValue('Literal\\U+0041😀','Shown\\U+0042');
  c.ReplaceContent('N\\U+0043','D😀',null,[e]);assert.equal(c.Name,'N\\U+0043');assert.equal(c.StoredValues.get_Item(2).Value,'Literal\\U+0041😀');
  assert.ok(Array.from(c.Payload).some(t=>t.Code===1&&String(t.Value).includes('\\U+005C')));
});
for(const text of ['TABLEFORMAT_BEGIN','CELLCONTENT_BEGIN','DATAMAP_BEGIN','ACVALUE_END'])test('TABLECONTENT recognizes scalar text that resembles '+text,()=>{
  const c=content(document(),tableContentPacket({text}));assert.equal(c.StoredValues.Count,4);assert.equal(c.StoredValues.get_Item(2).Value,text);
});
test('TABLECONTENT R2004 values reject display strings but accept null display',()=>{
  const doc=new api.DxfDocument(api.DxfVersion.AutoCad2004),c=content(doc,tableContentPacket({version:14})),v=c.StoredValues.get_Item(0);
  assert.equal(v.FormattedText,null);assert.throws(()=>v.WithValue(1,''),{name:'ArgumentException',ParamName:'formattedText'});c.ReplaceContent(c.Name,c.Description,null,[v.WithValue(1)]);assert.equal(c.StoredValues.get_Item(0).Value,1);
});
for(const stage of ['get','move','current','dispose'])test('TABLECONTENT rejects caught recursive editing during '+stage,()=>{
  const c=content(),before=c.Payload,e=c.StoredValues.get_Item(0).WithValue(1,'display'),call=phase=>{if(phase===stage)assert.throws(()=>c.ReplaceContent(c.Name,c.Description,null,[]),{name:'InvalidOperationException'});};
  const source={GetEnumerator(){call('get');let at=0;return{MoveNext(){call('move');return at++===0;},get Current(){call('current');return e;},Dispose(){call('dispose');}};}};
  assert.throws(()=>c.ReplaceContent('changed',c.Description,null,source),{name:'InvalidOperationException'});assert.equal(c.Payload,before);c.ReplaceContent(c.Name,c.Description,null,[]);
});
test('TABLECONTENT preserves caller side effects while rejecting invalidated source',()=>{
  const c=content(),doc=c.Database.Document,before=c.Payload;const source={GetEnumerator(){return{MoveNext:()=>false,Dispose(){doc.DrawingVariables.AcadVer=17;}};}};
  assert.throws(()=>c.ReplaceContent('changed',c.Description,null,source),{name:'InvalidOperationException'});assert.equal(c.Payload,before);assert.equal(doc.DrawingVariables.AcadVer,17);
});
test('TABLECONTENT stops endless edit enumeration at the current inventory bound and disposes once',()=>{
  const c=content(),e=c.StoredValues.get_Item(0).WithValue(2,'display');let current=0,disposed=0;
  assert.throws(()=>c.ReplaceContent(c.Name,c.Description,null,{GetEnumerator(){return{MoveNext:()=>true,get Current(){current++;return e;},Dispose(){disposed++;}};}}),{name:'ArgumentException',ParamName:'values'});
  assert.equal(current,c.StoredValues.Count+1);assert.equal(disposed,1);
});
test('TABLECONTENT malformed framing is rejected without registering an object',()=>{
  const doc=document(),before=doc.DrawingVariables.HandleSeed,bad=tableContentPacket();bad.splice(bad.length-2,0,[1,'TABLEFORMAT_BEGIN']);
  assert.throws(()=>new api.DxfStoredTableContent(doc,tags(bad),DecodeTableText),{name:'FormatException'});assert.equal(doc.DrawingVariables.HandleSeed,before);
});
test('TABLECONTENT stored references prevent removal and include owner-held attributes',()=>{
  const doc=document(),block=new api.Block('B');block.AttributeDefinitions.Add(new api.AttributeDefinition('TAG'));const insert=new api.Insert(block);doc.Entities.Add(insert);const attribute=insert.Attributes.get_Item(0);
  assert.equal(doc.GetObjectByHandle(attribute.Handle),null);
  const c=content(doc,tableContentPacket({extra:[[330,attribute.Handle],[340,attribute.Handle]]}));assert.equal(c.References.get_Item(0),attribute);assert.equal(c.References.get_Item(1),attribute);
  assert.equal(doc.Entities.Remove(insert),false);
});
test('TABLECONTENT validates UTF16 NUL and encoded-length limits before commit',()=>{
  const doc=new api.DxfDocument(api.DxfVersion.AutoCad2004),c=content(doc,tableContentPacket({version:14})),before=c.Payload;
  for(const name of ['a\0b','\ud800'])assert.throws(()=>c.ReplaceContent(name,'description',null,[]),{name:'ArgumentException',ParamName:'name'});
  assert.throws(()=>c.ReplaceContent('漢'.repeat(149797),'description',null,[]),{name:'ArgumentOutOfRangeException',ParamName:'text'});
  assert.throws(()=>c.ReplaceContent('x'.repeat(1048577),'description',null,[]),{name:'ArgumentOutOfRangeException',ParamName:'name'});assert.equal(c.Payload,before);
});
test('TABLECONTENT metadata-inclusive record count is validated even on no-op',()=>{
  const c=content(),input=new api.XData(new api.ApplicationRegistry('LIMIT'));c.XData.Add(input);const data=c.XData.get_Item('LIMIT');
  assert.notEqual(data,input); // Registered metadata has an independent stored copy.
  // A virtual size is not used: these are actual retained records in the model.
  const record=new api.XDataRecord(api.XDataCode.Int16,1);for(let i=0;i<1048576-c.Payload.Count;i++)data.XDataRecord.Add(record);
  assert.throws(()=>c.ReplaceContent(c.Name,c.Description,null,[]),{name:'InvalidOperationException'});
});
test('ACAD_TABLE preserves source normal without silently normalizing its public projection',()=>{
  const t=table();assert.deepEqual(t.Normal.ToArray(),[0,0,2]);t.Normal.Z=8;assert.equal(t.Normal.Z,2);
  t.Normal=new api.Vector3(0,0,2);assert.throws(()=>{t.Normal=api.Vector3.UnitZ;},{name:'NotSupportedException'});assert.equal(t.Position.X,-0);
});
test('ACAD_TABLE fixed-grid indexing is bounded and immutable',()=>{
  const t=table(),grid=t.Grid;assert.equal(grid.Cells.Count,3);assert.equal(grid.get_Item(0,1).LiteralValue,-0);
  assert.throws(()=>grid.get_Item(-1,0),{name:'ArgumentOutOfRangeException',ParamName:'row'});assert.throws(()=>grid.get_Item(0,3),{name:'ArgumentOutOfRangeException',ParamName:'column'});assert.throws(()=>grid.Cells.Clear(),{name:'NotSupportedException'});
});
test('ACAD_TABLE FIELD references take precedence over otherwise valid literal frames',()=>{
  const packet=storedTablePacket({columns:1,types:[4],values:['literal']});packet.splice(packet.findIndex(t=>t[0]===301),0,[344,'FF']);const t=table(document(),packet),cell=t.Grid.get_Item(0,0);assert.equal(cell.HasFieldReference,true);assert.equal(cell.HasLiteralValue,false);assert.equal(cell.LiteralValue,null);
});
test('ACAD_TABLE accepts only exact 3x3 and 4x4 identity transforms',()=>{
  const t=table();t.TransformBy(api.Matrix3.Identity,api.Vector3.Zero);t.TransformBy(api.Matrix4.Identity);
  const matrix=api.Matrix4.Identity;matrix.M41=1;assert.throws(()=>t.TransformBy(matrix),{name:'NotSupportedException'});assert.throws(()=>t.TransformBy(api.Matrix3.Identity,new api.Vector3(0,0,1)),{name:'NotSupportedException'});assert.throws(()=>t.Clone(),{name:'NotSupportedException'});
});
test('ACAD_TABLE resource rename projection excludes private packets and blocks removal',()=>{
  const doc=document(),style=doc.TextStyles.Add(new api.TextStyle('Named','txt.shx')),display=doc.Blocks.Add(new api.Block('Display')),t=table(doc,storedTablePacket({style:'Named',tail:[[102,'{PRIVATE'],[7,'Named'],[102,'}']]}));
  assert.equal(doc.TextStyles.Remove(style),false);assert.equal(doc.Blocks.Remove(display),false);style.Name='Renamed';display.Name='Moved';
  assert.equal(t.ChangedResourceName(Array.from(t.Payload).find(tag=>tag.Code===2)),'Moved');const names=Array.from(t.Payload).filter(tag=>tag.Code===7).map(tag=>t.ChangedResourceName(tag));assert.deepEqual(names,['Renamed','Renamed','Renamed','Renamed',null]);
});
test('ACAD_TABLE removal retires the instance and prevents detached-block reattachment',()=>{
  const doc=document(),t=table(doc);assert.equal(doc.Entities.Remove(t),true);assert.equal(t.Handle,null);assert.throws(()=>doc.Entities.Add(t),{name:'InvalidOperationException'});assert.throws(()=>new api.Block('Detached').Entities.Add(t),{name:'InvalidOperationException'});
});
test('ACAD_TABLE pending fixtures cannot be adopted by another document through nested blocks',()=>{
  const doc=document(),t=new api.StoredTable(doc,tags(storedTablePacket()),DecodeTableText),block=new api.Block('B');block.Entities.Add(t);const other=document();assert.throws(()=>other.Entities.Add(new api.Insert(block)),{name:'InvalidOperationException'});assert.equal(other.Entities.All.Count,0);
});
test('TABLECONTENT edits update backing agreement but do not rewrite ACAD_TABLE literals',()=>{
  const doc=document(),t=table(doc,storedTablePacket({columns:1,types:[1],values:[{int:-2147483648}]}),false),ext=new api.DxfDictionary(),record=new api.DxfXRecord();ext.Add('Roundtrip',record);doc.Objects.SetExtensionDictionary(t,ext);
  const c=new api.DxfStoredTableContent(doc,tags(tableContentPacket({types:[1]})),DecodeTableText);c.Owner=record;doc.Objects.Register(c,false);c.Resolve(h=>doc.StoredTableHandleTarget(h));t.Resolve();assert.equal(t.BackingLiteralValuesAgree,true);
  const before=t.Payload;c.ReplaceContent(c.Name,c.Description,null,[c.StoredValues.get_Item(0).WithValue(123,'123')]);assert.equal(t.BackingLiteralValuesAgree,false);assert.equal(t.Payload,before);assert.equal(t.Grid.get_Item(0,0).LiteralValue,-2147483648);assert.equal(doc.Entities.Remove(t),false);
});
test('TABLECONTENT owned by ACAD_TABLE cannot rebind its style independently',()=>{
  const doc=document(),t=table(doc,storedTablePacket(),false),ext=new api.DxfDictionary(),record=new api.DxfXRecord();ext.Add('Roundtrip',record);doc.Objects.SetExtensionDictionary(t,ext);
  const c=new api.DxfStoredTableContent(doc,tags(tableContentPacket()),DecodeTableText);c.Owner=record;doc.Objects.Register(c,false);c.Resolve(h=>doc.StoredTableHandleTarget(h));t.Resolve();
  const style=new api.DxfTableStyle(doc,[new api.DxfTag(100,'AcDbTableStyle')],DecodeTableText);doc.NamedObjects.Add('Style',style);style.Resolve(h=>doc.StoredTableHandleTarget(h),DecodeTableText);
  assert.throws(()=>c.ReplaceContent(c.Name,c.Description,style,[]),{name:'NotSupportedException'});assert.equal(c.TableStyle,null);
});
test('both retained-table corpora are deterministic and contain no embedded expected results',()=>{
  for(const factory of [tableContentCorpus,storedTableCorpus]){const cases=factory();assert.deepEqual(cases,factory());assert.equal(new Set(cases.map(p=>p.name)).size,cases.length);assert.ok(cases.every(p=>!Object.hasOwn(p,'expected')));}
});
