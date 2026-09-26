// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native mirror of the pinned netDxf/Entities/HatchPattern.cs.
import { ArgumentException, ArgumentOutOfRangeException, FileLoadException, NullReferenceException } from '../../runtime/Errors.js';
import { Copy, Culture, NumberText } from '../../runtime/GeometryRuntime.js';
import { TrimDotNet as trim, ParseInvariantFloat as parseDouble } from '../../runtime/InvariantFloat.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { StringReader } from '../../runtime/StringReader.js';
import { PatternFileSystem } from '../../runtime/PatternFileSystem.js';
import { Vector2 } from '../Vector2.js';
import { MathHelper } from '../MathHelper.js';
import { HatchPatternLineDefinition } from './HatchPatternLineDefinition.js';
import { HatchFillType } from './HatchFillType.js';
import { HatchType } from './HatchType.js';
import { HatchStyle } from './HatchStyle.js';

function header(line) {
  const comma = line.indexOf(',');
  if (comma < 1) throw new ArgumentOutOfRangeException('length');
  return [line.slice(1, comma), trim(line.slice(comma + 1))];
}

export class HatchPattern {
  $name; $description; $style = HatchStyle.Normal; $fill; $type = HatchType.UserDefined;
  $origin = Vector2.Zero; $angle = 0; $scale = 1; $lineDefinitions; IsDouble = false;
  constructor(name, linesOrDescription = null, description = '') {
    if (arguments.length < 1 || arguments.length > 3) throw new ArgumentException('No matching HatchPattern constructor.');
    const lines = arguments.length < 3 && typeof linesOrDescription === 'string' ? null : linesOrDescription;
    if (arguments.length < 3 && typeof linesOrDescription === 'string') description = linesOrDescription;
    this.$name = name == null || name === '' ? '' : name;
    this.$description = description == null || description === '' ? '' : description;
    this.$fill = this.$name === 'SOLID' ? HatchFillType.SolidFill : HatchFillType.PatternFill;
    this.$lineDefinitions = new ReferenceList(lines ?? []);
  }
  static get Solid() { const p = new HatchPattern('SOLID', 'Solid fill'); p.Type = HatchType.Predefined; return p; }
  static get Line() { return HatchPattern.$preset('LINE', 'Parallel horizontal lines', [0]); }
  static get Net() { return HatchPattern.$preset('NET', 'Horizontal / vertical grid', [0, 90]); }
  static get Dots() {
    const p = HatchPattern.$preset('DOTS', 'A series of dots', [0]);
    const line = p.LineDefinitions.get_Item(0);
    line.Delta = new Vector2(0.03125, 0.0625); line.DashPattern.AddRange([0, -0.0625]);
    return p;
  }
  static $preset(name, description, angles) {
    const p = new HatchPattern(name, description);
    for (const angle of angles) {
      const line = new HatchPatternLineDefinition(); line.Angle = angle; line.Delta = new Vector2(0, 0.125);
      p.LineDefinitions.Add(line);
    }
    p.Type = HatchType.Predefined; return p;
  }
  get Name() { return this.$name; }
  get Description() { return this.$description; }
  set Description(value) { this.$description = value; }
  get Style() { return this.$style; }
  // Original internal setters used by HATCH affine pattern reconstruction.
  set Style(value) { this.$style = value; }
  get Fill() { return this.$fill; }
  set Fill(value) { this.$fill = value; }
  get Type() { return this.$type; }
  set Type(value) { this.$type = value; }
  get Origin() { return Copy(this.$origin); }
  set Origin(value) { this.$origin = Copy(value); }
  get Angle() { return this.$angle; }
  set Angle(value) { this.$angle = MathHelper.NormalizeAngle(value); }
  get Scale() { return this.$scale; }
  set Scale(value) {
    if (value <= 0) throw new ArgumentOutOfRangeException('value', value, 'The scale can not be zero or less.');
    this.$scale = value;
  }
  get LineDefinitions() { return this.$lineDefinitions; }
  static NamesFromFile(file) { return HatchPattern.NamesFromText(PatternFileSystem.ReadAllText(file)); }
  static Load(file, patternName) { return HatchPattern.LoadText(PatternFileSystem.ReadAllText(file), patternName, file); }
  Save(file) { PatternFileSystem.AppendAllText(file, this.ToPatString(PatternFileSystem.NewLine)); }

  /** JavaScript convenience: the same parser without a filesystem dependency. */
  static NamesFromText(text) {
    const reader = new StringReader(text), names = new ReferenceList();
    for (let line; (line = reader.ReadLine()) !== null;) if (line.startsWith('*')) names.Add(header(line)[0]);
    return names;
  }
  /** JavaScript convenience. PAT lookup stops at the first matching definition. */
  static LoadText(text, patternName, file = null) {
    const reader = new StringReader(text);
    for (let line; (line = reader.ReadLine()) !== null;) {
      line = trim(line); if (!line.startsWith('*')) continue;
      const [name, description] = header(line);
      if (patternName == null || OrdinalIgnoreCaseKey(name) !== OrdinalIgnoreCaseKey(patternName)) continue;
      line = reader.ReadLine();
      if (line === null) throw new FileLoadException('Unknown error reading PAT file.', file);
      const pattern = new HatchPattern(name, description);
      while (line !== null) {
        line = trim(line); if (!line || line.startsWith('*') || line.startsWith(';')) break;
        const tokens = line.split(',');
        if (tokens.length < 5) throw new FileLoadException('The hatch pattern definition lines must contain at least 5 values.', file);
        const values = tokens.map(parseDouble), definition = new HatchPatternLineDefinition();
        definition.Angle = values[0]; definition.Origin = new Vector2(values[1], values[2]); definition.Delta = new Vector2(values[3], values[4]);
        definition.DashPattern.AddRange(values.slice(5)); pattern.LineDefinitions.Add(definition); pattern.Type = HatchType.UserDefined;
        line = reader.ReadLine();
      }
      return pattern;
    }
    return null;
  }
  /** JavaScript convenience; Save uses the host platform's newline. */
  ToPatString(newLine = Culture.NewLine) {
    let text = `*${this.Name},${this.Description ?? ''}${newLine}`;
    for (const line of this.LineDefinitions) {
      if (line == null) throw new NullReferenceException();
      const values = [line.Angle, line.Origin.X, line.Origin.Y, line.Delta.X, line.Delta.Y, ...line.DashPattern];
      text += values.map(value => NumberText(value, Culture.Invariant)).join(',') + newLine;
    }
    return text;
  }
  $copyPatternTo(copy) {
    copy.$style = this.$style; copy.$fill = this.$fill; copy.Type = this.Type; copy.IsDouble = this.IsDouble;
    copy.Origin = this.Origin; copy.Angle = this.Angle; copy.Scale = this.Scale;
    for (const line of this.LineDefinitions) {
      if (line == null) throw new NullReferenceException();
      copy.LineDefinitions.Add(line.Clone());
    }
    return copy;
  }
  Clone() { return this.$copyPatternTo(new HatchPattern(this.Name, this.Description)); }
}
