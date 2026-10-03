// Private state shared only by the source's TABLESTYLE partials.
const states=new WeakMap();
export function SetTableStyleState(owner,state){states.set(owner,state);}
export function TableStyleState(owner){return states.get(owner);}
