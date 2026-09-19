// Independent oracle inputs, not expected values or replacement model algorithms.
import { D, I, R, V, A } from './geometry-corpus.mjs';
const N=(type,args=[],id='p')=>({kind:'new',type:type.includes('.')||type.startsWith('List<')?type:'Objects.'+type,args,id});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic:true}:{})});
const C=(target,member,args=[],id,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const shell=()=>C('p','CloneShell',[],'copy',true);
const E=(name,ref)=>({new:'Objects.DxfLayerIndexEntry',args:[name,R(ref)]});
const entries=values=>A('Objects.DxfLayerIndexEntry',values);
const name=n=>Object.is(n,-0)?'-0':String(n);
export function indexFilterCorpus() {
  const cases=[],add=(n,steps)=>cases.push({name:'index-filter/'+n,category:'index-filter',request:{steps}});
  for(const type of ['DxfIdBuffer','DxfLayerIndex','DxfLayerFilter','DxfSpatialIndex','DxfSpatialFilter'])
    add('default/'+type,[N(type),C('p','ToString'),shell(),snap('copy')]);
  for(const type of ['DxfSpatialIndex','DxfLayerIndex'])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,2451545,Number.MAX_VALUE,Infinity,NaN])
    add(type+'/timestamp/'+name(value),[N(type),S('p','Timestamp',D(value)),snap('p'),shell()]);
  for(const text of [null,'',' ','\t','A','a','A/B','a|b','日本😀','A\0B','A\rB','A\nB',{utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[0xd800,65]},{utf16:[0xd800,0xdc00]}]) {
    add('entry-name/'+cases.length,[N('DxfIdBuffer',[],'buffer'),N('DxfLayerIndexEntry',[text,R('buffer')]),N('DxfLayerIndexEntry',[text,null],'invalid')]);
    add('filter-name/'+cases.length,[N('DxfLayerFilter',[A('String',[text])]),N('DxfLayerFilter',[],'q'),G('q','LayerNames','names'),C('names','Add',[text]),snap('q')]);
  }
  add('filter-constructor-null',[N('DxfLayerFilter',[null])]);
  for(const type of ['DxfIdBuffer','DxfLayerFilter'])for(const member of ['Insert','set_Item'])for(const index of [-1,0,1,2])for(const invalid of [false,true]) {
    const isRef=type==='DxfIdBuffer',value=isRef?null:invalid?'bad\nname':'good';
    add(`${type}/${member}/${index}/${invalid}`,[N(type),G('p',isRef?'References':'LayerNames','list'),C('list','Add',[isRef?null:'seed']),C('list',member,[I(index),value]),snap('p'),shell(),snap('copy')]);
  }
  add('buffer-reference-order',[N('DxfIdBuffer'),N('Entities.Point',[],'point'),N('DxfPlaceholder',[],'object'),G('p','References','refs'),
    C('refs','Add',[R('point')]),C('refs','Add',[null]),C('refs','Add',[R('point')]),C('refs','Add',[R('object')]),snap('p'),shell(),C('refs','Remove',[R('point')]),snap('p'),snap('copy')]);
  add('buffer-copy-mapping',[N('DxfIdBuffer'),N('Entities.Point',[],'point'),N('Entities.Point',[],'replacement'),G('p','References','refs'),C('refs','Add',[R('point')]),C('refs','Add',[R('point')]),shell(),
    C('p','CopyDatabaseReferencesTo',[R('copy'),{resolver:[[R('point'),R('replacement')]]}],null,true),snap('p'),snap('copy')]);
  add('layer-adopt-release',[N('DxfLayerIndex'),N('DxfIdBuffer',[],'a'),N('DxfIdBuffer',[],'b'),C('p','SetEntries',[entries([E('A','a'),E('A','b')])]),G('p','Entries','old'),
    G('a','References','refs'),C('refs','Add',[null]),C('refs','Add',[R('p')]),snap('p'),snap('a'),C('p','SetEntries',[entries([E('B','b')])]),snap('old'),snap('p'),snap('a'),snap('b')]);
  for(const fault of ['null-values','null-entry','duplicate','foreign','erased-index','erased-child','cycle']) {
    const steps=[N('DxfLayerIndex'),N('DxfIdBuffer',[],'a'),N('DxfIdBuffer',[],'b'),C('p','SetEntries',[entries([E('A','a')])])];
    if(fault==='foreign')steps.push(N('DxfDictionary',[],'other'),S('b','Owner',R('other'),true));
    if(fault==='erased-index')steps.push(S('p','IsErased',true,true));
    if(fault==='erased-child')steps.push(S('b','IsErased',true,true));
    if(fault==='cycle')steps.push(S('p','Owner',R('b'),true));
    const values=fault==='null-values'?null:entries(fault==='null-entry'?[null]:fault==='duplicate'?[E('B','b'),E('duplicate','b')]:[E('B','b')]);
    steps.push(C('p','SetEntries',[values]),snap('p'),snap('a'),snap('b'));add('adopt/'+fault,steps);
  }
  add('layer-copy-mapping',[N('DxfLayerIndex'),N('DxfIdBuffer',[],'a'),N('DxfIdBuffer',[],'b'),C('p','SetEntries',[entries([E('A','a')])]),shell(),
    C('p','CopyDatabaseReferencesTo',[R('copy'),{resolver:[[R('a'),R('b')]]}],null,true),snap('p'),snap('copy'),snap('a'),snap('b')]);
  for(const type of ['DxfLayerIndex','DxfLayerFilter','DxfSpatialFilter'])for(const owner of ['none','dictionary','buffer']) {
    add(`schema/${type}/${owner}`,[N(type),N(owner==='buffer'?'DxfIdBuffer':'DxfDictionary',[],'owner'),...(owner==='none'?[]:[S('p','Owner',R('owner'),true)]),
      N('List<String>',[],'errors'),C('p','ValidateDatabaseSchema',[null,R('errors')],null,true),snap('errors')]);
  }
  for(const member of ['Normal','Origin'])for(const xyz of [[0,0,0],[-0,0,0],[0,0,5],[Number.MIN_VALUE,0,0],[-1,3,4],[NaN,0,1],[0,Infinity,1],[0,1,-Infinity]])
    add('spatial/'+member+'/'+cases.length,[N('DxfSpatialFilter'),S('p',member,V('Vector3',...xyz)),snap('p'),shell(),snap('copy')]);
  for(const member of ['FrontClippingDistance','BackClippingDistance'])for(const value of [null,-Infinity,-2,-0,0,Number.MIN_VALUE,1,Number.MAX_VALUE,Infinity,NaN])
    add(`spatial/${member}/${name(value)}`,[N('DxfSpatialFilter'),S('p',member,value===null?null:D(value)),snap('p'),shell()]);
  for(const member of ['InverseInsertTransform','ClipBoundaryTransform'])for(let cell=0;cell<16;cell++)for(const value of [NaN,Infinity,0,2]) {
    const cells=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];cells[cell]=value;
    add(`spatial/${member}/${cell}/${value}`,[N('DxfSpatialFilter'),S('p',member,{new:'Matrix4',args:cells.map(D)}),snap('p'),shell()]);
  }
  for(const values of [null,[],[V('Vector2',0,0)],[V('Vector2',0,0),V('Vector2',1,1)],[V('Vector2',0,0),V('Vector2',1,1),V('Vector2',NaN,0)]])
    add('spatial/boundary/'+cases.length,[N('DxfSpatialFilter'),C('p','SetBoundary',[values===null?null:A('Vector2',values)]),snap('p'),shell()]);
  add('spatial/snapshot',[N('DxfSpatialFilter'),G('p','Boundary','before'),G('p','Normal','normal'),S('normal','Z',D(99)),
    C('p','SetBoundary',[A('Vector2',[V('Vector2',2,3),V('Vector2',4,5)])]),S('p','IsClippingEnabled',false),snap('before'),snap('p'),shell(),snap('copy')]);
  return cases;
}
