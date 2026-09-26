// Deterministic inputs only. Defaults, results, exceptions and references come from C# at run time.
import {mleaderSchema} from './mleader-schema.mjs';
import {D,I,R,V,E} from './geometry-corpus.mjs';
const N=(type,args=[],id='p')=>({kind:'new',type,args,id,...(type==='Blocks.BlockRecord'?{nonPublic:true}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id,nonPublic:true});
const C=(target,member,args=[],id)=>({kind:'call',target,member,args,...(id?{id}:{}),nonPublic:true});
const snap=target=>({kind:'snapshot',target});
const index=(target,i,id)=>({kind:'index',target,args:[I(i)],id});
const resources={BlockRecord:{new:'Blocks.BlockRecord',args:['B'],nonPublic:true},TextStyle:{new:'Tables.TextStyle',args:['T','simplex.shx']},Linetype:{new:'Tables.Linetype',args:['L']},DxfMLeaderStyle:{new:'Objects.DxfMLeaderStyle',args:[]},AttributeDefinition:{new:'Entities.AttributeDefinition',args:['TAG']}};
export function mleaderCorpus(){
 const out=[],add=(name,category,steps)=>out.push({name:'mleader/'+name,category,request:{steps}});
 const numbers=[-Infinity,-1,-0,0,Number.MIN_VALUE,1,4,Infinity,NaN];
 for(const [type,properties]of Object.entries(mleaderSchema)){
  const init=N('Entities.'+type);
  add(type+'/default','fields',[init,C('p','Clone',[],'q'),G('p','References','refs'),snap('q')]);
  for(const [key,typename,code]of properties){const t=typename.replace('?','');let values=t==='double'?numbers.map(D):t==='int'?[-2147483648,-1,0,1,2147483647].map(I):t==='short'?[-32768,-1,0,1,32767].map(short=>({short})):t==='bool'?[false,true]:t==='Vector3'?numbers.map(n=>V('Vector3',n,2,3)):t==='string'?[null,'','line\nline','a\rb','a\0b','東京 😀',{utf16:[0xd800]},{utf16:[0xdc00]}]:[null,resources[t]];
   if(typename.endsWith('?'))values=[null,...values];
   values.forEach((value,i)=>add(`${type}/${key}/${i}`,'fields',[init,C('p','Field',[{short:code}],'f'),S('p',key,value),G('p',key,'v'),C('p','Clone',[],'q'),snap('p'),snap('q'),C('p','ValidateValues',[E('Header.DxfVersion',18)])]));
   for(const version of [14,15,16,17,18])if(typename.endsWith('?'))add(`${type}/profile/${key}/${version}`,'profiles',[init,S('p',key,t==='bool'?true:t==='double'?D(1):{[t]:1}),C('p','ValidateValues',[E('Header.DxfVersion',version)]),S('p',key,null),C('p','ValidateValues',[E('Header.DxfVersion',version)]),snap('p')]);
  }
 }
 for(const [parent,member,child]of [['MLeaderContext','Leaders','MLeaderNode'],['MLeaderNode','Lines','MLeaderLine'],['MLeaderProperties','ArrowHeads','MLeaderArrowHead'],['MLeaderProperties','BlockAttributes','MLeaderBlockAttribute']])for(const action of ['same','null','duplicate','foreign','remove','clear','clone']){
  add(`ownership/${member}/${action}`,'ownership',[N('Entities.'+parent),G('p',member,'list'),N('Entities.'+child,[],'a'),C('list','Add',[R('a')]),N('Entities.'+parent,[],'other'),G('other',member,'otherList'),
   ...(action==='same'?[{kind:'set-index',target:'list',args:[I(0)],value:R('a')}]:action==='null'?[{kind:'set-index',target:'list',args:[I(0)],value:null}]:action==='foreign'?[C('otherList','Add',[R('a')])]:action==='duplicate'?[C('list','Add',[R('a')])]:action==='remove'?[C('list','Remove',[R('a')]),C('otherList','Add',[R('a')])]:action==='clear'?[C('list','Clear'),C('otherList','Add',[R('a')])]:[C('p','Clone',[],'q'),snap('q')]),snap('p'),snap('other'),snap('a')]);
 }
 for(const kind of ['MText','Block'])for(const action of ['same','foreign','opposite','null','clone'])add(`content/${kind}/${action}`,'ownership',[
  N('Entities.MLeaderContext'),N('Entities.MLeader'+(kind==='MText'?'MTextContent':'BlockContent'),[],'content'),S('p',kind,R('content')),N('Entities.MLeaderContext',[],'other'),
  ...(action==='same'?[S('p',kind,R('content'))]:action==='foreign'?[S('other',kind,R('content'))]:action==='opposite'?[S('p',kind==='MText'?'Block':'MText',{new:'Entities.MLeader'+(kind==='MText'?'BlockContent':'MTextContent'),args:[]})]:action==='null'?[S('p',kind,null),S('other',kind,R('content'))]:[C('p','Clone',[],'q'),snap('q')]),snap('p'),snap('other'),snap('content')]);
 for(const [type,key,required]of [['MLeaderMTextContent','ColumnHeights','Style'],['MLeaderBlockContent','TransformationMatrix','Block']])for(const values of [[],[0],[-1],[NaN],Array(16).fill(1),Array(17).fill(1)])add(`lists/${type}/${values.length}/${values[0]}`,'lists',[
  N('Entities.'+type),S('p',required,resources[required==='Style'?'TextStyle':'BlockRecord']),G('p',key,'list'),...values.map(v=>C('list','Add',[D(v)])),C('p','ValidateValues',[E('Header.DxfVersion',18)]),C('p','Clone',[],'q'),C('list','Clear'),snap('p'),snap('q')]);
 for(const target of ['valid','null','wrong'])add('remap/'+target,'references',[
  N('Entities.MLeaderMTextContent'),N('Tables.TextStyle',['Old','simplex.shx'],'old'),N('Tables.TextStyle',['New','simplex.shx'],'new'),N('Blocks.BlockRecord',['B'],'wrong'),S('p','Style',R('old')),
  C('p','MapReferences',[{resolver:[[R('old'),target==='valid'?R('new'):target==='wrong'?R('wrong'):null]]}]),snap('p'),G('p','References','refs')]);
 for(const type of ['Entities.MultiLeader','Objects.DxfMLeaderStyle'])for(const value of [null,{short:-1},{short:0},{short:1},{short:2},{short:3}])add(`envelope/${type}/${JSON.stringify(value)}`,'envelopes',[N(type),S('p',type.startsWith('Entities')?'StoredVersion':'StoredEnvelopeValue',value),C('p',type.startsWith('Entities')?'Clone':'CloneShell',[],'q'),snap('p'),snap('q')]);
 for(const size of [3,4])for(let row=1;row<=size;row++)for(let col=1;col<=size;col++)for(const number of [Number.MIN_VALUE,1e-12,NaN,Infinity]){const v=row===col?(Number.isFinite(number)?1+1e-12:number):number;add(`transform/${size}/${row}/${col}/${String(number)}`,'transforms',[
  N('Entities.MultiLeader'),{kind:'value',value:{static:'Matrix'+size,property:'Identity'},id:'m'},S('m','M'+row+col,D(v)),C('p','TransformBy',size===3?[R('m'),V('Vector3',0,0,0)]:[R('m')]),snap('p')]);}
 for(const version of [14,15,16,17,18])for(const type of [0,1,2,3])add(`validate/${version}/${type}`,'grammar',[
  N('Entities.MultiLeader'),G('p','Properties','props'),G('p','Context','context'),S('props','Style',resources.DxfMLeaderStyle),S('props','TextStyle',resources.TextStyle),S('props','LeaderLinetype',resources.Linetype),S('props','ContentType',{short:type}),
  ...(type===2?[N('Entities.MLeaderMTextContent',[],'text'),S('text','Style',resources.TextStyle),S('context','MText',R('text'))]:type===1?[N('Entities.MLeaderBlockContent',[],'block'),S('block','Block',resources.BlockRecord),S('context','Block',R('block'))]:[]),C('p','Validate',[null,E('Header.DxfVersion',version)]),C('p','Clone',[],'q'),snap('q')]);
 for(const [type,member]of [['MLeaderMTextContent','ColumnHeights'],['MLeaderBlockContent','TransformationMatrix']])for(const action of ['none','add','clear','set','remove','reset'])add(`value-iterator/${member}/${action}`,'lists',[
  N('Entities.'+type),G('p',member,'list'),C('list','GetEnumerator',[],'empty'),G('empty','Current'),C('empty','MoveNext'),G('empty','Current'),C('list','Add',[D(NaN)]),C('list','Contains',[D(NaN)]),C('list','IndexOf',[D(NaN)]),C('list','GetEnumerator',[],'it'),G('it','Current'),C('it','MoveNext'),G('it','Current'),
  ...(action==='add'?[C('list','Add',[D(2)])]:action==='clear'?[C('list','Clear')]:action==='set'?[{kind:'set-index',target:'list',args:[I(0)],value:D(3)}]:action==='remove'?[C('list','Remove',[D(NaN)])]:action==='reset'?[{kind:'enumerator-reset',target:'it'}]:[]),C('it','MoveNext'),G('it','Current'),snap('list')]);
 add('late-tree-enumeration','ownership',[N('Entities.MLeaderContext'),C('p','Tree',[],'tree'),G('p','Children','children'),G('p','Leaders','leaders'),N('Entities.MLeaderNode',[],'node'),C('leaders','Add',[R('node')]),snap('tree'),snap('tree'),snap('children')]);
 add('remap-atomic-two-references','references',[N('Entities.MLeaderProperties'),N('Tables.TextStyle',['OldText','simplex.shx'],'ts'),N('Tables.TextStyle',['NewText','simplex.shx'],'newTs'),N('Tables.Linetype',['OldLine'],'lt'),N('Tables.Linetype',['NewLine'],'newLt'),S('p','LeaderLinetype',R('lt')),S('p','TextStyle',R('ts')),C('p','MapReferences',[{resolver:[[R('lt'),R('newLt')],[R('ts'),R('newLt')]]}]),G('p','LeaderLinetype'),G('p','TextStyle'),C('p','MapReferences',[{resolver:[[R('lt'),R('newLt')],[R('ts'),R('newTs')]]}]),G('p','LeaderLinetype'),G('p','TextStyle')]);
 return out;
}
