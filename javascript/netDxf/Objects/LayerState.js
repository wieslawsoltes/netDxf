// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { ObservableDictionary } from '../Collections/ObservableDictionary.js';
import { LayerStateProperties } from './LayerStateProperties.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { AciColor } from '../AciColor.js';
import { Transparency } from '../Transparency.js';
import { ArgumentException, ArgumentNullException, NullReferenceException, NotSupportedException, FormatException } from '../../runtime/Errors.js';
let fileHost = null;
export function SetLayerStateFileHost(host) { fileHost = host; }
const requireHost = () => { if (!fileHost) throw new NotSupportedException('Layer-state file operations require an explicit host.'); return fileHost; };
export class LayerState extends TableObject {
  #description = ''; #currentLayer = Layer.DefaultName; #properties;
  constructor(name, layers = []) {
    super(name, 'ACAD_LAYERSTATES', true); this.PaperSpace = false;
    this.#properties = new ObservableDictionary();
    this.#properties.BeforeAddItem.Add((_, e) => {
      if (e.Item.Value == null) e.Cancel = true;
      else if (this.Owner !== null) {
        const doc = this.Owner.Owner;
        if (!doc.Layers.Contains(e.Item.Key) || !doc.Linetypes.Contains(e.Item.Value.LinetypeName)) e.Cancel = true;
      } else e.Cancel = false;
    });
    if (layers == null) throw new ArgumentNullException('layers');
    for (const layer of layers) {
      if (layer == null) throw new NullReferenceException();
      const property = new LayerStateProperties(layer); this.Properties.Add(property.Name, property);
    }
  }
  get Description() { return this.#description; }
  set Description(value) { this.#description = value ?? ''; }
  get CurrentLayer() { return this.#currentLayer; }
  set CurrentLayer(value) {
    if (value == null || value === '') throw new ArgumentNullException('value');
    if (this.Owner !== null && !this.Owner.Owner.Layers.Contains(value)) throw new ArgumentException('The current layer does not exist in the owner document.', 'value');
    this.#currentLayer = value;
  }
  get Properties() { return this.#properties; }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const result = new LayerState(newName); result.Description = this.Description; result.CurrentLayer = this.CurrentLayer;
    for (const item of this.Properties.Values) { const copy = item.Clone(); result.Properties.Add(copy.Name, copy); }
    return result; // Source deliberately resets PaperSpace and does not clone XData.
  }
  static Load(file) {
    const input = requireHost().OpenRead(file); // Opening errors propagate, parsing errors do not.
    try { return LayerState.LoadText(input.ReadAll()); } catch { return null; } finally { input.Close(); }
  }
  Save(file) {
    const output = requireHost().Create(file);
    try { output.Write(this.ToLasString(output.NewLine ?? '\n')); return true; } catch { return false; } finally { output.Close(); }
  }
  /** Portable LAS serialization; explicit host supplies file access and line endings. */
  ToLasString(newLine = '\n') {
    const lines = [], write = (code, value) => { lines.push(String(code), typeof value === 'boolean' ? (value ? '1':'0') : String(value ?? '')); };
    write(0,'LAYERSTATEDICTIONARY'); write(0,'LAYERSTATE'); write(1,this.Name); write(91,2047);
    write(301,this.Description); write(290,this.PaperSpace); write(302,this.CurrentLayer);
    for (const lp of this.Properties.Values) {
      if (lp == null || lp.Color == null || lp.Transparency == null) throw new NullReferenceException();
      write(8,lp.Name); write(90,lp.Flags); write(62,lp.Color.Index); write(370,lp.Lineweight); write(6,lp.LinetypeName);
      write(440,lp.Transparency.StoredAlphaValue ?? (lp.Transparency.Value === 0 ? 0 : Transparency.ToAlphaValue(lp.Transparency)));
      if (lp.Color.UseTrueColor) write(92,AciColor.ToTrueColor(lp.Color));
    }
    return lines.join(newLine) + newLine;
  }
  /** Portable LAS text input; validates paired records and bounded integer fields. */
  static LoadText(text) {
    if (text == null) throw new ArgumentNullException('text');
    const lines = text.replace(/^\ufeff/, '').split(/\r\n|\n|\r/); if (lines.at(-1) === '') lines.pop();
    if (lines.length % 2) throw new FormatException('Incomplete LAS code/value pair.');
    const number = (text, min=-2147483648, max=2147483647) => {
      if (!/^\s*[+-]?\d+\s*$/.test(text)) throw new FormatException('Invalid LAS integer.');
      const value = Number(text); if (!Number.isInteger(value) || value < min || value > max) throw new FormatException('LAS integer out of range.'); return value;
    };
    const pairs=[]; for(let i=0;i<lines.length;i+=2) pairs.push([number(lines[i],0,1071),lines[i+1]]);
    pairs.push([0,'EOF']); let at=0;
    if (pairs[at][0] !== 0 || pairs[at++][1] !== 'LAYERSTATEDICTIONARY' || pairs[at][0] !== 0 || pairs[at++][1] !== 'LAYERSTATE') throw new FormatException('Invalid LAS header.');
    let name='',description='',current=Layer.DefaultName,paper=false; const properties=[];
    while(pairs[at][0] !== 0) {
      const [code,value]=pairs[at++];
      if(code===1)name=value; else if(code===301)description=value; else if(code===302)current=value;
      else if(code===290)paper=number(value,0,255)!==0;
      else if(code===8) {
        const property=new LayerStateProperties(value);
        while(pairs[at][0]!==0 && pairs[at][0]!==8) {
          const [c,v]=pairs[at++];
          if(c===90)property.Flags=number(v); else if(c===62)property.Color=AciColor.FromCadIndex(number(v,-32768,32767));
          else if(c===370)property.Lineweight=number(v,-32768,32767); else if(c===6)property.LinetypeName=v;
          else if(c===440){const alpha=number(v);property.Transparency=alpha===0?new Transparency(0,alpha):Transparency.FromAlphaValue(alpha);}
          else if(c===92)property.Color=AciColor.FromTrueColor(number(v));
        }
        properties.push(property);
      }
    }
    const result=new LayerState(name);result.Description=description;result.CurrentLayer=current;result.PaperSpace=paper;
    for(const property of properties)if(!result.Properties.ContainsKey(property.Name))result.Properties.Add(property.Name,property);
    return result;
  }
}
