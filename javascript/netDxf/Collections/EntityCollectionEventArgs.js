// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
export class EntityCollectionEventArgs {
  Cancel=false;
  constructor(item) { Object.defineProperty(this,'Item',{value:item,enumerable:true}); }
}
