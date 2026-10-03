// Input descriptions only; each expected outcome comes from the pinned C# assembly.
import {D,I,R,V,E,A} from './geometry-corpus.mjs';
const N=(type,args=[],id='p',extra={})=>({kind:'new',type:type.includes('.')?type:'Entities.'+type,args,id,...extra});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const C=(target,member,args=[],id,extra={})=>({kind:'call',target,member,args,...(id?{id}:{}),...extra});
const snap=target=>({kind:'snapshot',target});
const mat=m=>({new:'Matrix3',args:m.map(D)}),v=p=>V('Vector3',...p);
const bytes=p=>A('Byte',p.map(byte=>({byte})));
const make=(kind,id='p')=>kind==='AttributeDefinition'?[N(kind,['TAG'],id)]:[N('AttributeDefinition',['TAG'],'def'),N('Attribute',[R('def')],id)];
const label=x=>Object.is(x,-0)?'-0':String(x);
export function attributeCorpus(){
 const out=[],add=(name,steps,category='attributes')=>out.push({name:'attribute/'+name,category,request:{steps}});
 for(const tag of [null,'','TAG','T AG','  ','MiXeD','Żółć','青','\u0000']){
  add('tag/'+out.length,[N('AttributeDefinition',[tag]),C('p','Clone'),C('p','ToString')]);
  add('raw/'+out.length,[N('Attribute',[tag],'p',{nonPublic:true,signature:['String']}),C('p','Clone'),C('p','ToString')]);
 }
 for(const args of [[null,null],['TAG',null],[null,D(0),null],['',D(0),null],['TAG',D(0),null],['TAG',D(2),null]])add('constructor-guards/'+out.length,[N('AttributeDefinition',args),N('Attribute',[null],'a',{signature:['Entities.AttributeDefinition']})]);
 for(const fixed of [0,1e-13,.5,2.5])for(const explicit of [null,-1,-0,0,Number.MIN_VALUE,3,Infinity,NaN])add('heights/'+out.length,[N('Tables.TextStyle',['STYLE','txt.shx'],'style'),S('style','Height',D(fixed)),N('AttributeDefinition',explicit===null?['TAG',R('style')]:['TAG',D(explicit),R('style')]),C('p','Clone')]);
 for(const kind of ['AttributeDefinition','Attribute']){
  for(const key of ['Height','Width','WidthFactor','ObliqueAngle','Rotation','LinetypeScale'])for(const value of [-Infinity,-100,-85,-1,-0,0,Number.MIN_VALUE,.001,1,85,100,101,Infinity,NaN])
    add(`${kind}/${key}/${label(value)}`,[...make(kind),S('p',key,D(value)),snap('p'),C('p','Clone')]);
  for(const key of ['Value',...(kind==='AttributeDefinition'?['Prompt']:[])])for(const value of [null,'',' ','  padded  ','a\n\0青'])
    add(`${kind}/${key}/${out.length}`,[...make(kind),S('p',key,value),snap('p'),C('p','Clone')]);
  for(const key of ['Layer','Linetype','Style','Color','Transparency'])add(`${kind}/null/${key}`,[...make(kind),S('p',key,null),snap('p')]);
  for(const flags of [-1,0,1,2,4,8,16,32767])add(`${kind}/flags/${flags}`,[...make(kind),S('p','Flags',E('Entities.AttributeFlags',flags)),S('p','IsBackward',true),S('p','IsUpsideDown',true),C('p','Clone')]);
  for(const normal of [[0,0,0],[0,0,3],[1,2,3],[-0,0,-1],[Number.MIN_VALUE,0,0],[Infinity,1,0],[NaN,0,1]])add(`${kind}/normal/${out.length}`,[...make(kind),S('p','Normal',v(normal)),G('p','Normal','n'),S('n','X',D(17)),snap('p'),C('p','Clone')]);
  for(const [property,event,resource] of [['Layer','LayerChanged','Layer'],['Linetype','LinetypeChanged','Linetype'],['Style',kind==='AttributeDefinition'?'TextStyleChange':'TextStyleChanged','TextStyle']])for(const failure of [false,true])for(const replaceNull of [false,true]){
    const args=resource==='TextStyle'?['PROPOSED','txt.shx']:['PROPOSED'];
    add(`${kind}/event/${property}/${failure}/${replaceNull}`,[...make(kind),N('Tables.'+resource,args,'proposed'),N('Tables.'+resource,resource==='TextStyle'?['SUB','txt.shx']:['SUB'],'sub'),
     {kind:'observe',target:'p',member:event,observer:'change',replace:replaceNull?null:R('sub'),throw:failure},S('p',property,R('proposed')),snap('p'),{kind:'events'},C('p','Clone'),{kind:'unobserve',observer:'change'},S('p',property,R('proposed')),snap('p')]);
  }
  for(const shadow of [null,-1,0,1,3,4])for(const proxy of [null,[],[1,0,128,255]])add(`${kind}/common/${out.length}`,[...make(kind),S('p','ShadowMode',shadow===null?null:E('Entities.EntityShadowMode',shadow)),S('p','ColorName','BOOK$青'),S('p','ProxyGraphics',proxy===null?null:bytes(proxy)),S('p','Handle','AB'),C('p','Clone',[],'q'),C('q','ClearProxyGraphics'),snap('p'),snap('q')]);
  const matrices=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[1,0,0,0,-1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[2,1,0,0,3,1,0,0,4],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]];
  for(const mirror of [false,true])for(const alignment of [0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,99])for(const [mIndex,m] of matrices.entries())add(`${kind}/transform/${mirror}/${alignment}/${mIndex}`,[...make(kind),S('p','Position',v([2,3,4])),S('p','Normal',v([0,3,4])),S('p','Height',D(2)),S('p','Width',D(9)),S('p','WidthFactor',D(.75)),S('p','ObliqueAngle',D(-25)),S('p','Rotation',D(37)),S('p','Alignment',E('Entities.TextAlignment',alignment)),S('p','ProxyGraphics',bytes([1,2])),
    {kind:'set',type:'Entities.Text',member:'DefaultMirrText',value:mirror},C('p','TransformBy',[mat(m),v([10,-20,30])],null,{signature:['Matrix3','Vector3']}),snap('p'),C('p','Clone')]);
  add(`${kind}/matrix4`,[...make(kind),C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}],null,{signature:['Matrix4']}),snap('p')]);
 }
 add('definition-sharing',[N('AttributeDefinition',['TAG'],'d'),S('d','Value','source'),S('d','ColorName','Book'),S('d','ProxyGraphics',bytes([1,2])),N('Attribute',[R('d')],'a'),C('a','Clone',[],'b'),G('a','Definition','da'),G('b','Definition','db'),{kind:'reference-equals',args:[R('d'),R('da')]},{kind:'reference-equals',args:[R('da'),R('db')]},G('d','Layer','l'),S('l','Name','SHARED'),G('a','Color','c'),S('c','Index',{short:2}),S('d','ProxyGraphics',bytes([9])),snap('a'),snap('d'),snap('b')]);
 return out;
}
