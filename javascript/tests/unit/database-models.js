import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDatabaseObject, DxfDictionary, DxfDictionaryWithDefault, DxfPlaceholder, DxfXRecord, DxfDictionaryVariable, DxfObjectPointer, DxfOpaqueObject,
  DxfDataTable, DxfDataColumn, DxfDataCellType as Type, DxfSun, DxfTag, Point, Layer, Vector3, ApplicationRegistry, XData, XDataRecord, XDataCode } from '../../index.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, FormatException, InvalidOperationException, KeyNotFoundException, NotSupportedException } from '../../runtime/Errors.js';
const payload=()=>{const r=new DxfXRecord();for(const [code,value] of [[102,'ACAD_ROUNDTRIP_2008_TABLE_ENTITY'],[360,'A'],[361,'B']])r.Data.Add(new DxfTag(code,value));return r;};
const targets=()=>[new DxfOpaqueObject('TABLECONTENT',[]),new DxfOpaqueObject('TABLEGEOMETRY',[])];

test('typed database base remains abstract and default shells do not fabricate registration',()=>{
  assert.throws(()=>new DxfDatabaseObject('CUSTOM'),NotSupportedException);
  for(const Type of [DxfDictionary,DxfDictionaryWithDefault,DxfPlaceholder,DxfXRecord,DxfDictionaryVariable,DxfObjectPointer,DxfDataTable,DxfSun]){
    const value=new Type(),copy=value.CloneShell();assert.equal(copy.Database,null);assert.equal(copy.Owner,null);assert.equal(copy.Handle,null);assert.equal(copy.IsErased,false);assert.notEqual(copy,value);
  }
});
test('typed dictionaries preserve aliases and owner identity after unlinking all names',()=>{
  const d=new DxfDictionary(),other=new DxfDictionary(),child=new DxfPlaceholder();d.Add('First',child);d.Add('Alias',child,false);
  assert.equal(d.get_Item('FIRST'),child);assert.equal(d.get_Item('alias'),child);assert.equal(child.Owner,d);const entries=d.Entries;
  assert.deepEqual([...entries].map(e=>[e.Name,e.IsHardOwner]),[['First',true],['Alias',false]]);
  assert.throws(()=>other.Add('Transfer',child,false),ArgumentException);d.Remove('first');d.Remove('ALIAS');assert.equal(entries.Count,0);assert.equal(child.Owner,d);
  assert.throws(()=>other.Add('StillOwned',child),ArgumentException);assert.throws(()=>d.get_Item('missing'),KeyNotFoundException);
});
test('dictionary names retain lexical spelling and use ordinal rather than expanding Unicode case',()=>{
  const d=new DxfDictionary();d.Add('σ',new DxfPlaceholder());assert.equal(d.Contains('ς'),true);assert.throws(()=>d.Add('Σ',new DxfPlaceholder()),ArgumentException);
  d.Add('ß',new DxfPlaceholder());d.Add('SS',new DxfPlaceholder());d.Add(' ',new DxfPlaceholder());
  for(const name of [null,'','a\0b','a\nb','a\rb'])assert.throws(()=>d.Add(name,new DxfPlaceholder()),{name:'ArgumentException',ParamName:'name'});
  assert.throws(()=>d.Contains(null),{name:'ArgumentNullException',ParamName:'key'});assert.throws(()=>d.Remove(null),{name:'ArgumentNullException',ParamName:'key'});
  assert.throws(()=>d.get_Item(null),{name:'ArgumentNullException',ParamName:'name'});assert.equal(d.Count,4);
});
test('dictionary hard flags are independent while non-database resources require soft aliases',()=>{
  const d=new DxfDictionary(),layer=new Layer('L');assert.throws(()=>d.Add('layer',layer,false),ArgumentException);d.IsHardOwner=false;
  assert.throws(()=>d.Add('layer',layer,true),ArgumentException);d.Add('layer',layer,false);assert.equal(layer.Owner,null);
  assert.throws(()=>d.Add('point',new Point(),false),ArgumentException);assert.throws(()=>d.Add('sun',new DxfSun(),false),ArgumentException);
  d.Add('child',new DxfPlaceholder(),true);assert.equal(d.Entries.get_Item(1).IsHardOwner,true);assert.equal(d.IsHardOwner,false);
});
test('dictionary cycles, duplicates and erased attachment fail before mutation',()=>{
  const root=new DxfDictionary(),child=new DxfDictionary();root.Add('child',child);assert.throws(()=>child.Add('root',root),ArgumentException);assert.throws(()=>root.Add('self',root),ArgumentException);
  const replacement=new DxfPlaceholder();assert.throws(()=>root.Add('CHILD',replacement),ArgumentException);assert.equal(replacement.Owner,null);
  replacement.IsErased=true;assert.throws(()=>root.Add('new',replacement),InvalidOperationException);root.IsErased=true;assert.throws(()=>root.Add(null,null),InvalidOperationException);
  assert.equal(root.Remove('child'),true);assert.equal(child.Owner,root);
});
test('dictionary read-only entry lists are live and version-check enumerators',()=>{
  const d=new DxfDictionary(),view=d.Entries;assert.equal(view.Add,undefined);d.Add('a',new DxfPlaceholder());assert.equal(view.Count,1);
  const entry=view.get_Item(0);assert.throws(()=>{entry.Name='mutated';},TypeError);const iterator=view.GetEnumerator();assert.equal(iterator.MoveNext(),true);
  d.Add('b',new DxfPlaceholder());assert.throws(()=>iterator.MoveNext(),InvalidOperationException);
  d.Cloning=4;const shell=d.CloneShell();assert.equal(shell.Count,0);assert.equal(shell.Cloning,4);assert.equal(d.Count,2);
});
test('dictionary fallback is a pointer and does not change Contains or target ownership',()=>{
  const d=new DxfDictionaryWithDefault(),fallback=new DxfPlaceholder();d.Default=fallback;assert.equal(d.Contains('missing'),false);assert.equal(d.get_Item('missing'),fallback);assert.equal(fallback.Owner,null);
  d.AddLoaded('NULL',null,false);const output={};assert.equal(d.TryGetValue('NULL',output),true);assert.equal(output.value,null);
  assert.throws(()=>d.AddLoaded('null',new DxfPlaceholder(),true),FormatException);assert.throws(()=>{d.Default=new Point();},ArgumentException);
  assert.equal(d.CloneShell().Default,null);assert.equal(d.Default,fallback);
});
test('XRECORD authored validation and loaded preservation remain different paths',()=>{
  const r=new DxfXRecord();for(const tag of [new DxfTag(0,'LINE'),new DxfTag(5,'A'),new DxfTag(105,'A'),new DxfTag(370,25),new DxfTag(999,'c'),new DxfTag(1000,'x'),new DxfTag(310,new Uint8Array(128))])assert.throws(()=>r.Data.Add(tag),ArgumentException);
  r.AddLoadedData(new DxfTag(370,25));r.AddLoadedData(new DxfTag(310,new Uint8Array(128)));const copy=r.CloneShell();assert.equal(copy.Data.Count,2);assert.equal(copy.Data.get_Item(0),r.Data.get_Item(0));
  copy.Data.Clear();assert.equal(r.Data.Count,2);assert.throws(()=>r.AddLoadedData(new DxfTag(999,'bad')),ArgumentException);
});
test('XRECORD schema binding is all-or-nothing and generic edits become read-only',()=>{
  const r=payload(),[content,geometry]=targets();geometry.Owner=new DxfDictionary();assert.throws(()=>r.BindTableRoundtripChildren(content,geometry),ArgumentException);assert.equal(content.Owner,null);assert.equal(r.IsSchemaManaged,false);
  geometry.Owner=null;r.BindTableRoundtripChildren(content,geometry);assert.equal(content.Owner,r);assert.equal(geometry.Owner,r);assert.deepEqual(r.DeclaredOwnedObjects,[content,geometry]);
  for(const action of [()=>r.Data.Add(new DxfTag(1,'x')),()=>r.Data.Insert(0,null),()=>r.Data.set_Item(0,null),()=>r.Data.RemoveAt(0),()=>r.Data.Clear()])assert.throws(action,InvalidOperationException);
  assert.throws(()=>r.Data.Insert(-1,null),ArgumentOutOfRangeException);assert.equal(r.Data.Remove(new DxfTag(1,'absent')),false);
  const copy=r.CloneShell();assert.equal(copy.IsSchemaManaged,false);assert.equal(copy.Data.Count,3);
});
test('declared schema owner handles materialize without permitting ordinary data edits',()=>{
  const r=payload(),[content,geometry]=targets();r.BindTableRoundtripChildren(content,geometry);content.Handle='AC';geometry.Handle='BC';r.MaterializeOwnedObjectReferences();
  assert.equal(r.Data.get_Item(1).Value,'AC');assert.equal(r.Data.get_Item(2).Value,'BC');const copy=r.CloneShell(),replacement=targets(),map=new Map([[content,replacement[0]],[geometry,replacement[1]]]);
  r.CopyDatabaseReferencesTo(copy,v=>map.get(v));assert.deepEqual(copy.DeclaredOwnedObjects,replacement);assert.equal(replacement[0].Owner,copy);assert.equal(content.Owner,r);
  r.ReplaceLoadedData(0,new DxfTag(1,'broken'));const errors=new ReferenceList();r.ValidateDatabaseSchema(null,errors);assert.deepEqual([...errors],['Invalid TABLE roundtrip ownership envelope: ']);
});
test('composite TABLE schema validates every owned candidate before adopting any',()=>{
  const codes=[102,360,70,90,10,20,30,90,90,361,102,90,91,102,360],values=['ACAD_ROUNDTRIP_2008_TABLE_ENTITY','A',0,0,1,2,3,0,0,'B','ACAD_ROUNDTRIP_PRE2007_TABLE',0,0,'ACAD_ROUNDTRIP_PRE2007_TABLECELL','C'];
  const r=new DxfXRecord();codes.forEach((code,i)=>r.Data.Add(new DxfTag(code,values[i])));const [content,geometry]=targets(),table=new DxfDataTable();table.IsErased=true;
  assert.throws(()=>r.BindCompositeTableRoundtripChildren(content,geometry,table),InvalidOperationException);assert.equal(content.Owner,null);assert.equal(geometry.Owner,null);table.IsErased=false;
  r.BindCompositeTableRoundtripChildren(content,geometry,table);assert.equal(table.Owner,r);assert.deepEqual(r.DeclaredOwnedObjects,[content,geometry,table]);
});
test('data column boxing distinguishes explicit CLR Int32 and Double despite shared Number representation',()=>{
  assert.equal(new DxfDataColumn(Type.Integer,'',[new BoxedScalar('Int32',3)]).Values.get_Item(0),3);
  assert.equal(new DxfDataColumn(Type.Double,'',[new BoxedScalar('Double',3)]).Values.get_Item(0),3);
  assert.throws(()=>new DxfDataColumn(Type.Integer,'',[new BoxedScalar('Double',3)]),ArgumentException);
  assert.throws(()=>new DxfDataColumn(Type.Double,'',[new BoxedScalar('Int32',3)]),ArgumentException);
  assert.throws(()=>new DxfDataColumn(Type.String,'',[new BoxedScalar('Int16',3)]),ArgumentNullException);
  assert.ok(Object.is(new DxfDataColumn(Type.Integer,'',[-0]).Values.get_Item(0),0));assert.throws(()=>{DxfDataTable.MaximumCells=1;},TypeError);
});
test('data column snapshots retain object references and independently copy value coordinates',()=>{
  const p=new Vector3(1,2,3),child=new DxfPlaceholder(),column=new DxfDataColumn(Type.Point,'',[p]),references=new DxfDataColumn(Type.ObjectId,'',[child]);
  p.X=9;column.Values.get_Item(0).Y=9;assert.deepEqual([column.Values.get_Item(0).X,column.Values.get_Item(0).Y],[1,2]);assert.equal(references.Values.get_Item(0),child);
  child.Handle='AA';assert.equal(references.Values.get_Item(0).Handle,'AA');assert.throws(()=>{column.Name='changed';},TypeError);
});
test('data-table replacement validates dimensions and owners before altering the prior snapshot',()=>{
  const table=new DxfDataTable(),child=new DxfPlaceholder(),column=new DxfDataColumn(Type.HardOwner,'',[child]);table.SetColumns(1,[column]);const old=table.Columns;
  for(const action of [()=>table.SetColumns(2,old),()=>table.SetColumns(1,[column,column]),()=>new DxfDataTable().SetColumns(1,old),()=>table.SetColumns(1,[new DxfDataColumn(Type.HardOwner,'',[table])])])assert.throws(action,ArgumentException);
  assert.equal(table.Columns,old);assert.equal(child.Owner,table);table.SetColumns(0,[]);assert.equal(child.Owner,null);assert.equal(old.Count,1);
});
test('data-table caller enumeration finishes before checking current ownership and erased state',()=>{
  const table=new DxfDataTable(),child=new DxfPlaceholder(),other=new DxfDictionary();function* values(){yield new DxfDataColumn(Type.HardOwner,'',[child]);other.Add('OWNED',child);}
  assert.throws(()=>table.SetColumns(1,values()),ArgumentException);assert.equal(table.Columns.Count,0);assert.equal(child.Owner,other);
  function* erased(){yield new DxfDataColumn(Type.Integer,'',[3]);table.IsErased=true;}
  assert.throws(()=>table.SetColumns(1,erased()),InvalidOperationException);assert.equal(table.Columns.Count,0);
});
test('data-table text validation rejects malformed UTF16 and preserves admissible exact strings',()=>{
  for(const name of ['\ud800','\udc00','a\0b','a\rb','a\nb'])assert.throws(()=>new DxfDataColumn(Type.String,'',[name]),ArgumentException);
  for(const name of ['','\t','日本😀'])assert.equal(new DxfDataColumn(Type.String,name,[name]).Values.get_Item(0),name);
  assert.throws(()=>new DxfDataColumn(Type.Double,'',[Infinity]),ArgumentException);assert.throws(()=>new DxfDataColumn(Type.Integer,'',[1n]),ArgumentException);
});
test('data-table reference mapping preserves nulls, remaps every pointer strength, and adopts new owner slots',()=>{
  const source=new DxfDataTable(),child=new DxfPlaceholder(),external=new Point();source.Name='SOURCE';source.SetColumns(2,[new DxfDataColumn(Type.HardOwner,'',[child,null]),new DxfDataColumn(Type.HardPointer,'',[external,external])]);
  const copy=source.CloneShell(),newChild=new DxfPlaceholder(),newExternal=new Point();source.CopyDatabaseReferencesTo(copy,v=>v===child?newChild:newExternal);
  assert.equal(copy.RowCount,2);assert.equal(copy.Columns.get_Item(1).Values.get_Item(0),newExternal);assert.equal(copy.Columns.get_Item(1).Values.get_Item(1),newExternal);assert.equal(copy.Columns.get_Item(0).Values.get_Item(1),null);
  assert.equal(newChild.Owner,copy);assert.equal(child.Owner,source);assert.equal(newExternal.Owner,null);
});
test('SUN stores exact raw settings, validates editing and rejects mutation after erasure',()=>{
  const sun=new DxfSun();sun.ColorIndex=0;sun.TrueColor=null;sun.Intensity=-2.5;sun.StoredTime=-2147483648;sun.JulianDay=2147483647;sun.ShadowMapSize=4096;sun.ShadowSoftness=255;
  const copy=sun.CloneShell();assert.equal(copy.TrueColor,null);assert.equal(copy.ColorIndex,0);assert.equal(copy.Intensity,-2.5);assert.equal(copy.StoredTime,-2147483648);assert.equal(copy.ShadowMapSize,4096);
  assert.throws(()=>{sun.ShadowMapSize=65;},ArgumentOutOfRangeException);sun.IsErased=true;
  for(const member of ['Enabled','ColorIndex','TrueColor','Intensity','ShadowsEnabled','JulianDay','StoredTime','DaylightSavingTime','ShadowType','ShadowMapSize','ShadowSoftness'])assert.throws(()=>{sun[member]=null;},InvalidOperationException);
});
test('opaque object snapshots cannot be cloned and do not become typed SUN by code name',()=>{
  const tags=[new DxfTag(100,'Custom'),new DxfTag(1,'opaque')],opaque=new DxfOpaqueObject('SUN',tags);tags.length=0;assert.equal(opaque.Tags.Count,2);assert.throws(()=>opaque.CloneShell(),NotSupportedException);
  const d=new DxfDictionary();d.Add('opaque',opaque);assert.equal(opaque.Owner,d);assert.throws(()=>d.Add('typed',new DxfSun()),ArgumentException);
});
test('detached object pointer and SUN schema errors reflect their actual owners',()=>{
  const pointer=new DxfObjectPointer(),errors=new ReferenceList();pointer.ValidateDatabaseSchema(null,errors);assert.deepEqual([...errors],['OBJECT_PTR requires a dictionary owner.']);
  new DxfDictionary().Add('P',pointer);errors.Clear();pointer.ValidateDatabaseSchema(null,errors);assert.equal(errors.Count,0);
  const sun=new DxfSun();sun.Owner=pointer;sun.ValidateDatabaseSchema({Document:{DrawingVariables:{AcadVer:14}}},errors);assert.deepEqual([...errors],['Typed SUN requires R2007 or later.','SUN requires a view or viewport owner.']);
});
test('database-bound XData callback adapter privately rebinds before reserving and raising notification',()=>{
  // Supplemental adapter test only: this deliberately does not count as a registered DxfDocument test.
  const item=new DxfPlaceholder(),data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,'ABC'));const calls=[];
  item.Database={ReserveUnresolvedReference(tag){calls.push('reserve:'+tag.Value);assert.notEqual(item.XData.get_Item('APP'),data);}};
  item.XDataAddAppReg.Add(()=>calls.push('event'));item.XData.Add(data);assert.deepEqual(calls,['reserve:ABC','event']);assert.equal(data.XDataRecord.Count,1);
});
test('DATATABLE streaming admission limits stop at the first excess cell or column',()=>{
  let cells=0;function* numbers(){for(;;){cells++;yield 1;}}
  assert.throws(()=>new DxfDataColumn(Type.Integer,'',numbers()),{name:'ArgumentException',ParamName:'values'});assert.equal(cells,DxfDataTable.MaximumCells+1);
  const table=new DxfDataTable(),column=new DxfDataColumn(Type.Integer,'',[]);let columns=0;function* many(){for(;;){columns++;yield column;}}
  assert.throws(()=>table.SetColumns(0,many()),ArgumentException);assert.equal(columns,DxfDataTable.MaximumCells+1);assert.equal(table.Columns.Count,0);assert.equal(table.RowCount,0);
});
