// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObjectDatabase } from './Objects/DxfObjectDatabase.js';
export function InstallDocumentObjects(Type){
  const databases=new WeakMap();
  Object.defineProperties(Type.prototype,{
    Objects:{get(){if(!databases.has(this))databases.set(this,new DxfObjectDatabase(this));return databases.get(this);}},
    NamedObjects:{get(){return this.Objects.Root;}}
  });
}
