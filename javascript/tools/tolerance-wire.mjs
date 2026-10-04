import {leaderWire} from './leader-wire.mjs';
// Observation only; both languages serialize values from their own production model.
import {Tolerance,ToleranceEntry,ToleranceValue,DatumReferenceValue} from '../index.js';
export function toleranceValueWire(value,wire){
  if(value instanceof ToleranceEntry)return {type:'ToleranceEntry',symbol:value.GeometricSymbol,tolerance1:wire(value.Tolerance1),tolerance2:wire(value.Tolerance2),datum1:wire(value.Datum1),datum2:wire(value.Datum2),datum3:wire(value.Datum3)};
  if(value instanceof ToleranceValue)return {type:'ToleranceValue',diameter:value.ShowDiameterSymbol,text:wire(value.Value),material:value.MaterialCondition};
  if(value instanceof DatumReferenceValue)return {type:'DatumReferenceValue',text:wire(value.Value),material:value.MaterialCondition};
}
export function toleranceWire(value,wire){
  const leader=leaderWire(value,wire);if(leader!==undefined)return leader;
  const detail=toleranceValueWire(value,wire);if(detail!==undefined)return detail;
  if(!(value instanceof Tolerance))return;
  const common={type:value.constructor.name,kind:value.Type,code:value.CodeName,handle:value.Handle,
    owner:value.Owner?.CodeName??null,color:wire(value.Color),layer:wire(value.Layer),linetype:wire(value.Linetype),lineweight:value.Lineweight,
    transparency:wire(value.Transparency),linetypeScale:wire(value.LinetypeScale),normal:wire(value.Normal),visible:value.IsVisible,
    colorName:value.ColorName,shadow:wire(value.ShadowMode),proxy:wire(value.ProxyGraphics),
    reactors:Array.from(value.Reactors,r=>r===null?null:{code:r.CodeName,handle:r.Handle}),xdata:Array.from(value.XData.Values,wire)};
  return {common,entry1:wire(value.Entry1),entry2:wire(value.Entry2),position:wire(value.Position),rotation:wire(value.Rotation),height:wire(value.TextHeight),style:wire(value.Style),projected:wire(value.ProjectedToleranceZoneValue),show:value.ShowProjectedToleranceZoneSymbol,datum:wire(value.DatumIdentifier)};
}
