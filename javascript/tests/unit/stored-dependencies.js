import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { CanonicalDependencyHandle } from '../../runtime/StoredDependencyCollections.js';
import { DecodeTableText } from '../../runtime/TablePayload.js';
import { storedDependenciesCorpus, sunStudyPacket } from '../../tools/stored-dependencies-corpus.mjs';
const doc=()=>new api.DxfDocument(api.DxfVersion.AutoCad2018);
const tag=(code,value)=>new api.DxfTag(code,value);
function register(document,value,owner=document.NamedObjects,name='value',handle=null) {
  value.Owner=owner;
  if(handle!==null)value.Handle=handle;
  document.Objects.Register(value,handle!==null);
  if(owner instanceof api.DxfDictionary && name!==null)owner.AddLoaded(name,value,true);
  return value;
}
function errors(value,database=value.Database) {const list=new ReferenceList();value.ValidateDatabaseSchema(database,list);return Array.from(list);}
function field(document,payload=[]) {return new api.DxfStoredField(document,[tag(100,'AcDbField'),...payload],'AcObjProp','%<stored only>%');}
function line(document) {const value=new api.Line();document.Entities.Add(value);return value;}
function association(document,{points=1,backlink=false}={}) {
  const dimension=new api.AlignedDimension();document.Entities.Add(dimension);const geometry=line(document),extension=new api.DxfDictionary();
  document.Objects.SetExtensionDictionary(dimension,extension);
  const values=Array.from({length:points},(_,slot)=>new api.DxfStoredDimAssocPoint(slot,13,geometry.Handle,-1,slot,-0,new api.Vector3(1,2,3)));
  const value=new api.DxfStoredDimAssoc(document,[tag(100,'AcDbDimAssoc'),tag(330,dimension.Handle)],dimension.Handle,points?((1<<points)-1):0,1,0,values);
  register(document,value,extension,'ACAD_DIMASSOC');
  value.PersistentReactors.Add(dimension);if(backlink)geometry.PersistentReactors.Add(value);
  value.Resolve(handle=>document.GetObjectByHandle(handle));return {value,dimension,geometry,extension,values};
}
function study(document,refs=['0','0','0','0'],hours=[true,false]) {
  const payload=sunStudyPacket(hours,refs).map(([code,input])=>tag(code,input&&typeof input==='object'?(input.int??input.short??-0):input));
  const value=new api.DxfStoredSunStudy(document,payload,DecodeTableText);register(document,value);value.Resolve(handle=>document.GetObjectByHandle(handle));return value;
}

