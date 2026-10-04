// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentException, ArgumentOutOfRangeException, InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';
import { TextStyleFlags } from './TextStyleFlags.js';
import { TextStyleFontData } from './TextStyleFontData.js';
import { ApplicationRegistry } from './ApplicationRegistry.js';
import { XData } from '../XData.js';
import { XDataRecord } from '../XDataRecord.js';
import { XDataCode } from '../XDataCode.js';
const prefix = data => data.XDataRecord.Count >= 2 && data.XDataRecord.get_Item(0).Code === XDataCode.String && data.XDataRecord.get_Item(1).Code === XDataCode.Int32;
export function InstallTextStyleFidelity(Type) {
  Object.defineProperties(Type.prototype, {
    Flags: { get() { return this.$flags; }, set(value) {
      if ((value & TextStyleFlags.Shape) !== 0) throw new ArgumentException('Use ShapeStyle for a shape STYLE record.', 'value');
      this.$flags = value;
    } },
    TextGenerationFlags: { get() { return this.$generation; }, set(value) { this.$generation = RequireInteger(value, -32768, 32767); } },
    LastHeight: { get() { return this.$lastHeight; }, set(value) {
      if (value !== null && !Number.isFinite(value)) throw new ArgumentOutOfRangeException('value'); this.$lastHeight = value;
    } },
    ExtendedFontData: { get() {
      const box = {}; if (!this.XData.TryGetValue(ApplicationRegistry.DefaultName, box) || !prefix(box.value)) return null;
      return new TextStyleFontData(box.value.XDataRecord.get_Item(0).Value, box.value.XDataRecord.get_Item(1).Value);
    }, set(value) {
      const box = {};
      if (!this.XData.TryGetValue(ApplicationRegistry.DefaultName, box)) {
        if (value === null) return;
        const data = new XData(new ApplicationRegistry(ApplicationRegistry.DefaultName));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, value.FamilyName));
        data.XDataRecord.Add(new XDataRecord(XDataCode.Int32, value.Flags)); this.XData.Add(data); return;
      }
      const data = box.value;
      if (prefix(data)) {
        if (value === null && data.XDataRecord.Count >= 4 && data.XDataRecord.get_Item(2).Code === XDataCode.String && data.XDataRecord.get_Item(3).Code === XDataCode.Int32)
          throw new InvalidOperationException('Removing this font prefix would expose another font prefix.');
        data.XDataRecord.RemoveAt(0); data.XDataRecord.RemoveAt(0);
      }
      if (value !== null) {
        data.XDataRecord.Insert(0, new XDataRecord(XDataCode.Int32, value.Flags));
        data.XDataRecord.Insert(0, new XDataRecord(XDataCode.String, value.FamilyName));
      }
    } }
  });
  Type.prototype.SetStoredFontFiles = function(font, bigFontFile) { this.$file = font; this.$bigFont = bigFontFile; };
}
