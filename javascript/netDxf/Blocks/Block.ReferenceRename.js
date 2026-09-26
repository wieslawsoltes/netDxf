// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { NativeString } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
export function InstallBlockReferenceRename(Type) {
  Type.prototype.OnNameChangedEvent=function(oldName,newName){
    if(NativeString.IsNullOrWhiteSpace(newName))throw new ArgumentException('A Block name cannot be blank.','newName');
    if(this.Record.Owner!==null)this.Record.Owner.ValidateMLeaderResourceRename(this,newName);
    TableObject.prototype.OnNameChangedEvent.call(this,oldName,newName);
    // Ownership may change in a callback. Re-read it before committing indexes.
    if(this.Record.Owner!==null){this.Record.Owner.ValidateMLeaderResourceRename(this,newName);this.Record.Owner.CommitMLeaderResourceRename(this,newName);}
    this.Record.Name=newName;
  };
}
