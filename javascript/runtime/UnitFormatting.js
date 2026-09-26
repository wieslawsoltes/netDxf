import {FormatUnitInteger} from './IntegerFormatting.js';
// Decimal formatting used by the pinned .NET 8 netDxf unit-format APIs.
// Conversion first rounds binary64 to 15 significant digits (ties to even),
// then the custom 0/# mask rounds decimal digits away from zero. No native host is used.
import {ArgumentException,ArgumentNullException,FormatException,OverflowException,RequireInteger} from './Errors.js';
import {DotNetMath} from './GeometryRuntime.js';
const bits=new DataView(new ArrayBuffer(8));
const powers=new Map([[0,1n]]);
const pow10=n=>{if(!powers.has(n))powers.set(n,10n**BigInt(n));return powers.get(n);};
function significant(value){
  if(value===0)return {digits:0n,exponent:0};
  bits.setFloat64(0,value);const raw=bits.getBigUint64(0),encoded=Number((raw>>52n)&2047n);
  let numerator=(raw&0xfffffffffffffn)|(encoded===0?0n:0x10000000000000n),denominator=1n;
  const binaryExponent=(encoded===0?-1022:encoded-1023)-52;
  if(binaryExponent>=0)numerator<<=BigInt(binaryExponent);else denominator<<=BigInt(-binaryExponent);
  let exponent=Number(value.toExponential().split('e')[1]);
  const compare=e=>e>=0?numerator-denominator*pow10(e):numerator*pow10(-e)-denominator;
  while(compare(exponent)<0n)exponent--;while(compare(exponent+1)>=0n)exponent++;
  const scale=14-exponent;
  if(scale>=0)numerator*=pow10(scale);else denominator*=pow10(-scale);
  let digits=numerator/denominator;const twice=2n*(numerator%denominator);
  if(twice>denominator||twice===denominator&&(digits&1n)!==0n)digits++;
  if(digits===1000000000000000n){digits/=10n;exponent++;}
  return {digits,exponent};
}
function roundDecimal(digits,shift){
  if(shift>=0)return digits*pow10(shift);
  const divisor=pow10(-shift),q=digits/divisor;
  return q+(2n*(digits%divisor)>=divisor?1n:0n);
}
export function CheckDecimalSeparator(value){
  if(value==null)throw new ArgumentNullException('value');
  if(value==='')throw new ArgumentException('The decimal separator cannot be empty.','value');
  return value;
}
/** Exact masks constructed by AngleUnitFormat/LinearUnitFormat; not a general CLR formatter. */
export function UnitDecimal(value,places,separator,suppressLeading=false,suppressTrailing=false,scientific=false){
  CheckDecimalSeparator(separator);RequireInteger(places,0,32767,'places');
  if(!Number.isFinite(value))return Number.isNaN(value)?'NaN':value<0?'-Infinity':'Infinity';
  const negative=value<0||Object.is(value,-0),buffer=significant(Math.abs(value));
  let exponent=buffer.exponent;
  let scaled=roundDecimal(buffer.digits,scientific?places-14:exponent-14+places);
  if(scientific&&scaled>=pow10(places+1)){scaled/=10n;exponent++;}
  const text=scaled.toString().padStart(places+1,'0');
  let integral=text.slice(0,text.length-places),fraction=places?text.slice(-places):'';
  if(suppressTrailing)fraction=fraction.replace(/0+$/,'');
  if(suppressLeading&&!scientific&&integral==='0')integral='';
  const result=integral+(fraction?separator+fraction:'')+(scientific?'E'+(exponent<0?'-':'+')+String(Math.abs(exponent)).padStart(2,'0'):'');
  return (negative&&result!==''?'-':'')+result;
}
export function UnitCheckedInt32(value){
  const rounded=DotNetMath.Round(value);
  if(!Number.isFinite(rounded)||rounded<-2147483648||rounded>2147483647)throw new OverflowException('Value was outside the Int32 range.');
  return rounded|0;
}
/** Composite formatting needed when callers embed braces/alignment in angle symbols. */
export function UnitComposite(template,values,separator){
  let result='';
  for(let i=0;i<template.length;){
    const c=template[i++];
    if(c==='}'&&template[i]==='}'){result+='}';i++;continue;}
    if(c!== '{'){if(c==='}')throw new FormatException('Unexpected closing brace.');result+=c;continue;}
    if(template[i]==='{'){result+='{';i++;continue;}
    const match=/^(\d+) *(?:, *(-?\d+) *)?(?::([^{}]*))?}/.exec(template.slice(i));
    if(!match)throw new FormatException('Invalid composite format.');
    const at=Number(match[1]);if(at>=values.length)throw new FormatException('Format index is outside the argument list.');
    const value=values[at];let text=typeof value==='number'?FormatUnitInteger(value,match[3]??'',separator):String(value??'');
    if(match[2]){const width=Number(match[2]);if(!Number.isSafeInteger(width)||Math.abs(width)>=1000000)throw new FormatException('Invalid alignment.');text=width<0?text.padEnd(-width):text.padStart(width);}
    result+=text;i+=match[0].length;
  }
  return result;
}
