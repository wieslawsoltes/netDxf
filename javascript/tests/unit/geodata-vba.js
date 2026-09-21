import test from 'node:test';
import assert from 'node:assert/strict';
import {DxfGeoData,DxfGeoCoordinateType,DxfGeoScaleEstimation,DxfGeoMeshPoint,DxfGeoMeshFace,DxfVbaProject,
  DxfDictionary,DxfXRecord,Block,Vector2,Vector3,DrawingUnits} from '../../index.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,FormatException,InvalidOperationException} from '../../runtime/Errors.js';
import {jsGeometry} from '../../tools/foundations-wire.mjs';
const geo=()=>new DxfGeoData(new Block('Host').Record);
const pt=i=>new DxfGeoMeshPoint(new Vector2(i,i+1),new Vector2(i+10,i+20));
const triple=()=>[pt(0),pt(1),pt(2)];
const bits=n=>{const v=new DataView(new ArrayBuffer(8));v.setFloat64(0,n);return v.getBigUint64(0).toString(16);};
test('GEODATA defaults retain a detached block reference, not document registration',()=>{
  const g=geo();assert.equal(g.Version,2);assert.equal(g.HostBlock.Name,'Host');assert.equal(g.HostBlock.Owner,null);
  assert.equal(g.CoordinateType,DxfGeoCoordinateType.Geographic);assert.equal(g.ScaleEstimation,DxfGeoScaleEstimation.None);
  assert.equal(g.HorizontalUnits,DrawingUnits.Meters);assert.equal(g.VerticalUnitScale,1);assert.equal(g.Database,null);
  assert.deepEqual([...g.DatabaseReferences],[g.HostBlock]);assert.throws(()=>new DxfGeoData(null),ArgumentNullException);
});
test('GEODATA copied finite coordinates retain signed zero and subnormal direction magnitudes',()=>{
  const g=geo(),value=new Vector3(-0,Number.MIN_VALUE,3);g.DesignPoint=value;value.Z=99;g.DesignPoint.Y=2;
  assert.equal(g.DesignPoint.Z,3);assert.equal(g.DesignPoint.Y,Number.MIN_VALUE);assert.ok(Object.is(g.DesignPoint.X,-0));
  g.UpDirection=new Vector3(0,0,Number.MIN_VALUE);g.NorthDirection=new Vector2(Number.MIN_VALUE,0);
  assert.equal(g.UpDirection.Z,Number.MIN_VALUE);assert.equal(g.NorthDirection.X,Number.MIN_VALUE);
  assert.throws(()=>{g.UpDirection=Vector3.Zero;},ArgumentOutOfRangeException);
  assert.throws(()=>{g.NorthDirection=new Vector2(NaN,1);},ArgumentOutOfRangeException);
  assert.equal(g.UpDirection.Z,Number.MIN_VALUE);
});
test('GEODATA scalar admission preserves state and independent unit declarations',()=>{
  const g=geo();g.HorizontalUnits=DrawingUnits.Feet;g.HorizontalUnitScale=.3048;g.VerticalUnits=DrawingUnits.Inches;
  g.SeaLevelElevation=-0;g.CoordinateProjectionRadius=-0;
  for(const bad of [0,-1,NaN,Infinity,-Infinity])assert.throws(()=>{g.HorizontalUnitScale=bad;},ArgumentOutOfRangeException);
  assert.equal(g.HorizontalUnitScale,.3048);assert.equal(g.VerticalUnitScale,1);
  assert.ok(Object.is(g.CloneShell().CoordinateProjectionRadius,-0));assert.ok(Object.is(g.CloneShell().SeaLevelElevation,-0));
});
test('GEODATA text remains uninterpreted and only definition text accepts LF',()=>{
  const g=geo(),text='<xml>Zażółć 東京 😀\nEPSG:unresolved</xml>';g.CoordinateSystemDefinition=text;
  assert.equal(g.CloneShell().CoordinateSystemDefinition,text);
  for(const key of ['CoordinateSystemDefinition','GeoRssTag','ObservationFrom','ObservationTo','ObservationCoverage']){
    const old=g[key];
    for(const bad of [null,'\r','\0','\ud800','\udc00','x\ud800y'])assert.throws(()=>{g[key]=bad;});
    assert.equal(g[key],old);
  }
  assert.throws(()=>{g.CoordinateSystemDefinition='literal^J';},ArgumentException);
  g.CoordinateSystemDefinition='literal^j';g.GeoRssTag='literal^J';
  assert.throws(()=>{g.GeoRssTag='two\nlines';},ArgumentException);
});
test('mesh points and faces expose immutable values rather than mutable coordinate aliases',()=>{
  const source=new Vector2(-0,2),p=new DxfGeoMeshPoint(source,Vector2.UnitY);source.Y=8;p.Source.X=3;
  assert.equal(p.Source.Y,2);assert.ok(Object.is(p.Source.X,-0));
  const f=new DxfGeoMeshFace(0,1,2);assert.throws(()=>{f.First=3;},TypeError);
  assert.throws(()=>new DxfGeoMeshFace(0,-1,0),{name:'ArgumentOutOfRangeException',ParamName:'first'});
  assert.throws(()=>new DxfGeoMeshPoint(new Vector2(Infinity,0),Vector2.Zero),{name:'ArgumentOutOfRangeException',ParamName:'source'});
});
test('SetMesh enumerates points and faces before validating and atomically replacing both lists',()=>{
  const g=geo(),initial=triple(),face=new DxfGeoMeshFace(0,1,2);g.SetMesh(initial,[face]);
  const points=g.MeshPoints,faces=g.MeshFaces,events=[];
  function* first(){events.push('points');yield null;events.push('points-end');}
  function* second(){events.push('faces');yield face;events.push('faces-end');}
  assert.throws(()=>g.SetMesh(first(),second()),ArgumentException);
  assert.deepEqual(events,['points','points-end','faces','faces-end']);
  assert.equal(g.MeshPoints,points);assert.equal(g.MeshFaces,faces);assert.deepEqual([...points],initial);
  assert.throws(()=>g.SetMesh([initial[0]],[face]),{name:'ArgumentException',ParamName:'meshFaces'});
  assert.deepEqual([...faces],[face]);
});
test('SetMesh preserves source data and disposes generators when enumeration fails',()=>{
  const g=geo();g.SetMesh(triple(),[new DxfGeoMeshFace(0,1,2)]);const first=g.MeshPoints.get_Item(0);let disposed=false;
  function* broken(){try{yield first;throw new InvalidOperationException('source failure');}finally{disposed=true;}}
  assert.throws(()=>g.SetMesh(broken(),[]),InvalidOperationException);assert.equal(disposed,true);
  assert.equal(g.MeshPoints.Count,3);assert.equal(g.MeshFaces.Count,1);
  g.SetMesh(g.MeshPoints,g.MeshFaces);assert.equal(g.MeshPoints.get_Item(0),first);
});
test('mesh collections check the index before null admission and invalidate iterators',()=>{
  const g=geo(),p=pt(0);assert.throws(()=>g.MeshPoints.Insert(-1,null),{name:'ArgumentOutOfRangeException',ParamName:'index'});
  assert.throws(()=>g.MeshPoints.Insert(0,null),{name:'ArgumentNullException',ParamName:'item'});g.MeshPoints.Add(p);
  assert.throws(()=>g.MeshPoints.set_Item(0,null),ArgumentNullException);assert.equal(g.MeshPoints.get_Item(0),p);
  const it=g.MeshPoints.GetEnumerator();it.MoveNext();g.MeshPoints.Add(pt(1));assert.throws(()=>it.MoveNext(),InvalidOperationException);
});
test('GEODATA CloneShell isolates lists, shares immutable elements, and deliberately leaves the host unmapped',()=>{
  const g=geo();g.SetMesh(triple(),[new DxfGeoMeshFace(0,1,2)]);g.Handle='A';g.Owner=new DxfDictionary();
  const q=g.CloneShell();assert.equal(q.HostBlock,null);assert.equal(q.Owner,null);assert.equal(q.Handle,null);
  assert.notEqual(q.MeshPoints,g.MeshPoints);assert.equal(q.MeshPoints.get_Item(0),g.MeshPoints.get_Item(0));
  q.MeshPoints.Clear();assert.equal(g.MeshPoints.Count,3);
  g.MeshPoints.RemoveAt(2);assert.throws(()=>g.CloneShell(),ArgumentException);
});
test('GEODATA host remapping rejects wrong mapped types and leaves an existing target intact',()=>{
  const g=geo(),q=g.CloneShell(),mapped=new Block('Destination').Record;
  g.CopyDatabaseReferencesTo(q,x=>{assert.equal(x,g.HostBlock);return mapped;});assert.equal(q.HostBlock,mapped);
  for(const bad of [null,new DxfXRecord()]){assert.throws(()=>g.CopyDatabaseReferencesTo(q,()=>bad),FormatException);assert.equal(q.HostBlock,mapped);}
});
test('GEODATA schema host protocol checks version, mesh bounds and reciprocal dictionary ownership',()=>{
  const g=geo(),errors=new ReferenceList(),database={Document:{DrawingVariables:{AcadVer:15}}};
  g.SetMesh(triple(),[new DxfGeoMeshFace(0,1,2)]);g.MeshPoints.RemoveAt(2);g.ValidateDatabaseSchema(database,errors);
  assert.equal(errors.Count,3);assert.match(errors.get_Item(0),/2010/);assert.match(errors.get_Item(1),/outside/);
  const ext=new DxfDictionary();ext.Owner=g.HostBlock;ext.Add('ACAD_GEOGRAPHICDATA',g);g.MeshFaces.Clear();errors.Clear();
  database.Document.DrawingVariables.AcadVer=18;g.ValidateDatabaseSchema(database,errors);assert.equal(errors.Count,0);
  // This tests a supplied internal host protocol, not typed document registration.
  g.Database=database;g.ValidateDatabaseSchema(database,errors);assert.deepEqual([...errors],['GEODATA host extension dictionary is not attached.']);
});
test('VBA Data canonicalizes chunks and defensively copies Uint8Array and Node Buffer inputs',()=>{
  for(const buffer of [Uint8Array.from({length:300},(_,i)=>i),Buffer.from(Array.from({length:300},(_,i)=>i))]){
    const p=new DxfVbaProject();p.Data=buffer;buffer[0]=99;
    assert.deepEqual([...p.Chunks].map(c=>c.length),[127,127,46]);assert.equal(p.Data[0],0);assert.equal(p.DataLength,300);
    const bytes=p.Data;bytes[0]=7;assert.equal(p.Data[0],0);
  }
});
test('VBA physical chunks include empties and preserve per-snapshot byte identities without storage aliases',()=>{
  const p=new DxfVbaProject(),input=[Uint8Array.of(1,2),new Uint8Array(),Uint8Array.of(3)];p.SetChunks(input);input[0][0]=77;
  const snapshot=p.Chunks;snapshot.get_Item(0)[1]=88;assert.equal(snapshot.get_Item(0)[1],88);assert.equal(p.Chunks.get_Item(0)[1],2);
  assert.deepEqual([...p.Data],[1,2,3]);assert.equal(p.DataLength,3);assert.deepEqual([...p.Chunks].map(c=>c.length),[2,0,1]);
  assert.throws(()=>{snapshot.Count=0;},TypeError);p.Data=new Uint8Array();assert.equal(p.Chunks.Count,0);assert.equal(snapshot.Count,3);
});
test('VBA SetChunks stages and copies each yielded chunk before advancing the iterator',()=>{
  const p=new DxfVbaProject(),chunk=Uint8Array.of(1,2);
  function* reused(){yield chunk;chunk[0]=9;yield chunk;chunk[0]=8;}
  p.SetChunks(reused());assert.deepEqual([...p.Data],[1,2,9,2]);
  function* broken(){yield Uint8Array.of(5);throw new InvalidOperationException('stop');}
  assert.throws(()=>p.SetChunks(broken()),InvalidOperationException);assert.deepEqual([...p.Data],[1,2,9,2]);
});
test('VBA malformed input rejects without changing either chunks or byte count',()=>{
  const p=new DxfVbaProject();p.SetChunks([new Uint8Array(),Uint8Array.of(7)]);
  for(const fn of [()=>{p.Data=null;},()=>p.SetChunks(null),()=>p.SetChunks([null]),()=>p.SetChunks([new Uint8Array(128)])])assert.throws(fn);
  assert.equal(p.DataLength,1);assert.deepEqual([...p.Chunks].map(c=>c.length),[0,1]);assert.deepEqual([...p.Data],[7]);
});
test('VBA CloneShell preserves exact chunk boundaries but not graph identity or byte aliases',()=>{
  const p=new DxfVbaProject();p.SetChunks([Uint8Array.of(1,2),new Uint8Array()]);p.Handle='F';p.Owner=new DxfDictionary();
  const q=p.CloneShell();assert.equal(q.Handle,null);assert.equal(q.Owner,null);assert.equal(q.Chunks.Count,2);
  p.StoredChunks.get_Item(0)[0]=9;assert.deepEqual([...q.Data],[1,2]);assert.deepEqual([...p.Data],[9,2]);
});
test('VBA chunk-count admission stops at the first overflow and leaves the previous value intact',()=>{
  const p=new DxfVbaProject();p.Data=Uint8Array.of(7);let yielded=0,disposed=false;
  function* over(){try{for(;;){yielded++;yield new Uint8Array();}}finally{disposed=true;}}
  assert.throws(()=>p.SetChunks(over()),ArgumentOutOfRangeException);
  assert.equal(yielded,DxfVbaProject.MaximumChunkCount+1);assert.equal(disposed,true);assert.deepEqual([...p.Data],[7]);
});
test('byte-array observation adapters enforce exact bounds and mutate only the selected snapshot',()=>{
  const r=jsGeometry({steps:[
    {kind:'new',type:'Objects.DxfVbaProject',args:[],id:'p'},
    {kind:'set',target:'p',member:'Data',value:{array:'Byte',values:[{byte:7},{byte:8}]}},
    {kind:'get',target:'p',member:'Data',id:'bytes'},
    {kind:'set-index',target:'bytes',args:[{int:0}],value:{byte:3}},
    {kind:'index',target:'bytes',args:[{int:-1}]},
    {kind:'set-index',target:'bytes',args:[{int:2}],value:{byte:3}},
    {kind:'get',target:'p',member:'Data'}
  ]});
  assert.deepEqual(r[4],{ok:false,error:'IndexOutOfRangeException',param:null});
  assert.deepEqual(r[5],{ok:false,error:'IndexOutOfRangeException',param:null});assert.equal(r[6].value[0].double,'401C000000000000');
});
test('GEODATA/VBA corpus remains deterministic, complete, unique and free of embedded expected results',async()=>{
  const {geoDataVbaCorpus}=await import('../../tools/geodata-vba-corpus.mjs');const a=geoDataVbaCorpus();
  assert.deepEqual(a,geoDataVbaCorpus());assert.equal(a.length,398);assert.equal(new Set(a.map(p=>p.name)).size,398);
  assert.equal(a.reduce((n,p)=>n+p.request.steps.length,0),2390);assert.ok(a.every(p=>!Object.hasOwn(p,'expected')));
});
