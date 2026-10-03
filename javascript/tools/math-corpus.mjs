// General, seeded coverage. No expected values or retained mismatch identities influence it.
import { doubleBits, fromBits } from './wire.mjs';
export function mathCorpus() {
  const probes=[];
  const add=(member,args,category)=>probes.push({id:`${category}/${member}/${probes.length}`,member,args:args.map(x=>typeof x==='string'?x:doubleBits(x))});
  const special=[0,-0,Number.MIN_VALUE,-Number.MIN_VALUE,2**-1022,-(2**-1022),1,-1,Infinity,-Infinity,
    '7FF8000000000000','FFF8000000000000','7FF8000000000123','FFF8000000000123','7FF0000000000123','FFF0000000000123'];
  const centers=[0,2**-56,2**-27,0.5,1,Math.PI/4,Math.PI/2,Math.PI,2*Math.PI,2**54,Number.MAX_VALUE];
  for(const member of ['Sin','Cos','Tan','Asin','Acos','Atan']) {
    for(const x of special)add(member,[x],'special');
    for(const center of centers) {
      const b=BigInt('0x'+doubleBits(center));
      for(let delta=-4n;delta<=4n;delta++){
        const n=b+delta;if(n<0n||n>0x7ff0000000000000n)continue;
        const x=fromBits(n.toString(16).padStart(16,'0'));
        add(member,[x],'neighbors');add(member,[-x],'neighbors');
      }
    }
  }
  for(const member of ['Atan2','Remainder']) for(const a of special)for(const b of special)add(member,[a,b],'special-pairs');
  let seed=0x4e554d45;
  const next=()=>seed=(Math.imul(seed,1664525)+1013904223)>>>0;
  const raw=()=>fromBits(((BigInt(next())<<32n)|BigInt(next())).toString(16).padStart(16,'0'));
  for(let i=0;i<2048;i++) {
    const angle=(next()/2**32-0.5)*2**16,inverse=(next()/2**32-0.5)*2;
    for(const member of ['Sin','Cos','Tan','Atan'])add(member,[angle],'seeded-range');
    for(const member of ['Asin','Acos'])add(member,[inverse],'seeded-range');
    const a=raw(),b=raw();
    for(const member of ['Sin','Cos','Tan','Asin','Acos','Atan'])add(member,[a],'seeded-bits');
    add('Atan2',[a,b],'seeded-bits');add('Remainder',[a,b],'seeded-bits');
  }
  // Preserve every earlier case identity. Add independent neighborhoods around
  // finite ratio scaling thresholds in all quadrants and at widely different exponents.
  for (const center of [2**-56, 2**-55, 2**54, 2**55, 2**56]) {
    const rawCenter = BigInt('0x' + doubleBits(center));
    for (let offset = -8n; offset <= 8n; offset++) {
      const value = fromBits((rawCenter + offset).toString(16).padStart(16, '0'));
      for (const ySign of [-1, 1]) for (const xSign of [-1, 1])
        add('Atan2', [ySign * value, xSign], 'atan2-ratio-neighbors');
    }
  }
  const ratioCenter = BigInt('0x' + doubleBits(2**54));
  for (const exponent of [-1074, -500, 0, 500, 950]) {
    const scale = 2**exponent;
    for (let offset = -4n; offset <= 4n; offset++) {
      const value = fromBits((ratioCenter + offset).toString(16).padStart(16, '0'));
      for (const ySign of [-1, 1]) for (const xSign of [-1, 1])
        add('Atan2', [ySign * value * scale, xSign * scale], 'atan2-scaled-ratio');
    }
  }
  return probes;
}
