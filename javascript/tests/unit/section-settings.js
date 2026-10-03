import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfSectionSettings as Settings, DxfSectionTypeSettings as Type, DxfSectionGeometrySettings as Geometry, DxfPlaceholder, Line } from '../../index.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidCastException, InvalidOperationException } from '../../runtime/Errors.js';
function bundle(sources=[],geometry=[],repeat=true){return new Type(-17,33,sources,null,String.raw`inert\U+0041.dwg`,geometry,repeat);}
function* repeat(item,count){for(let i=0;i<count;i++)yield item;}
test('section appearance values retain raw flags finite negative values and independent clones',()=>{
  const g=new Geometry();g.Flags=-2147483648;g.Lineweight=-32768;g.HatchPatternType=32767;g.LinetypeScale=-3;g.HatchAngle=-720;g.HatchScale=-0;g.HatchSpacing=-2;
  const q=g.Clone();assert.equal(q.Flags,-2147483648);assert.equal(q.Lineweight,-32768);assert.equal(q.HatchAngle,-720);assert.ok(Object.is(q.HatchScale,-0));q.LayerName='edited';assert.equal(g.LayerName,'0');
  for(const key of ['LinetypeScale','HatchAngle','HatchScale','HatchSpacing']){const before=g[key];for(const n of [NaN,Infinity,-Infinity])assert.throws(()=>{g[key]=n;},ArgumentOutOfRangeException);assert.ok(Object.is(g[key],before));}
});
test('section type construction validates null arguments before enumerating and snapshots geometry independently',()=>{
  let enumerated=false;const sources={*[Symbol.iterator](){enumerated=true;yield null;}};
  assert.throws(()=>new Type(0,0,null,null,'',null),{name:'ArgumentNullException',ParamName:'sourceObjects'});
  assert.throws(()=>new Type(0,0,sources,null,'',null),{name:'ArgumentNullException',ParamName:'geometrySettings'});assert.equal(enumerated,false);
  const g=new Geometry(),input=[g],line=new Line(),refs=[line,null,line],t=bundle(refs,input,false);input.length=0;refs.length=0;g.HatchScale=9;
  assert.deepEqual([...t.SourceObjects],[line,null,line]);assert.equal(t.GeometrySettings.Count,1);assert.equal(t.GeometrySettings.get_Item(0).HatchScale,1);assert.equal(t.RepeatGeometryMarkers,false);
  assert.notEqual(t.GeometrySettings,t.GeometrySettings);assert.notEqual(t.SourceObjects,t.SourceObjects);assert.equal(t.GeometrySettings.Add,undefined);assert.throws(()=>{t.GenerationOptions=1;},TypeError);
});
test('section text guards keep literal names while rejecting transport delimiters and broken UTF-16',()=>{
  const g=new Geometry();for(const key of ['LayerName','LinetypeName','PlotStyleName','HatchPatternName']){
    g[key]='';g[key]=String.raw`Literal\U+000A`;const before=g[key];
    for(const value of ['x\n','x\r','x\0','\ud800','\udc00'])assert.throws(()=>{g[key]=value;},ArgumentException);
    assert.throws(()=>{g[key]=null;},ArgumentNullException);assert.equal(g[key],before);
  }
  assert.throws(()=>new Type(0,0,[],null,null,[]),{name:'ArgumentNullException',ParamName:'destinationFileName'});
});
test('section settings replacements preserve old read-only views and isolate duplicate type entries',()=>{
  const g=new Geometry(),t=bundle([], [g]),s=new Settings();s.SetTypeSettings([t,t]);const old=s.TypeSettings;
  assert.notEqual(old,s.TypeSettings);assert.notEqual(old.get_Item(0),t);assert.notEqual(old.get_Item(0),old.get_Item(1));
  old.get_Item(0).GeometrySettings.get_Item(0).HatchScale=9;assert.equal(old.get_Item(1).GeometrySettings.get_Item(0).HatchScale,1);assert.equal(t.GeometrySettings.get_Item(0).HatchScale,1);
  s.SetTypeSettings([]);assert.equal(s.TypeSettings.Count,0);assert.equal(old.Count,2);assert.equal(old.get_Item(0).GeometrySettings.get_Item(0).HatchScale,9);
});
test('section settings caller enumeration failures do not publish a partial reference list',()=>{
  const s=new Settings(),t=bundle();s.SetTypeSettings([t]);const retained=s.TypeSettings.get_Item(0);let disposed=false;
  function* failing(){try{yield t;throw new InvalidOperationException('caller failed');}finally{disposed=true;}}
  assert.throws(()=>s.SetTypeSettings(failing()),InvalidOperationException);assert.equal(disposed,true);assert.equal(s.TypeSettings.get_Item(0),retained);
  assert.throws(()=>s.SetTypeSettings([t,null]),ArgumentException);assert.equal(s.TypeSettings.get_Item(0),retained);
  assert.throws(()=>s.SetTypeSettings(null),ArgumentNullException);
});
test('section admission limits accept exact boundaries and terminate unbounded caller enumeration',()=>{
  const empty=bundle(),s=new Settings();s.SetTypeSettings(repeat(empty,Settings.MaximumTypeSettings));assert.equal(s.TypeSettings.Count,1024);const old=s.TypeSettings.get_Item(0);let disposed=false;
  function* forever(){try{for(;;)yield empty;}finally{disposed=true;}}
  assert.throws(()=>s.SetTypeSettings(forever()),{name:'ArgumentException',ParamName:'values'});assert.equal(disposed,true);assert.equal(s.TypeSettings.get_Item(0),old);
  const source=bundle(repeat(null,Settings.MaximumSourceReferences));assert.equal(source.SourceObjects.Count,1048576);
  assert.throws(()=>bundle(repeat(null,Settings.MaximumSourceReferences+1)),{name:'ArgumentException',ParamName:'sourceObjects'});
  const geometry=bundle([],repeat(new Geometry(),Settings.MaximumGeometrySettings));assert.equal(geometry.GeometrySettings.Count,65536);
  assert.throws(()=>bundle([],repeat(new Geometry(),Settings.MaximumGeometrySettings+1)),{name:'ArgumentException',ParamName:'geometrySettings'});
});
test('section aggregate source and geometry limits are checked before publishing replacement bundles',()=>{
  const s=new Settings();s.SetTypeSettings([bundle()]);const old=s.TypeSettings.get_Item(0);
  const refs=bundle(repeat(null,Settings.MaximumSourceReferences));assert.throws(()=>s.SetTypeSettings([refs,bundle([null])]),ArgumentException);assert.equal(s.TypeSettings.get_Item(0),old);
  const geom=bundle([],repeat(new Geometry(),Settings.MaximumGeometrySettings));assert.throws(()=>s.SetTypeSettings([geom,bundle([], [new Geometry()])]),ArgumentException);assert.equal(s.TypeSettings.get_Item(0),old);
});
test('section reference admission preserves duplicates nulls and source validation order',()=>{
  const s=new Settings(),line=new Line(),object=new DxfPlaceholder();s.SetTypeSettings([bundle([line,null,line,s,object])]);assert.deepEqual([...s.DatabaseReferences],[line,null,line,s,object,null]);
  const old=s.TypeSettings.get_Item(0);object.IsErased=true;assert.throws(()=>s.SetTypeSettings([bundle([object])]),InvalidOperationException);assert.equal(s.TypeSettings.get_Item(0),old);
  assert.throws(()=>s.ValidateValues(),InvalidOperationException);s.SetTypeSettings([]);assert.doesNotThrow(()=>s.ValidateValues());
  const visited=[];s.Database={CheckRegistered(item){visited.push(item);if(item===object)throw new ArgumentException('foreign');}};
  object.IsErased=false;assert.throws(()=>s.SetTypeSettings([bundle([line,null,line,object])]),ArgumentException);assert.deepEqual(visited,[line,line,object]);assert.equal(s.TypeSettings.Count,0);
});
test('section erased settings reject mutation after caller enumeration without changing the previous list',()=>{
  const s=new Settings();s.SetTypeSettings([bundle()]);const old=s.TypeSettings.get_Item(0);s.IsErased=true;let enumerated=false;
  function* input(){enumerated=true;yield bundle();}
  assert.throws(()=>s.SetTypeSettings(input()),InvalidOperationException);assert.equal(enumerated,true);assert.equal(s.TypeSettings.get_Item(0),old);
  assert.throws(()=>s.SetTypeSettings([null]),ArgumentException);assert.throws(()=>s.ValidateValues(),InvalidOperationException);
});
test('section clone hooks resolve destination before deferred sources and preserve reference identity',()=>{
  const one=new Line(),two=new Line(),g=new Geometry(),t=bundle([one,null,one],[g]),calls=[];
  const copied=t.Copy(item=>{calls.push(item);return item===one?two:null;});assert.deepEqual(calls,[null,one,null,one]);assert.deepEqual([...copied.SourceObjects],[two,null,two]);
  assert.notEqual(copied.GeometrySettings.get_Item(0),t.GeometrySettings.get_Item(0));assert.throws(()=>t.Copy(null),{name:'ArgumentNullException',ParamName:'selector'});
  assert.throws(()=>t.Copy(()=>new DxfPlaceholder()),InvalidCastException);
  const s=new Settings('SECTION_SETTINGS');s.SectionType=-17;s.SetTypeSettings([t]);s.Handle='AB';const shell=s.CloneShell();assert.equal(shell.CodeName,'SECTION_SETTINGS');assert.equal(shell.SectionType,-17);assert.equal(shell.TypeSettings.Count,0);assert.equal(shell.Handle,null);
  s.CopyDatabaseReferencesTo(shell,item=>item===one?two:null);assert.deepEqual([...shell.DatabaseReferences],[two,null,two,null]);assert.notEqual(shell.TypeSettings.get_Item(0),s.TypeSettings.get_Item(0));
});
test('section schema validation collects recognized validation errors without masking unrelated exceptions',()=>{
  const s=new Settings(),errors=new ReferenceList();s.ValidateDatabaseSchema(null,errors);assert.equal(errors.Count,0);s.IsErased=true;s.ValidateDatabaseSchema(null,errors);
  assert.equal(errors.Count,1);assert.equal(errors.get_Item(0),'Erased section settings cannot be registered or written.');
  const injected=new Error('unrelated');s.ValidateValues=()=>{throw injected;};assert.throws(()=>s.ValidateDatabaseSchema(null,errors),error=>error===injected);assert.equal(errors.Count,1);
});
