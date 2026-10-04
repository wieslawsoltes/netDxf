// Source-independent scenario inputs; all expected results come from actual C# APIs.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const P=(u,v,delta=0)=>A('Vector3',Array.from({length:Math.max(0,u*v+delta)},(_,i)=>V('Vector3',i%u,Math.floor(i/u),((i*i%7)-3)*.125)));
const N=(u=4,v=4,delta=0)=>({kind:'new',type:'Entities.PolygonMesh',args:[{short:u},{short:v},P(u,v,delta)],id:'p'});
const S=(member,value)=>({kind:'set',target:'p',member,value});
const C=(member,args=[],id,nonPublic=false)=>({kind:'call',target:'p',member,args,...(id?{id}:{}),...(nonPublic?{nonPublic}:{})});
const G=(member,id)=>({kind:'get',target:'p',member,id});const snap=()=>({kind:'snapshot',target:'p'});
const label=n=>Object.is(n,-0)?'-0':String(n);
function retained(){
 const tag=(c,v)=>({new:'IO.DxfTag',args:[{short:c},v]});
 const record=(id,code='VERTEX')=>({kind:'new',type:'Entities.PolygonMeshRecord',args:[code,{new:'List<IO.DxfTag>',args:[A('IO.DxfTag',[tag(0,code),tag(5,'A'),tag(330,'0')])]}],id,nonPublic:true});
 return [N(2,2),...['a','b','c','d'].map(id=>record(id)),record('end','SEQEND'),C('SetStoredRecords',[null,A('Entities.PolygonMeshRecord',['a','b','c','d'].map(R)),R('end')],null,true)];
}
export function polygonmeshCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'polygonmesh/'+name,category:'polygonmesh',request:{steps}});
 for(const axis of [0,1])for(const count of [-32768,-1,0,1,257,32767])add(`dimension/${axis}/${count}`,[{kind:'new',type:'Entities.PolygonMesh',args:[{short:axis===0?count:2},{short:axis===1?count:2},null],id:'p'}]);
 for(const delta of [-4,-1,0,1])add('count/'+delta,[N(2,2,delta)]);
 add('null',[{kind:'new',type:'Entities.PolygonMesh',args:[{short:2},{short:2},null],id:'p'}]);
 for(const u of [2,3,4,5])for(const v of [2,3,4])for(const smooth of [0,5,6])for(const close of [0,1,2,3])add(`grid/${u}/${v}/${smooth}/${close}`,[N(u,v),S('SmoothType',E('Entities.PolylineSmoothType',smooth)),S('IsClosedInU',(close&1)!==0),S('IsClosedInV',(close&2)!==0),S('DensityU',{short:4}),S('DensityV',{short:5}),C('MeshVertexes',[I(4),I(5)]),C('ToMesh',[I(4),I(5)]),C('Explode'),C('Clone')]);
 for(const member of ['DensityU','DensityV'])for(const value of [-1,0,1,2,3,6,201,202,32767])add(`${member}/${value}`,[N(),S(member,{short:value}),snap(),C('Clone')]);
 for(const smooth of [-1,0,5,6,8,32767])add('smooth/'+smooth,[N(),S('SmoothType',E('Entities.PolylineSmoothType',smooth)),snap(),C('Clone')]);
 for(const defaults of [0,1,2,6,200])for(const smooth of [0,5,6])add(`defaults/${defaults}/${smooth}`,[{kind:'set',type:'Entities.PolygonMesh',member:'DefaultSurfU',value:{short:defaults}},{kind:'set',type:'Entities.PolygonMesh',member:'DefaultSurfV',value:{short:2}},N(),S('SmoothType',E('Entities.PolylineSmoothType',smooth)),C('MeshVertexes'),C('ToMesh'),C('Explode'),{kind:'set',type:'Entities.PolygonMesh',member:'DefaultSurfU',value:{short:6}},{kind:'set',type:'Entities.PolygonMesh',member:'DefaultSurfV',value:{short:6}}]);
 for(const property of ['DefaultSurfU','DefaultSurfV'])for(const value of [-1,201])add(`${property}/${value}`,[{kind:'set',type:'Entities.PolygonMesh',member:property,value:{short:value}},{kind:'get',type:'Entities.PolygonMesh',member:property}]);
 for(const u of [-1,0,2,3])for(const v of [-1,0,2,3])add(`precision/${u}/${v}`,[N(),C('MeshVertexes',[I(u),I(v)]),C('ToMesh',[I(u),I(v)])]);
 for(const x of [-1,0,1,2])for(const y of [-1,0,1,2])add(`index/${x}/${y}`,[N(2,2),C('SetVertex',[I(x),I(y),V('Vector3',9,8,7)]),C('GetVertex',[I(x),I(y)],'point'),{kind:'set',target:'point',member:'X',value:D(99)},snap()]);
 for(const bad of [NaN,Infinity,-Infinity])for(const smooth of [0,6])add(`nonfinite/${bad}/${smooth}`,[N(),G('Vertexes','vs'),{kind:'set-index',target:'vs',args:[I(1)],value:V('Vector3',bad,0,0)},S('SmoothType',E('Entities.PolylineSmoothType',smooth)),C('MeshVertexes'),C('Clone'),C('ToMesh'),snap()]);
 for(const normal of [[0,0,1],[0,3,4]])for(const [i,m] of [[1,0,0,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[-1,0,0,0,1,0,0,0,1],[NaN,0,0,0,1,0,0,0,1]].entries())add(`transform/${normal}/${i}`,[N(),S('Normal',V('Vector3',...normal)),C('TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',3,-4,5)]),snap(),C('Clone'),C('MeshVertexes')]);
 add('appearance',[N(),S('Layer',{new:'Tables.Layer',args:['GRID']}),S('IsVisible',false),S('ProxyGraphics',A('Byte',[{byte:7}])),S('ColorName','Book'),C('ToMesh'),C('Explode'),C('Clone')]);
 add('retained-clone',[...retained(),C('Clone',[],'q'),C('SetVertex',[I(0),I(0),V('Vector3',10,20,30)]),snap(),{kind:'snapshot',target:'q'}]);
 add('retained-private',[...retained(),{kind:'set',target:'a',member:'HasPrivateData',value:true,nonPublic:true},C('Clone')]);
 add('retained-smooth',[...retained(),S('SmoothType',E('Entities.PolylineSmoothType',5)),C('Clone'),snap()]);
 add('retained-invalid',[...retained(),C('SetVertex',[I(0),I(0),V('Vector3',NaN,0,0)]),C('Clone')]);
 add('retained-resources',[...retained(),{kind:'get',target:'a',member:'Resources',id:'resources',nonPublic:true},{kind:'map-add',target:'resources',args:[I(0),{new:'Tables.Layer',args:['VERTEX']}]},C('Clone'),{kind:'call',target:'a',member:'TopologyTagCount',args:[],nonPublic:true}]);
 return out;
}
