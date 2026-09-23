// Shared private state for the original-path TABLEGEOMETRY partials.
const states = new WeakMap();
export const TableGeometryState = value => states.get(value);
export const SetTableGeometryState = (value, state) => states.set(value, state);
