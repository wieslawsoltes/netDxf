// Complete detached original only; typed document/transport originals remain unregistered.
import {DimensionStyle,DimensionStyleOverride,DimensionStyleOverrideType as O,DimensionStyleTextDirection} from '../../index.js';
import {BoxedScalar} from '../../runtime/BoxedScalar.js';
import {ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Throws} from './TestHarness.js';
export function RegisterDimensionStyleParityTests(){Run('dimstyle-parity/clone-and-validation',DimensionStyleParityValidation);}
export function DimensionStyleParityValidation(){
 const style=Object.assign(new DimensionStyle('Clone'),{ExtLineFixed:true,ExtLineFixedLength:4.75,TextInsideAlign:true,TextOutsideAlign:true,TextDirection:DimensionStyleTextDirection.RightToLeft,TickSize:2.5,TextVerticalPosition:-3.25,UserPositionedText:true});
 const clone=style.Clone('Clone2');Check(clone.ExtLineFixed&&clone.TextInsideAlign&&clone.TextOutsideAlign,'stored clone boolean settings');Equal(4.75,clone.ExtLineFixedLength,'clone fixed extension length');Equal(style.TextDirection,clone.TextDirection,'clone text direction');Equal(2.5,clone.TickSize,'clone ticks');Equal(-3.25,clone.TextVerticalPosition,'clone vertical offset');Check(clone.UserPositionedText,'clone user text');
 clone.TickSize=9;Equal(2.5,style.TickSize,'clone editing independence');
 for(const invalid of [NaN,Infinity,-Infinity]){Throws(ArgumentOutOfRangeException,()=>{style.TickSize=invalid;});Throws(ArgumentOutOfRangeException,()=>{style.TextVerticalPosition=invalid;});Throws(ArgumentOutOfRangeException,()=>new DimensionStyleOverride(O.TickSize,invalid));Throws(ArgumentOutOfRangeException,()=>new DimensionStyleOverride(O.TextVerticalPosition,invalid));}
 Throws(ArgumentOutOfRangeException,()=>{style.TickSize=-1;});Throws(ArgumentOutOfRangeException,()=>new DimensionStyleOverride(O.TickSize,-1));
 Throws(ArgumentException,()=>new DimensionStyleOverride(O.TickSize,new BoxedScalar('Int32',1)));Throws(ArgumentException,()=>new DimensionStyleOverride(O.TextVerticalPosition,'1'));Throws(ArgumentException,()=>new DimensionStyleOverride(O.UserPositionedText,new BoxedScalar('Int16',1)));
 const defaults=DimensionStyle.Default;Equal(0,defaults.TickSize,'default ticks');Equal(0,defaults.TextVerticalPosition,'default vertical');Check(!defaults.UserPositionedText,'default user text');
}
