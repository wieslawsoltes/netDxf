// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { LayerState } from '../Objects/LayerState.js';
import { LayerStateProperties } from '../Objects/LayerStateProperties.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { ArgumentException } from '../../runtime/Errors.js';
export class LayerStateManager extends RegisteredTable {
  constructor(document, handle = null) { super(document,'ACAD_LAYERSTATES',handle,{eventRename:true,valueMembership:true}); this.Options=2047; }
  AddNew(name, description='') { const state=new LayerState(name,this.Owner.Layers);state.Description=description;this.Add(state); }
  Restore(name) {
    const state=this.List.get_Item(name); if(state===null)throw new ArgumentException('Invalid layer state name.','layerStateName');
    this.Owner.DrawingVariables.CLayer=state.CurrentLayer;
    for(const property of state.Properties.Values)property.CopyTo(this.Owner.Layers.get_Item(property.Name) ?? this.Owner.Layers.Add(new Layer(property.Name)),this.Options);
  }
  Update(name) {
    const state=this.List.get_Item(name); if(state===null)throw new ArgumentException('Invalid layer state name.','layerStateName');
    state.CurrentLayer=this.Owner.DrawingVariables.CLayer;
    for(const layer of this.Owner.Layers.Items) {
      if(state.Properties.ContainsKey(layer.Name))state.Properties.get_Item(layer.Name).CopyFrom(layer,this.Options);
      else state.Properties.Add(layer.Name,new LayerStateProperties(layer));
    }
  }
  Import(file, overwrite) {
    const state=LayerState.Load(file);if(state===null)throw new Error('Unknown error when loading the LAS file: '+file);
    if(this.List.ContainsKey(state.Name)){if(overwrite){this.Remove(this.List.get_Item(state.Name));this.Add(state);}}else this.Add(state);
    this.Restore(state.Name);
  }
  Export(file, name) { const state=this.List.get_Item(name);if(state===null)throw new ArgumentException('Invalid layer state name.','layerStateName');state.Save(file); }
  RemoveAll() { for(const name of Array.from(this.Names))this.Remove(name); }
  AfterRegister(state) {
    for(const property of state.Properties.Values){
      if(!this.Owner.Layers.Contains(property.Name))this.Owner.Layers.Add(new Layer(property.Name));
      if(!this.Owner.Linetypes.Contains(property.LinetypeName))property.LinetypeName=Linetype.DefaultName;
    }
  }
}
