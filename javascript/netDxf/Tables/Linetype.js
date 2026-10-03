// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ObservableCollection } from '../Collections/ObservableCollection.js';
import { LinetypeSimpleSegment } from './LinetypeSimpleSegment.js';
import { LinetypeTextSegment } from './LinetypeTextSegment.js';
import { LinetypeShapeSegment } from './LinetypeShapeSegment.js';
import { LinetypeSegmentType } from './LinetypeSegmentType.js';
import { LinetypeSegmentRotationType } from './LinetypeSegmentRotationType.js';
import { LinetypeSegmentChangeEventArgs } from './LinetypeSegmentChangeEventArgs.js';
import { TableObjectChangedEventArgs } from './TableObjectChangedEventArgs.js';
import { TextStyle } from './TextStyle.js';
import { ShapeStyle } from './ShapeStyle.js';
import { Vector2 } from '../Vector2.js';
import { MathHelper } from '../MathHelper.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { EventHook } from '../../runtime/EventHook.js';
import { Culture, NumberText } from '../../runtime/GeometryRuntime.js';
import { ParseInvariantFloat, TrimDotNet, InvariantIgnoreCaseEquals } from '../../runtime/InvariantFloat.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { PathExtension, PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { PatternFileSystem } from '../../runtime/PatternFileSystem.js';
import { StringReader } from '../../runtime/StringReader.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, FileLoadException, FormatException, IndexOutOfRangeException } from '../../runtime/Errors.js';
import { InstallLinetypeReferenceRename } from './Linetype.ReferenceRename.js';
function header(line) {
  const end = line.indexOf(','); if (end < 1) throw new ArgumentOutOfRangeException('length');
  return [line.slice(1, end), TrimDotNet(line.slice(end + 1))];
}
const number = value => NumberText(value, Culture.Invariant);
export class Linetype extends TableObject {
  $description; $segments;
  $textStyleChanged = (sender, e) => { e.NewValue = this.OnLinetypeTextSegmentStyleChangedEvent(e.OldValue, e.NewValue); };
  $shapeStyleChanged = (sender, e) => { e.NewValue = this.OnLinetypeShapeSegmentStyleChangedEvent(e.OldValue, e.NewValue); };
  static get ByLayerName() { return 'ByLayer'; }
  static get ByBlockName() { return 'ByBlock'; }
  static get DefaultName() { return 'Continuous'; }
  static get ByLayer() { return new Linetype(Linetype.ByLayerName); }
  static get ByBlock() { return new Linetype(Linetype.ByBlockName); }
  static get Continuous() { return new Linetype(Linetype.DefaultName, 'Solid line'); }
  static get Center() { return new Linetype('Center', [1.25,-0.25,0.25,-0.25].map(n => new LinetypeSimpleSegment(n)), 'Center, ____ _ ____ _ ____ _ ____ _ ____ _ ____'); }
  static get DashDot() { return new Linetype('Dashdot', [0.5,-0.25,0,-0.25].map(n => new LinetypeSimpleSegment(n)), 'Dash dot, __ . __ . __ . __ . __ . __ . __ . __'); }
  static get Dashed() { return new Linetype('Dashed', [0.5,-0.25].map(n => new LinetypeSimpleSegment(n)), 'Dashed, __ __ __ __ __ __ __ __ __ __ __ __ __ _'); }
  static get Dot() { return new Linetype('Dot', [0,-0.25].map(n => new LinetypeSimpleSegment(n)), 'Dot, . . . . . . . . . . . . . . . . . . . . . . . .'); }
  constructor(name, segmentsOrDescription = null, description = '', checkName = true) {
    super(name, DxfObjectCode.Linetype, checkName);
    if (name == null || name === '') throw new ArgumentNullException('name');
    const segments = typeof segmentsOrDescription === 'string' ? null : segmentsOrDescription;
    if (typeof segmentsOrDescription === 'string') description = segmentsOrDescription;
    this.IsReserved = [Linetype.ByLayerName,Linetype.ByBlockName,Linetype.DefaultName].some(value => OrdinalIgnoreCaseEquals(name, value));
    this.$description = description == null || description === '' ? '' : description;
    for (const event of ['LinetypeSegmentAdded','LinetypeSegmentRemoved','LinetypeTextSegmentStyleChanged','LinetypeShapeSegmentStyleChanged'])
      Object.defineProperty(this, event, { value: new EventHook(), enumerable: true });
    this.$segments = new ObservableCollection();
    this.$segments.BeforeAddItem.Add((sender, e) => { e.Cancel = e.Item === null; });
    this.$segments.AddItem.Add((sender, e) => {
      this.OnLinetypeSegmentAddedEvent(e.Item);
      if (e.Item.Type === LinetypeSegmentType.Text) e.Item.TextStyleChanged.Add(this.$textStyleChanged);
      if (e.Item.Type === LinetypeSegmentType.Shape) e.Item.ShapeStyleChanged.Add(this.$shapeStyleChanged);
    });
    this.$segments.RemoveItem.Add((sender, e) => {
      this.OnLinetypeSegmentRemovedEvent(e.Item);
      if (e.Item.Type === LinetypeSegmentType.Text) e.Item.TextStyleChanged.Remove(this.$textStyleChanged);
      if (e.Item.Type === LinetypeSegmentType.Shape) e.Item.ShapeStyleChanged.Remove(this.$shapeStyleChanged);
    });
    if (segments !== null) this.$segments.AddRange(segments);
  }
  OnLinetypeSegmentAddedEvent(item) { this.LinetypeSegmentAdded.Invoke(this, new LinetypeSegmentChangeEventArgs(item)); }
  OnLinetypeSegmentRemovedEvent(item) { this.LinetypeSegmentRemoved.Invoke(this, new LinetypeSegmentChangeEventArgs(item)); }
  OnLinetypeTextSegmentStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.LinetypeTextSegmentStyleChanged.Invoke(this, e); return e.NewValue;
  }
  OnLinetypeShapeSegmentStyleChangedEvent(oldValue, newValue) {
    const e = new TableObjectChangedEventArgs(oldValue, newValue); this.LinetypeShapeSegmentStyleChanged.Invoke(this, e); return e.NewValue;
  }
  get IsByLayer() { return InvariantIgnoreCaseEquals(this.Name, Linetype.ByLayerName); }
  get IsByBlock() { return InvariantIgnoreCaseEquals(this.Name, Linetype.ByBlockName); }
  get Description() { return this.$description; }
  set Description(value) { this.$description = value == null || value === '' ? '' : value; }
  get Segments() { return this.$segments; }
  Length() { let length = 0; for (const segment of this.$segments) length += Math.abs(segment.Length); return length; }
  static $file(file) {
    if (file == null || file === '') throw new ArgumentNullException('file');
    if (!InvariantIgnoreCaseEquals(PathExtension(file), '.LIN')) throw new ArgumentException('The linetype definitions file must have the extension LIN.', 'file');
  }
  static NamesFromFile(file) { Linetype.$file(file); return Linetype.NamesFromText(PatternFileSystem.ReadAllText(file)); }
  static Load(file, linetypeName) {
    Linetype.$file(file); if (linetypeName == null || linetypeName === '') return null;
    return Linetype.LoadText(PatternFileSystem.ReadAllText(file), linetypeName, file);
  }
  static NamesFromText(text) {
    const reader = new StringReader(text), names = new ReferenceList();
    for (let line; (line = reader.ReadLine()) !== null;) if (line.startsWith('*')) names.Add(header(line)[0]);
    return names;
  }
  static LoadText(text, linetypeName, file = null) {
    if (linetypeName == null || linetypeName === '') return null;
    const reader = new StringReader(text);
    for (let line; (line = reader.ReadLine()) !== null;) {
      if (!line.startsWith('*')) continue;
      const [name, description] = header(line); if (!OrdinalIgnoreCaseEquals(name, linetypeName)) continue;
      line = reader.ReadLine(); if (line === null) throw new FileLoadException('Unknown error reading LIN file.', file);
      const tokens = line.split(/,(?=(?:[^"]*"[^"]*")*[^"]*$)/), segments = [];
      for (let i = 1; i < tokens.length; i++) {
        let length; try { length = ParseInvariantFloat(tokens[i]); } catch (error) { if (!(error instanceof FormatException)) throw error; throw new FormatException('The linetype definition is not well formatted.'); }
        if (i + 1 < tokens.length && tokens[i+1].startsWith('[')) {
          const data = []; for (++i; i < tokens.length; i++) { data.push(tokens[i]); if (tokens[i].endsWith(']')) break; }
          const segment = Linetype.$complex(data, length); if (segment !== null) segments.push(segment);
        } else segments.push(new LinetypeSimpleSegment(length));
      }
      return new Linetype(name, segments, description);
    }
    return null;
  }
  static $complex(data, length) {
    if (data[0].length < 2) throw new IndexOutOfRangeException();
    const isText = data[0][1] === '"'; data[0] = data[0].slice(1);
    if (!data.at(-1).length) throw new ArgumentOutOfRangeException('startIndex');
    data[data.length-1] = data.at(-1).slice(0, -1);
    if (data.length < 2) return null;
    const name = data[0].replace(/^"+|"+$/g, ''), style = data[1];
    let offsetX = 0, offsetY = 0;
    let rotationType = LinetypeSegmentRotationType.Relative, rotation = 0, scale = 0.1;
    for (let i = 2; i < data.length; i++) {
      if (data[i].length < 2) throw new ArgumentOutOfRangeException('count');
      const value = data[i].slice(2), key = data[i].slice(0,2).toUpperCase();
      if (key === 'X=') offsetX = ParseInvariantFloat(value);
      else if (key === 'Y=') offsetY = ParseInvariantFloat(value);
      else if (key === 'S=') { scale = ParseInvariantFloat(value); if (scale <= 0) scale = 0.1; }
      else if (key === 'A=' || key === 'R=' || key === 'U=') {
        rotationType = { 'A=': LinetypeSegmentRotationType.Absolute, 'R=': LinetypeSegmentRotationType.Relative, 'U=': LinetypeSegmentRotationType.Upright }[key];
        const suffix = value.slice(-1).toUpperCase(), factor = suffix === 'F' ? MathHelper.RadToDeg : suffix === 'G' ? MathHelper.GradToDeg : 1;
        rotation = ParseInvariantFloat(['D','F','G'].includes(suffix) ? value.slice(0,-1) : value) * factor;
      }
    }
    const offset = new Vector2(offsetX, offsetY);
    return isText ? new LinetypeTextSegment(name, new TextStyle(style, TextStyle.DefaultFont), length, offset, rotationType, rotation, scale)
      : new LinetypeShapeSegment(name, new ShapeStyle(PathFileNameWithoutExtension(style), style), length, offset, rotationType, rotation, scale);
  }
  Save(file) { PatternFileSystem.AppendAllText(file, this.ToLinString(PatternFileSystem.NewLine)); }
  ToLinString(newLine = Culture.NewLine) {
    let text = `*${this.Name},${this.$description}${newLine}A`;
    for (const segment of this.$segments) {
      if (segment.Type === LinetypeSegmentType.Simple) { text += ',' + number(segment.Length); continue; }
      if (segment.Type !== LinetypeSegmentType.Text && segment.Type !== LinetypeSegmentType.Shape) continue;
      const isText = segment.Type === LinetypeSegmentType.Text;
      const rotation = segment.RotationType === LinetypeSegmentRotationType.Absolute ? 'A=' : segment.RotationType === LinetypeSegmentRotationType.Upright ? 'U=' : 'R=';
      text += `,${number(segment.Length)},[${isText ? '"' + segment.Text + '"' : segment.Name},${isText ? segment.Style.Name : segment.Style.File},S=${number(segment.Scale)},${rotation}${number(segment.Rotation)},X=${number(segment.Offset.X)},Y=${number(segment.Offset.Y)}]`;
    }
    return text + newLine;
  }
  HasReferences() { return this.Owner !== null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  Clone(newName = this.Name) {
    const copy = new Linetype(newName, Array.from(this.$segments, segment => segment.Clone()), this.$description);
    for (const data of this.XData.Values) copy.XData.Add(data.Clone()); return copy;
  }
}
InstallLinetypeReferenceRename(Linetype);
