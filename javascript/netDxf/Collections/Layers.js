// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, BindResource, ChangeResource, Listen } from '../../runtime/RegisteredTable.js';
import { LayerStateManager } from './LayerStateManager.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
export class Layers extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.LayerTable, handle, {eventRename:true, valueMembership:true}); this.StateManager = new LayerStateManager(document); }
  BeforeOwner(item) { BindResource(this, item, 'Linetype', this.Owner.Linetypes); }
  AfterOwner(item) { Listen(this, item, 'LinetypeChanged', (sender, e) => ChangeResource(sender, e, this.Owner.Linetypes)); }
  BeforeRemove(item) { this.Owner.Linetypes.References.get_Item(item.Linetype.Name).Remove(item); }
}
