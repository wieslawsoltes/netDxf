// Complete first three original API cases. All later cases construct typed documents and remain unported.
import { DxfSectionGeometrySettings } from '../../index.js';
import { ArgumentException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Equal, Throws } from './TestHarness.js';
function SectionAppearance(ordinal,colorCode){return Object.assign(new DxfSectionGeometrySettings(),{
  SectionType:-17+ordinal,GeometryValue:1<<ordinal,Flags:0x80000005|0,ColorCode:colorCode,ColorIndex:ordinal===2?256:ordinal+1,LayerName:ordinal===0?'*_BackgroundLines':'東京',
  LinetypeName:ordinal===1?String.raw`Literal\U+0041`:'ByLayer',LinetypeScale:1.125+ordinal,PlotStyleName:String.raw`plot\U+0041`,Lineweight:40,FaceTransparency:ordinal*30,EdgeTransparency:100,
  HatchPatternType:ordinal,HatchPatternName:ordinal===0?'':ordinal===1?'SectionGeometrySettings':'ANSI31',HatchAngle:-7.125+ordinal,HatchScale:21.5+ordinal,HatchSpacing:-0.125-ordinal
});}
export function RegisterSectionSettingsTests(){for(let i=0;i<3;i++)Run(`section-settings/api/${i}`,()=>SectionSettingsApi(i));}
export function SectionSettingsApi(scenario){
  const value=SectionAppearance(0,63);
  if(scenario===0){for(const bad of [NaN,Infinity,-Infinity])Throws(ArgumentOutOfRangeException,()=>{value.HatchScale=bad;});Equal(21.5,value.HatchScale,'rejected scalar changed previous value');return;}
  if(scenario===1){for(const bad of ['x\n','x\0','\ud800','\udc00'])Throws(ArgumentException,()=>{value.LayerName=bad;});return;}
  if(scenario===2){Throws(ArgumentOutOfRangeException,()=>{value.ColorCode=64;});Throws(ArgumentOutOfRangeException,()=>{value.ColorIndex=257;});Throws(ArgumentOutOfRangeException,()=>{value.EdgeTransparency=-1;});return;}
}
