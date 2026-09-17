// Independent-oracle inputs only: neither production algorithms nor expected values.
import { mtextCorpus } from './mtext-corpus.mjs';
import { displayEntityCorpus } from './display-entity-corpus.mjs';
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const names=['Point','Line','Ray','XLine','Face3D','Solid','Trace'];
const N=(type,args=[],id='p',signature)=>({kind:'new',type:type.includes('.')?type:'Entities.'+type,args,id,...(signature?{signature}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const mat=(...values)=>({new:'Matrix3',args:values.map(D)});
const identity=()=>mat(1,0,0,0,1,0,0,0,1);
const vector=value=>V('Vector3',...value);
const bytes=values=>A('Byte',values.map(value=>({byte:value})));
const specialName=n=>Object.is(n,-0)?'-0':String(n);
const constructors=name=> {
 const v2=[V('Vector2',1.25,-3),V('Vector2',-2,4.5),V('Vector2',7,-8),V('Vector2',-9,10)];
 const v3=[V('Vector3',1.25,-3,5),V('Vector3',-2,4.5,6),V('Vector3',7,-8,9),V('Vector3',-9,10,11)];
 if(name==='Point')return [[],[v2[0]],[v3[0]],[D(-0),D(4),D(5)]];
 if(['Line','Ray','XLine'].includes(name))return [[],v2.slice(0,2),v3.slice(0,2)];
 return name==='Face3D'?[[],v2.slice(0,3),v2,v3.slice(0,3),v3]:[[],v2.slice(0,3),v2];
};
export function entityCorpus() {
 const probes=[],add=(name,category,steps)=>probes.push({name,category,request:{steps}});
 for(const name of names)constructors(name).forEach((args,i)=>add(`${name}/constructor/${i}`,'constructors',[N(name,args),C('p','ToString'),C('p','Clone',[],'q'),snap('q')]));
 for(const name of names) {
   add(`${name}/attributes-clone`,'common-data',[N(name),
     S('p','Layer',{new:'Tables.Layer',args:['AUTHORED']}),S('p','Linetype',{static:'Tables.Linetype',property:'Dashed'}),
     S('p','Color',{static:'AciColor',property:'Red'}),S('p','Transparency',{new:'Transparency',args:[{short:37}]}),
     S('p','Lineweight',E('Lineweight',35)),S('p','LinetypeScale',D(2.5)),S('p','IsVisible',false),
     S('p','ColorName','Book$青\0\n'),S('p','ShadowMode',E('Entities.EntityShadowMode',0)),S('p','ProxyGraphics',bytes([0,128,255])),
     S('p','Handle','CAFE'),N('Line',[],'reactor'),C('p','AddReactor',[R('reactor')],null,null,true),
     C('p','Clone',[],'q'),G('q','Layer','layer'),S('layer','Name','CLONE_LAYER'),
     G('q','Color','color'),S('color','Index',{short:2}),G('q','ProxyGraphics','proxy'),
     C('q','ClearProxyGraphics'),snap('p'),snap('q')]);
   for(const member of ['Layer','Linetype','Color','Transparency'])add(`${name}/null/${member}`,'guards',[N(name),S('p',member,null),snap('p')]);
   for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1,Infinity,NaN])add(`${name}/scale/${specialName(value)}`,'guards',[N(name),S('p','LinetypeScale',D(value)),snap('p'),C('p','Clone')]);
   for(const value of [null,-1,0,1,2,3,4,32767])add(`${name}/shadow/${value}`,'guards',[N(name),S('p','ShadowMode',value===null?null:E('Entities.EntityShadowMode',value)),snap('p')]);
   for(const value of [null,[],[1],[0,255,128]])add(`${name}/proxy/${JSON.stringify(value)}`,'common-data',[N(name),S('p','ProxyGraphics',value===null?null:bytes(value)),C('p','Clone',[],'q'),C('p','ClearProxyGraphics'),snap('q'),snap('p')]);
   for(const [i,normal] of [[0,[0,0,0]],[1,[0,0,2]],[2,[0,3,4]],[3,[2,-3,4]],[4,[Number.MIN_VALUE,0,0]],[5,[Infinity,1,0]],[6,[NaN,0,1]],[7,[-0,0,-1]]])
     add(`${name}/normal/${i}`,'normals',[N(name),S('p','Normal',vector(normal)),G('p','Normal','normal'),S('normal','X',D(99)),snap('p'),C('p','Clone')]);
   for(const event of ['Layer','Linetype'])for(const failure of [false,true])add(`${name}/event/${event}/${failure}`,'events',[
     N(name),N('Tables.'+event,['SUBSTITUTE'],'sub'),N('Tables.'+event,['PROPOSED'],'prop'),
     {kind:'observe',target:'p',member:event+'Changed',observer:'change',replace:R('sub'),throw:failure},
     S('p',event,R('prop')),snap('p'),{kind:'events'}, {kind:'unobserve',observer:'change'},S('p',event,R('prop')),snap('p')]);
   const args=constructors(name).at(-1);
   const matrices=[identity(),mat(2,0,0,0,2,0,0,0,2),mat(-1,0,0,0,1,0,0,0,1),mat(0,-1,0,1,0,0,0,0,1),
     mat(0,0,0,0,0,0,0,0,0),mat(1,0,0,0,1,0,0,0,0),mat(1,2,0,0,1,0,0,0,1),mat(2,1,-1,1,3,2,-1,0,2)];
   matrices.forEach((m,i)=>add(`${name}/transform/${i}`,'transforms',[
     N(name,args),S('p','ProxyGraphics',bytes([7,8,9])),
     ...(['Solid','Trace'].includes(name)?[S('p','Elevation',D(2.5)),S('p','Thickness',D(-1))]:[]),
     C('p','Clone',[],'q'),C('p','TransformBy',[m,vector([3,-4,5])],null,['Matrix3','Vector3']),snap('p'),snap('q'),C('p','Clone')]));
   add(`${name}/matrix4`,'transforms',[N(name,args),C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}],null,['Matrix4']),snap('p')]);
 }
 for(const type of ['Ray','XLine'])for(const direction of [[0,0,0],[0,0,2],[3,4,0],[2,-3,4],[1e-300,0,0],[Infinity,1,0],[NaN,0,1]])
   add(`${type}/direction/${direction}`,'directions',[N(type,[vector([1,2,3]),vector(direction)]),N(type,[],'q'),S('q','Direction',vector(direction)),snap('q'),C('q','Clone')]);
 for(const type of ['Point','Line'])for(const value of [-0,1,-1,Infinity,-Infinity,NaN])
   add(`${type}/scalar/${specialName(value)}`,'scalars',[N(type),S('p','Thickness',D(value)),...(type==='Point'?[S('p','Rotation',D(value))]:[C('p','Reverse')]),snap('p'),C('p','Clone')]);
 for(const value of [-1,0,1,2,4,8,15,32767])add(`Face3D/edges/${value}`,'scalars',[N('Face3D'),S('p','EdgeFlags',E('Entities.Face3DEdgeFlags',value)),C('p','Clone')]);
 add('reactors/live-view','reactors',[N('Line'),N('Point',[],'one'),N('Point',[],'two'),G('p','Reactors','list'),
   C('p','AddReactor',[R('one')],null,null,true),C('p','AddReactor',[R('one')],null,null,true),C('p','AddReactor',[null],null,null,true),snap('list'),
   C('p','RemoveReactor',[R('two')],null,null,true),C('p','RemoveReactor',[R('one')],null,null,true),snap('list'),snap('p'),C('p','Clone')]);
 for(const member of ['StartWidth','EndWidth','StartWidthOverride','EndWidthOverride'])for(const value of [-Infinity,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1,Infinity,NaN])
   add(`vertex/${member}/${specialName(value)}`,'vertex-widths',[N('Polyline2DVertex',[D(1),D(2),D(-.5)]),S('p',member,D(value)),snap('p'),C('p','Clone',[],'q'),S('q','StartWidthOverride',null),S('q','EndWidthOverride',null),snap('p'),snap('q')]);
 for(const value of [null,-2147483648,-1,0,1,2147483647])add(`vertex/id/${value}`,'vertex-model',[N('Polyline2DVertex',[V('Vector2',3,4)]),S('p','VertexIdentifier',value===null?null:I(value)),C('p','Clone')]);
 for(const args of [[],[D(1),D(2)],[D(1),D(2),D(-0)],[V('Vector2',3,4)],[V('Vector2',3,4),D(2)], [null]])
   add(`vertex/constructor/${probes.length}`,'vertex-model',[N('Polyline2DVertex',args)]);
 add('vertex/copy-isolation','vertex-model',[N('Polyline2DVertex',[D(1),D(2),D(.5)]),S('p','StartWidth',D(-0)),S('p','VertexIdentifier',I(0)),
   N('Polyline2DVertex',[R('p')],'q'),G('q','Position','position'),S('position','X',D(99)),S('q','StartWidth',D(5)),snap('p'),snap('q'),C('p','ToString')]);
 // Deterministic seeded nonuniform transforms; kept even when they expose inherited numerical differences.
 let seed=0x454e5449;const next=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed;};
 for(let i=0;i<49;i++){
   const name=names[i%names.length],numbers=Array.from({length:9},()=>((next()%17)-8)/4),position=Array.from({length:3},()=>((next()%33)-16)/8);
   add(`seeded/${name}/${i}`,'seeded-transforms',[N(name,constructors(name).at(-1)),C('p','TransformBy',[mat(...numbers),vector(position)],null,['Matrix3','Vector3']),snap('p')]);
 }
 return probes.concat(displayEntityCorpus(),mtextCorpus());
}
