// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { ArgumentException, NotSupportedException } from '../../runtime/Errors.js';
const bits=new DataView(new ArrayBuffer(8));
const abs=x=>x<0n?-x:x;
const length=x=>x===0n?0:x.toString(2).length;
const bytes=x=>Math.max(1,Math.ceil((length(x<0n?-x-1n:x)+1)/8));
function check(x){if(bytes(x)>65536)throw new NotSupportedException('Exact periodic arithmetic budget exceeded.');}
function product(a,b){if(a===0n||b===0n)return 0n;if(bytes(a)+bytes(b)>65536)throw new NotSupportedException('Exact periodic arithmetic budget exceeded.');return a*b;}
function gcd(a,b){while(b!==0n){const c=a%b;a=b;b=c;}return a;}
class Rational {
  constructor(n,d=1n,reduce=true){
    if(d===0n)throw new ArgumentException('An exact periodic divisor is zero.');
    if(n===0n){this.n=0n;this.d=1n;return;}if(d<0n){n=-n;d=-d;}check(n);check(d);
    if(reduce){const g=gcd(abs(n),d);n/=g;d/=g;}this.n=n;this.d=d;
  }
  static FromDouble(v){
    bits.setFloat64(0,v);const raw=bits.getBigUint64(0),field=Number((raw>>52n)&2047n);
    if(field===2047)throw new ArgumentException('Exact periodic inputs must be finite.');
    let n=raw&0xfffffffffffffn;if(field!==0)n|=1n<<52n;if(n===0n)return zero;
    if(raw>>63n)n=-n;const e=field===0?-1074:field-1075;
    return e>=0?new Rational(n<<BigInt(e),1n,false):new Rational(n,1n<<BigInt(-e));
  }
  add(b){if(this.n===0n)return b;if(b.n===0n)return this;const shared=gcd(this.d,b.d),ls=b.d/shared,rs=this.d/shared;return new Rational(product(this.n,ls)+product(b.n,rs),product(this.d,ls));}
  sub(b){return this.add(new Rational(-b.n,b.d,false));}
  mul(b){if(this.n===0n||b.n===0n)return zero;const a=gcd(abs(this.n),b.d),c=gcd(abs(b.n),this.d);return new Rational(product(this.n/a,b.n/c),product(this.d/c,b.d/a),false);}
  div(b){if(b.n===0n)throw new ArgumentException('An exact periodic divisor is zero.');return this.mul(new Rational(b.d,b.n,false));}
  ToDouble(){
    if(this.n===0n)return 0;const sign=this.n<0n?1n<<63n:0n,m=abs(this.n);let e=length(m)-length(this.d);
    if(e>=0){if(m<(this.d<<BigInt(e)))e--;}else if((m<<BigInt(-e))<this.d)e--;
    if(e>1023)throw new ArgumentException('Exact periodic sample exceeds binary64 range.');
    const spacing=e<-1022?-1074:e-52;let dividend=m,divisor=this.d;
    if(spacing<0)dividend<<=BigInt(-spacing);else divisor<<=BigInt(spacing);
    let s=dividend/divisor;const rem=(dividend%divisor)<<1n;if(rem>divisor||(rem===divisor&&(s&1n)!==0n))s++;
    if(e<-1022){bits.setBigUint64(0,sign|s);return bits.getFloat64(0);}
    if(s===1n<<53n){s>>=1n;e++;}if(e>1023)throw new ArgumentException('Exact periodic sample rounds outside binary64 range.');
    bits.setBigUint64(0,sign|(BigInt(e+1023)<<52n)|(s-(1n<<52n)));return bits.getFloat64(0);
  }
}
const zero=new Rational(0n,1n,false),one=new Rational(1n,1n,false);
export class PeriodicSplineExactEvaluation {
  static Evaluate(controls,weights,knots,degree,parameter,first){
    if(degree<1||degree>10)throw new ArgumentException('Exact periodic evaluation requires a supported degree.');
    const u=Rational.FromDouble(parameter),local=Array.from({length:2*degree+1},(_,i)=>Rational.FromDouble(knots[first+i]));
    const basis=Array(degree+1).fill(zero),left=[],right=[];basis[0]=one;
    for(let j=1;j<=degree;j++){
      left[j]=u.sub(local[degree+1-j]);right[j]=local[degree+j].sub(u);let saved=zero;
      for(let k=0;k<j;k++){
        const divisor=right[k+1].add(left[j-k]);if(divisor.n<=0n)throw new ArgumentException('Exact periodic knots must increase.');
        const t=basis[k].div(divisor);basis[k]=saved.add(right[k+1].mul(t));saved=left[j-k].mul(t);
      }basis[j]=saved;
    }
    let den=zero,x=zero,y=zero,z=zero;
    for(let i=0;i<=degree;i++){
      const w=basis[i].mul(Rational.FromDouble(weights[first+i]));if(w.n<0n)throw new ArgumentException('Exact periodic weights must be positive.');if(w.n===0n)continue;
      const p=controls[first+i];den=den.add(w);x=x.add(w.mul(Rational.FromDouble(p.X)));y=y.add(w.mul(Rational.FromDouble(p.Y)));z=z.add(w.mul(Rational.FromDouble(p.Z)));
    }
    if(den.n<=0n)throw new ArgumentException('Exact periodic denominator is zero.');
    return new Vector3(x.div(den).ToDouble(),y.div(den).ToDouble(),z.div(den).ToDouble());
  }
}