test('retained dependency classes have identical barrel and standalone exports',async()=>{
  for(const [file,names] of [['DxfStoredField',['DxfStoredField']],['DxfStoredDimAssoc',['DxfStoredDimAssoc','DxfStoredDimAssocPoint']],['DxfStoredSunStudy',['DxfStoredSunStudy']]]) {
    const module=await import(`../../netDxf/Objects/${file}.js`);for(const name of names)assert.equal(module[name],api[name]);
  }
});
test('FIELD snapshots payload and evaluator text without executing embedded text',()=>{
  const document=doc(),input=[tag(100,'AcDbField'),tag(2,'not executed')],value=new api.DxfStoredField(document,input,'Evaluator','throw new Error()');input.length=0;
  assert.equal(value.Payload.Count,2);assert.equal(value.FieldCode,'throw new Error()');assert.throws(()=>{value.FieldCode='replace';},TypeError);
  assert.throws(()=>value.Payload.Clear(),{name:'NotSupportedException'});
});
test('FIELD dependency views are live stable wrappers with versioned read-only iteration',()=>{
  const document=doc(),target=line(document),value=field(document,[tag(340,target.Handle)]);register(document,value);
  const refs=value.References,iterator=refs.GetEnumerator();assert.equal(refs.Count,0);value.Resolve([],[],h=>document.GetObjectByHandle(h));
  assert.equal(refs,value.References);assert.equal(refs.get_Item(0),target);assert.throws(()=>iterator.MoveNext(),{name:'InvalidOperationException'});
  assert.throws(()=>refs.Add(target),{name:'NotSupportedException'});assert.equal(refs.IsReadOnly,true);
});
test('FIELD preserves repeated semantic references and leading null object slots',()=>{
  const document=doc(),target=line(document),value=field(document,[tag(340,target.Handle),tag(331,'00'+target.Handle),tag(331,'0')]);register(document,value);
  value.Resolve([],['0',target.Handle,target.Handle,'000'],h=>document.GetObjectByHandle(h));
  assert.deepEqual([...value.References],[target,target]);assert.deepEqual([...value.ReferencedObjects],[null,target,target,null]);
});
test('FIELD ignores arbitrary and identity handles while retaining their payload tags',()=>{
  const document=doc(),value=field(document,[tag(320,'DEAD'),tag(329,'BEEF'),tag(5,'A'),tag(105,'B')]);register(document,value);
  value.Resolve([],[],()=>{throw new Error('Unexpected lookup');});assert.equal(value.References.Count,0);assert.equal(value.Payload.Count,5);
});
test('FIELD canonicalizes semantic handles but forwards original leading object spelling',()=>{
  const document=doc(),target=line(document),spelling='00'+target.Handle.toLowerCase(),value=field(document,[tag(340,spelling)]);register(document,value);const calls=[];
  value.Resolve([],[spelling],h=>{calls.push(h);return document.GetObjectByHandle(h);});assert.deepEqual(calls,[target.Handle,spelling]);
});
test('FIELD child graph retains reciprocal ownership and declared child identities',()=>{
  const document=doc(),parent=field(document,[tag(360,'C01')]),child=field(document,[tag(340,'C00')]);register(document,parent,document.NamedObjects,'root','C00');register(document,child,parent,null,'C01');
  parent.Resolve(['C01'],[],h=>document.GetObjectByHandle(h));child.Resolve([],[],h=>document.GetObjectByHandle(h));
  assert.deepEqual([...parent.Children],[child]);assert.deepEqual([...parent.DeclaredOwnedObjects],[child]);assert.equal(child.Owner,parent);assert.deepEqual(errors(parent),[]);
});
test('FIELD rejects duplicate children while retaining completed earlier binding',()=>{
  const document=doc(),parent=field(document,[tag(360,'C01')]),child=field(document);register(document,parent,document.NamedObjects,'root','C00');register(document,child,parent,null,'C01');
  assert.throws(()=>parent.Resolve(['C01','C01'],[],h=>document.GetObjectByHandle(h)),{name:'FormatException'});assert.equal(parent.Children.Count,1);assert.equal(parent.References.Count,1);
});
test('FIELD detects undeclared owned children but exempts its real extension dictionary',()=>{
  const document=doc(),value=field(document),extension=new api.DxfDictionary();register(document,value);register(document,extension,value,null);
  assert.throws(()=>value.Resolve([],[],h=>document.GetObjectByHandle(h)),{name:'FormatException'});
  value.ExtensionDictionary=extension;assert.deepEqual(errors(value),[]);
});
test('FIELD resolver exceptions preserve completed dependencies and do not mark resolution complete',()=>{
  const document=doc(),a=line(document),b=line(document),value=field(document,[tag(340,a.Handle),tag(341,b.Handle)]);register(document,value);const failure=new Error('caller');let calls=0;
  assert.throws(()=>value.Resolve([],[],h=>{if(++calls===2)throw failure;return document.GetObjectByHandle(h);}),e=>e===failure);
  assert.deepEqual([...value.References],[a]);assert.throws(()=>value.ValidateSource(document),{name:'InvalidOperationException'});
});
test('FIELD validates identity replacement and source profile without re-evaluation',()=>{
  const document=doc(),target=line(document),value=field(document,[tag(340,target.Handle)]);register(document,value);value.Resolve([],[],h=>document.GetObjectByHandle(h));
  document.AddedObjects.set_Item(target.Handle,new api.Line());assert.match(errors(value).join(' '),/dependency identity changed/);
  document.DrawingVariables.AcadVer=api.DxfVersion.AutoCad2013;assert.throws(()=>value.ValidateSource(document),{name:'NotSupportedException'});
});
test('FIELD dependency handles preserve full UInt64 range and leading zeroes',()=>{
  assert.equal(CanonicalDependencyHandle('000000000000000000ABC'),'ABC');assert.equal(CanonicalDependencyHandle('ffffffffffffffff'),'FFFFFFFFFFFFFFFF');
  assert.throws(()=>CanonicalDependencyHandle('10000000000000000'),{name:'OverflowException'});assert.throws(()=>CanonicalDependencyHandle(' 0A'),{name:'FormatException'});
  assert.throws(()=>CanonicalDependencyHandle(null),{name:'ArgumentNullException',ParamName:'s'});
});
test('DIMASSOC point snapshots copy vectors and preserve the exact negative-zero parameter',()=>{
  const input=new api.Vector3(1,2,3),value=new api.DxfStoredDimAssocPoint(2,13,'00AB',-1,-3,-0,input);input.X=10;value.Point.Y=20;
  assert.deepEqual(value.Point.ToArray(),[1,2,3]);assert.equal(Object.is(value.NearParameter,-0),true);assert.throws(()=>{value.MarkerIndex=4;},TypeError);assert.equal(value.GeometryHandle,'00AB');
});
test('DIMASSOC exposes repeated live geometry identities and fresh dependency views',()=>{
  const {value,geometry}=association(doc(),{points:3});assert.deepEqual([...value.References].slice(1),[geometry,geometry,geometry]);
  assert.notEqual(value.References,value.References);assert.equal(value.PointReferences.get_Item(0).Geometry,geometry);assert.deepEqual(errors(value),[]);
});
test('DIMASSOC rejects nonentity geometry and keeps prior dimension binding',()=>{
  const document=doc(),dimension=new api.AlignedDimension();document.Entities.Add(dimension);const p=new api.DxfStoredDimAssocPoint(0,1,'BAD',0,0,0,api.Vector3.Zero),value=new api.DxfStoredDimAssoc(document,[],dimension.Handle,1,0,0,[p]);
  assert.throws(()=>value.Resolve(h=>h===dimension.Handle?dimension:document.TextStyles.get_Item('Standard')),{name:'FormatException'});
  assert.equal(value.Dimension,dimension);assert.equal(value.References.Count,1);assert.equal(p.Geometry,null);
});
test('DIMASSOC requires an exact case-sensitive hard-owner ACAD_DIMASSOC alias',()=>{
  const {value,extension}=association(doc());extension.Remove('ACAD_DIMASSOC');extension.AddLoaded('acad_dimassoc',value,true);
  assert.match(errors(value).join(' '),/dimension ownership changed/);extension.Remove('acad_dimassoc');extension.AddLoaded('ACAD_DIMASSOC',value,false);assert.match(errors(value).join(' '),/ownership changed/);
});
test('DIMASSOC snapshots both present and absent source backlinks',()=>{
  for(const backlink of [false,true]){const {value,geometry}=association(doc(),{backlink});if(backlink)geometry.PersistentReactors.Clear();else geometry.PersistentReactors.Add(value);
    assert.match(errors(value).join(' '),/source reactor backlink changed/);}
});
test('DIMASSOC persistent reactor order is checked separately from membership',()=>{
  const {value,geometry}=association(doc());value.PersistentReactors.Add(geometry);assert.match(errors(value).join(' '),/persistent reactors changed/);
});
test('DIMASSOC validation retains its resolved/null-database exception boundary',()=>{
  const document=doc(),value=new api.DxfStoredDimAssoc(document,[],'0',0,0,0,[]);assert.equal(errors(value,null).length,1);
  const valid=association(document).value;assert.throws(()=>errors(valid,null),{name:'NullReferenceException'});
});
test('SUNSTUDY projects stored signed values, decoded names and raw hour flags',()=>{
  const value=study(doc());assert.equal(value.Name,'Study Ω');assert.equal(value.OutputType,-3);assert.equal(value.StartTime,-2147483648);assert.equal(value.EndTime,2147483647);
  assert.equal(value.Interval,-1);assert.deepEqual([...value.RawHourFlags],[true,false]);assert.equal(value.DateCount,0);assert.throws(()=>{value.Name='change';},TypeError);
});
test('SUNSTUDY preserves repeated role references and null role slots',()=>{
  const document=doc(),target=line(document),value=study(document,[target.Handle,target.Handle,'0',target.Handle]);
  assert.equal(value.PageSetup,target);assert.equal(value.View,target);assert.equal(value.VisualStyle,null);assert.equal(value.TextStyle,target);assert.deepEqual([...value.References],[target,target,target]);
});
test('SUNSTUDY ancestry validation detects moving an ancestor after resolution',()=>{
  const document=doc(),value=study(document);document.NamedObjects.Owner=null;assert.match(errors(value).join(' '),/source ownership changed/);
});
test('SUNSTUDY retains lexical handle keys when reporting replaced dependencies',()=>{
  const document=doc(),target=line(document),spelling='00'+target.Handle.toLowerCase(),value=study(document,[spelling,'0','0','0']);document.AddedObjects.Remove(target.Handle);
  assert.ok(errors(value).includes('Stored SUNSTUDY dependency identity changed: '+spelling));
});
test('SUNSTUDY repeat Resolve preserves native partial mutation before duplicate ancestry rejection',()=>{
  const document=doc(),target=line(document),value=study(document,[target.Handle,'0','0','0']),refs=value.References;
  assert.throws(()=>value.Resolve(h=>document.GetObjectByHandle(h)),{name:'ArgumentException'});assert.equal(refs.Count,2);assert.equal(refs,value.References);
});
for(const name of ['DxfStoredField','DxfStoredDimAssoc','DxfStoredSunStudy'])test(name+' rejects generic clone and erasure before graph mutation',()=>{
  const document=doc();let value;if(name==='DxfStoredField'){value=field(document);register(document,value);value.Resolve([],[],h=>document.GetObjectByHandle(h));}
  else if(name==='DxfStoredDimAssoc')value=association(document).value;else value=study(document);
  const seed=document.DrawingVariables.HandleSeed,count=document.Objects.Items.Count,owner=value.Owner;
  assert.throws(()=>value.CloneShell(),{name:'NotSupportedException'});assert.throws(()=>document.Objects.EraseOwnedTree(value),{name:'NotSupportedException'});
  assert.equal(document.DrawingVariables.HandleSeed,seed);assert.equal(document.Objects.Items.Count,count);assert.equal(value.Owner,owner);assert.equal(value.IsErased,false);
});
test('registered retained dependencies guard ordinary entity and STYLE removal',()=>{
  const document=doc(),target=line(document),style=document.TextStyles.Add(new api.TextStyle('Referenced','txt.shx')),value=field(document,[tag(340,target.Handle),tag(343,style.Handle),tag(343,style.Handle)]);register(document,value);value.Resolve([],[],h=>document.GetObjectByHandle(h));
  assert.equal(document.Entities.Remove(target),false);assert.equal(document.TextStyles.Remove(style),false);assert.equal(document.TextStyles.GetReferences(style).get_Item(0).Uses,2);
});
test('dependency corpus is complete, deterministic and contains no expected-result tables',()=>{
  const first=storedDependenciesCorpus();assert.deepEqual(first,storedDependenciesCorpus());assert.equal(first.length,738);assert.equal(new Set(first.map(p=>p.name)).size,738);
  assert.equal(first.reduce((n,p)=>n+p.request.steps.length,0),5566);assert.ok(first.every(p=>!Object.hasOwn(p,'expected')));
});

for(const [kind,code,tail] of [['handle',340,[0]],['boolean',290,[2]],['double',40,[0,0,0,0,0,0,240,127]]])
  test('binary '+kind+' diagnostics retain group and absolute value address',async()=>{
    const {BinaryCodeValueReader}=await import('../../netDxf/IO/BinaryCodeValueReader.js');
    const signature=[...new TextEncoder().encode('AutoCAD Binary DXF\r\n'),26,0];
    for(const legacy of [false,true])for(const offset of [0,7]) {
      const group=legacy?(code>=255?[255,code&255,code>>>8]:[code]):[code&255,code>>>8];
      const stream=new api.MemoryStream(Uint8Array.from([...Array(offset).fill(0),...signature,...group,...tail]));stream.Position=offset;
      const reader=new BinaryCodeValueReader(stream,undefined,legacy),address=offset+signature.length+group.length;
      assert.throws(()=>reader.Next(),error=>error.name==='InvalidDataException'&&error.message.includes('group code '+code+' at byte address '+address+'.'));
      assert.equal(reader.Code,code);assert.equal(stream.CanRead,true);
    }
  });
