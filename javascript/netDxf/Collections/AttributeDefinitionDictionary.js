// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { StringDictionary } from '../../runtime/StringDictionary.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { EventHook } from '../../runtime/EventHook.js';
import { AttributeDefinitionDictionaryEventArgs as Args } from './AttributeDefinitionDictionaryEventArgs.js';
import { ArgumentException, ArgumentNullException, RequireInteger } from '../../runtime/Errors.js';
/** Case-insensitive tag keys with source-ordered, cancellable collection notifications. */
export class AttributeDefinitionDictionary {
  #items=new StringDictionary(true);
  constructor(capacity=0){RequireInteger(capacity,0,2147483647,'capacity');for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])Object.defineProperty(this,event,{value:new EventHook(),enumerable:true});}
  #event(name,item){const e=new Args(item);this[name].Invoke(this,e);return e.Cancel;}
  get Count(){return this.#items.Count;}
  get IsReadOnly(){return false;}
  get Tags(){return this.#items.Keys;}
  get Keys(){return this.#items.Keys;}
  get Values(){return this.#items.Values;}
  get_Item(tag){return this.#items.get_Item(tag);}
  set_Item(tag,value){
    if(value==null)throw new ArgumentNullException('value');
    if(!OrdinalIgnoreCaseEquals(tag,value.Tag))throw new ArgumentException('The dictionary tag and attribute definition tag must be the same.');
    if(this.#items.get_Item(tag)===value)return;
    const remove=this.#items.get_Item(tag);
    if(this.#event('BeforeRemoveItem',remove)||this.#event('BeforeAddItem',value))return;
    this.#items.set_Item(tag,value);this.#event('AddItem',value);this.#event('RemoveItem',remove);
  }
  Add(item,value){
    // The original explicit IDictionary.Add ignores its key and uses value.Tag.
    if(arguments.length===2)item=value;
    if(item==null)throw new ArgumentNullException('item');
    if(this.#event('BeforeAddItem',item))throw new ArgumentException('The attribute definition cannot be added to the collection.','item');
    this.#items.Add(item.Tag,item);this.#event('AddItem',item);
  }
  AddRange(collection){if(collection==null)throw new ArgumentNullException('collection');for(const item of collection)this.Add(item);}
  Remove(tag){const output={};if(!this.#items.TryGetValue(tag,output))return false;const remove=output.value;if(this.#event('BeforeRemoveItem',remove))return false;this.#items.Remove(tag);this.#event('RemoveItem',remove);return true;}
  Clear(){for(const tag of Array.from(this.#items.Keys))this.Remove(tag);}
  ContainsTag(tag){return this.#items.ContainsKey(tag);}
  ContainsKey(tag){return this.ContainsTag(tag);}
  ContainsValue(value){return this.#items.ContainsValue(value);}
  TryGetValue(tag,output){return this.#items.TryGetValue(tag,output);}
  // Explicit ICollection<KeyValuePair<...>> adapters; pair objects have Key/Value.
  AddPair(pair){this.Add(pair.Value);}
  RemovePair(pair){if(pair.Value!==this.#items.get_Item(pair.Key))return false;return this.Remove(pair.Key);}
  ContainsPair(pair){const output={};return this.#items.TryGetValue(pair.Key,output)&&(output.value===pair.Value||(output.value?.Equals?.(pair.Value)??false));}
  CopyTo(array,index){this.#items.CopyTo(array,index);}
  GetEnumerator(){return this.#items.GetEnumerator();}
  [Symbol.iterator](){return this.GetEnumerator();}
}
