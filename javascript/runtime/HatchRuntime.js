// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Copy } from './GeometryRuntime.js';
import { ValueList } from './ValueList.js';
import { ArgumentOutOfRangeException, NullReferenceException, RequireInteger } from './Errors.js';
export const Required = value => { if (value == null) throw new NullReferenceException(); return value; };
/** Field locations copy vector assignments but allow editing the field's components. */
export function VectorField(target, name, initial) {
  let stored = Copy(initial);
  Object.defineProperty(target, name, { enumerable:true, get:()=>stored, set:v=>{stored=Copy(v);} });
}
export function DoubleFields(target, names) {
  const data = new DataView(new ArrayBuffer(names.length * 8));
  names.forEach((name,i)=>Object.defineProperty(target,name,{enumerable:true,get:()=>data.getFloat64(i*8),set:v=>data.setFloat64(i*8,v)}));
}
export function FiniteVector2(value, parameter) {
  if (!Number.isFinite(value.X) || !Number.isFinite(value.Y)) throw new ArgumentOutOfRangeException(parameter,value);
}
/** Collection<Vector2> validates indexes before invoking the derived finite-item guard. */
export class FiniteVector2Collection extends ValueList {
  #parameter;
  constructor(parameter) { super(); this.#parameter=parameter; }
  Add(value) { FiniteVector2(value,this.#parameter); super.Add(value); }
  Insert(index,value) { RequireInteger(index,0,this.Count,'index'); FiniteVector2(value,this.#parameter); super.Insert(index,value); }
  set_Item(index,value) { RequireInteger(index,0,this.Count-1,'index'); FiniteVector2(value,this.#parameter); super.set_Item(index,value); }
}
