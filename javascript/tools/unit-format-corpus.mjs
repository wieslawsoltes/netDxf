// Deterministic source-independent inputs. The C# oracle supplies every expected string/error.
import {D} from './geometry-corpus.mjs';
export const unitFormatMethods=[['Units.LinearUnitFormat',['ToDecimal','ToScientific','ToArchitectural','ToEngineering','ToFractional']],['Units.AngleUnitFormat',['ToDecimal','ToDegreesMinutesSeconds','ToGradians','ToRadians']]];
const text=value=>typeof value==='string'?{utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))}:value;
export function unitFormatCorpus(){
  const probes=[];
  function add(name,value,settings={},culture=''){
    const steps=[{kind:'new',type:'Units.UnitStyleFormat',id:'f',args:[]}];
    for(const [member,value] of Object.entries(settings))steps.push({kind:'set',target:'f',member,value:member==='FractionType'?{enum:'Units.FractionFormatType',value}:member.endsWith('DecimalPlaces')?{short:value}:typeof value==='number'?D(value):text(value)});
    for(const [type,methods] of unitFormatMethods)for(const member of methods)steps.push({kind:'call',type,member,args:[D(value),{ref:'f'}]});
    probes.push({name:'unit-format/'+name,category:name.split('/')[0],request:{steps,culture}});
  }
  const values=[-Infinity,-Number.MAX_VALUE,-2147483648.5,-2147483648,-2147483647,-123.456,-25.5,-12,-11.999,-1.005,-.999,-.125,-1e-12,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1e-12,.005,.125,.375,.625,.875,.999,1.005,1.2345678901234567,2.675,9.999,11.999,12,25.5,32.999999,59.9999,90,360,9999999999999.99,100000000000000.5,100000000000001.5,1e20,1e-7,2147483647,2147483648,Number.MAX_VALUE,Infinity,NaN];
  for(const precision of [0,1,2,3,4,5,8,14,15,16,31])for(const leading of [false,true])for(const trailing of [false,true])for(let i=0;i<values.length;i++)
    add(`numeric/${precision}/${+leading}/${+trailing}/${i}`,values[i],{LinearDecimalPlaces:precision,AngularDecimalPlaces:precision,SuppressLinearLeadingZeros:leading,SuppressAngularLeadingZeros:leading,SuppressLinearTrailingZeros:trailing,SuppressAngularTrailingZeros:trailing});
  for(const fraction of [-1,0,1,2,3])for(const feet of [false,true])for(const inches of [false,true])for(const value of [-25.5,-12,-.75,0,.75,11.999,12,25.5])
    add(`fractions/${fraction}/${+feet}/${+inches}/${value}`,value,{FractionType:fraction,SuppressZeroFeet:feet,SuppressZeroInches:inches,LinearDecimalPlaces:3});
  for(const culture of ['', 'de-DE','pl-PL','fr-FR','ja-JP'])for(const value of [-25.5,-.005,0,.005,1.125,12,25.5,Infinity])
    add(`culture/${culture}/${value}`,value,{FractionHeightScale:0.75,DecimalSeparator:'::',LinearDecimalPlaces:5,AngularDecimalPlaces:6},culture);
  const symbols=[null,'','::','東京 😀','\ud800','\udc00','\0','{','}','{{°}}','{0:D5}','{0,8:X}','{2}','{999}','line\nline'];
  for(const property of ['DecimalSeparator','FeetSymbol','InchesSymbol','FeetInchesSeparator','DegreesSymbol','MinutesSymbol','SecondsSymbol','RadiansSymbol','GradiansSymbol'])for(let i=0;i<symbols.length;i++)
    add(`symbols/${property}/${i}`,25.56789,{[property]:symbols[i],AngularDecimalPlaces:5});
  for(const value of [-0,Number.MIN_VALUE,1.2345678901234567,Number.MAX_VALUE])add('max-precision/'+D(value).double,value,{LinearDecimalPlaces:32767,AngularDecimalPlaces:32767});
  let seed=0x61e389fd;const rng=()=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return seed>>>0;},view=new DataView(new ArrayBuffer(8));
  for(let i=0;i<400;i++){view.setUint32(0,rng());view.setUint32(4,rng());add(`random/${i}`,view.getFloat64(0),{LinearDecimalPlaces:i%17,AngularDecimalPlaces:i%17,SuppressLinearLeadingZeros:!!(i&1),SuppressLinearTrailingZeros:!!(i&2),SuppressZeroFeet:!!(i&4),SuppressZeroInches:!!(i&8)});}
  const masks=['G2','g2','P1','C1','F3','N2','X4','B8','0x','00-00','0.##','##','0,,','0%','0‰','0.0E+00','000.0E+00','0.0;[0.0];ZERO','literal','\"literal\"','\\0','0#0',' #,##0 ','0.### kg','Q','{0}','0.0.0','0\\;x','0;;ZERO','0.0E-000'];
  for(let i=0;i<masks.length;i++)for(const value of [-2147483648,-125,-1,0,1,125,9999])
    add(`composite/${i}/${value}`,value,{DegreesSymbol:'|{0:'+masks[i]+'}',AngularDecimalPlaces:0});
  for(const [type,methods] of unitFormatMethods)for(const member of methods)probes.push({name:`unit-format/null/${type}/${member}`,category:'null',request:{steps:[{kind:'call',type,member,args:[D(1),null]}]}});
  return probes;
}
