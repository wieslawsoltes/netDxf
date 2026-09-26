// Complete original constructor case, including readonly IList and enumeration cleanup.
// Typed round-trip and independent-fixture cases remain unported, not shortened.
import { DxfTableStyleBorderValues, DxfTableStyleRowBorders } from '../../index.js';
import { ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException } from '../../runtime/Errors.js';
import { Run,Throws,Check,Equal } from './TestHarness.js';
export function RegisterTableStyleBorderTests(){Run('table-style-borders/constructors',TableStyleBorderConstructors);}
export function TableStyleBorderConstructors(){
  const one=new DxfTableStyleBorderValues(-2,true,256);
  Throws(ArgumentNullException,()=>new DxfTableStyleRowBorders(null));
  for(const count of [0,5,7])Throws(ArgumentException,()=>new DxfTableStyleRowBorders(Array(count).fill(one)));
  const source=Array(6).fill(one),borders=new DxfTableStyleRowBorders(source);source[0]=null;
  Check(one===borders.Values.get_Item(0),'border values snapshot must not alias caller array');
  Throws(ArgumentException,()=>new DxfTableStyleRowBorders(source));
  Throws(ArgumentOutOfRangeException,()=>borders.WithBorder(-1,one));Throws(ArgumentOutOfRangeException,()=>borders.WithBorder(6,one));
  Throws(ArgumentNullException,()=>borders.WithBorder(0,null));Throws(NotSupportedException,()=>borders.Values.set_Item(0,one));
  let disposed=false,consumed=0;
  function* Endless(){try{while(true){consumed++;yield one;}}finally{disposed=true;}}
  Throws(ArgumentException,()=>new DxfTableStyleRowBorders(Endless()));Check(disposed&&consumed===7,'constructor must bound and dispose enumeration');
  function* Broken(){try{yield* borders.Values;}finally{throw new InvalidOperationException('dispose');}}
  Throws(InvalidOperationException,()=>new DxfTableStyleRowBorders(Broken()));
  const replacement=borders.WithBorder(3,new DxfTableStyleBorderValues(-32768,false,32767));
  Equal(-2,borders.Values.get_Item(3).StoredLineweight,'WithBorder retains earlier snapshot');Equal(-32768,replacement.Values.get_Item(3).StoredLineweight,'signed stored values are not normalized');
}
