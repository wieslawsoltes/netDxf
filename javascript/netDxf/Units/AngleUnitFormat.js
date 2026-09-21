// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import {ArgumentNullException} from '../../runtime/Errors.js';
import {DotNetMath,Int32,MultiplyDouble as mul} from '../../runtime/GeometryRuntime.js';
import {UnitDecimal,UnitComposite,CheckDecimalSeparator} from '../../runtime/UnitFormatting.js';
import {MathHelper} from '../MathHelper.js';
const check=format=>{if(format==null)throw new ArgumentNullException('format');CheckDecimalSeparator(format.DecimalSeparator);return format;};
const decimal=(value,f)=>UnitDecimal(value,f.AngularDecimalPlaces,f.DecimalSeparator,f.SuppressAngularLeadingZeros,f.SuppressAngularTrailingZeros);
export class AngleUnitFormat {
  static ToDecimal(angle,format){const f=check(format);return decimal(angle,f)+(f.DegreesSymbol??'');}
  static ToGradians(angle,format){const f=check(format);return decimal(mul(angle,MathHelper.DegToGrad),f)+(f.GradiansSymbol??'');}
  static ToRadians(angle,format){const f=check(format);return decimal(mul(angle,MathHelper.DegToRad),f)+(f.RadiansSymbol??'');}
  static ToDegreesMinutesSeconds(angle,format){
    if(format==null)throw new ArgumentNullException('format');
    const degrees=angle,minutes=mul(degrees-Int32(degrees),60),seconds=mul(minutes-Int32(minutes),60),f=check(format),p=f.AngularDecimalPlaces;
    if(p===0)return UnitComposite('{0}'+(f.DegreesSymbol??''),[Int32(DotNetMath.Round(degrees))],f.DecimalSeparator);
    const first='{0}'+(f.DegreesSymbol??'')+'{1}'+(f.MinutesSymbol??'');
    if(p===1||p===2)return UnitComposite(first,[Int32(degrees),Int32(DotNetMath.Round(minutes))],f.DecimalSeparator);
    const template=first+'{2}'+(f.SecondsSymbol??'');
    return UnitComposite(template,[Int32(degrees),Int32(minutes),p===3||p===4?Int32(DotNetMath.Round(seconds)):UnitDecimal(seconds,p-4,f.DecimalSeparator)],f.DecimalSeparator);
  }
}
