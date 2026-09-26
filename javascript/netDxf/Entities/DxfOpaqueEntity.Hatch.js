// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { OpaqueEntityState } from '../../runtime/OpaqueEntityState.js';
import { ValueEquals } from '../../runtime/GenericDictionary.js';
import { InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const same=(a,b)=>a.length===b.length&&a.every((item,i)=>ValueEquals(item,b[i]));
export function InstallOpaqueHatch(Type){
  Object.defineProperty(Type.prototype,'QualifiedReactorIndices',{get(){const state=OpaqueEntityState(this);return state.qualifiedReactors??=new Set();}});
  Type.prototype.ValidateHatchSourceAddition=function(){if(!OpaqueEntityState(this).pending)throw new NotSupportedException('A retained unknown HATCH source cannot acquire a new boundary association.');};
  Type.prototype.ValidateHatchSourceRelease=function(){const s=OpaqueEntityState(this);if(!s.pending&&!s.retired)this.Validate(s.source);};
  Type.prototype.ReleaseHatchSourceBacklink=function(hatch){
    const s=OpaqueEntityState(this);if(s.pending||s.retired)return;
    const expected=s.permittedManagedReactors??s.originalManagedReactors,current=Array.from(this.Reactors),count=current.filter(item=>item===hatch).length,removed=expected.filter(item=>item===hatch).length-count;
    if(removed<0)throw new InvalidOperationException('Unknown HATCH source acquired an unauthorized reactor.');
    const remaining=expected.slice();for(let i=0;i<removed;i++){const at=remaining.findIndex(item=>ValueEquals(item,hatch));if(at>=0)remaining.splice(at,1);}
    if(!same(current,remaining))throw new InvalidOperationException('Unknown HATCH source reactor changes exceed the authorized release.');
    s.permittedManagedReactors=current;if(count!==0)return;
    for(const index of this.QualifiedReactorIndices)if(s.links.get(index)===hatch)s.releasedHatchReactors.add(index);
    s.permittedPersistentReactors=(s.permittedPersistentReactors??s.originalReactors).filter(item=>item!==hatch);
  };
  Type.prototype.ReferencesRemoval=function(removed){
    const s=OpaqueEntityState(this);
    for(const target of this.References){
      if(!removed.has(target))continue;
      if(!(target instanceof api.Hatch)||!Array.from(this.Reactors).some(item=>item===target))return true;
      for(const [index,item] of s.links)if(!s.releasedHatchReactors.has(index)&&item===target&&!this.QualifiedReactorIndices.has(index))return true;
      for(const data of this.XData.Values)for(const tag of data.XDataRecord)if(this.XDataTarget(tag)===target)return true;
    }
    return false;
  };
}
