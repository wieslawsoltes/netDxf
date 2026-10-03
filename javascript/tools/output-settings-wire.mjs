// Test adapters only. No expected results or plot algorithms are implemented here.
import { PaperMargin, PlotSettings, DxfPlotSettingsObject, DxfWipeoutVariables, RasterVariables } from '../index.js';
const reference = value => value === null ? null : {type: value.constructor.name, code: value.CodeName, handle: value.Handle};
export function outputSettingsWire(value, wire) {
  if (value instanceof PaperMargin) return {type:'PaperMargin',left:wire(value.Left),bottom:wire(value.Bottom),right:wire(value.Right),top:wire(value.Top)};
  if (value instanceof PlotSettings) {
    const fields = {};
    for (const key of ['PageSetupName','PlotterName','PaperSizeName','ViewName','CurrentStyleSheet','PaperMargin','PaperSize','Origin',
      'WindowUpRight','WindowBottomLeft','ScaleToFit','PrintScaleNumerator','PrintScaleDenominator','PrintScale','Flags','PlotType',
      'PaperUnits','PaperRotation','ShadePlotMode','ShadePlotResolutionMode','ShadePlotDPI','PaperImageOrigin','StandardScaleType','StandardScaleFactor'])
      fields[key] = wire(value[key]);
    return {type:'PlotSettings',fields,shade:reference(value.ShadePlotObject)};
  }
  if (value instanceof DxfPlotSettingsObject || value instanceof DxfWipeoutVariables) {
    const common = {type:value.constructor.name,code:value.CodeName,handle:value.Handle,owner:reference(value.Owner),erased:value.IsErased,
      xdata:Array.from(value.XData.Values,wire),references:Array.from(value.DatabaseReferences,reference)};
    return value instanceof DxfPlotSettingsObject ? {common,settings:wire(value.Settings)} : {common,displayFrame:value.DisplayFrame};
  }
  if (value instanceof RasterVariables) return {type:'RasterVariables',code:value.CodeName,handle:value.Handle,owner:reference(value.Owner),
    frame:value.DisplayFrame,quality:wire(value.DisplayQuality),units:wire(value.Units),xdata:Array.from(value.XData.Values,wire)};
  return undefined;
}
