// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { PaperMargin } from './PaperMargin.js';
import { Vector2 } from '../Vector2.js';
import { PlotFlags } from './PlotFlags.js';
import { PlotType } from './PlotType.js';
import { PlotPaperUnits } from './PlotPaperUnits.js';
import { PlotRotation } from './PlotRotation.js';
import { ShadePlotMode } from './ShadePlotMode.js';
import { ShadePlotResolutionMode } from './ShadePlotResolutionMode.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
import { InstallPlotSettingsFidelity } from './PlotSettings.Fidelity.js';
export class PlotSettings {
  PageSetupName = ''; PlotterName = 'none_device'; PaperSizeName = 'ISO_A4_(210.00_x_297.00_MM)';
  ViewName = ''; CurrentStyleSheet = '';
  Flags = PlotFlags.DrawViewportsFirst | PlotFlags.PrintLineweights | PlotFlags.PlotPlotStyles | PlotFlags.UseStandardScale;
  PlotType = PlotType.DrawingExtents; PaperUnits = PlotPaperUnits.Milimeters; PaperRotation = PlotRotation.Degrees90;
  ShadePlotMode = ShadePlotMode.AsDisplayed; ShadePlotResolutionMode = ShadePlotResolutionMode.Normal;
  #margin = new PaperMargin(7.5, 20, 7.5, 20); #paper = new Vector2(210, 297); #origin = Vector2.Zero;
  #up = Vector2.Zero; #bottom = Vector2.Zero; #image = Vector2.Zero; #dpi = 300;
  #scalars = new DataView(new ArrayBuffer(16));
  constructor() { this.#scalars.setFloat64(0, 1); this.#scalars.setFloat64(8, 1); }
  get PaperMargin() { return Copy(this.#margin); } set PaperMargin(value) { this.#margin = Copy(value); }
  get PaperSize() { return Copy(this.#paper); } set PaperSize(value) { this.#paper = Copy(value); }
  get Origin() { return Copy(this.#origin); } set Origin(value) { this.#origin = Copy(value); }
  get WindowUpRight() { return Copy(this.#up); } set WindowUpRight(value) { this.#up = Copy(value); }
  get WindowBottomLeft() { return Copy(this.#bottom); } set WindowBottomLeft(value) { this.#bottom = Copy(value); }
  get PaperImageOrigin() { return Copy(this.#image); } set PaperImageOrigin(value) { this.#image = Copy(value); }
  get ScaleToFit() { return this.StandardScaleType === 0; }
  set ScaleToFit(value) { if (value) this.StandardScaleType = 0; else if (this.StandardScaleType === 0) this.StandardScaleType = 16; }
  get PrintScaleNumerator() { return this.#scalars.getFloat64(0); }
  set PrintScaleNumerator(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#scalars.setFloat64(0, value); }
  get PrintScaleDenominator() { return this.#scalars.getFloat64(8); }
  set PrintScaleDenominator(value) { if (value <= 0) throw new ArgumentOutOfRangeException('value', value); this.#scalars.setFloat64(8, value); }
  get PrintScale() { return this.PrintScaleNumerator / this.PrintScaleDenominator; }
  get ShadePlotDPI() { return this.#dpi; } set ShadePlotDPI(value) { this.#dpi = RequireInteger(value, 100, 32767); }
  Clone() {
    const copy = new PlotSettings();
    // Preserve source assignment order and intentionally share the shade reference.
    for (const key of ['PageSetupName', 'PlotterName', 'PaperSizeName', 'ViewName', 'CurrentStyleSheet', 'PaperMargin',
      'PaperSize', 'Origin', 'WindowUpRight', 'WindowBottomLeft', 'StandardScaleType', 'StandardScaleFactor', 'ShadePlotObject',
      'PrintScaleNumerator', 'PrintScaleDenominator', 'Flags', 'PlotType', 'PaperUnits', 'PaperRotation', 'ShadePlotMode',
      'ShadePlotResolutionMode', 'ShadePlotDPI', 'PaperImageOrigin']) copy[key] = this[key];
    return copy;
  }
}
InstallPlotSettingsFidelity(PlotSettings);
