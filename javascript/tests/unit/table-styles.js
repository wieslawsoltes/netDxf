import test from 'node:test';
import assert from 'node:assert/strict';
import * as a from '../../index.js';
import { DecodeTableText, EncodeTableText, CheckTableText, TableSnapshot, TableHandleMap } from '../../runtime/TablePayload.js';
import { tableStylePacket, cellMapPacket, tableStyleCorpus } from '../../tools/table-style-corpus.mjs';
import { tableStyleSnapshot } from '../../tools/table-style-wire.mjs';
const read=v=>v&&typeof v==='object'?'short'in v?v.short:'int'in v?v.int:'double'in v?-0:v:v;
const tags=rows=>rows.map(([c,v])=>new a.DxfTag(c,read(v)));
function document(version=18){const d=new a.DxfDocument(version);d.TextStyles.Add(new a.TextStyle('STYLE_REF','txt.shx'));d.TextStyles.Add(new a.TextStyle('ALT','txt.shx'));return d;}
function style(d=document(),options={}){const value=new a.DxfTableStyle(d,tags(tableStylePacket(options)),DecodeTableText);d.NamedObjects.Add('STYLE',value);value.Resolve(h=>d.StoredTableHandleTarget(h),DecodeTableText);return value;}
function map(d=document(),count=2){const value=new a.DxfStoredCellStyleMap(d,tags(cellMapPacket(count)),DecodeTableText);d.NamedObjects.Add('MAP',value);value.Resolve(h=>d.StoredTableHandleTarget(h));return value;}
const changed=()=>new a.DxfTableStyleRowValues(4.5,-12,-1,256,false);
function immutable(object){for(const key of Object.keys(object))assert.throws(()=>{object[key]=null;},TypeError);}

