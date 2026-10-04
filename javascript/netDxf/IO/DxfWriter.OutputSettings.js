// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { PlotSettings } from '../Objects/PlotSettings.js';
import { DxfPlotSettingsObject, DxfWipeoutVariables } from '../Objects/DxfOutputSettings.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { InvalidOperationException, NullReferenceException } from '../../runtime/Errors.js';
export function ValidateOutputSettings(document) {
  const errors=new ReferenceList(),qualified=document.DrawingVariables.AcadVer>=15;
  if(!qualified)for(const item of document.AddedObjects.Values)if(item instanceof DxfPlotSettingsObject&&item.Settings.ShadePlotObject!==null)
    errors.Add('Plot-settings shade references require the qualified AutoCAD 2007 or later export profile.');
  for(const layout of document.Layouts) {
    PlotSettings.ValidateValues(layout.PlotSettings,errors);
    const shade=layout.PlotSettings?.ShadePlotObject??null;
    if(!qualified&&shade!==null)errors.Add('Layout shade references require the qualified AutoCAD 2007 or later export profile.');
    if(shade!==null&&(shade.Handle===null||document.GetObjectByHandle(shade.Handle)!==shade))errors.Add('A layout shade-plot reference is not registered in this document.');
  }
  if(errors.Count)throw new InvalidOperationException('Invalid plot settings: '+Array.from(errors).join('; '));
}
export function WriteOutputSettingsPayload(chunk,version,item) {
  if(item instanceof DxfPlotSettingsObject)WritePlotSettingsPayload(chunk,version,item.Settings);
  else if(item instanceof DxfWipeoutVariables){chunk.Write(100,'AcDbWipeoutVariables');chunk.Write(70,item.DisplayFrame?1:0);}
  else return false;
  return true;
}
export function WritePlotSettingsPayload(chunk,version,plot) {
  chunk.Write(100,'AcDbPlotSettings');if(plot==null)throw new NullReferenceException();
  // Read each live property at the source Write call, not an eager snapshot.
  const text=(code,key)=>chunk.Write(code,EncodeDxfDatabaseText(plot[key],version));
  text(1,'PageSetupName');text(2,'PlotterName');text(4,'PaperSizeName');text(6,'ViewName');
  chunk.Write(40,plot.PaperMargin.Left);chunk.Write(41,plot.PaperMargin.Bottom);chunk.Write(42,plot.PaperMargin.Right);chunk.Write(43,plot.PaperMargin.Top);
  chunk.Write(44,plot.PaperSize.X);chunk.Write(45,plot.PaperSize.Y);chunk.Write(46,plot.Origin.X);chunk.Write(47,plot.Origin.Y);
  chunk.Write(48,plot.WindowBottomLeft.X);chunk.Write(49,plot.WindowBottomLeft.Y);chunk.Write(140,plot.WindowUpRight.X);chunk.Write(141,plot.WindowUpRight.Y);
  chunk.Write(142,plot.PrintScaleNumerator);chunk.Write(143,plot.PrintScaleDenominator);chunk.Write(70,(plot.Flags<<16)>>16);
  chunk.Write(72,(plot.PaperUnits<<16)>>16);chunk.Write(73,(plot.PaperRotation<<16)>>16);chunk.Write(74,(plot.PlotType<<16)>>16);
  text(7,'CurrentStyleSheet');chunk.Write(75,plot.StandardScaleType);chunk.Write(76,(plot.ShadePlotMode<<16)>>16);chunk.Write(77,(plot.ShadePlotResolutionMode<<16)>>16);chunk.Write(78,plot.ShadePlotDPI);
  chunk.Write(147,plot.StandardScaleFactor??plot.PrintScale);chunk.Write(148,plot.PaperImageOrigin.X);chunk.Write(149,plot.PaperImageOrigin.Y);
  if(plot.ShadePlotObject!==null)chunk.Write(333,plot.ShadePlotObject.Handle);
}
