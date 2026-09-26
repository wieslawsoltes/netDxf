// Shared private state for the original opaque-entity partials.
const values=new WeakMap();
export function SetOpaqueEntityState(entity,state){values.set(entity,state);}
export function OpaqueEntityState(entity){return values.get(entity);}
// The guard is installed by the optional opaque model after evaluation. Blocks
// must not import its package barrel while their own class is still initializing.
let rejectBlockGeometry=null;
export function InstallOpaqueBlockGuard(guard){rejectBlockGeometry=guard;}
export function RejectOpaqueBlockGeometry(block){rejectBlockGeometry?.(block);}
