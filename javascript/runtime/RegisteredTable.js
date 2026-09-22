// Shared implementation for original-path registered table collections.
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObjects } from '../netDxf/Collections/TableObjects.js';
import { DxfObjectReferences } from '../netDxf/Collections/DxfObjectReferences.js';
import { ArgumentException } from './Errors.js';
const subscriptions = new WeakMap();
export function Listen(owner, item, event, handler) {
  let items = subscriptions.get(owner); if (!items) subscriptions.set(owner, items = new WeakMap());
  let hooks = items.get(item); if (!hooks) items.set(item, hooks = []);
  item[event].Add(handler); hooks.push([event, handler]);
}
export function Unlisten(owner, item) {
  const hooks = subscriptions.get(owner)?.get(item) ?? [];
  for (const [event, handler] of hooks) item[event].Remove(handler);
  subscriptions.get(owner)?.delete(item);
}
export function BindResource(owner, item, property, table, assignHandle = true) {
  if (item[property] === null) return;
  item[property] = table.Add(item[property], assignHandle);
  table.References.get_Item(item[property].Name).Add(item);
}
export function ChangeResource(item, event, table) {
  if (event.OldValue !== null) table.References.get_Item(event.OldValue.Name).Remove(item);
  if (event.NewValue !== null) {
    event.NewValue = table.Add(event.NewValue);
    table.References.get_Item(event.NewValue.Name).Add(item);
  }
}
/** Common implementation; subclasses supply source-specific registration side effects. */
export class RegisteredTable extends TableObjects {
  constructor(document, codeName, handle, options = {}) { super(document, codeName, handle); this.RegistrationOptions = options; }
  AddRecord(item, assignHandle = true) {
    const existing = this.get_Item(item.Name); if (existing !== null) return existing;
    if (this.RegistrationOptions.cloneForeign && item.Owner !== null && item.Owner !== this) item = item.CloneStoredGraph();
    if (this.RegistrationOptions.rejectForeign && item.Owner !== null && item.Owner !== this)
      throw new ArgumentException('Clone the table record before moving it between documents.', this.RegistrationOptions.parameter);
    this.ValidateIncoming?.(item);
    if (assignHandle || !item.Handle) this.Owner.NumHandles = item.AssignHandle(this.Owner.NumHandles);
    this.BeforeIndex?.(item, assignHandle);
    this.List.Add(item.Name, item); this.References.Add(item.Name, new DxfObjectReferences(!!this.RegistrationOptions.identityReferences));
    this.BeforeOwner?.(item, assignHandle);
    item.Owner = this;
    if (this.RegistrationOptions.eventRename) Listen(this, item, 'NameChanged', (sender, e) => this.RenameFromEvent(sender, e));
    this.AfterOwner?.(item, assignHandle);
    this.Owner.AddedObjects.Add(item.Handle, item);
    this.AfterRegister?.(item, assignHandle);
    return item;
  }
  Remove(value) {
    const item = typeof value === 'string' ? this.get_Item(value) : value;
    if (item == null) return false;
    if (this.RegistrationOptions.valueMembership ? !this.Contains(item) : item.Owner !== this || this.get_Item(item.Name) !== item) return false;
    if (item.IsReserved || this.HasReferences(item) || this.CannotRemove?.(item)) return false;
    this.BeforeRemove?.(item);
    this.Owner.AddedObjects.Remove(item.Handle); this.References.Remove(item.Name); this.List.Remove(item.Name);
    item.Handle = null; item.Owner = null; Unlisten(this, item); return true;
  }
  ValidateRecordRename(item, name) {
    const other = this.get_Item(name);
    if (other !== null && other !== item) throw new ArgumentException('There is already another table record with the same name.');
  }
  CommitRecordRename(item, name) {
    const references = this.References.get_Item(item.Name);
    this.List.Remove(item.Name); this.References.Remove(item.Name);
    this.List.Add(name, item); this.References.Add(name, references);
  }
  ValidateMLeaderResourceRename(item, name) { this.ValidateRecordRename(item, name); }
  CommitMLeaderResourceRename(item, name) { this.CommitRecordRename(item, name); }
  RenameFromEvent(item, e) {
    if (this.Contains(e.NewValue)) throw new ArgumentException('There is already another table record with the same name.');
    this.List.Remove(item.Name); this.List.Add(e.NewValue, item);
    const references = this.GetReferences(item.Name);
    this.References.Remove(item.Name); this.References.Add(e.NewValue, new DxfObjectReferences());
    this.References.get_Item(e.NewValue).Add(references, true);
  }
}
