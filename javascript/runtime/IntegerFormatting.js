// Invariant Int32 formatting for the composite placeholders accepted by angle symbols.
// Integer masks retain exact decimal digits; unlike Double masks they do not use a 15-digit conversion.
import {FormatException} from './Errors.js';
const power=n=>10n**BigInt(n);
function round(value,shift){if(shift>=0)return value*power(shift);const divisor=power(-shift);return value/divisor+(value%divisor*2n>=divisor?1n:0n);}
function sections(format){
  const parts=[''];let quote=null;
  for(let i=0;i<format.length&&format[i]!=='\0';i++){
    const c=format[i];
    if(c==='\\'){parts[parts.length-1]+=c+(format[++i]??'');continue;}
    if(quote){parts[parts.length-1]+=c;if(c===quote)quote=null;continue;}
    if(c==='"'||c==="'"){quote=c;parts[parts.length-1]+=c;continue;}
    if(c===';'&&parts.length<3){parts.push('');continue;}
    if(c===';')break;
    parts[parts.length-1]+=c;
  }
  return parts;
}
function render(value,mask,separator){
  const tokens=[];let integerCount=null,digitCount=0,lastInteger=-1,scale=0,scientific=null;
  for(let i=0;i<mask.length;i++){
    const c=mask[i];
    if(c==='\\'){if(i+1<mask.length)tokens.push({kind:'literal',text:mask[++i]});continue;}
    if(c==='"'||c==="'"){let text='';while(++i<mask.length&&mask[i]!==c)text+=mask[i];tokens.push({kind:'literal',text});continue;}
    if(c==='0'||c==='#'){if(integerCount===null)lastInteger=tokens.length;tokens.push({kind:'digit',required:c==='0',index:digitCount++});continue;}
    if(c==='.'){const first=integerCount===null;if(first)integerCount=digitCount;tokens.push({kind:'point',first});continue;}
    if(c===','){tokens.push({kind:'comma'});continue;}
    if(c==='%'||c==='‰'){scale+=c==='%'?2:3;tokens.push({kind:'literal',text:c});continue;}
    const exponent=(c==='E'||c==='e')?/^([+-]?)(0+)/.exec(mask.slice(i+1)):null;
    if(exponent&&!scientific){scientific={kind:'exponent',letter:c,plus:exponent[1]==='+',width:exponent[2].length};tokens.push(scientific);i+=exponent[0].length;continue;}
    tokens.push({kind:'literal',text:c});
  }
  integerCount??=digitCount;
  for(let i=lastInteger+1;i<tokens.length&&tokens[i].kind==='comma';i++)scale-=3;
  const grouping=tokens.some((token,i)=>token.kind==='comma'&&i<lastInteger);
  const fractionCount=digitCount-integerCount,absolute=BigInt(Math.abs(value)),raw=absolute.toString();
  let exponent=absolute===0n?0:raw.length-integerCount+scale;
  let coefficient=round(absolute,scientific?digitCount-raw.length:scale+fractionCount);
  if(scientific&&digitCount>0&&coefficient>=power(digitCount)){coefficient/=10n;exponent++;}
  const scaledText=coefficient.toString().padStart(fractionCount+1,'0');
  let integral=scaledText.slice(0,scaledText.length-fractionCount),fraction=fractionCount?scaledText.slice(-fractionCount):'';
  const digits=tokens.filter(t=>t.kind==='digit'),firstZero=digits.findIndex(t=>t.index<integerCount&&t.required);
  let fractionMinimum=0;for(const token of digits)if(token.index>=integerCount&&token.required)fractionMinimum=token.index-integerCount+1;
  const fractionLength=Math.max(fractionMinimum,fraction.replace(/0+$/,'').length);
  if(coefficient===0n&&firstZero<0&&!scientific)integral='';
  const minimum=firstZero<0?0:integerCount-firstZero,integerLength=Math.max(integral.length,minimum);
  let written=0,result='',emittedExtra=false;
  function emitDigit(c){if(grouping&&written>0&&(integerLength-written)%3===0)result+=',';result+=c;written++;}
  for(const token of tokens){
    if(token.kind==='literal'){result+=token.text;continue;}
    if(token.kind==='point'){if(token.first&&fractionLength>0)result+=separator;continue;}
    if(token.kind==='exponent'){result+=token.letter+(exponent<0?'-':token.plus?'+':'')+String(Math.abs(exponent)).padStart(token.width,'0');continue;}
    if(token.kind!=='digit')continue;
    if(token.index<integerCount){
      if(!emittedExtra){emittedExtra=true;for(const c of integral.slice(0,Math.max(0,integral.length-integerCount)))emitDigit(c);}
      const at=integral.length-integerCount+token.index;
      if(at>=0&&integral!=='')emitDigit(integral[at]);else if(firstZero>=0&&token.index>=firstZero)emitDigit('0');
    }else{const at=token.index-integerCount;if(at<fractionLength)result+=fraction[at]??'0';}
  }
  return {text:result,zero:digitCount>0&&coefficient===0n};
}
export function FormatIntegerMask(value,format,separator='.'){
  const parts=sections(format),zeroSection=parts[2]?2:0;
  let section=value===0?zeroSection:value<0&&parts[1]?1:0;
  let output=render(value,parts[section],separator);
  if(output.zero&&section!==zeroSection){section=zeroSection;output=render(0,parts[section],separator);}
  return (value<0&&section===0&&!output.zero?'-':'')+output.text;
}
export function FormatUnitInteger(value,format,separator='.'){
  if(format==='')return String(value);
  const standard=/^([a-zA-Z])(\d*)$/.exec(format);
  if(!standard)return FormatIntegerMask(value,format,separator);
  const letter=standard[1],upper=letter.toUpperCase(),precision=standard[2]?Number(standard[2]):null;
  if(precision!==null&&(!Number.isInteger(precision)||precision>999999999))throw new FormatException('Invalid numeric precision.');
  const signed=digits=>(value<0?'-':'')+digits;
  if(upper==='D')return signed(String(Math.abs(value)).padStart(precision??1,'0'));
  if(upper==='X'||upper==='B'){const text=(value<0?value>>>0:value).toString(upper==='X'?16:2).padStart(precision??1,'0');return letter===upper?text.toUpperCase():text;}
  if(upper==='G'){
    if(!precision||String(Math.abs(value)).length<=precision)return String(value);
    return FormatIntegerMask(value,'0.'+'#'.repeat(precision-1)+(letter==='g'?'e':'E')+'+00',separator);
  }
  if(upper==='F'||upper==='N')return FormatIntegerMask(value,(upper==='N'?'#,##0':'0')+'.'+'0'.repeat(precision??2),separator);
  if(upper==='P')return FormatIntegerMask(value,'#,##0.'+'0'.repeat(precision??2)+' %',separator);
  if(upper==='C'){const text=FormatIntegerMask(Math.abs(value),'#,##0.'+'0'.repeat(precision??2),separator);return value<0?'(¤'+text+')':'¤'+text;}
  if(upper==='E')return FormatIntegerMask(value,'0.'+'0'.repeat(precision??6)+letter+'+000',separator);
  throw new FormatException('Invalid integer format specifier.');
}
