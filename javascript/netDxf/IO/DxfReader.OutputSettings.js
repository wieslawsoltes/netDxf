// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Explicit private-method adapters from the pinned DxfReader.OutputSettings.cs.
import { PlotSettings } from '../Objects/PlotSettings.js';
import { DxfPlotSettingsObject, DxfWipeoutVariables } from '../Objects/DxfOutputSettings.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { PayloadEnd, WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, FormatException } from '../../runtime/Errors.js';
const codes = new Set([1,2,4,6,7,40,41,42,43,44,45,46,47,48,49,140,141,142,143,70,72,73,74,75,76,77,78,147,148,149,333]);
const strings = {1:'PageSetupName',2:'PlotterName',4:'PaperSizeName',6:'ViewName',7:'CurrentStyleSheet'};
const scalars = {142:'PrintScaleNumerator',143:'PrintScaleDenominator',70:'Flags',72:'PaperUnits',73:'PaperRotation',74:'PlotType',75:'StandardScaleType',76:'ShadePlotMode',77:'ShadePlotResolutionMode',78:'ShadePlotDPI',147:'StandardScaleFactor'};
export function ParsePlotSettings(context, tags, shadeHandle) {
  shadeHandle.value = null;
  if (!tags.length || tags[0].Code !== 100 || tags[0].Value !== 'AcDbPlotSettings') throw new FormatException('Missing AcDbPlotSettings subclass.');
  const plot = new PlotSettings(), seen = new Set();
  const values = {margin:plot.PaperMargin,paper:plot.PaperSize,origin:plot.Origin,lower:plot.WindowBottomLeft,upper:plot.WindowUpRight,image:plot.PaperImageOrigin};
  const components = {40:['margin','Left'],41:['margin','Bottom'],42:['margin','Right'],43:['margin','Top'],44:['paper','X'],45:['paper','Y'],46:['origin','X'],47:['origin','Y'],48:['lower','X'],49:['lower','Y'],140:['upper','X'],141:['upper','Y'],148:['image','X'],149:['image','Y']};
  try {
    for (let i=1;i<tags.length;i++) {
      const tag=tags[i],code=tag.Code;
      if (!codes.has(code) || seen.has(code)) throw new FormatException('Unsupported or duplicate plot settings group: '+code);
      seen.add(code);
      if (strings[code]) plot[strings[code]]=DecodeDxfText(tag.Value);
      else if (scalars[code]) plot[scalars[code]]=tag.Value;
      else if (components[code]) { const [name,key]=components[code];values[name][key]=tag.Value; }
      else if (code===333) shadeHandle.value=tag.Value;
    }
    plot.PaperMargin=values.margin;plot.PaperSize=values.paper;plot.Origin=values.origin;plot.WindowBottomLeft=values.lower;plot.WindowUpRight=values.upper;plot.PaperImageOrigin=values.image;
    const errors=new ReferenceList();PlotSettings.ValidateValues(plot,errors);
    if(errors.Count)throw new FormatException(Array.from(errors).join('; '));
    return plot;
  } catch(error) { if(error instanceof ArgumentException)throw WrappedFormat('Invalid plot settings field.',error);throw error; }
}
export function ReadOutputSettingsPayload(context, record, type, tags, start) {
  if(type!=='PLOTSETTINGS'&&type!=='WIPEOUTVARIABLES')return false;
  const end=PayloadEnd(tags,start),body=tags.slice(start,end),marker=type==='PLOTSETTINGS'?'AcDbPlotSettings':'AcDbWipeoutVariables';
  if(!body.length||body[0].Code!==100||body[0].Value!==marker)return false;
  if(body.slice(1).some(tag=>type==='PLOTSETTINGS'?!codes.has(tag.Code):tag.Code!==70))return false;
  if(type==='PLOTSETTINGS') {
    const handle={},item=new DxfPlotSettingsObject(ParsePlotSettings(context,body,handle));
    context.outputShadeReferences.push([item.Settings,handle.value]);record.Object=item;
  } else {
    if(body.length>2)throw new FormatException('Duplicate WIPEOUTVARIABLES frame flag.');
    const flag=body.length===2?body[1].Value:0;
    if(flag!==0&&flag!==1)throw new FormatException('WIPEOUTVARIABLES frame flag must be zero or one.');
    record.Object=new DxfWipeoutVariables();record.Object.DisplayFrame=flag===1;
  }
  if(end<tags.length)context.ReadDatabaseXData(record.Object,tags,end);
  return true;
}
export function ResolveOutputSettingsReferences(context) {
  for(const [settings,handle] of context.outputShadeReferences) {
    if(handle==null||handle===''||handle==='0')continue;
    const target=context.Document.GetObjectByHandle(handle);
    if(target===null)throw new FormatException('Unresolved plot-settings shade reference: '+handle);
    try {settings.ShadePlotObject=target;}
    catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid plot-settings shade object.',error);throw error;}
  }
}