test('table-style constructors are available through barrel and standalone modules',async()=>{
  for(const [file,names] of [['DxfTableStyle',['DxfTableStyle']],['DxfTableStyle.Projection',['DxfTableStyleHeader','DxfTableStyleRow','DxfTableStyleRowEdit','DxfTableStyleRowValues']],['DxfTableStyle.Borders',['DxfTableStyleBorderValues','DxfTableStyleRowBorders']],['DxfTableStyle.DataTypes',['DxfTableStyleRowDataTypes']],['DxfStoredCellStyleMap',['DxfStoredCellStyleMap','DxfStoredCellStyleMapEntry']]]){
    const module=await import(`../../netDxf/Objects/${file}.js`);for(const name of names)assert.equal(module[name],a[name]);
  }
});
test('table-style projections preserve source order and all opaque/private tags',()=>{
  const value=style(undefined,{privateGroups:true});assert.equal(value.Rows.Count,3);assert.equal(value.Tags.Count,110);
  assert.equal(value.Rows.get_Item(0).Values.TextHeight,1);assert.equal(value.Rows.get_Item(2).Values.TextHeight,3);
  assert.equal(value.Rows.get_Item(0).Tags.Count,27);assert.equal(value.Database.Validate().Count,0);
});
test('classic and leading-version header projections are source-profile specific',()=>{
  assert.equal(style(document(14),{versioned:true}).Header,null);assert.equal(style(document(16),{versioned:true}).Header.StoredVersion,0);
  assert.equal(style(document(15)).Header.StoredVersion,null);
});
test('equal style edits preserve packet, header and row snapshot identities and seed',()=>{
  const value=style(),packet=value.Tags,rows=value.Rows,header=value.Header,seed=value.Database.Document.DrawingVariables.HandleSeed;
  value.ReplaceStyle(header,Array.from(rows,row=>row.WithValues(row.Values).WithBorders(row.Borders).WithDataTypes(row.DataTypes)));
  assert.equal(value.Tags,packet);assert.equal(value.Rows,rows);assert.equal(value.Header,header);assert.equal(value.Database.Document.DrawingVariables.HandleSeed,seed);
});
test('changed style edits retain old rows and reuse every untouched tag object',()=>{
  const value=style(),before=tableStyleSnapshot(value),packet=value.Tags,rows=value.Rows,row=rows.get_Item(0);
  value.ReplaceStyle(null,[row.WithValues(changed())]);assert.notEqual(value.Tags,packet);assert.notEqual(value.Rows,rows);
  assert.deepEqual(Array.from(packet,t=>tableStyleSnapshot(t)),before.tags);assert.equal(row.Values.TextHeight,1);
  const replaced=new Set([140,170,62,63,283]),start=Array.from(packet).findIndex(t=>t.Code===7),next=Array.from(packet).findIndex((t,i)=>i>start&&t.Code===7);
  Array.from(packet).forEach((tag,i)=>{if(i<=start||i>=next||!replaced.has(tag.Code))assert.equal(value.Tags.get_Item(i),tag);});
});
test('stale and duplicate row edits reject before packet/handle mutation',()=>{
  const value=style(),row=value.Rows.get_Item(0),edit=row.WithValues(changed());value.ReplaceStyle(null,[edit]);const packet=value.Tags;
  assert.throws(()=>value.ReplaceStyle(null,[edit]),{name:'ArgumentException',ParamName:'rows'});assert.equal(value.Tags,packet);
  const fresh=value.Rows.get_Item(0).WithValues(changed());assert.throws(()=>value.ReplaceStyle(null,[fresh,fresh]),{name:'ArgumentException',ParamName:'rows'});assert.equal(value.Tags,packet);
});
test('row edit composition preserves the original identity and every selected field',()=>{
  const value=style(),row=value.Rows.get_Item(1),border=new a.DxfTableStyleBorderValues(-32768,false,32767);
  const edit=row.WithValues(changed()).WithBorders(row.Borders.WithBorder(3,border)).WithDataTypes(new a.DxfTableStyleRowDataTypes(-2147483648,2147483647)).WithTextStyle(value.Database.Document.TextStyles.get_Item('ALT'));
  assert.equal(edit.Original,row);value.ReplaceStyle(null,[edit]);const result=value.Rows.get_Item(1);assert.equal(result.Borders.Values.get_Item(3).StoredLineweight,-32768);assert.equal(result.TextStyle.Name,'ALT');assert.equal(result.DataTypes.StoredDataType,-2147483648);
});
test('named resource reassignment releases old usage without losing exposed handle references',()=>{
  const d=document(),first=d.TextStyles.get_Item('STYLE_REF'),rows=tableStylePacket();rows.push([340,first.Handle]);
  const value=new a.DxfTableStyle(d,tags(rows),DecodeTableText);d.NamedObjects.Add('STYLE',value);value.Resolve(h=>d.GetObjectByHandle(h),DecodeTableText);
  const old=value.References;value.ReplaceStyle(null,Array.from(value.Rows,row=>row.WithTextStyle(d.TextStyles.get_Item('ALT'))));
  assert.equal(old.Count,4);assert.equal(value.References.get_Item(0),first);assert.equal(d.TextStyles.GetReferences(first).get_Item(0).Uses,1);assert.equal(d.TextStyles.Remove(first),false);
});
test('style renames remain live in bindings but do not rewrite old stored-name snapshots',()=>{
  const value=style(),row=value.Rows.get_Item(0),tag=row.Tags.get_Item(0);row.TextStyle.Name='Renamed_Ω';
  assert.equal(row.StoredTextStyleName,'STYLE_REF');assert.equal(value.ChangedStyleName(tag),'Renamed_Ω');
  value.ReplaceStyle(null,[row.WithValues(changed())]);assert.equal(value.Rows.get_Item(0).TextStyle,row.TextStyle);assert.equal(value.ChangedStyleName(tag),'Renamed_Ω');
});
test('foreign same-name TextStyle is rejected without imports or handle allocation',()=>{
  const value=style(),foreign=document().TextStyles.get_Item('STYLE_REF'),before=tableStyleSnapshot(value);
  assert.throws(()=>value.ReplaceStyle(null,[value.Rows.get_Item(0).WithTextStyle(foreign)]),{name:'ArgumentException',ParamName:'textStyle'});assert.deepEqual(tableStyleSnapshot(value),before);
});
test('private scalar duplicates do not invalidate public projections',()=>{const value=style(undefined,{privateGroups:true});assert.equal(value.Rows.get_Item(0).Values.TextHeight,1);});
test('ambiguous or missing public scalar/border/type fields remain unprojected',()=>{
  for(const code of [140,274,90]){const d=document(),packet=tableStylePacket();packet.splice(packet.findIndex(t=>t[0]===code),1);const v=new a.DxfTableStyle(d,tags(packet),DecodeTableText),row=v.Rows.get_Item(0);
    assert.equal(row[code===140?'Values':code===274?'Borders':'DataTypes'],null);
  }
});
test('replacement descriptions encode literal backslashes and UTF16 code units by version',()=>{
  for(const version of [14,15,18]){const value=style(document(version)),text='東京 🧪 \\U+0041';value.ReplaceStyle(new a.DxfTableStyleHeader(text,1,0,0,0,false,false),[]);
    assert.equal(value.Header.Description,text);const encoded=Array.from(value.Tags).find(t=>t.Code===3).Value;assert.equal(encoded.includes('\\U+005C'),true);assert.equal(DecodeTableText(encoded),text);if(version===14)assert.ok(!/[^\x00-\x7f]/.test(encoded));
  }
});
test('negative zero is retained in public scalar snapshots and edited tags',()=>{
  const value=style(),row=value.Rows.get_Item(0);value.ReplaceStyle(null,[row.WithValues(new a.DxfTableStyleRowValues(-0,0,0,0,false))]);
  assert.ok(Object.is(value.Rows.get_Item(0).Values.TextHeight,-0));assert.ok(Object.is(value.Rows.get_Item(0).Tags.get_Item(1).Value,-0));
});
test('immutable headers, scalar objects, borders and data pairs reject assignment',()=>{
  const value=style();for(const object of [value.Header,value.Rows.get_Item(0).Values,value.Rows.get_Item(0).Borders.Values.get_Item(0),value.Rows.get_Item(0).DataTypes])immutable(object);
});
for(const method of ['Add','Clear','Remove','RemoveAt','Insert','set_Item'])test('table packet readonly IList rejects '+method,()=>{const packet=style().Tags;assert.throws(()=>packet[method](0,null),{name:'NotSupportedException'});});
test('readonly table views preserve Contains, IndexOf and CopyTo behavior',()=>{const tag=new a.DxfTag(90,1),view=TableSnapshot([tag]),output=[null];assert.equal(view.Contains(tag),true);assert.equal(view.IndexOf(tag),0);view.CopyTo(output,0);assert.equal(output[0],tag);});
for(const stage of ['get','move','current','dispose'])test('caught style reentry at '+stage+' invalidates the outer edit',()=>{
  const value=style(),packet=value.Tags,edit=value.Rows.get_Item(0).WithValues(changed());let at=-1;
  const hook=phase=>{if(phase===stage)assert.throws(()=>value.ReplaceStyle(null,[]),{name:'InvalidOperationException'});};
  const input={GetEnumerator(){hook('get');return{MoveNext(){hook('move');return ++at===0;},get Current(){hook('current');return edit;},Dispose(){hook('dispose');}};}};
  assert.throws(()=>value.ReplaceStyle(null,input),{name:'InvalidOperationException'});assert.equal(value.Tags,packet);value.ReplaceStyle(null,[edit]);
});
test('caller mutations survive enumeration failure while the TABLESTYLE packet does not change',()=>{
  const value=style(),d=value.Database.Document,packet=value.Tags;function* input(){d.Layers.Add(new a.Layer('Caller'));throw new Error('Caller');}
  assert.throws(()=>value.ReplaceStyle(null,input()),/Caller/);assert.equal(value.Tags,packet);assert.equal(d.Layers.Contains('Caller'),true);
});
test('CELLSTYLEMAP entry identifiers, order and raw format payloads are not normalized',()=>{const d=document(),value=map(d,4);assert.deepEqual(Array.from(value.Entries,e=>e.Id),[0,1,0,1]);assert.ok(Array.from(value.Entries).every(e=>e.StoredType===-1));assert.equal(value.Payload.Count,38);});
test('CELLSTYLEMAP name edits retain immutable old containers and share untouched format tags',()=>{
  const value=map(),payload=value.Payload,entries=value.Entries,format=entries.get_Item(0).FormatPayload;value.ReplaceEntryNames(['A','A']);assert.notEqual(value.Payload,payload);assert.equal(entries.get_Item(0).Name,'Entry 0');assert.equal(value.Entries.get_Item(0).FormatPayload.get_Item(0),format.get_Item(0));
});
test('CELLSTYLEMAP equal names retain exact snapshot identities',()=>{const value=map(),payload=value.Payload,entries=value.Entries;value.ReplaceEntryNames(Array.from(entries,e=>e.Name));assert.equal(value.Payload,payload);assert.equal(value.Entries,entries);});
test('CELLSTYLEMAP source profile drift blocks even a no-op request',()=>{const value=map(),packet=value.Payload;value.Database.Document.DrawingVariables.AcadVer=17;assert.throws(()=>value.ReplaceEntryNames(['Entry 0','Entry 1']),{name:'InvalidOperationException'});assert.equal(value.Payload,packet);});
test('CELLSTYLEMAP cloning and generic erasure remain unsupported rather than losing payload',()=>{const value=map();assert.throws(()=>value.CloneShell(),{name:'NotSupportedException'});assert.throws(()=>value.Database.EraseOwnedTree(value),{name:'NotSupportedException'});assert.equal(value.IsErased,false);});
test('TABLESTYLE resolves its exact owned typed CELLSTYLEMAP attachment',()=>{
  const d=document(),value=style(d),extension=new a.DxfDictionary(),cell=new a.DxfStoredCellStyleMap(d,tags(cellMapPacket()),DecodeTableText);
  extension.Add('ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP',cell);d.Objects.SetExtensionDictionary(value,extension);cell.Resolve(h=>d.StoredTableHandleTarget(h));
  // Resolve is a loader phase, so build an unresolved style first for a single pass.
  const source=new a.DxfTableStyle(d,tags(tableStylePacket()),DecodeTableText);d.NamedObjects.Add('SECOND',source);const ext=new a.DxfDictionary(),owned=new a.DxfStoredCellStyleMap(d,tags(cellMapPacket()),DecodeTableText);ext.Add('ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP',owned);d.Objects.SetExtensionDictionary(source,ext);owned.Resolve(h=>d.StoredTableHandleTarget(h));source.Resolve(h=>d.StoredTableHandleTarget(h),DecodeTableText);assert.equal(source.StoredCellStyleMap,owned);
});
test('editable text limits count UTF16 code units and reject NUL/unpaired surrogates',()=>{
  for(const text of ['\0','\ud800','\udc00','x\ud800z'])assert.throws(()=>CheckTableText(text,'name'),{name:'ArgumentException',ParamName:'name'});
  assert.doesNotThrow(()=>CheckTableText('🧪','name'));assert.throws(()=>CheckTableText('a'.repeat(1048577),'name'),{name:'ArgumentOutOfRangeException'});
  assert.throws(()=>EncodeTableText('\\'.repeat(149797),14,'name'),{name:'ArgumentOutOfRangeException',ParamName:'name'});
});
test('table handle dictionary retains first spelling when updating a case-insensitive slot',()=>{const map=new TableHandleMap();map.set('ab',1);map.set('AB',2);assert.deepEqual(Array.from(map),[['ab',2]]);assert.equal(map.get('Ab'),2);});
test('table style corpus is input-only deterministic and JSON preserves every explicit sign bit',()=>{
  const corpus=tableStyleCorpus();assert.deepEqual(corpus,tableStyleCorpus());assert.equal(new Set(corpus.map(p=>p.name)).size,corpus.length);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));assert.deepEqual(JSON.parse(JSON.stringify(corpus)),corpus);
});

test('managed enumeration rejects a null enumerator as a native NullReferenceException',()=>{
  const value=style(),tags=value.Tags;assert.throws(()=>value.ReplaceStyle(null,{GetEnumerator(){return null;}}),{name:'NullReferenceException'});assert.equal(value.Tags,tags);
  assert.throws(()=>new a.DxfTableStyleRowBorders({GetEnumerator(){return null;}}),{name:'NullReferenceException'});
});
