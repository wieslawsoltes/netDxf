// Private state shared by the source's CELLSTYLEMAP partials.
const states=new WeakMap();
export function SetCellStyleMapState(owner,state){states.set(owner,state);}
export function CellStyleMapState(owner){return states.get(owner);}
