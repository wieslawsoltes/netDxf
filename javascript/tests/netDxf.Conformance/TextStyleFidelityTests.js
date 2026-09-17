// Complete detached API bodies from the pinned identically named C# test file.
// Typed document/wire/legacy cases in that file remain unregistered until implemented.
import { TextStyle, ShapeStyle, TextStyleFontData, FontStyle, XDataCode, XDataRecord } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
const StyleFontBits=0xF3123456|0;
const short=value=>(value<<16)>>16;
// A complete XData snapshot; only equality before/after an edit is observed by the source test.
const StyleSnapshot=style=>JSON.stringify(Array.from(style.XData.Values,x=>[x.ApplicationRegistry.Name,Array.from(x.XDataRecord,r=>[r.Code,r.Value])]));
export function RegisterTextStyleFidelityTests() {
  Run('style/fidelity/api/flags-height',StyleFidelityFlags);
  Run('style/fidelity/api/font-prefix',StyleFidelityPrefix);
  Run('style/fidelity/api/clone',StyleFidelityClone);
}
export function StyleFidelityFlags() {
  const style=new TextStyle('FLAGS','simplex.shx');Object.assign(style,{Flags:short(0xFFF4),TextGenerationFlags:short(0xFFFF),LastHeight:-3.25});
  style.IsVertical=false;style.IsBackward=false;style.IsUpsideDown=false;
  Equal(short(0xFFF0),style.Flags,'Vertical setter lost unknown bits');Equal(short(0xFFF9),style.TextGenerationFlags,'Generation setter lost unknown bits');
  const shape=new ShapeStyle('SHAPE','shape.shx');Object.assign(shape,{Flags:short(0xFFFF),LastHeight:0});
  Throws(ArgumentException,()=>{style.Flags|=1;});Throws(ArgumentException,()=>{shape.Flags=0;});
  for(const bad of [NaN,Infinity,-Infinity]){Throws(ArgumentOutOfRangeException,()=>{style.LastHeight=bad;});Throws(ArgumentOutOfRangeException,()=>{shape.LastHeight=bad;});}
  Equal(-3.25,style.LastHeight,'Invalid height mutated STYLE');Equal(0,shape.LastHeight,'Invalid height mutated shape STYLE');
}
export function StyleFidelityPrefix() {
  const style=new TextStyle('FONT','Arial.ttf');style.ExtendedFontData=new TextStyleFontData('Family',StyleFontBits);style.FontStyle=FontStyle.Regular;
  Equal(StyleFontBits&~0x03000000,style.ExtendedFontData.Flags,'FontStyle lost pitch/family/charset/unknown bits');
  const records=style.XData.get_Item('ACAD').XDataRecord;records.set_Item(0,new XDataRecord(XDataCode.String,'Direct edit'));
  Equal('Direct edit',style.FontFamilyName,'Font property has stale duplicated state');
  records.Add(new XDataRecord(XDataCode.String,'Ambiguous tail'));records.Add(new XDataRecord(XDataCode.Int32,99));
  const before=StyleSnapshot(style);Throws(InvalidOperationException,()=>{style.ExtendedFontData=null;});Equal(before,StyleSnapshot(style),'Ambiguous removal mutated XData');
  Throws(InvalidOperationException,()=>{style.FontFile='new.ttf';});Equal('Arial.ttf',style.FontFile,'Rejected file setter changed file');
  Throws(ArgumentOutOfRangeException,()=>{style.FontFamilyName='x'.repeat(256);});Equal('Arial.ttf',style.FontFile,'Invalid family setter changed file');
  Throws(ArgumentNullException,()=>new TextStyleFontData(null,0));
  const constructor=new TextStyle('CONSTRUCTOR','Family',FontStyle.Bold);Equal(0x02000000,constructor.ExtendedFontData.Flags,'Legacy constructor prefix');Check(constructor.FontFile==='','Legacy family constructor file');
}
export function StyleFidelityClone() {
  const source=new TextStyle('CLONE','asian.shx');Object.assign(source,{BigFont:'big.shx',Flags:0x4074,TextGenerationFlags:short(0xF006),LastHeight:9.75,ExtendedFontData:new TextStyleFontData('Family',StyleFontBits)});
  const copy=source.Clone('RENAMED');Equal(source.BigFont,copy.BigFont,'Clone BigFont');Equal(source.Flags,copy.Flags,'Clone flags');Equal(source.TextGenerationFlags,copy.TextGenerationFlags,'Clone generation');Equal(source.LastHeight,copy.LastHeight,'Clone last height');
  Equal(source.ExtendedFontData.Flags,copy.ExtendedFontData.Flags,'Clone font bits');Check(source.XData.get_Item('ACAD')!==copy.XData.get_Item('ACAD'),'Clone shares XData');
  copy.FontStyle=FontStyle.Regular;copy.IsVertical=false;copy.LastHeight=null;Equal(StyleFontBits,source.ExtendedFontData.Flags,'Clone changed source font bits');Check(source.IsVertical&&source.LastHeight===9.75,'Clone changed source flags/height');
  const shape=new ShapeStyle('SHAPE','shape.shx');Object.assign(shape,{Flags:0x4175,TextGenerationFlags:16,LastHeight:-8});
  const shapeCopy=shape.Clone('SHAPE_COPY');Equal(shape.Flags,shapeCopy.Flags,'Shape clone flags');Equal(shape.TextGenerationFlags,shapeCopy.TextGenerationFlags,'Shape clone generation');Equal(shape.LastHeight,shapeCopy.LastHeight,'Shape clone last height');
}
