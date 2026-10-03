// Request descriptions only; neither algorithms nor expected numeric answers are encoded here.
import {D,I,R,V,A} from './geometry-corpus.mjs';
const N=(type,args=[],id='p')=>({kind:'new',type:'GTE.'+type,args,id});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const G=(target,member,id)=>({kind:'get',target,member,id});
const S=(target,member,value)=>({kind:'set',target,member,value});
const snap=target=>({kind:'snapshot',target});
const index=(target,i,id)=>({kind:'index',target,args:[I(i)],id});
const set=(target,i,value)=>({kind:'set-index',target,args:[I(i)],value});
const B=(n,d)=>({new:'GTE.BasisFunctionInput',args:[I(n),I(d)]});
const P=(u,v,mode=0)=>A('Vector3',Array.from({length:u*v},(_,i)=>V('Vector3',(i%u)-1,Math.floor(i/u)*.25,mode?((i*i%7)-3)*.125:0)));
const label=v=>Object.is(v,-0)?'-0':String(v);
const derivative=(u,v,o)=>C('p','Evaluate',[D(u),D(v),I(o),{out:'Vector3[]'}],undefined,['Double','Double','Int32','Vector3[]&']);
function periodic(n,d,id){const knots=Array.from({length:n+2*d+1},(_,i)=>({new:'GTE.UniqueKnot',args:[D((i-d)/n),I(1)]}));return [N('BasisFunctionInput',[],id),S(id,'NumControls',I(n)),S(id,'Degree',I(d)),S(id,'Periodic',true),S(id,'Uniform',true),S(id,'NumUniqueKnots',I(knots.length)),S(id,'UniqueKnots',A('GTE.UniqueKnot',knots))];}
export function surfaceCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'surface/'+name,category:'surfaces',request:{steps}});
 for(const n of [2,3,4,6,9])for(let d=1;d<=Math.min(4,n-1);d++)for(const t of [-Infinity,-2,-0,0,.125,.5,.9,1,2,Infinity,NaN])for(const order of [0,1,2,3]){
  const steps=[N('BasisFunction',[B(n,d)]),C('p','Evaluate',[D(t),I(order),{out:'Int32'},{out:'Int32'}],undefined,['Double','Int32','Int32&','Int32&'])];
  for(let k=0;k<n+d;k++)for(let o=0;o<=order;o++)steps.push(C('p','GetValue',[I(o),I(k)]));add(`basis/${n}/${d}/${label(t)}/${order}`,steps);
 }
 for(const n of [3,5,8])for(const degree of [1,2])for(const t of [-2.25,-1,-0,0,.1,.75,1,2.75])add(`periodic/${n}/${degree}/${label(t)}`,[...periodic(n,degree,'input'),N('BasisFunction',[R('input')]),C('p','Evaluate',[D(t),I(3),{out:'Int32'},{out:'Int32'}],undefined,['Double','Int32','Int32&','Int32&']),...Array.from({length:n+degree},(_,i)=>[0,1,2,3].map(o=>C('p','GetValue',[I(o),I(i)]))).flat()]);
 add('value-input-copy',[N('BasisFunctionInput',[I(5),I(3)],'input'),G('input','UniqueKnots','source'),N('BasisFunction',[R('input')]),index('source',0,'k'),S('k','T',D(-2)),snap('input'),set('source',0,{new:'GTE.UniqueKnot',args:[D(-1),I(4)]}),snap('input'),snap('p'),C('p','Create',[R('input')]),snap('p')]);
 add('mutable-knots-cached-domain',[N('BasisFunction',[B(4,2)]),G('p','Knots','k'),set('k',3,D(.625)),C('p','Evaluate',[D(.6),I(2),{out:'Int32'},{out:'Int32'}],undefined,['Double','Int32','Int32&','Int32&']),...Array.from({length:6},(_,i)=>C('p','GetValue',[I(0),I(i)])),snap('p')]);
 for(const o of [-1,0,3,4])for(const i of [-1,0,7,8])add(`basis-error/${o}/${i}`,[N('BasisFunction',[B(5,3)]),C('p','GetValue',[I(o),I(i)])]);
 add('stale-derivative-storage',[N('BasisFunction',[B(6,3)]),...[.1,.7,.2,1,0].flatMap((t,j)=>[C('p','Evaluate',[D(t),I(j%4),{out:'Int32'},{out:'Int32'}],undefined,['Double','Int32','Int32&','Int32&']),...Array.from({length:9},(_,i)=>[0,1,2,3].map(o=>C('p','GetValue',[I(o),I(i)]))).flat()])]);
 for(const type of ['BSplineSurface','NURBSSurface'])for(const u of [2,4,6])for(const v of [2,3,5])for(const order of [0,1,2,3,6])for(const point of [[0,0],[1,1],[.3,.7],[-1,2]]){
  const args=[B(u,Math.min(3,u-1)),B(v,Math.min(2,v-1)),P(u,v,1)];if(type==='NURBSSurface')args.push(A('Double',Array.from({length:u*v},(_,i)=>D(1+(i%4)*.25))));
  add(`${type}/${u}/${v}/${order}/${point}`,[N(type,args),derivative(...point,order),C('p','GetPosition',point.map(D)),C('p','GetUTangent',point.map(D)),C('p','GetVTangent',point.map(D))]);
 }
 for(const type of ['BSplineSurface','NURBSSurface'])for(const closed of [1,2,3]){
  const u=5,v=4,steps=[];if(closed&1)steps.push(...periodic(u,3,'u'));else steps.push(N('BasisFunctionInput',[I(u),I(3)],'u'));
  if(closed&2)steps.push(...periodic(v,2,'v'));else steps.push(N('BasisFunctionInput',[I(v),I(2)],'v'));
  steps.push(N(type,[R('u'),R('v'),P(u,v,1),...(type==='NURBSSurface'?[A('Double',Array(u*v).fill(D(1)))]:[])]),...[-1,.3,1,2.3].flatMap(t=>[derivative(t,.7,2)]));add(`${type}/periodic/${closed}`,steps);
 }
 for(const type of ['BSplineSurface','NURBSSurface'])for(const count of [0,3,4,5]){
  add(`${type}/partial-copy/${count}`,[N(type,[B(2,1),B(2,1),P(count,1,1),...(type==='NURBSSurface'?[A('Double',Array(count).fill(D(1)))]:[])]),derivative(.4,.6,2)]);
 }
 for(const weights of [null,[0,0,0,0],[1,-1,1,-1],[1,1,1,1],[1,2,3],[NaN,1,2,3],[Infinity,1,2,3]])add(`weights/${out.length}`,[N('NURBSSurface',[B(2,1),B(2,1),P(2,2,1),weights===null?null:A('Double',weights.map(D))]),derivative(.3,.7,2)]);
 for(const type of ['BSplineSurface','NURBSSurface'])add(`${type}/control-value-location`,[N(type,[B(2,1),B(2,1),null,...(type==='NURBSSurface'?[null]:[])]),C('p','GetControl',[I(-1),I(1)],'q'),S('q','X',D(12)),C('p','SetControl',[I(-1),I(0),V('Vector3',9,9,9)]),C('p','SetControl',[I(1),I(1),V('Vector3',1,2,3)]),snap('p'),C('p','GetControl',[I(4),I(3)]),...[0,1,2,-1].map(d=>C('p','BasisFunction',[I(d)]))]);
 return out;
}
