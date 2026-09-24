// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { IsDatabaseModel } from '../../runtime/DatabaseModel.js';
import { EntityObject } from '../Entities/EntityObject.js';
import { Attribute } from '../Entities/Attribute.js';
import { AttributeDefinition } from '../Entities/AttributeDefinition.js';
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
const states = new WeakMap();
const state = object => {
  if (!states.has(object)) states.set(object, { type: 0, factor: null, shade: null });
  return states.get(object);
};
function validText(text) {
  if (text === null || /[\0\r\n]/.test(text)) return false;
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = text.charCodeAt(++i);
      if (!(next >= 0xdc00 && next <= 0xdfff)) return false;
    } else if (code >= 0xdc00 && code <= 0xdfff) return false;
  }
  return true;
}
export function InstallPlotSettingsFidelity(Type) {
  Object.defineProperties(Type.prototype, {
    StandardScaleType: {
      get() { return state(this).type; },
      set(value) { state(this).type = RequireInteger(value, 0, 32); }
    },
    StandardScaleFactor: {
      get() { return state(this).factor; },
      set(value) {
        if (value !== null && !Number.isFinite(value)) throw new ArgumentOutOfRangeException('value');
        state(this).factor = value;
      }
    },
    ShadePlotObject: {
      get() { return state(this).shade; },
      set(value) {
        if (value instanceof EntityObject || value instanceof Attribute || value instanceof AttributeDefinition || IsDatabaseModel(value,'DxfDocument'))
          throw new ArgumentException('A shade-plot reference must identify a nongraphical object.', 'value');
        state(this).shade = value;
      }
    }
  });
  Type.ValidateValues = (plot, errors) => {
    if (plot === null) { errors.Add('Plot settings cannot be null.'); return; }
    for (const key of ['PageSetupName', 'PlotterName', 'PaperSizeName', 'ViewName', 'CurrentStyleSheet'])
      if (!validText(plot[key])) errors.Add('Plot settings strings must be nonnull single-line Unicode text.');
    const margin = plot.PaperMargin, paper = plot.PaperSize, origin = plot.Origin;
    const bottom = plot.WindowBottomLeft, top = plot.WindowUpRight, image = plot.PaperImageOrigin;
    for (const value of [margin.Left, margin.Bottom, margin.Right, margin.Top, paper.X, paper.Y,
      origin.X, origin.Y, bottom.X, bottom.Y, top.X, top.Y, plot.PrintScaleNumerator,
      plot.PrintScaleDenominator, plot.StandardScaleFactor ?? plot.PrintScale, image.X, image.Y])
      if (!Number.isFinite(value)) errors.Add('Plot settings numeric values must be finite.');
    if (plot.PrintScaleNumerator <= 0 || plot.PrintScaleDenominator <= 0) errors.Add('Plot settings custom scales must be positive.');
    if (plot.PaperUnits < 0 || plot.PaperUnits > 2 || plot.PaperRotation < 0 || plot.PaperRotation > 3 ||
      plot.PlotType < 0 || plot.PlotType > 5 || plot.ShadePlotMode < 0 || plot.ShadePlotMode > 3 ||
      plot.ShadePlotResolutionMode < 0 || plot.ShadePlotResolutionMode > 5 || plot.ShadePlotDPI < 100)
      errors.Add('Invalid plot settings enumeration or shade resolution.');
    if (plot.Flags < -32768 || plot.Flags > 32767) errors.Add('Plot flags must fit their signed 16-bit DXF field.');
  };
}
