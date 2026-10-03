// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { AciColor } from '../AciColor.js';
import { Linetype } from '../Tables/Linetype.js';
import { MathHelper } from '../MathHelper.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { EventHook } from '../../runtime/EventHook.js';
import { DoubleHash, Format } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
export class MLineStyleElement {
  #number = new DataView(new ArrayBuffer(8)); #color; #linetype;
  constructor(offset, color = AciColor.ByLayer, linetype = Linetype.ByLayer) {
    if (![1,3].includes(arguments.length)) throw new ArgumentException('No matching MLineStyleElement constructor.');
    this.Offset = offset; this.#color = color; this.#linetype = linetype;
    Object.defineProperty(this,'LinetypeChanged',{value:new EventHook(),enumerable:true});
  }
  get Offset() { return this.#number.getFloat64(0); } set Offset(value) { this.#number.setFloat64(0,value); }
  get Color() { return this.#color; } set Color(value) { if(value==null)throw new ArgumentNullException('value');this.#color=value; }
  get Linetype() { return this.#linetype; } set Linetype(value) { if(value==null)throw new ArgumentNullException('value');this.#linetype=this.OnLinetypeChangedEvent(this.#linetype,value); }
  OnLinetypeChangedEvent(oldValue,newValue) { const e=new TableObjectChangedEventArgs(oldValue,newValue);this.LinetypeChanged.Invoke(this,e);return e.NewValue; }
  CompareTo(other) {
    if(other==null)throw new ArgumentNullException('other');
    const a=this.Offset,b=other.Offset;
    return (-(a<b?-1:a>b?1:a===b?0:Number.isNaN(a)?(Number.isNaN(b)?0:-1):1))|0;
  }
  Equals(other) { return other!=null&&this.constructor===other.constructor&&MathHelper.IsEqual(this.Offset,other.Offset); }
  GetHashCode() { return DoubleHash(this.Offset); }
  Clone() {
    const copy=new MLineStyleElement(this.Offset);
    if(this.Color==null)throw new NullReferenceException();copy.Color=this.Color.Clone();
    if(this.#linetype==null)throw new NullReferenceException();copy.Linetype=this.#linetype.Clone();return copy;
  }
  ToString() { return Format('{0}, color:{1}, line type:{2}',this.Offset,this.Color,this.Linetype); }
}
