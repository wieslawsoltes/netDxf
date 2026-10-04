// Independent seeded and boundary inputs; no production answers are stored here.
import {doubleBits,fromBits} from './wire.mjs';
export function expLogCorpus(){
 const cases=[];const add=(name,x)=>cases.push({id:`exp-log/${name}/${cases.length}`,name,args:[typeof x==='string'?x:doubleBits(x)]});
 const special=['0000000000000000','8000000000000000','0000000000000001','8000000000000001','0010000000000000','7FEFFFFFFFFFFFFF','FFEFFFFFFFFFFFFF','7FF0000000000000','FFF0000000000000','7FF8000000000000','FFF8000000000000','7FF0000000000001','FFF0000000000001','7FF8000000001234','FFF8000000001234'];
 for(const name of ['Exp','Log'])for(const bits of special)add(name,bits);
 const near=(name,value,radius=12)=>{const center=BigInt('0x'+doubleBits(value));for(let d=-radius;d<=radius;d++){const bits=BigInt.asUintN(64,center+BigInt(d)).toString(16).padStart(16,'0').toUpperCase();add(name,bits);}};
 for(const value of [1,-1,2**-54,-(2**-54),512,-512,1024,-1024,709.782712893384,-708.3964185322641,-744.4400719213812,-745.1332191019411])near('Exp',value);
 for(const value of [1,1-2**-4,1+1.03515625/16,.6875,1.375,2**-1022,2,10,1e-300,1e300])near('Log',value);
 for(let i=1;i<=128;i++)near('Log',.6875*(1+i/128),2);
 for(let i=-128;i<=128;i++)near('Exp',(i+.5)*Math.LN2/128,2);
 for(let exponent=-1074;exponent<=1023;exponent+=7){const x=2**exponent;add('Log',x);add('Log',x*1.2345678901234567);}
 let seed=0x4558504c4f47444fn;const next=()=>seed=(seed*6364136223846793005n+1442695040888963407n)&((1n<<64n)-1n);
 for(let i=0;i<4096;i++){const raw=next()&0x7fffffffffffffffn;add('Log',raw.toString(16).padStart(16,'0').toUpperCase());add('Exp',Number(next()>>11n)/9007199254740992*1500-750);}
 for(let i=0;i<1024;i++)add('Exp',-746+i*0.05);
 for(const x of [-1,-.5,-2,-1e-300,-1e300])add('Log',x);
 for(let e=-1074;e<=1023;e+=13)add('Log',-(2**e));
 for(let i=0;i<1024;i++)add('Exp',next().toString(16).padStart(16,'0').toUpperCase());
 return cases;
}
