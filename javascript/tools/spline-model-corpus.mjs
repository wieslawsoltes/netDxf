// Request descriptions only: actual pinned C# APIs supply the expected results.
import {D,I,R,V,E,A} from './geometry-corpus.mjs';
const N=(args,id='p',signature,nonPublic=false)=>({kind:'new',type:'Entities.Spline',args,id,...(signature?{signature}:{}),...(nonPublic?{nonPublic}:{})});
const P=(args=[],id='p')=>({kind:'new',type:'Entities.Polyline3D',args,id});
const C=(target,member,args=[],id,extra={})=>({kind:'call',target,member,args,...(id?{id}:{}),...extra});
const S=(target,member,value,extra={})=>({kind:'set',target,member,value,...extra});
const G=(target,member,id,extra={})=>({kind:'get',target,member,id,...extra});
const snap=target=>({kind:'snapshot',target});
const ix=(target,i,value)=>({kind:'set-index',target,args:[I(i)],value});
const equal=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const points=(n=5,loop=false)=>A('Vector3',Array.from({length:n},(_,i)=>V('Vector3',loop&&i===n-1?-2:i*1.25-2,loop&&i===n-1?-3:(i*i%7)-3,loop&&i===n-1?0:i%3)));
const weights=n=>A('Double',Array.from({length:n},(_,i)=>D((i+1)*.25)));
const ordinary=(n=5,degree=3,periodic=false)=>N([points(n),weights(n),{short:degree},periodic]);
const numlabel=n=>Object.is(n,-0)?'-0':String(n);
const matrices=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,3,0,0,0,-4],[1,2,0,0,1,3,4,0,1],[0,-1,0,1,0,0,0,0,1],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]];
export function splineModelCorpus(){
 const probes=[],add=(name,steps,category='spline-model')=>probes.push({name:category+'/'+name,category,request:{steps}});
 for(const degree of [-1,0,1,2,3,6,10,11])for(const n of [0,1,2,4,11])for(const periodic of [false,true])add(`construct/${degree}/${n}/${periodic}`,[ordinary(n,degree,periodic)]);
 for(const args of [[points(),null],[points(),null,{short:2}],[points(),null,true],[points(),null,false],[null,null,{short:2}],[points(),A('Double',[D(1)]),{short:2}]])add('overload/'+probes.length,[N(args),C('p','Clone')]);
 for(const curveKind of ['BezierCurveQuadratic','BezierCurveCubic'])for(const count of [0,1,2,5]){
  const degree=curveKind==='BezierCurveCubic'?3:2,curves=A(curveKind,Array.from({length:count},(_,k)=>({new:curveKind,args:Array.from({length:degree+1},(_,i)=>V('Vector3',k+i,i*i,0))})));
  add(`bezier/${curveKind}/${count}`,[N([curves],'p',[`IEnumerable<${curveKind}>`]),C('p','Clone'),C('p','PolygonalVertexes',[I(11)])]);
 }
 for(const kind of ['Vector3','BezierCurveCubic','BezierCurveQuadratic'])for(const value of [null,A(kind,[]),...(kind==='Vector3'?[]:[A(kind,[null])])])add('null-empty/'+probes.length,[N([value],'p',[`IEnumerable<${kind}>`])]);
 for(const n of [1,2,3,5,9])add('fit/'+n,[N([points(n)]),C('p','Clone'),C('p','PolygonalVertexes',[I(11)])]);
 for(const member of ['KnotTolerance','CtrlPointTolerance','FitTolerance'])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1e-8,Infinity,NaN])add(`${member}/${numlabel(value)}`,[ordinary(),S('p',member,D(value)),snap('p'),C('p','Clone')]);
 for(const param of [-1,0,32,64,128,256,999])add('parameterization/'+param,[ordinary(),S('p','KnotParameterization',E('Entities.SplineKnotParameterization',param)),C('p','Clone')]);
 for(const n of [3,5,7])for(const degree of [1,2])for(const periodic of [false,true])for(const domain of [-7,0,1e308]){
  const count=n+degree+1+(periodic?degree:0),ks=Array.from({length:count},(_,i)=>domain+i*(domain===1e308?1e292:.125));
  add(`reverse/${n}/${degree}/${periodic}/${domain}`,[N([points(n),weights(n),A('Double',ks.map(D)),{short:degree},periodic]),G('p','Knots','knots'),G('p','ControlPoints','controls'),C('p','Clone',[],'before'),S('p','StartTangent',V('Vector3',2,-3,4)),S('p','EndTangent',V('Vector3',-4,5,6)),C('p','Reverse'),snap('p'),G('p','Knots','after'),equal('knots','after'),C('p','Reverse'),snap('p')]);
 }
 for(const bad of [NaN,Infinity,-Infinity,-100])add('reverse-rejection/'+bad,[ordinary(),G('p','Knots','knots'),ix('knots',4,D(bad)),C('p','Reverse'),snap('p')]);
 for(const periodic of [false,true])add('periodic-rebuild/'+periodic,[ordinary(),G('p','Knots','before'),S('p','IsClosedPeriodic',periodic),G('p','Knots','after'),equal('before','after'),S('p','IsClosedPeriodic',periodic),G('p','Knots','again'),equal('after','again'),snap('p')]);
 for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1e308,Infinity,NaN])add('weights/'+numlabel(value),[ordinary(),C('p','SetUniformWeights',[D(value)]),snap('p'),C('p','PolygonalVertexes',[I(7)]),C('p','Clone')]);
 for(const fitted of [false,true])for(const [index,m] of matrices.entries())for(const mask of [0,1,2,3]){
  add(`transform/${fitted}/${index}/${mask}`,[fitted?N([points()]):ordinary(),S('p','StartTangent',mask&1?V('Vector3',2,-3,4):null),S('p','EndTangent',mask&2?V('Vector3',-5,6,7):null),C('p','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',100,-200,300)]),snap('p'),C('p','Clone'),C('p','PolygonalVertexes',[I(7)])]);
 }
 for(const periodic of [false,true])for(const closed of [false,true])for(const precision of [-1,0,1,2,7])add(`conversion/${periodic}/${closed}/${precision}`,[N([points(5,closed),null,{short:2},periodic]),S('p','IsVisible',false),S('p','Normal',V('Vector3',0,3,4)),C('p','ToPolyline2D',[I(precision)]),C('p','ToPolyline3D',[I(precision)])]);
 for(const method of [0,1,99])for(const controls of [null,points(0),points(5)])for(const fit of [null,points(0),points(3)])add(`stored/${method}/${probes.length}`,[N([controls,null,controls===null?null:A('Double',[0,0,0,0,1,2,2,2,2].map(D)),{short:3},fit,E('Entities.SplineCreationMethod',method),false],'p',['IEnumerable<Vector3>','IEnumerable<Double>','IEnumerable<Double>','Int16','IEnumerable<Vector3>','Entities.SplineCreationMethod','Boolean'],true),C('p','Clone'),C('p','SetUniformWeights',[D(2)]),C('p','PolygonalVertexes',[I(5)])]);
 add('mutable-arrays',[N([points()]),G('p','ControlPoints','c'),G('p','Weights','w'),G('p','Knots','k'),ix('c',0,V('Vector3',9,8,7)),ix('w',0,D(.125)),ix('k',0,D(-10)),C('p','Clone',[],'q'),G('q','ControlPoints','qc'),G('q','Weights','qw'),G('q','Knots','qk'),ix('qc',0,V('Vector3',1,1,1)),ix('qw',0,D(2)),ix('qk',0,D(-20)),snap('p'),snap('q'),equal('c','qc'),equal('w','qw'),equal('k','qk')]);
 add('matrix4',[ordinary(),C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap('p')]);
 return probes;
}
export function polyline3dCorpus(){
 const probes=[],add=(name,steps)=>probes.push({name:'polyline3d/'+name,category:'polyline3d',request:{steps}});
 for(const n of [0,1,2,4,7])for(const closed of [false,true])for(const smooth of [0,5,6,99])add(`model/${n}/${closed}/${smooth}`,[P([points(n),closed]),S('p','SmoothType',E('Entities.PolylineSmoothType',smooth)),C('p','PolygonalVertexes',[I(7)]),C('p','Explode'),C('p','Clone'),C('p','ToPolyline2D',[I(7)]),C('p','Reverse'),snap('p')]);
 for(const [i,m] of matrices.entries())add('transform/'+i,[P([points()]),S('p','Normal',V('Vector3',0,3,4)),C('p','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',3,-4,5)]),snap('p'),C('p','Clone')]);
 for(const precision of [-1,0,1,2,3])for(const smooth of [0,5,6])add(`precision/${precision}/${smooth}`,[P([points()]),S('p','SmoothType',E('Entities.PolylineSmoothType',smooth)),C('p','PolygonalVertexes',[I(precision)])]);
 for(const from of [-1,0,2,4,5])for(const to of [-1,0,2,4,5])add(`move/${from}/${to}`,[P([points()]),C('p','MoveVertex',[I(from),I(to)]),snap('p')]);
 for(const n of [0,1,2,4])for(const index of [-1,0,1,4,5])add(`remove/${n}/${index}`,[P([points(n)]),C('p','RemoveVertexAt',[I(index)]),snap('p')]);
 for(const index of [-1,0,2,5,6])for(const value of [0,-0,NaN,Infinity])add(`insert/${index}/${numlabel(value)}`,[P([points()]),C('p','InsertVertex',[I(index),V('Vector3',value,2,3)]),snap('p')]);
 for(const value of [NaN,Infinity])add('invalid-existing/'+value,[P([A('Vector3',[V('Vector3',value,0,0),V('Vector3',1,2,3)])]),C('p','MoveVertex',[I(0),I(0)]),C('p','RemoveVertexAt',[I(0)]),C('p','InsertVertex',[I(0),V('Vector3',0,0,0)]),snap('p')]);
 add('null',[P([null])]);add('default',[P(),C('p','Clone')]);
 const tag=(c,v)=>({new:'IO.DxfTag',args:[{short:c},v]});
 const record=(id,code='VERTEX')=>({kind:'new',type:'Entities.Polyline3DRecord',args:[code,{new:'List<IO.DxfTag>',args:[A('IO.DxfTag',[tag(0,code)])]}],id,nonPublic:true});
 const retained=()=>[P([points(3)]),record('a'),record('b'),record('c'),record('end','SEQEND'),C('p','SetStoredRecords',[null,{new:'List<Entities.Polyline3DRecord>',args:[A('Entities.Polyline3DRecord',[R('a'),R('b'),R('c')])]},R('end')],null,{nonPublic:true})];
 add('retained-reverse',[...retained(),G('p','VertexRecords','before'),C('p','Reverse'),G('p','VertexRecords','after'),equal('before','after'),snap('p'),C('p','Clone')]);
 for(const smooth of [0,5])add('retained-guards/'+smooth,[...retained(),S('p','SmoothType',E('Entities.PolylineSmoothType',smooth)),C('p','MoveVertex',[I(0),I(1)]),C('p','InsertVertex',[I(0),V('Vector3',0,0,0)]),C('p','RemoveVertexAt',[I(0)]),C('p','Reverse'),C('p','Clone'),snap('p')]);
 add('retained-count-mismatch',[...retained(),G('p','Vertexes','v'),C('v','Clear'),C('p','Reverse'),C('p','Clone')]);
 return probes;
}
