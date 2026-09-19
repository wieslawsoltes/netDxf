// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { EventHook } from '../../runtime/EventHook.js';
import { OrdinalIgnoreCaseEquals, OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { TableObjectChangedEventArgs } from './TableObjectChangedEventArgs.js';
import { ArgumentException, ArgumentNullException, InvalidCastException, NullReferenceException, NotSupportedException } from '../../runtime/Errors.js';
const invalid = Object.freeze(['\\','/',':','*','?','"','<','>','|',';',',','=','`']);
const trim = /^[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+|[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+$/g;
export class TableObject extends DxfObject {
  #name; #reserved = false;
  constructor(name, codeName, checkName) {
    super(codeName); if (new.target === TableObject) throw new NotSupportedException('TableObject is abstract.');
    if (name == null) throw new NullReferenceException('Name is null.');
    name = name.replace(trim, '');
    if (checkName && !TableObject.IsValidName(name)) throw new ArgumentException('Invalid table object name.', 'name');
    this.#name = name; Object.defineProperty(this, 'NameChanged', { value: new EventHook(), enumerable: true });
  }
  get Name() { return this.#name; }
  set Name(value) { this.SetName(value, true); }
  get IsReserved() { return this.#reserved; }
  set IsReserved(value) { this.#reserved = value; } // internal in C#
  static get InvalidCharacters() { return invalid.slice(); }
  static IsValidName(name) { return typeof name === 'string' && name.length > 0 && !invalid.some(c => name.includes(c)); }
  SetName(newName, checkName) {
    if (newName == null || newName === '') throw new ArgumentNullException('newName');
    if (this.IsReserved) throw new ArgumentException('Reserved table objects cannot be renamed.', 'newName');
    if (OrdinalIgnoreCaseEquals(this.#name, newName)) return;
    if (checkName && !TableObject.IsValidName(newName)) throw new ArgumentException('Invalid table object name.', 'newName');
    this.OnNameChangedEvent(this.#name, newName); this.#name = newName;
  }
  OnNameChangedEvent(oldName, newName) { this.NameChanged.Invoke(this, new TableObjectChangedEventArgs(oldName, newName)); }
  get UsesReferenceIdentity() { return false; }
  ToString() { return this.Name; }
  CompareTo(other) {
    if (other != null && !(other instanceof TableObject)) throw new InvalidCastException('Expected TableObject.');
    if (other == null) throw new ArgumentNullException('other');
    if (this.constructor !== other.constructor) return 0;
    const a = OrdinalIgnoreCaseKey(this.Name), b = OrdinalIgnoreCaseKey(other.Name), count = Math.min(a.length, b.length);
    for (let i = 0; i < count; i++) if (a.charCodeAt(i) !== b.charCodeAt(i)) return a.charCodeAt(i) - b.charCodeAt(i);
    return a.length - b.length;
  }
  Equals(other) { return other != null && this.constructor === other.constructor && this.EqualsTableObject(other); }
  EqualsTableObject(other) { return other != null && ((this.UsesReferenceIdentity || other.UsesReferenceIdentity) ? this === other : OrdinalIgnoreCaseEquals(this.Name, other.Name)); }
  GetHashCode() {
    // String.GetHashCode is process-specific in .NET. Numeric cross-process equality is not promised.
    let code = 2166136261; for (let i = 0; i < this.Name.length; i++) code = Math.imul(code ^ this.Name.charCodeAt(i), 16777619); return code | 0;
  }
}
