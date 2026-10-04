// Independent requests; expected geometry is produced by the unchanged pinned C# assembly.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const vector=(x=0,y=0,z=0)=>V('Vector3',x,y,z);
const spline=()=>({new:'Entities.Spline',args:[A('Vector3',[vector(),vector(1,2,3),vector(3,-1,2),vector(4,5,6)]),A('Double',[1,.5,.75,1].map(D)),{short:3}]});
const H=()=>({kind:'new',type:'Entities.Helix',args:[spline()],id:'h'});
const S=(member,value)=>({kind:'set',target:'h',member,value});
const C=(member,args=[],id)=>({kind:'call',target:'h',member,args,...(id?{id}:{})});
const snap=()=>({kind:'snapshot',target:'h'});
const get=(member,id)=>({kind:'get',target:'h',member,id});
const label=x=>Object.is(x,-0)?'-0':String(x);
const create=(base,start,axis,radius=1,turns=1,height=1,right=true,tol=1e-3,budget=256)=>({kind:'call',type:'Entities.Helix',member:'Create',args:[base,start,axis,D(radius),D(turns),D(height),right,D(tol),I(budget)],id:'h'});
const sample=[0,.125,.5,.875,1].flatMap(t=>[C('EvaluateDefinition',[D(t)]),C('EvaluateDefinitionDerivative',[D(t)])]);
export function helixCorpus(){
  const probes=[],add=(name,steps)=>probes.push({name:'helix/'+name,category:'helix',request:{steps}});
  add('defaults',[H(),C('Clone'),C('ToSpline'),C('ToString')]);
  add('null',[{kind:'new',type:'Entities.Helix',args:[null]}]);
  for(const member of ['Radius','Turns','TurnHeight'])for(const value of [-Infinity,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1,2.75,Number.MAX_VALUE,Infinity,NaN])
    add(`${member}/${label(value)}`,[H(),S(member,D(value)),snap(),C('Clone'),C('ToSpline')]);
  for(const member of ['MajorReleaseNumber','MaintenanceReleaseNumber','Constraint'])for(const value of [-1,0,1,2,3,29,2147483647])
    add(`${member}/${value}`,[H(),S(member,member==='Constraint'?E('Entities.HelixConstraint',value):I(value)),snap(),C('Clone')]);
  for(const member of ['AxisBasePoint','StartPoint','AxisVector'])for(let axis=0;axis<3;axis++)for(const value of [-Infinity,-2,-0,0,Number.MIN_VALUE,Number.MAX_VALUE,Infinity,NaN]){
    const values=[0,0,0];values[axis]=value;
    add(`${member}/${axis}/${label(value)}`,[H(),S(member,vector(...values)),snap(),get(member,'copy'),{kind:'set',target:'copy',member:'X',value:D(9)},snap(),C('Clone')]);
  }
  for(let shape=0;shape<6;shape++)for(const right of [false,true])for(let pose=0;pose<3;pose++){
    const base=pose===2?[100,-200,30]:[0,0,0],axis=pose===0?[0,0,3]:pose===1?[0,3,4]:[2,-3,4];
    const radial=shape===4?[0,0,0]:pose===2?[3,2,0]:[5,0,0],start=base.map((x,i)=>x+radial[i]),radius=shape===0?5:shape===5?0:2,height=shape===2?0:shape===3?-1.5:1.5;
    add(`create/${shape}/${right}/${pose}`,[create(vector(...base),vector(...start),vector(...axis),radius,2.25,height,right),...sample,C('GetApproximationErrorBound',[I(36)]),C('Clone'),C('ToSpline')]);
  }
  for(const parameter of [-Infinity,-1,-Number.MIN_VALUE,-0,0,1,1.0000000000000002,Infinity,NaN])
    add('parameter/'+label(parameter),[H(),C('EvaluateDefinition',[D(parameter)]),C('EvaluateDefinitionDerivative',[D(parameter)])]);
  for(const segments of [-2147483648,-1,0,1,7,2147483647])add('segments/'+segments,[H(),C('GetApproximationErrorBound',[I(segments)])]);
  for(const start of [0,Number.MIN_VALUE,1e-300,1,1e308])for(const end of [0,1,1e308])for(const turns of [Number.MIN_VALUE,1e-100,1,1e100])
    add(`bounds/${start}/${end}/${turns}`,[H(),S('StartPoint',vector(start,0,0)),S('Radius',D(end)),S('Turns',D(turns)),C('GetApproximationErrorBound',[I(1)]),C('GetApproximationErrorBound',[I(20)])]);
  for(const tol of [-1,-0,0,Number.MIN_VALUE,1e-20,1e-5,1,Infinity,NaN])add('tolerance/'+label(tol),[create(vector(),vector(1,0,0),vector(0,0,1),1,1,1,true,tol,64)]);
  for(const budget of [-1,0,1,2,8,64,1048577,2147483647])add('budget/'+budget,[create(vector(),vector(1,0,0),vector(0,0,1),1,2,1,true,1e-5,budget)]);
  for(const scale of [0,Number.MIN_VALUE,1e-300,1,1e308,Infinity,NaN])add('axis-scale/'+scale,[create(vector(),vector(1,0,0),vector(0,0,scale)),...sample]);
  for(const hint of [null,vector(0,0,2),vector(0,1,1),vector(Infinity,0,0)])add('phase/'+probes.length,[H(),S('StartPoint',vector()),S('StartTangent',hint),...sample,C('WithRegeneratedSpline',[D(1e-3),I(256)])]);
  for(const initial of [0,1])for(const right of [false,true])for(const [i,m] of [[1,0,0,0,1,0,0,0,1],[-2,0,0,0,2,0,0,0,2],[0,-2,0,2,0,0,0,0,2],[2,0,0,0,3,0,0,0,4],[1,1,0,0,1,0,0,0,1],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]].entries()){
    add(`transform/${initial}/${right}/${i}`,[create(vector(),vector(initial,0,0),vector(0,0,2),2,1.25,-.5,right),C('TransformBy',[{new:'Matrix3',args:m.map(D)},vector(3,-4,5)]),snap(),...sample,C('Clone'),C('WithRegeneratedSpline',[D(1e-3),I(256)])]);
  }
  add('no-refit',[H(),get('ControlPoints','controls'),S('Radius',D(3)),S('Turns',D(2)),get('ControlPoints','again'),{kind:'reference-equals',args:[R('controls'),R('again')]},C('Clone',[],'clone'),C('ToSpline',[],'spline'),C('WithRegeneratedSpline',[D(1e-3),I(256)],'fresh'),snap()]);
  return probes;
}
