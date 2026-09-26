import test from 'node:test';
import assert from 'node:assert/strict';
import * as Trig from '../../tools/HighPrecisionMath.mjs';
import { Sin as ReferenceSin } from '../../runtime/reference-math/sincos.js';
import { Atan2 as ReferenceAtan2 } from '../../runtime/reference-math/atan2.js';
import { DotNetMath, RemainderDouble, ReadDotNetNaN } from '../../runtime/GeometryRuntime.js';
import { Matrix3, Vector2 } from '../../index.js';
import { doubleBits, fromBits } from '../../tools/wire.mjs';

const negativeNaN='FFF8000000000000';
test('deterministic trig retains signed zero and exact axes',()=>{
  for(const key of ['Sin','Tan','Atan','Asin']) for(const zero of [0,-0]) assert.ok(Object.is(Trig[key](zero),zero),key);
  assert.equal(Trig.Cos(-0),1);assert.equal(Trig.Acos(0),Math.PI/2);
  assert.equal(Trig.Asin(1),Math.PI/2);assert.equal(Trig.Asin(-1),-Math.PI/2);
  assert.equal(Trig.Acos(1),0);assert.equal(Trig.Acos(-1),Math.PI);
});
test('special functions quiet signaling NaNs while retaining sign and payload',()=>{
  for(const key of ['Sin','Cos','Tan','Asin','Acos','Atan']) for(const h of ['7FF0000000000123','FFF0000000000123','7FF8000000000456','FFF8000000000456']){
    const expected=(BigInt('0x'+h)|0x8000000000000n).toString(16).toUpperCase().padStart(16,'0');
    assert.equal(doubleBits(Trig[key](fromBits(h))),expected,key);
  }
});
test('nonfinite arguments retain the observed .NET domain-result signs',()=>{
  for(const infinity of [Infinity,-Infinity]){
    for(const key of ['Sin','Cos','Tan'])assert.equal(doubleBits(Trig[key](infinity)),negativeNaN);
    assert.equal(Trig.Atan(infinity),Math.sign(infinity)*Math.PI/2);
  }
  for(const value of [-Infinity,-2,2,Infinity]) for(const key of ['Asin','Acos'])assert.equal(doubleBits(Trig[key](value)),'7FF8000000000000');
});
test('atan2 distinguishes both signed axes and the four infinite quadrants',()=>{
  for(const y of [0,-0])for(const x of [0,-0,1,-1,Infinity,-Infinity]){
    const sy=Object.is(y,-0)?-1:1,sx=x<0||Object.is(x,-0);
    assert.ok(Object.is(Trig.Atan2(y,x),sx?sy*Math.PI:y));
  }
  for(const y of [Infinity,-Infinity])for(const x of [Infinity,-Infinity])assert.equal(Trig.Atan2(y,x),Math.sign(y)*(x>0?Math.PI/4:3*Math.PI/4));
  assert.equal(Trig.Atan2(1,0),Math.PI/2);assert.equal(Trig.Atan2(-1,-0),-Math.PI/2);
});
test('atan2 uses exact exponent scaling without premature quotient overflow',()=>{
  assert.equal(Trig.Atan2(Number.MAX_VALUE,Number.MIN_VALUE),Math.PI/2);
  assert.ok(Object.is(Trig.Atan2(-Number.MIN_VALUE,Number.MAX_VALUE),-0));
  assert.equal(Trig.Atan2(Number.MIN_VALUE,Number.MIN_VALUE),Math.PI/4);
  assert.equal(Trig.Atan2(Number.MIN_VALUE,-Number.MIN_VALUE),3*Math.PI/4);
});
test('single-pair sine/cosine cache cannot leak or corrupt results between arguments',()=>{
  const values=[1,2,3,1e100,Number.MAX_VALUE,Math.PI/2,-1e200];
  const expected=values.map(x=>[doubleBits(Trig.Sin(x)),doubleBits(Trig.Cos(x))]);
  for(let i=0;i<200;i++)for(const [at,x] of values.entries()){
    assert.equal(doubleBits(Trig.Cos(x)),expected[at][1]);assert.equal(doubleBits(Trig.Sin(x)),expected[at][0]);
  }
});
test('large argument reduction does not reduce by rounded binary64 pi',()=>{
  // Independently rounded mathematical values; complete .NET comparison is a separate gate.
  assert.equal(doubleBits(Trig.Sin(1e100)),'BFD85C5E5B929359');
  assert.equal(doubleBits(Trig.Cos(1e100)),'3FED9757496841F5');
  assert.equal(doubleBits(Trig.Sin(Number.MAX_VALUE)),'3F7452FC98B34E97');
});
test('tiny exact binary64 arguments avoid fixed-point underflow',()=>{
  for(const x of [Number.MIN_VALUE,2**-1022,2**-100,-Number.MIN_VALUE,-(2**-100)]){
    for(const key of ['Sin','Tan','Atan','Asin'])assert.ok(Object.is(Trig[key](x),x));
    assert.equal(Trig.Cos(x),1);
  }
});
test('remainder preserves divisor NaN metadata and canonical invalid-dividend results',()=>{
  for(const h of ['7FF8000000000000','7FF0000000000123','FFF8000000000456']){
    const nan=fromBits(h),quiet=(BigInt('0x'+h)|0x8000000000000n).toString(16).toUpperCase();
    assert.equal(doubleBits(RemainderDouble(1,nan)),quiet);
    assert.equal(doubleBits(RemainderDouble(nan,1)),negativeNaN);
  }
  for(const args of [[1,0],[1,-0],[Infinity,1],[-Infinity,Infinity]])assert.equal(doubleBits(RemainderDouble(...args)),negativeNaN);
  assert.ok(Object.is(RemainderDouble(-0,2),-0));assert.equal(RemainderDouble(7,Infinity),7);
});
test('binary-backed invalid remainder NaNs remain stable in warmed geometry paths',()=>{
  for(let i=0;i<20000;i++){
    assert.equal(doubleBits(RemainderDouble(i%2?Infinity:-Infinity,360)),negativeNaN);
    assert.equal(doubleBits(ReadDotNetNaN()),negativeNaN);
  }
});
test('core geometry remains independent of host trigonometric calls',()=>{
  const names=['sin','cos','tan','asin','acos','atan','atan2'],saved=Object.fromEntries(names.map(key=>[key,Math[key]]));
  for(const key of names)Math[key]=()=>{throw new Error('Native trig must not be used');};
  try {
    assert.equal(DotNetMath.Sin,ReferenceSin);assert.notEqual(DotNetMath.Sin,Trig.Sin);assert.equal(DotNetMath.Atan2,ReferenceAtan2);assert.notEqual(DotNetMath.Atan2,Trig.Atan2);
    assert.equal(Matrix3.RotationZ(0).M11,1);assert.ok(Number.isFinite(Matrix3.RotationZ(1.5).M11));
    assert.equal(Vector2.Angle(new Vector2(1,1)),Math.PI/4);
  } finally {for(const key of names)Math[key]=saved[key];}
});

test('atan2 retains the near-axis correction before negative-x quadrant rounding', () => {
  const ratio = 2**54 + 4;
  for (const exponent of [-1074, -500, 0, 500, 950]) {
    const scale = 2**exponent;
    assert.equal(doubleBits(Trig.Atan2(ratio * scale, -scale)), '3FF921FB54442D19');
    assert.equal(doubleBits(Trig.Atan2(-ratio * scale, -scale)), 'BFF921FB54442D19');
    assert.equal(doubleBits(Trig.Atan2(ratio * scale, scale)), '3FF921FB54442D18');
  }
});
