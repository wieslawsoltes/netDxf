// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import {ArgumentNullException,OverflowException} from '../../runtime/Errors.js';
import {DotNetMath,Int32,Int16,NumberText,MultiplyDouble as mul} from '../../runtime/GeometryRuntime.js';
import {UnitDecimal,UnitCheckedInt32,CheckDecimalSeparator} from '../../runtime/UnitFormatting.js';
import {MathHelper} from '../MathHelper.js';
import {FractionFormatType as Fraction} from './FractionFormatType.js';
const check=format=>{if(format==null)throw new ArgumentNullException('format');return format;};
const decimal=(value,f,scientific=false)=>UnitDecimal(value,f.LinearDecimalPlaces,f.DecimalSeparator,f.SuppressLinearLeadingZeros,f.SuppressLinearTrailingZeros,scientific);
const symbol=value=>value??'';
function fraction(number,precision){
  let numerator=UnitCheckedInt32(mul(number-Int32(number),precision));let a=numerator,b=precision;
  while(b!==0){if(a===-2147483648&&b===-1)throw new OverflowException();const next=a%b;a=b;b=next;}
  const common=a<=0?1:a;return [Int32(numerator/common),Int32(precision/common)];
}
function fractionText(integer,numerator,denominator,f,prefix=''){
  switch(f.FractionType){
    case Fraction.Diagonal:case Fraction.Horizontal:return '\\A1;'+prefix+integer+'{\\H'+NumberText(f.FractionHeightScale)+'x;\\S'+numerator+(f.FractionType===Fraction.Diagonal?'#':'/')+denominator+';}';
    case Fraction.NotStacked:return prefix+integer+' '+numerator+'/'+denominator;
    default:return '';
  }
}
function zeroInches(feet,f){
  const ft=symbol(f.FeetSymbol),inch=symbol(f.InchesSymbol),separator=symbol(f.FeetInchesSeparator);
  if(feet===0){if(f.SuppressZeroFeet)return '0'+inch;if(f.SuppressZeroInches)return '0'+ft;return '0'+ft+separator+'0'+inch;}
  return feet+ft+(f.SuppressZeroInches?'':separator+'0'+inch);
}
export class LinearUnitFormat {
  static ToDecimal(length,format){return decimal(length,check(format));}
  static ToScientific(length,format){return decimal(length,check(format),true);}
  static ToEngineering(length,format){
    const f=check(format);CheckDecimalSeparator(f.DecimalSeparator);
    const feet=Int32(length/12),inches=length-Math.imul(12,feet);
    if(MathHelper.IsZero(inches))return zeroInches(feet,f);
    const inchesDec=decimal(inches,f);
    // Preserve the C# branch that uses unrounded current-culture inches when feet are hidden.
    if(feet===0&&f.SuppressZeroFeet)return NumberText(inches)+symbol(f.InchesSymbol);
    return feet+symbol(f.FeetSymbol)+symbol(f.FeetInchesSeparator)+inchesDec+symbol(f.InchesSymbol);
  }
  static ToArchitectural(length,format){
    const f=check(format),feet=Int32(length/12),inchesDec=length-Math.imul(12,feet),inches=Int32(inchesDec);
    if(MathHelper.IsZero(inchesDec))return zeroInches(feet,f);
    const [numerator,denominator]=fraction(inchesDec,Int16(DotNetMath.Pow(2,f.LinearDecimalPlaces)));
    if(numerator===0){
      if(inches===0)return zeroInches(feet,f);
      if(feet===0&&f.SuppressZeroFeet)return inches+symbol(f.InchesSymbol);
      return feet+symbol(f.FeetSymbol)+symbol(f.FeetInchesSeparator)+inches+symbol(f.InchesSymbol);
    }
    const prefix=f.SuppressZeroFeet&&feet===0?'':feet+symbol(f.FeetSymbol)+symbol(f.FeetInchesSeparator);
    const text=fractionText(inches,numerator,denominator,f,prefix);
    return text===''?'':text+symbol(f.InchesSymbol);
  }
  static ToFractional(length,format){
    const f=check(format),integer=Int32(length),[numerator,denominator]=fraction(length,Int16(DotNetMath.Pow(2,f.LinearDecimalPlaces)));
    return numerator===0?String(Int32(length)):fractionText(integer,numerator,denominator,f);
  }
}
