// Supplemental descriptions only; numerical and validation results come from C#.
import { D,I,R,V,E,A } from './geometry-corpus.mjs';
const N=(args=[],id='p')=>({kind:'new',type:'Entities.Ellipse',args,id});
const S=(member,value)=>({kind:'set',target:'p',member,value});
const C=(member,args=[],id)=>({kind:'call',target:'p',member,args,...(id?{id}:{})});
const snap=()=>({kind:'snapshot',target:'p'});
const label=n=>Object.is(n,-0)?'-0':String(n);
const mat=values=>({new:'Matrix3',args:values.map(D)});
export function ellipseCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'ellipse/'+name,category:'ellipse',request:{steps}});
 add('default',[N(),C('Clone'),C('PolygonalVertexes',[I(5)]),C('ToPolyline2D',[I(5)])]);
 for(const major of [-1,-0,0,Number.MIN_VALUE,1,4,Infinity,NaN])for(const minor of [-1,0,.5,2,Infinity,NaN])
  add(`axes/${label(major)}/${label(minor)}`,[N([V('Vector3',1,2,3),D(major),D(minor)]),N([],'q'),{kind:'call',target:'q',member:'SetAxis',args:[D(major),D(minor)]},{kind:'snapshot',target:'q'},{kind:'call',target:'q',member:'Clone'}]);
 for(const pos of [V('Vector2',3,4),V('Vector3',3,4,5)])add('overload/'+out.length,[N([pos,D(4),D(2)]),C('Clone')]);
 for(const name of ['Rotation','StartAngle','EndAngle','Thickness'])for(const value of [-Infinity,-450,-0,0,1e-13,37,360,Infinity,NaN])
  add(`${name}/${label(value)}`,[N(),S(name,D(value)),snap(),C('Clone'),C('PolygonalVertexes',[I(4)])]);
 for(const angle of [-Infinity,-360,-90,-0,0,30,90,370,Infinity,NaN])for(const major of [1,4,1e150])add(`polar/${label(angle)}/${major}`,[N([V('Vector3',3,4,5),D(major),D(major*.5)]),C('PolarCoordinateRelativeToCenter',[D(angle)])]);
 for(const start of [0,1e-13,30,270])for(const end of [0,1e-13,120,300])for(const rotation of [0,37])for(const precision of [2,7])
  add('sampling/'+out.length,[N([V('Vector3',3,4,5),D(8),D(2)]),S('StartAngle',D(start)),S('EndAngle',D(end)),S('Rotation',D(rotation)),C('PolygonalVertexes',[I(precision)]),C('ToPolyline2D',[I(precision)])]);
 for(const precision of [-1,0,1])add('precision/'+precision,[N(),C('PolygonalVertexes',[I(precision)]),C('ToPolyline2D',[I(precision)])]);
 const matrices=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,3,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[1,1,0,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2]];
 // Finite nonsingular transforms qualify conic reconstruction. Debug.Assert process
 // termination on degenerate input is not relabeled as a normal throwing API.
 for(const [mi,m] of matrices.entries())for(const normal of [[0,0,1],[0,3,4],[2,-3,4]])for(const arcs of [false,true])for(const axes of [[4,2],[4,4]])
  add(`transform/${mi}/${normal}/${arcs}/${axes}`,[N([V('Vector3',1,2,3),D(axes[0]),D(axes[1])]),S('Normal',V('Vector3',...normal)),S('Rotation',D(37)),S('EndAngle',D(arcs?250:0)),C('TransformBy',[mat(m),V('Vector3',3,-4,5)]),snap(),C('Clone'),C('PolygonalVertexes',[I(7)]),C('ToPolyline2D',[I(7)])]);
 add('appearance',[N(),S('IsVisible',false),S('ColorName','Book'),S('ProxyGraphics',A('Byte',[{byte:9}])),S('Layer',{new:'Tables.Layer',args:['ELLIPSE']}),C('ToPolyline2D',[I(7)]),C('Clone')]);
 add('matrix4',[N([V('Vector3',1,2,3),D(4),D(2)]),C('TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap()]);
 return out;
}
