// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
export class DrawingEntities {
  #document; #activeLayout='Model';
  constructor(document){this.#document=document;}
  get ActiveLayout(){return this.#activeLayout;}
  set ActiveLayout(value){if(!this.#document.Layouts.Contains(value))throw new ArgumentException('The layout '+value+' does not exist.','value');this.#activeLayout=value;}
  get All(){const layout=this.#document.Layouts.get_Item(this.#activeLayout);if(layout===null)throw new NullReferenceException();return layout.AssociatedBlock.Entities;}
  #ofType(name){const entities=this.All;return{*[Symbol.iterator](){for(const e of entities)if(api[name]&&e instanceof api[name])yield e;}};}
  Add(entity,enumerable=entity!=null&&typeof entity[Symbol.iterator]==='function'){
    if(enumerable){if(entity==null)throw new ArgumentNullException('entities');for(const item of entity)this.Add(item,false);return;}
    if(entity==null)throw new NullReferenceException();
    if(entity.Owner!==null)throw new ArgumentException('The entity already belongs to a document. Clone it instead.','entity');
    this.#document.ValidateStoredTableEntityAdoption(entity);
    this.All.Add(entity);
  }
  Remove(entity,enumerable=entity!=null&&typeof entity[Symbol.iterator]==='function'){
    if(enumerable){if(entity==null)throw new ArgumentNullException('entities');for(const item of entity)this.Remove(item,false);return;}
    if(entity==null||entity.Owner===null||entity.Owner.Record.Layout===null)return false;
    if(entity.Owner.Owner.Owner.Owner!==this.#document)return false;
    return this.#document.Blocks.get_Item(entity.Owner.Name).Entities.Remove(entity);
  }
  get Arcs(){return this.#ofType('Arc');}
  get Ellipses(){return this.#ofType('Ellipse');}
  get Circles(){return this.#ofType('Circle');}
  get Faces3D(){return this.#ofType('Face3D');}
  get Solids(){return this.#ofType('Solid');}
  get Traces(){return this.#ofType('Trace');}
  get Inserts(){return this.#ofType('Insert');}
  get Lines(){return this.#ofType('Line');}
  get Shapes(){return this.#ofType('Shape');}
  get Polylines2D(){return this.#ofType('Polyline2D');}
  get Polylines3D(){return this.#ofType('Polyline3D');}
  get PolyfaceMeshes(){return this.#ofType('PolyfaceMesh');}
  get PolygonMeshes(){return this.#ofType('PolygonMesh');}
  get Points(){return this.#ofType('Point');}
  get Texts(){return this.#ofType('Text');}
  get MTexts(){return this.#ofType('MText');}
  get Hatches(){return this.#ofType('Hatch');}
  get Images(){return this.#ofType('Image');}
  get Meshes(){return this.#ofType('Mesh');}
  get Leaders(){return this.#ofType('Leader');}
  get Tolerances(){return this.#ofType('Tolerance');}
  get Underlays(){return this.#ofType('Underlay');}
  get MLines(){return this.#ofType('MLine');}
  get Dimensions(){return this.#ofType('Dimension');}
  get Helices(){return this.#ofType('Helix');}
  get Bodies(){return this.#ofType('Body');}
  get Regions(){return this.#ofType('Region');}
  get Solids3D(){return this.#ofType('Solid3D');}
  get OleFrames(){return this.#ofType('OleFrame');}
  get Ole2Frames(){return this.#ofType('Ole2Frame');}
  get StoredTables(){return this.#ofType('StoredTable');}
  get OpaqueEntities(){return this.#ofType('DxfOpaqueEntity');}
  get Sections(){return this.#ofType('Section');}
  get MultiLeaders(){return this.#ofType('MultiLeader');}
  get Lights(){return this.#ofType('Light');}
  get Splines(){return this.#ofType('Spline');}
  get Rays(){return this.#ofType('Ray');}
  get Viewports(){return this.#ofType('Viewport');}
  get XLines(){return this.#ofType('XLine');}
  get Wipeouts(){return this.#ofType('Wipeout');}
}
