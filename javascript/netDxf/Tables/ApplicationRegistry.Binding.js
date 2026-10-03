// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// State shared only with ApplicationRegistry.cs's matching JavaScript partial implementation.
const bindings = new WeakMap();
function state(registry) { let value = bindings.get(registry); if (!value) bindings.set(registry, value = new Set()); return value; }
export function AttachXData(registry, dictionary) { state(registry).add(dictionary); }
export function DetachXData(registry, dictionary) { state(registry).delete(dictionary); }
function ValidateBindingRename(registry, newName) {
  registry.Owner?.ValidateRecordRename(registry, newName);
  for (const dictionary of state(registry)) dictionary.ValidateApplicationRegistryRename(registry, newName);
}
export function OnNameChangedEvent(registry, oldName, newName, notify) {
  ValidateBindingRename(registry, newName); notify(oldName, newName);
  ValidateBindingRename(registry, newName);
  const current = Array.from(state(registry));
  registry.Owner?.CommitRecordRename(registry, newName);
  for (const dictionary of current) dictionary.CommitApplicationRegistryRename(registry, newName);
}
