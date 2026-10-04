// Shared private storage for the original TABLECONTENT partial classes.
const states = new WeakMap();
export const TableContentState = owner => states.get(owner);
export const SetTableContentState = (owner, state) => states.set(owner, state);
