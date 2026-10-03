import { CopyReferenceArray } from '../../runtime/ReferenceArrayCopy.js';
// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { EventHook } from '../../runtime/EventHook.js';
import { EntityCollectionEventArgs } from './EntityCollectionEventArgs.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
/** Preserve the pinned collection's event sequence, including its nonstandard Insert contract. */
export class EntityCollection {
  #items=new ReferenceList();
  constructor(capacity=0){
    RequireInteger(capacity,0,2147483647,'capacity');
    for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])Object.defineProperty(this,event,{value:new EventHook(),enumerable:true});
  }
  get Count(){return this.#items.Count;}
  get IsReadOnly(){return false;}
  get_Item(index){return this.#items.get_Item(index);}
  set_Item(index,value){
    if(value==null)throw new ArgumentNullException('value');
    const remove=this.#items.get_Item(index);
    if(this.OnBeforeRemoveItemEvent(remove)||this.OnBeforeAddItemEvent(value))return;
    this.#items.set_Item(index,value);this.OnAddItemEvent(value);this.OnRemoveItemEvent(remove);
  }
  OnBeforeAddItemEvent(item){const e=new EntityCollectionEventArgs(item);this.BeforeAddItem.Invoke(this,e);return e.Cancel;}
  OnAddItemEvent(item){this.AddItem.Invoke(this,new EntityCollectionEventArgs(item));}
  OnBeforeRemoveItemEvent(item){const e=new EntityCollectionEventArgs(item);this.BeforeRemoveItem.Invoke(this,e);return e.Cancel;}
  OnRemoveItemEvent(item){this.RemoveItem.Invoke(this,new EntityCollectionEventArgs(item));}
  Add(item){if(this.OnBeforeAddItemEvent(item))throw new ArgumentException('The entity cannot be added to the collection.','item');this.#items.Add(item);this.OnAddItemEvent(item);}
  AddRange(collection){if(collection==null)throw new ArgumentNullException('collection');for(const item of collection)this.Add(item);}
  #sourceIndex(index){
    if(!Number.isInteger(index)||index<0||index>=this.#items.Count)throw new ArgumentOutOfRangeException(`The parameter index ${index} must be in between 0 and ${this.#items.Count}.`);
  }
  Insert(index,item){
    this.#sourceIndex(index);
    if(this.OnBeforeRemoveItemEvent(this.#items.get_Item(index)))return;
    if(this.OnBeforeAddItemEvent(item))throw new ArgumentException('The entity cannot be added to the collection.','item');
    // C# raises removal for the current element without deleting it before insertion.
    this.OnRemoveItemEvent(this.#items.get_Item(index));this.#items.Insert(index,item);this.OnAddItemEvent(item);
  }
  Remove(item,enumerable=item!=null&&typeof item[Symbol.iterator]==='function'){
    if(enumerable){if(item==null)throw new ArgumentNullException('items');for(const value of item)this.Remove(value,false);return;}
    if(this.OnBeforeRemoveItemEvent(item))return false;
    const ok=this.#items.Remove(item);if(ok)this.OnRemoveItemEvent(item);return ok;
  }
  RemoveAt(index){this.#sourceIndex(index);const remove=this.#items.get_Item(index);if(this.OnBeforeRemoveItemEvent(remove))return;this.#items.RemoveAt(index);this.OnRemoveItemEvent(remove);}
  Clear(){for(const item of this.#items.ToArray())this.Remove(item,false);}
  IndexOf(item){return this.#items.IndexOf(item);}
  Contains(item){return this.#items.Contains(item);}
  CopyTo(array,arrayIndex){CopyReferenceArray(this.#items,array,arrayIndex);}
  GetEnumerator(){return this.#items.GetEnumerator();}
  [Symbol.iterator](){return this.GetEnumerator();}
  // Original internal transactional SECTION hooks bypass public notifications.
  AddPreparedSection(section){this.#items.Add(section);}
  RemovePreparedSection(section){this.#items.Remove(section);}
}
