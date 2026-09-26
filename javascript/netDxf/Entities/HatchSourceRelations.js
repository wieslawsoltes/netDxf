// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {ArgumentException} from '../../runtime/Errors.js';
export class HatchSourceRelations {
  static ValidateOwner(hatch,block,document=null){for(const path of hatch.BoundaryPaths)this.ValidatePathOwner(hatch,path,block,document);}
  static ValidatePathOwner(hatch,path,block,document=null){
    if(path.ContainingHatch!==null&&path.ContainingHatch!==hatch)throw new ArgumentException('A boundary path cannot belong to multiple hatches; clone it before reuse.');
    document??=block?.Record?.Owner?.Owner??null;
    for(const entity of path.Entities){
      if(entity==null||entity===hatch||(block!=null&&entity.Owner!=null&&entity.Owner!==block))throw new ArgumentException('HATCH sources must belong to the same block.');
      if(document!=null&&entity.Owner===null)document.ValidateStoredTableEntityAdoption(entity);
    }
  }
}
