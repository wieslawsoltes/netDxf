// Private state shared by original-path ACAD_TABLE partials.
const states = new WeakMap();
export const StoredTableState = owner => states.get(owner);
export const SetStoredTableState = (owner,state) => states.set(owner,state);
