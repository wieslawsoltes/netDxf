// Pure reproducible input generation. No expected values and no runtime dependency on this corpus.
import { doubleBits,fromBits } from './wire.mjs';
export function referenceMathCorpus() {
  const calls=[];
  const add=(name,args,id)=>calls.push({name,args:args.map(doubleBits),id});
  const unary=['Sin','Cos','Tan','Asin','Acos','Atan'];
  const values=[0,-0,Number.MIN_VALUE,-Number.MIN_VALUE,2**-1022,-(2**-1022),Number.MAX_VALUE,-Number.MAX_VALUE,Infinity,-Infinity,
    fromBits('7FF8000000000000'),fromBits('FFF8000000000000'),fromBits('7FF8000000001234'),fromBits('FFF8000000001234')];
  const boundaries=[2**-27,2**-26,0.126,0.125,0.25,0.5,0.75,0.921875,0.953125,0.96875,1,
    0.85546875,2.426265,105414350,1.259e-8,0.0608,0.787,25,1e8,0.0625,16,5805361265115136,Math.PI/2,Math.PI,2*Math.PI];
  for(const value of boundaries)for(const sign of [1,-1]){
    const b=BigInt('0x'+doubleBits(value*sign));
    for(const delta of [-2n,-1n,0n,1n,2n])values.push(fromBits((b+delta).toString(16).padStart(16,'0')));
  }
  for(const [i,x] of values.entries())for(const name of unary)add(name,[x],`boundary/${name}/${i}`);
  let seed=0x4d415448;
  const next=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed;};
  for(let i=0;i<4000;i++){
    const raw=fromBits(next().toString(16).padStart(8,'0')+next().toString(16).padStart(8,'0'));
    const bounded=(next()/4294967296-.5)*2;
    for(const name of unary)add(name,[name==='Asin'||name==='Acos'?bounded:raw],`random/${name}/${i}`);
    add('Atan2',[raw,fromBits(next().toString(16).padStart(8,'0')+next().toString(16).padStart(8,'0'))],`random/Atan2/${i}`);
    add('Fma',[raw,bounded,fromBits(next().toString(16).padStart(8,'0')+next().toString(16).padStart(8,'0'))],`random/Fma/${i}`);
  }
  for(let i=0;i<4000;i++){
    const small=(next()/4294967296-.5)*64,medium=(next()/4294967296-.5)*2e8;
    for(const name of ['Sin','Cos','Tan']){add(name,[small],`small/${name}/${i}`);add(name,[medium],`medium/${name}/${i}`);}
  }
  for(const [i,x] of values.slice(0,14).entries())for(const [j,y] of values.slice(0,14).entries())add('Atan2',[x,y],`special/Atan2/${i}/${j}`);
  const fmaValues=[0,-0,1,-1,Number.MIN_VALUE,-Number.MIN_VALUE,2**-1022,2**1023,Number.MAX_VALUE,-Number.MAX_VALUE,Infinity,-Infinity,...values.slice(10,14)];
  for(const [i,x] of fmaValues.entries())for(const [j,y] of fmaValues.entries())for(const [k,z] of fmaValues.entries())add('Fma',[x,y,z],`special/Fma/${i}/${j}/${k}`);
  return calls;
}
