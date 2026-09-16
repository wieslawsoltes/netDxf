// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfClass } from '../DxfClass.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, KeyNotFoundException, RequireInteger } from '../../runtime/Errors.js';

/** Ordered KeyedCollection adapter with independent ordinal DXF and C++ name indexes. */
export class DxfClassCollection {
  #items = [];
  #names = new Map();
  #cpp = new Map();
  #version = 0;
  get Count() { return this.#items.length; }
  GetKeyForItem(item) { if (item == null) throw new ArgumentNullException('item'); return item.Name; }
  get_Item(key) {
    if (typeof key === 'number') { RequireInteger(key,0,this.Count-1,'index'); return this.#items[key]; }
    if (key == null) throw new ArgumentNullException('key');
    if (!this.#names.has(key)) throw new KeyNotFoundException('The given key was not present in the collection.');
    return this.#names.get(key);
  }
  set_Item(index,item) { RequireInteger(index,0,this.Count-1,'index'); this.SetItem(index,item); }
  #validate(item,replacedIndex) {
    if (item == null) throw new ArgumentNullException('item');
    if (!(item instanceof DxfClass)) throw new ArgumentException('Expected DxfClass.','item');
    const previous = this.#cpp.get(item.CppClassName);
    if (previous && (replacedIndex < 0 || this.#items[replacedIndex] !== previous))
      throw new ArgumentException('A CLASS C++ name must be unique within the collection.','item');
  }
  InsertItem(index,item) {
    this.#validate(item,-1);
    if (this.#names.has(item.Name)) throw new ArgumentException('An item with the same key has already been added.','key');
    RequireInteger(index,0,this.Count,'index');
    this.#names.set(item.Name,item);this.#cpp.set(item.CppClassName,item);this.#items.splice(index,0,item);this.#version++;
  }
  SetItem(index,item) {
    this.#validate(item,index);const previous=this.get_Item(index);
    if (previous.Name !== item.Name && this.#names.has(item.Name)) throw new ArgumentException('An item with the same key has already been added.','key');
    this.#names.delete(previous.Name);this.#cpp.delete(previous.CppClassName);
    this.#names.set(item.Name,item);this.#cpp.set(item.CppClassName,item);this.#items[index]=item;this.#version++;
  }
  Add(item) { this.InsertItem(this.Count,item); }
  Insert(index,item) { RequireInteger(index,0,this.Count,'index');this.InsertItem(index,item); }
  Contains(item) {
    if (typeof item === 'string') return this.#names.has(item);
    if (item == null) throw new ArgumentNullException('key');
    return this.#items.includes(item);
  }
  IndexOf(item) { return this.#items.indexOf(item); }
  Remove(item) {
    if (item == null) throw new ArgumentNullException('key');
    const value=typeof item === 'string'?this.#names.get(item):item;
    const index=this.#items.indexOf(value);if(index<0)return false;
    this.RemoveAt(index);return true;
  }
  RemoveAt(index) {
    const item=this.get_Item(index);this.#names.delete(item.Name);this.#cpp.delete(item.CppClassName);
    this.#items.splice(index,1);this.#version++;
  }
  Clear() { this.#items.length=0;this.#names.clear();this.#cpp.clear();this.#version++; }
  CopyTo(array,index=0) {
    if(array==null)throw new ArgumentNullException('array');
    RequireInteger(index,0,2147483647,'arrayIndex');
    if(array.length-index<this.Count)throw new ArgumentException('Destination array was not long enough.');
    for(let i=0;i<this.Count;i++)array[index+i]=this.#items[i];
  }
  GetEnumerator() {
    const version=this.#version,owner=this;let index=0,current=null;
    const check=()=>{if(version!==owner.#version)throw new InvalidOperationException('Collection was modified; enumeration operation may not execute.');};
    return {get Current(){return current;},MoveNext(){check();if(index<owner.Count){current=owner.#items[index++];return true;}index=owner.Count+1;current=null;return false;},Reset(){check();index=0;current=null;},Dispose(){},next(){const done=!this.MoveNext();return{done,value:done?undefined:current};},[Symbol.iterator](){return this;}};
  }
  [Symbol.iterator]() { return this.GetEnumerator(); }
}
