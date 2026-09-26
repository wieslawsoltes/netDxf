// Complete original background model case; typed background wire cases remain unported.
import { MText, MTextBackgroundFill, MTextBackgroundFillFlags, AciColor } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterMTextBackgroundTests() { Run('mtext/background/model',MTextBackgroundModel); }
export function EqualBackground(expected,actual) {
  if(expected===null){Check(actual===null,'An absent background was invented.');return;}
  Check(actual!==null,'Background data was discarded.');
  for(const key of ['Flags','ScaleFactor','ColorIndex','TrueColor','ColorName','Transparency'])Equal(expected[key],actual[key],'background '+key);
}
export function MTextBackgroundModel() {
  Check(new MText().BackgroundFill===null,'New MTEXT invented a background.');const bg=new MTextBackgroundFill();
  Equal(MTextBackgroundFillFlags.UseColor,bg.Flags,'default flags');Equal(1.5,bg.ScaleFactor,'default scale');Equal(7,bg.ColorIndex,'default ACI');
  Check(bg.TrueColor===null && bg.ColorName===null && bg.Transparency===null,'Default created optional fields.');
  for(const flags of [-1,4,8,20,32,2147483647])Throws(ArgumentOutOfRangeException,()=>{bg.Flags=flags;});
  for(const bad of [-1,0,NaN,Infinity,-Infinity])Throws(ArgumentOutOfRangeException,()=>{bg.ScaleFactor=bad;});
  for(const bad of [-1,257,-32768,32767])Throws(ArgumentOutOfRangeException,()=>{bg.ColorIndex=bad;});
  for(const bad of ['a\0b','a\nb','a\rb'])Throws(ArgumentException,()=>{bg.ColorName=bad;});
  Equal(MTextBackgroundFillFlags.UseColor,bg.Flags,'Rejected flags modified the object.');Equal(1.5,bg.ScaleFactor,'Rejected scale modified the object.');Equal(7,bg.ColorIndex,'Rejected ACI modified the object.');
  Check(bg.TrueColor===null && bg.ColorName===null,'Rejected color changed the object.');
  for(const index of [0,1,255,256])bg.ColorIndex=index;
  for(const rgb of [-2147483648,-1,0,1,2147483647,0xFFFFFF])bg.TrueColor=rgb;
  bg.ScaleFactor=null;bg.ColorIndex=null;bg.ColorName='';bg.Transparency=-2147483648;const copy=bg.Clone();EqualBackground(bg,copy);
  copy.TrueColor=7;copy.ColorName='changed';copy.Transparency=2147483647;
  Equal(0xFFFFFF,bg.TrueColor,'Clone changed source RGB');Equal('',bg.ColorName,'Clone changed source name');Equal(-2147483648,bg.Transparency,'Clone changed source transparency');
  const aci=AciColor.FromTrueColor(0x112233),converted=MTextBackgroundFill.FromColor(aci,2);Equal(0x112233,converted.TrueColor,'True color conversion');
  const fallback=aci.Index;aci.Index=3;Equal(fallback,converted.ColorIndex,'Retained mutable source ACI');Equal(0x112233,converted.TrueColor,'Retained mutable source RGB');
  Throws(ArgumentNullException,()=>MTextBackgroundFill.FromColor(null));Equal(3,MTextBackgroundFill.FromDrawingWindow().Flags,'Window factory flags');
  const frame=MTextBackgroundFill.CreateTextFrame();Equal(MTextBackgroundFillFlags.TextFrame,frame.Flags,'Frame factory flags');Check(frame.ScaleFactor===null && frame.ColorIndex===null,'Frame factory invented fill data.');
}
