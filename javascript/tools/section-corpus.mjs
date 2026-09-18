// Supplemental inputs; expected metadata, copy behavior and errors come from the pinned assembly.
import { D,R,E,A,V,I } from './geometry-corpus.mjs';
const N=(type='Section',args=[],id='p')=>({kind:'new',type:type.includes('.')?type:'Entities.'+type,args,id});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic}:{})});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(nonPublic?{nonPublic}:{})});
const snap=target=>({kind:'snapshot',target});
export function sectionCorpus(){
  const out=[],add=(name,steps)=>out.push({name:'section/'+name,category:'section',request:{steps}});
  for(const name of [null,'','SECTION','SECTIONOBJECT','section','SECTIONOBJECT '])add('constructor/'+JSON.stringify(name),[N('Section',[name])]);
  for(const member of ['Name','IndicatorColorName'])for(const text of [null,'','東京😀','a\nb','a\rb','a\0b',{utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[0xd800,0xdc00]},String.raw`Literal \U+000A`])
    add(member+'/'+out.length,[N(),S('p',member,text),snap('p'),C('p','Clone')]);
  for(const member of ['TopHeight','BottomHeight'])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1,Infinity,NaN])add(member+'/'+out.length,[N(),S('p',member,D(value)),snap('p'),C('p','Clone')]);
  for(const member of ['State','Flags','IndicatorTransparency','StoredIndicatorColor','StoredNativeIndicatorColor'])for(const value of [-32768,-1,0,1,256,32767,null]){
    if(value===null&&['State','Flags','IndicatorTransparency'].includes(member))continue;
    add(member+'/'+value,[N(),S('p',member,value===null?null:['State','Flags'].includes(member)?I(value):{short:value}),snap('p'),C('p','Clone')]);
  }
  for(const n of [-Infinity,NaN,Infinity,-0,0,Number.MIN_VALUE,7])for(let axis=0;axis<3;axis++){
    const v=[1,2,3];v[axis]=n;add('vertical/'+out.length,[N(),S('p','VerticalDirection',V('Vector3',...v)),G('p','VerticalDirection','v'),S('v','X',D(99)),snap('p')]);
    for(const member of ['Vertices','BackLineVertices'])add(member+'/'+out.length,[N(),G('p',member,'vs'),C('vs','Add',[V('Vector3',4,5,6)]),C('vs','Add',[V('Vector3',...v)]),C('vs','set_Item',[I(0),V('Vector3',...v)]),snap('p'),C('p','Clone')]);
  }
  for(const member of ['Vertices','BackLineVertices'])for(const index of [-1,0,1,2])add('index/'+member+'/'+index,[N(),G('p',member,'vs'),C('vs','Add',[V('Vector3',1,2,3)]),C('vs','Insert',[I(index),V('Vector3',NaN,0,0)]),C('vs','set_Item',[I(index),V('Vector3',NaN,0,0)]),snap('p')]);
  for(const present of [false,true])add('settings/'+present,[N(),S('p','HasSettingsField',present,true),C('p','Clone',[],'q'),snap('q')]);
  for(const name of ['IsErased','GeometrySettings'])add('reject/'+name,[N(),...(name==='IsErased'?[S('p',name,true,true)]:[N('Objects.DxfPlaceholder',[],'settings'),S('p',name,R('settings'),true)]),C('p','Clone'),snap('p')]);
  for(const handle of ['0','00','0000','AB'])add('xdata/'+handle,[N(),N('netDxf.XData',[{new:'Tables.ApplicationRegistry',args:['SEC']}],'data'),G('data','XDataRecord','records'),C('records','Add',[{new:'XDataRecord',args:[E('XDataCode',1005),handle]}]),G('p','XData','dict'),C('dict','Add',[R('data')]),C('p','Clone'),snap('p')]);
  for(const member of ['PersistentReactors','Reactors'])add('reactor/'+member,[N(),N('Line',[],'line'),...(member==='Reactors'?[C('p','AddReactor',[R('line')],null,true)]:[G('p',member,'rs'),C('rs','Add',[R('line')])]),C('p','Clone'),snap('p')]);
  for(let row=0;row<4;row++)for(let col=0;col<4;col++)for(const value of [Number.MIN_VALUE,NaN,Infinity,2]){
    const m=Array.from({length:16},(_,i)=>i%5===0?1:0);m[row*4+col]=value;add(`transform/${row}/${col}/${value}`,[N(),C('p','TransformBy',[{new:'Matrix4',args:m.map(D)}]),snap('p')]);
  }
  for(const erased of [false,true])add('view-reference/'+erased,[N(),S('p','IsErased',erased,true),N('Tables.View',['VIEW'],'view'),S('view','LiveSection',R('p')),G('view','LiveSection','target'),{kind:'reference-equals',args:[R('p'),R('target')]},C('view','Clone',[],'copy'),G('copy','LiveSection','target2'),{kind:'reference-equals',args:[R('p'),R('target2')]}]);
  return out;
}
