// SPDX-License-Identifier: LGPL-2.1-or-later
// Double-length arithmetic adapted from glibc 2.35 dbl-64/dla.h.
// Copyright (C) 2001-2022 Free Software Foundation, Inc.
// JavaScript adaptation: 2026 netDxf contributors. See LICENSE.LGPL-2.1.
// Words explicitly fixes little-endian word indices on every JavaScript host.
export class Words {
  constructor() {
    const view = new DataView(new ArrayBuffer(8));
    this.view = view;
    const i = {};
    for (let at = 0; at < 2; at++) Object.defineProperty(i, at, {
      get: () => view.getInt32(at*4,true), set: value => view.setInt32(at*4,value,true)
    });
    this.i=i;
  }
  get x() { return this.view.getFloat64(0,true); }
  set x(value) { this.view.setFloat64(0,value,true); }
  get d() { return this.x; }
  set d(value) { this.x=value; }
}
export function copySign(value, sign) {
  return sign < 0 || Object.is(sign,-0) ? -Math.abs(value) : Math.abs(value);
}
export function signArctan(sign,value) { return copySign(value,sign); }
export function add2(x,y) {
  const z=x+y, zz=Math.abs(x)>Math.abs(y) ? (x-z)+y : (y-z)+x;
  return [z,zz];
}
export function sub2(x,y) {
  const z=x-y, zz=Math.abs(x)>Math.abs(y) ? (x-z)-y : x-(y+z);
  return [z,zz];
}
export function mul2(x,y) {
  let p=134217729*x; const hx=(x-p)+p,tx=x-hx;
  p=134217729*y; const hy=(y-p)+p,ty=y-hy;
  const z=x*y, zz=(((hx*hy-z)+hx*ty)+tx*hy)+tx*ty;
  return [z,zz];
}
export function div2(x,xx,y,yy) {
  const c=x/y,[u,uu]=mul2(c,y),cc=((((x-u)-uu)+xx)-c*yy)/y;
  const z=c+cc; return [z,(c-z)+cc];
}

// Exact software binary64 fused multiply-add. BigInt is an internal arithmetic
// representation only: inputs/results are Numbers and no native addon is used.
const bits = new DataView(new ArrayBuffer(8));
function integerParts(value) {
  bits.setFloat64(0,value,false);
  const high=bits.getUint32(0,false), low=bits.getUint32(4,false), exponent=(high>>>20)&2047;
  let mantissa=(BigInt(high&0xfffff)<<32n)|BigInt(low);
  if(exponent)mantissa|=1n<<52n;
  if(high>>>31)mantissa=-mantissa;
  return [mantissa,exponent?exponent-1075:-1074];
}
export function fma(x,y,z) {
  if(Number.isNaN(y))return y+y;
  if(Number.isNaN(x))return x+x;
  if(Number.isNaN(z))return z+z;
  if(Number.isFinite(x)&&Number.isFinite(y)&&!Number.isFinite(z))return z+z;
  if(!Number.isFinite(x)||!Number.isFinite(y)||!Number.isFinite(z))return x*y+z;
  const [a,ae]=integerParts(x),[b,be]=integerParts(y),[c,ce]=integerParts(z),pe=ae+be;
  const e=Math.min(pe,ce), n=(a*b<<BigInt(pe-e))+(c<<BigInt(ce-e));
  if(n===0n)return Object.is(x*y,-0)&&Object.is(z,-0)?-0:0;
  const negative=n<0n,m=negative?-n:n,shift=Math.max(m.toString(2).length-53,-1074-e,0);
  let rounded=m;
  if(shift){const s=BigInt(shift),tail=m-((m>>s)<<s),half=1n<<(s-1n);rounded=m>>s;if(tail>half||(tail===half&&(rounded&1n)))rounded++;}
  const result=Number(rounded)*(2**(e+shift));return negative?-result:result;
}
