// Independent inputs for the production C# Spline.NurbsEvaluator and its extracted native JS helper.
import {D,I,V,A} from './geometry-corpus.mjs';
const vectors=points=>points===null?null:A('Vector3',points.map(p=>V('Vector3',...p)));
const numbers=values=>values===null?null:A('Double',values.map(D));
export function nurbsCorpus(){
 const probes=[],add=(name,controls,weights,knots,degree,closed,periodic,precision)=>probes.push({name:'nurbs/'+name,category:'nurbs',request:{steps:[{kind:'call',type:'Entities.Spline',member:'NurbsEvaluator',signature:['Vector3[]','Double[]','Double[]','Int32','Boolean','Boolean','Int32'],args:[vectors(controls),numbers(weights),numbers(knots),I(degree),closed,periodic,I(precision)]}]}});
 for(let degree=1;degree<=10;degree++)for(const extra of [1,3])for(const periodic of [false,true])for(const closed of [false,true])for(const mode of [0,1,2]){
  const n=degree+extra,points=Array.from({length:n},(_,i)=>[i-2,(i*i%7)-3,(i%3)*.125]),weights=mode===0?null:Array.from({length:n},(_,i)=>mode===1?(i+1)*.125:(i%2?1e-150:1e150));
  add(`degree/${degree}/${extra}/${periodic}/${closed}/${mode}`,points,weights,null,degree,closed,periodic,7);
 }
 for(const degree of [1,2,3,6,10])for(const x of [0,-0,Number.MIN_VALUE,1e-300,1e150,1e308])for(const alternating of [false,true]){
  const n=degree+3,points=Array.from({length:n},(_,i)=>[(alternating&&i%2?-1:1)*x,(i%3-1)*x,i===0?x:0]);
  add(`extreme/${degree}/${Object.is(x,-0)?'-0':x}/${alternating}`,points,Array(n).fill(1),null,degree,false,true,9);
 }
 for(const degree of [1,2,3,6,10])for(const malformed of [false,true]){
  const n=degree+3,points=Array.from({length:n},(_,i)=>[i,i%3,-i]),spans=Array.from({length:n},(_,i)=>(i%3)+1),knots=[-10];
  for(let i=0;i<n+2*degree;i++)knots.push(knots[i]+spans[i%n]);
  if(malformed)knots[knots.length-1]+=0.25;
  add(`nonuniform/${degree}/${malformed}`,points,null,knots,degree,false,true,17);
 }
 const points=[[0,0,0],[2,3,4],[3,-2,1],[1,4,-2]];
 for(const periodic of [false,true])for(const precision of [-1,0,1,2,7])for(const controls of [null,[],[[1,2,3]],points])add(`arguments/${probes.length}`,controls,null,null,3,false,periodic,precision);
 for(const periodic of [false,true])for(const weights of [[],[1],[1,1,1],[-1,1,1,1],[0,1,1,1],[NaN,1,1,1],[Infinity,1,1,1],[Number.MIN_VALUE,1,1,1],[Number.MIN_VALUE,Number.MIN_VALUE,Number.MIN_VALUE,Number.MIN_VALUE],[1e308,1e308,1e308,1e308]])add(`weights/${probes.length}`,points,weights,null,3,false,periodic,7);
 for(const index of [0,1,3])for(const value of [NaN,Infinity,-Infinity]){
  const controls=points.map(p=>p.slice());controls[index][1]=value;add(`control-finite/${index}/${value}`,controls,null,null,3,false,true,7);
 }
 for(const degree of [0,1,2,3,10,11])for(const count of [1,2,4]){
  if(degree>count+2&&!([10,11].includes(degree)))continue;
  const controls=Array.from({length:count},(_,i)=>[i,i,i]);add(`layout/${degree}/${count}`,controls,null,null,degree,false,true,5);
 }
 for(const periodic of [false,true])for(const degree of [1,2,3,10])for(const count of [degree+1,degree+4])probes.push({name:`nurbs/knots/${count}/${degree}/${periodic}`,category:'nurbs',request:{steps:[{kind:'call',type:'Entities.Spline',member:'CreateKnotVector',args:[I(count),I(degree),periodic],nonPublic:true}]}});
 return probes;
}
