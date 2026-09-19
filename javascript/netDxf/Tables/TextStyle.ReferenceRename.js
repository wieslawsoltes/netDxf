// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentException } from '../../runtime/Errors.js';
import { NativeString } from '../../runtime/GeometryRuntime.js';
import { TableObject } from './TableObject.js';
export function InstallTextStyleReferenceRename(Type) {
  Type.prototype.OnNameChangedEvent = function(oldName, newName) {
    if (NativeString.IsNullOrWhiteSpace(newName)) throw new ArgumentException('A TextStyle name cannot be blank.', 'newName');
    if (this.Owner !== null) this.Owner.ValidateMLeaderResourceRename(this, newName);
    TableObject.prototype.OnNameChangedEvent.call(this, oldName, newName);
    if (this.Owner !== null) { this.Owner.ValidateMLeaderResourceRename(this, newName); this.Owner.CommitMLeaderResourceRename(this, newName); }
  };
}
