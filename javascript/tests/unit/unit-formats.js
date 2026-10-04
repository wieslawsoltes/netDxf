import test from 'node:test';
import assert from 'node:assert/strict';
import {AngleUnitFormat as Angle,LinearUnitFormat as Linear,UnitStyleFormat,FractionFormatType,Culture} from '../../index.js';
import {ArgumentException,ArgumentNullException,OverflowException,FormatException} from '../../runtime/Errors.js';
import {UnitDecimal,UnitCheckedInt32} from '../../runtime/UnitFormatting.js';
import {FormatUnitInteger} from '../../runtime/IntegerFormatting.js';
const format=settings=>Object.assign(new UnitStyleFormat(),settings);
test('custom decimal masks use fifteen significant digits then decimal midpoint-away rounding',()=>{
  const f=format({LinearDecimalPlaces:2});assert.equal(Linear.ToDecimal(1.005,f),'1.01');assert.equal(Linear.ToDecimal(2.675,f),'2.68');
  assert.equal(Linear.ToDecimal(100000000000000.5,f),'100000000000000.00');
  assert.equal(Linear.ToDecimal(100000000000001.5,f),'100000000000002.00');
  assert.equal(Linear.ToDecimal(1.2345678901234567,format({LinearDecimalPlaces:20})),'1.23456789012346000000');
});
test('angle decimal and DMS paths preserve their different midpoint rounding contracts',()=>{
  const f=format({AngularDecimalPlaces:0});assert.equal(Angle.ToDecimal(2.5,f),'3°');assert.equal(Angle.ToDegreesMinutesSeconds(2.5,f),'2°');
  assert.equal(Angle.ToDecimal(-2.5,f),'-3°');assert.equal(Angle.ToDegreesMinutesSeconds(-2.5,f),'-2°');
});
test('signed zero and suppressed leading and trailing zeros are not conflated',()=>{
  const f=format({LinearDecimalPlaces:2});assert.equal(Linear.ToDecimal(-0,f),'-0.00');f.SuppressLinearLeadingZeros=true;
  assert.equal(Linear.ToDecimal(-0,f),'-.00');f.SuppressLinearTrailingZeros=true;
  assert.equal(Linear.ToDecimal(-0,f),'');assert.equal(Linear.ToDecimal(-.001,f),'');assert.equal(Linear.ToScientific(-0,f),'-0E+00');
});
test('scientific formatting handles binary64 endpoints and decimal carry exactly',()=>{
  const f=format({LinearDecimalPlaces:2});assert.equal(Linear.ToScientific(Number.MIN_VALUE,f),'4.94E-324');
  assert.equal(Linear.ToScientific(Number.MAX_VALUE,f),'1.80E+308');assert.equal(Linear.ToScientific(9.999,f),'1.00E+01');
});
test('all unit entrypoints reject null format with the original parameter name',()=>{
  for(const [type,methods] of [[Linear,['ToDecimal','ToScientific','ToEngineering','ToArchitectural','ToFractional']],[Angle,['ToDecimal','ToDegreesMinutesSeconds','ToRadians','ToGradians']]])
    for(const name of methods)assert.throws(()=>type[name](1,null),{name:'ArgumentNullException',ParamName:'format'});
});
test('only paths that create NumberFormatInfo validate the decimal separator',()=>{
  const f=format({DecimalSeparator:null});assert.throws(()=>Linear.ToDecimal(1,f),{name:'ArgumentNullException',ParamName:'value'});
  assert.throws(()=>Linear.ToEngineering(12,f),ArgumentNullException);assert.equal(Linear.ToArchitectural(12,f),"1'");
  f.DecimalSeparator='';assert.throws(()=>Angle.ToRadians(Infinity,f),{name:'ArgumentException',ParamName:'value'});
  f.DecimalSeparator='::';assert.equal(Linear.ToDecimal(1.25,f),'1::25');
});
test('engineering hidden-zero-feet path retains source unrounded current-culture inches',()=>{
  const saved=Culture.Current;try{Culture.Current='';const f=format({LinearDecimalPlaces:2});assert.equal(Linear.ToEngineering(1.005,f),'1.005"');
    f.SuppressZeroFeet=false;assert.equal(Linear.ToEngineering(1.005,f),'0\'-1.01"');
    f.SuppressZeroFeet=true;Culture.Current='de-DE';assert.equal(Linear.ToEngineering(1.005,f),'1,005"');
  }finally{Culture.Current=saved;}
});
test('architectural and fractional output preserve source carry and negative-fraction behavior',()=>{
  const f=format({FractionType:FractionFormatType.NotStacked,LinearDecimalPlaces:0});
  assert.equal(Linear.ToFractional(1.875,f),'1 1/1');assert.equal(Linear.ToArchitectural(11.999,f),'11 1/1"');
  f.LinearDecimalPlaces=3;assert.equal(Linear.ToFractional(-.5,f),'0 -4/8');
});
test('MTEXT fraction forms use source separators and current-culture height scales',()=>{
  const saved=Culture.Current;try{Culture.Current='';const f=format({FractionHeightScale:.75,LinearDecimalPlaces:2});
    assert.equal(Linear.ToFractional(1.5,f),'\\A1;1{\\H0.75x;\\S1/2;}');f.FractionType=FractionFormatType.Diagonal;
    assert.equal(Linear.ToFractional(1.5,f),'\\A1;1{\\H0.75x;\\S1#2;}');Culture.Current='pl-PL';
    assert.equal(Linear.ToFractional(1.5,f),'\\A1;1{\\H0,75x;\\S1#2;}');
  }finally{Culture.Current=saved;}
});
test('DMS retains independent negative components rather than normalizing or carrying them',()=>{
  const f=format({AngularDecimalPlaces:4});assert.equal(Angle.ToDegreesMinutesSeconds(-12.5,f),'-12°-30\'0"');
  f.AngularDecimalPlaces=6;assert.equal(Angle.ToDegreesMinutesSeconds(12.5,f),'12°30\'0.00"');
  assert.equal(Angle.ToGradians(90,format({AngularDecimalPlaces:0})),'100g');
});
test('unit symbols support Unicode, surrogate code units and composite escaping',()=>{
  const f=format({DegreesSymbol:'{{°}} {0:D5}',AngularDecimalPlaces:0});assert.equal(Angle.ToDegreesMinutesSeconds(12,f),'12{°} 00012');
  f.DegreesSymbol='{';assert.throws(()=>Angle.ToDegreesMinutesSeconds(12,f),FormatException);
  assert.equal(Angle.ToDecimal(12,f),'12{');f.DegreesSymbol='\ud800';assert.equal(Angle.ToDegreesMinutesSeconds(12,f),'12\ud800');
});
test('injected integer masks retain exact digits, grouping, scaling, signs and zero sections',()=>{
  assert.equal(FormatUnitInteger(125,'G2'),'1.3E+02');assert.equal(FormatUnitInteger(-125,'P1'),'-12,500.0 %');
  assert.equal(FormatUnitInteger(-125,'C1'),'(¤125.0)');assert.equal(FormatUnitInteger(125,'00-00'),'01-25');
  assert.equal(FormatUnitInteger(0,'0;[0];ZERO'),'ZERO');assert.equal(FormatUnitInteger(-125,'0,,'),'0');
  assert.equal(FormatUnitInteger(1,'000.0E+00'),'100.0E-02');assert.equal(FormatUnitInteger(-1,'X4'),'FFFFFFFF');
});
test('fraction conversions retain checked Int32 overflow and short precision casts',()=>{
  const f=format({LinearDecimalPlaces:2});for(const value of [NaN,Infinity,-Infinity,Number.MAX_VALUE])assert.throws(()=>Linear.ToFractional(value,f),OverflowException);
  assert.equal(UnitCheckedInt32(2.5),2);assert.equal(UnitCheckedInt32(3.5),4);assert.throws(()=>UnitCheckedInt32(2147483647.5),OverflowException);
  f.LinearDecimalPlaces=16;assert.equal(Linear.ToFractional(1.5,f),'1');
});
test('maximum public short precision emits all requested digits without a host dependency',()=>{
  const text=Linear.ToDecimal(0,format({LinearDecimalPlaces:32767}));assert.equal(text.length,32769);assert.equal(text,'0.'+'0'.repeat(32767));
  assert.equal(UnitDecimal(-0,0,'.'),'-0');
});
test('new unit-format corpus is deterministic, unique, complete and contains no expected results',async()=>{
  const {unitFormatCorpus}=await import('../../tools/unit-format-corpus.mjs');const a=unitFormatCorpus();assert.deepEqual(a,unitFormatCorpus());
  assert.equal(a.length,2938);assert.equal(new Set(a.map(p=>p.name)).size,2938);assert.equal(a.reduce((n,p)=>n+p.request.steps.length,0),45077);
  assert.ok(a.every(p=>!Object.hasOwn(p,'expected')));
});
