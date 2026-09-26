// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { RegisteredTable, ChangeResource, Listen } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Linetype } from '../Tables/Linetype.js';
import { LinetypeSegmentType } from '../Tables/LinetypeSegmentType.js';
import { FileNotFoundException, NotSupportedException } from '../../runtime/Errors.js';
let deleteFile = null;
export function SetLinetypeTableFileHost(callback) { deleteFile = callback; }
export class Linetypes extends RegisteredTable {
  constructor(document, handle = null) { super(document, DxfObjectCode.LinetypeTable, handle); }
  #table(segment) { return segment.Type === LinetypeSegmentType.Text ? this.Owner.TextStyles : segment.Type === LinetypeSegmentType.Shape ? this.Owner.ShapeStyles : null; }
  #addSegment(item, segment) { const table = this.#table(segment); if (table !== null) { segment.Style = table.Add(segment.Style); table.References.get_Item(segment.Style.Name).Add(item); } }
  BeforeIndex(item) { for (const segment of item.Segments) this.#addSegment(item, segment); }
  AfterOwner(item) {
    Listen(this, item, 'LinetypeSegmentAdded', (sender,e) => this.#addSegment(sender,e.Item));
    Listen(this, item, 'LinetypeSegmentRemoved', (sender,e) => { const table = this.#table(e.Item); if (table !== null) table.References.get_Item(e.Item.Style.Name).Remove(sender); });
    Listen(this, item, 'LinetypeTextSegmentStyleChanged', (sender,e) => ChangeResource(sender,e,this.Owner.TextStyles));
    Listen(this, item, 'LinetypeShapeSegmentStyleChanged', (sender,e) => ChangeResource(sender,e,this.Owner.ShapeStyles));
  }
  BeforeRemove(item) { item.Segments.Remove(Array.from(item.Segments)); }
  #find(file) { const found = this.Owner.SupportFolders.FindFile(file); if (!found) throw new FileNotFoundException('The LIN file has not been found.', file); return found; }
  NamesFromFile(file) { return Linetype.NamesFromFile(this.#find(file)); }
  AddFromFile(file, name, reload) {
    const found = this.#find(file);
    if (typeof name === 'boolean') { for (const value of Linetype.NamesFromFile(found)) this.AddFromFile(found, value, name); return; }
    const item = Linetype.Load(found, name); if (item === null) return false;
    const existing = this.get_Item(item.Name);
    if (existing !== null) { if (!reload) return false; existing.Description = item.Description; existing.Segments.Clear(); existing.Segments.AddRange(item.Segments); return true; }
    this.Add(item); return true;
  }
  Save(file, overwrite) {
    if (overwrite) { if (deleteFile === null) throw new NotSupportedException('Overwriting a LIN file requires a file host.'); deleteFile(file); }
    for (const item of this.Items) if (!item.IsReserved) item.Save(file);
  }
}
