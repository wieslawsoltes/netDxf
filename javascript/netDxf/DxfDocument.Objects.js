// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObjectDatabase } from './Objects/DxfObjectDatabase.js';
const databases=new WeakMap();
// Explicit internal-state observation, equivalent to the source private field;
// reading it never allocates a database and it is not exported by the package barrel.
export const PeekDocumentObjects=document=>databases.get(document)??null;
export function InstallDocumentObjects(Type){
  Object.defineProperties(Type.prototype,{
    Objects:{get(){if(!databases.has(this))databases.set(this,new DxfObjectDatabase(this));return databases.get(this);}},
    NamedObjects:{get(){return this.Objects.Root;}}
  });
}
