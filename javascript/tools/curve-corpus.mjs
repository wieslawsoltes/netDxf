import { ellipseCorpus } from './ellipse-corpus.mjs';
// Supplemental input descriptions only. Expected results come from the pinned production assembly.
import { D, I, R, V, E, A } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',extra={})=>({kind:'new',type:type.includes('.')?type:'Entities.'+type,args,id,...extra});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic:true}:{})});
const C=(target,member,args=[],id,nonPublic=false,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(nonPublic?{nonPublic:true}:{}),...(signature?{signature}:{})});
const snap=target=>({kind:'snapshot',target});
const mat=n=>({new:'Matrix3',args:n.map(D)});
const tag=(code,value)=>({new:'IO.DxfTag',args:[{short:code},value]});
const vertex=(x,y,b=0)=>({new:'Entities.Polyline2DVertex',args:[D(x),D(y),D(b)]});
const verts=(n,bulge=0)=>A('Entities.Polyline2DVertex',Array.from({length:n},(_,i)=>vertex(i*1.25-2,(i*i%7)-3,i%2?bulge:-bulge)));
const poly=(n=4,closed=false,bulge=.5)=>N('Polyline2D',[verts(n,bulge),closed]);
const idx=(target,i,id)=>({kind:'index',target,args:[I(i)],id});
const label=v=>Object.is(v,-0)?'-0':String(v);
const matrices=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,2,0,0,0,7],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[2,0,0,0,3,0,0,0,1],[1,2,0,0,1,0,0,0,1],[0,0,0,0,0,0,0,0,0],[1,0,1,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[NaN,0,0,0,1,0,0,0,1]];
function retained(){
 const record=(id,code='VERTEX')=>N('Entities.Polyline2DRecord',[code,{new:'List<IO.DxfTag>',args:[A('IO.DxfTag',[tag(0,code),tag(5,'A'),tag(330,'0')])]}],id,{nonPublic:true});
 return [poly(3,true,.25),G('p','Vertexes','vertices'),...['a','b','c'].flatMap((id,i)=>[idx('vertices',i,'v'+i),record(id),S(id,'StoredVertex',R('v'+i),true)]),record('end','SEQEND'),
  C('p','SetStoredRecords',[null,A('Entities.Polyline2DRecord',['a','b','c'].map(R)),R('end')],null,true),
  S('p','StoredHeaderTags',{new:'List<IO.DxfTag>',args:[A('IO.DxfTag',[tag(0,'POLYLINE')])]},true),S('p','StoredNormal',V('Vector3',0,0,1),true)];
}
export function curveCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'curves/'+name,category:'curves',request:{steps}});
 for(const type of ['Circle','Arc']){
  const args=(r=2)=>[V('Vector3',1,2,3),D(r),...(type==='Arc'?[D(-30),D(245)]:[])];
  add(type+'/default',[N(type),C('p','Clone'),C('p','ToString')]);
  for(const radius of [-Infinity,-1,-0,0,Number.MIN_VALUE,1,2,Infinity,NaN])add(type+'/radius/'+label(radius),[N(type,args(radius)),N(type,[],'q'),S('q','Radius',D(radius)),snap('q'),C('q','Clone')]);
  for(const center of [V('Vector2',1,2),V('Vector3',1,2,3)])add(type+'/overload/'+out.length,[N(type,[center,D(3),...(type==='Arc'?[D(20),D(260)]:[])]),C('p','Clone')]);
  for(const n of [-1,0,1,2,3,7,32])add(type+'/polygon/'+n,[N(type,args()),C('p','PolygonalVertexes',[I(n)]),C('p','ToPolyline2D',[I(n)])]);
  for(const thickness of [-Infinity,-2,-0,0,Infinity,NaN])add(type+'/thickness/'+label(thickness),[N(type,args()),S('p','Thickness',D(thickness)),C('p','Clone'),C('p','ToPolyline2D',[I(5)])]);
  for(const normal of [[0,0,1],[0,3,4],[2,-3,4]])for(const [i,m] of matrices.entries())add(type+'/transform/'+i+'/'+normal,[N(type,args()),S('p','Normal',V('Vector3',...normal)),S('p','Thickness',D(2)),C('p','TransformBy',[mat(m),V('Vector3',3,-4,5)]),snap('p'),C('p','Clone'),C('p','PolygonalVertexes',[I(5)])]);
  add(type+'/appearance',[N(type,args()),S('p','IsVisible',false),S('p','Layer',{new:'Tables.Layer',args:['L']}),S('p','Linetype',{static:'Tables.Linetype',property:'Dashed'}),S('p','Color',{static:'AciColor',property:'Red'}),S('p','ProxyGraphics',A('Byte',[{byte:1}])),S('p','ColorName','Book'),C('p','ToPolyline2D',[I(5)]),C('p','Clone')]);
  add(type+'/matrix4',[N(type,args()),C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap('p')]);
 }
 for(const start of [-450,-0,0,30,360,Infinity,NaN])for(const end of [0,30,270,720])add('Arc/angles/'+out.length,[N('Arc',[V('Vector3',1,2,3),D(2),D(start),D(end)]),C('p','PolygonalVertexes',[I(5)]),C('p','Clone')]);
 for(const bulge of [-Infinity,-5,-1,-.25,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,.5,1,5,Infinity,NaN])for(const same of [false,true])add('Arc/bulge/'+out.length,[N('Arc',[V('Vector2',1,2),same?V('Vector2',1,2):V('Vector2',7,-4),D(bulge)]),N('Circle',[],'safe')]);
 for(let n=0;n<=6;n++)for(const closed of [false,true])for(const b of [0,.25,-2])add('polyline/sampling/'+out.length,[poly(n,closed,b),C('p','PolygonalVertexes',[I(8)]),C('p','Explode'),C('p','Clone')]);
 for(const smooth of [0,5,6,99])for(let n=0;n<=7;n++)for(const closed of [false,true])for(const precision of [0,2,9])add('polyline/smooth/'+out.length,[poly(n,closed),S('p','SmoothType',E('Entities.PolylineSmoothType',smooth)),C('p','PolygonalVertexes',[I(precision)]),C('p','Explode'),C('p','Clone')]);
 for(const b of [0,1e-13,.5,-.5,2,-2])for(const threshold of [0,1e-12,1,1e5])add('polyline/threshold/'+out.length,[poly(3,true,b),C('p','PolygonalVertexes',[I(16),D(threshold),D(threshold)],null,false,['Int32','Double','Double'])]);
 for(const width of [null,-1,-0,0,1,Number.MIN_VALUE,NaN,Infinity])add('polyline/width/'+label(width),[poly(),S('p','ConstantWidth',width===null?null:D(width)),G('p','Vertexes','vs'),idx('vs',1,'v'),S('v','StartWidthOverride',D(2.5)),S('v','EndWidthOverride',D(0)),C('p','GetEffectiveStartWidth',[I(1)]),C('p','GetEffectiveEndWidth',[I(1)]),snap('p'),C('p','SetConstantWidth',[width===null?D(3):D(width)]),snap('p')]);
 for(const n of [0,1,2,4])for(const closed of [false,true])add('polyline/reverse/'+out.length,[poly(n,closed),G('p','Vertexes','vs'),...Array.from({length:n},(_,i)=>[idx('vs',i,'v'+i),S('v'+i,'StartWidthOverride',i%2?D(0):null),S('v'+i,'EndWidthOverride',D(i+1)),S('v'+i,'VertexIdentifier',I(i))]).flat(),C('p','Reverse'),snap('p'),C('p','Reverse'),snap('p')]);
 for(const width of [null,0,2])for(const bulge of [0,.5])for(const [i,m] of matrices.entries())for(const normal of [[0,0,1],[0,3,4]])add('polyline/transform/'+out.length,[poly(4,true,bulge),S('p','ConstantWidth',width===null?null:D(width)),S('p','Normal',V('Vector3',...normal)),S('p','Elevation',D(2.5)),S('p','Thickness',D(3)),C('p','TransformBy',[mat(m),V('Vector3',3,-4,5)]),snap('p'),C('p','Clone')]);
 for(const args of [[],[null],[A('Vector2',[V('Vector2',1,2),V('Vector2',3,4)])]])add('polyline/constructors/'+out.length,[N('Polyline2D',args,'p',args[0]===null?{signature:['IEnumerable<Entities.Polyline2DVertex>']}:{})]);
 add('polyline/null-vertex',[N('Polyline2D',[A('Entities.Polyline2DVertex',[null])]),C('p','Explode'),C('p','Clone'),C('p','Reverse'),C('p','SetConstantWidth',[D(1)]),C('p','TransformBy',[mat(matrices[0]),V('Vector3',0,0,0)])]);
 for(const index of [-1,0,3,4])add('polyline/width-index/'+index,[poly(),C('p','GetEffectiveStartWidth',[I(index)]),C('p','GetEffectiveEndWidth',[I(index)])]);
 for(const closed of [false,true])add('retained/clone-reverse/'+closed,[...retained(),S('p','IsClosed',closed),S('p','LegacyDefaultStartWidth',D(2),true),S('p','LegacyDefaultEndWidth',D(3),true),snap('p'),C('p','Clone',[],'q'),C('p','Reverse'),snap('p'),snap('q'),C('p','Reverse'),snap('p')]);
 for(const change of ['ConstantWidth','SmoothType','HasPrivateHeader'])add('retained/guard/'+change,[...retained(),S('p',change,change==='ConstantWidth'?D(0):change==='SmoothType'?E('Entities.PolylineSmoothType',5):true,change==='HasPrivateHeader'),C('p','Clone'),snap('p')]);
 for(const [i,m] of matrices.entries())add('retained/transform/'+i,[...retained(),S('p','Elevation',D(2)),S('p','Thickness',D(3)),S('p','LegacyDefaultStartWidth',D(1),true),C('p','TransformBy',[mat(m),V('Vector3',3,-4,5)]),snap('p'),C('p','Clone')]);
 add('retained/rebinding-guard',[...retained(),G('p','Vertexes','vs'),{kind:'set-index',target:'vs',args:[I(0)],value:vertex(1,2)},C('p','Clone'),C('p','Reverse')]);
 add('retained/geometry-tags',[...retained(),S('v0','VertexIdentifier',I(0)),S('v0','StartWidthOverride',D(0)),C('a','GeometryTags',[],null,true),C('a','TopologyTagCount',[],null,true),C('end','GeometryTags',[],null,true)]);
 for(const code of ['VERTEX','SEQEND'])add('retained/empty/'+code,[N('Polyline2DRecord',[code,null],'bad',{nonPublic:true}),N('Polyline2DRecord',[code,{new:'List<IO.DxfTag>',args:[]}],'r',{nonPublic:true}),C('r','GeometryTags',[],null,true),C('r','TopologyTagCount',[],null,true),G('r','Resources','resources',true),{kind:'map-add',target:'resources',args:[I(0),null]},C('r','CanClone',[],null,true),C('r','CopyForClone',[null],null,true)]);
 let seed=0x43555256;const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return (seed%33-16)/8;};
 for(let i=0;i<40;i++)add('seeded/'+i,[N(i%2?'Arc':'Circle',[V('Vector3',random(),random(),random()),D(1+Math.abs(random())),...(i%2?[D(random()*100),D(random()*100)]:[])]),C('p','TransformBy',[mat(Array.from({length:9},random)),V('Vector3',random(),random(),random())]),C('p','PolygonalVertexes',[I(7)]),C('p','ToPolyline2D',[I(7)])]);
 return out.concat(ellipseCorpus());
}
