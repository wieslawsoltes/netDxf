import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { HatchPattern, HatchGradientPattern, HatchPatternLineDefinition, AciColor, Vector2, HatchType, HatchFillType } from '../../node-entry.js';
import { DecodePatternText } from '../../runtime/NodePatternFileSystem.js';
import { SetPatternFileSystem } from '../../runtime/PatternFileSystem.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, FileLoadException, FormatException, NullReferenceException, FileNotFoundException, DirectoryNotFoundException, EncoderFallbackException } from '../../runtime/Errors.js';
const root = fileURLToPath(new URL('../../../', import.meta.url));
const temp = action => { const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'netdxf-pat-')); try { return action(dir); } finally { fs.rmSync(dir, { recursive: true, force: true }); } };

test('hatch pattern preserves constructor spelling, overloads and exact SOLID dispatch', () => {
  for (const name of [null, '', 'solid', 'SOLID', 'MiXeD']) {
    const p = new HatchPattern(name, null, null);
    assert.equal(p.Name, name ?? ''); assert.equal(p.Description, ''); assert.equal(p.Scale, 1);
    assert.equal(p.Fill, name === 'SOLID' ? HatchFillType.SolidFill : HatchFillType.PatternFill);
    assert.equal(p.Type, HatchType.UserDefined); assert.equal(p.IsDouble, false); assert.equal(p.LineDefinitions.Count, 0);
  }
  assert.throws(() => new HatchPattern(), ArgumentException);
  const line = new HatchPatternLineDefinition(), lines = [line];
  const p = new HatchPattern('P', lines, 'description'); lines.length = 0;
  assert.equal(p.Description, 'description'); assert.equal(p.LineDefinitions.Count, 1); assert.equal(p.LineDefinitions.get_Item(0), line);
  assert.equal(new HatchPattern('P', 'description').Description, 'description');
});
test('pattern origins have value semantics; clone owns lines and dash lists', () => {
  const p = HatchPattern.Dots, origin = new Vector2(2, 3); p.Origin = origin; origin.X = 99; p.Origin.Y = 99;
  assert.equal(p.Origin.X, 2); assert.equal(p.Origin.Y, 3);
  const q = p.Clone(); q.LineDefinitions.get_Item(0).DashPattern.Add(77); q.LineDefinitions.get_Item(0).Delta = Vector2.Zero;
  assert.equal(p.LineDefinitions.get_Item(0).DashPattern.Count, 2); assert.equal(p.LineDefinitions.get_Item(0).Delta.Y, 0.0625);
  p.Description = null; assert.equal(p.Clone().Description, '');
  p.LineDefinitions.Add(null); assert.throws(() => p.Clone(), NullReferenceException); assert.throws(() => p.ToPatString(), NullReferenceException);
});
test('all four presets return independent predefined model data', () => {
  for (const [preset, count] of [['Solid', 0], ['Line', 1], ['Net', 2], ['Dots', 1]]) {
    const p = HatchPattern[preset], q = HatchPattern[preset];
    assert.notEqual(p, q); assert.equal(p.Type, HatchType.Predefined); assert.equal(p.LineDefinitions.Count, count);
    p.LineDefinitions.Clear(); assert.equal(q.LineDefinitions.Count, count);
  }
  assert.equal(HatchPattern.Net.LineDefinitions.get_Item(1).Angle, 90);
});
test('pattern scale keeps the original nonpositive-only guard and angle normalization', () => {
  const p = HatchPattern.Line; p.Angle = -450; assert.equal(p.Angle, 270);
  for (const value of [0, -0, -1, -Infinity]) assert.throws(() => { p.Scale = value; }, { name: 'ArgumentOutOfRangeException', ParamName: 'value' });
  assert.equal(p.Scale, 1); p.Scale = NaN; assert.ok(Number.isNaN(p.Clone().Scale)); p.Scale = Infinity; assert.equal(p.Scale, Infinity);
});
test('gradient clone retains authored stops without rerunning tint editing', () => {
  const p = new HatchGradientPattern(AciColor.Red, 0.4, 7), second = p.Color2;
  p.Color1 = AciColor.Blue; p.Description = null; p.Color2AciIndex = null;
  const q = p.Clone(); assert.equal(q.SingleColor, true); assert.equal(q.Description, null);
  assert.notEqual(q.Color1, p.Color1); assert.notEqual(q.Color2, p.Color2);
  assert.deepEqual([q.Color2.R, q.Color2.G, q.Color2.B], [second.R, second.G, second.B]);
  q.Tint = 0.4; assert.notDeepEqual([q.Color2.R, q.Color2.G, q.Color2.B], [second.R, second.G, second.B]);
  assert.equal(q.Color2AciIndex, null); assert.equal(q.IsColor2AciIndexAutomatic, false);
});
test('gradient constructor guards retain CLR parameter names', () => {
  assert.throws(() => new HatchGradientPattern(null, 0.25, 0), { name: 'ArgumentNullException', ParamName: 'color' });
  assert.throws(() => new HatchGradientPattern(null, AciColor.Red, 0), { name: 'ArgumentNullException', ParamName: 'color1' });
  assert.throws(() => new HatchGradientPattern(AciColor.Red, null, 0), { name: 'ArgumentNullException', ParamName: 'color2' });
  assert.throws(() => new HatchGradientPattern(AciColor.Red, NaN, 0), { name: 'ArgumentOutOfRangeException', ParamName: 'tint' });
});
test('shift and tint retain signed zero; Centered explicitly selects endpoints', () => {
  for (const value of [-0, 0, Number.MIN_VALUE, 0.125, 0.5, 0.875, 0.9999999999999999, 1]) {
    const p = new HatchGradientPattern(); p.Shift = value; p.Tint = value;
    const q = p.Clone(); assert.ok(Object.is(q.Shift, value)); assert.ok(Object.is(q.Tint, value)); assert.equal(q.Centered, value === 0);
    q.Centered = true; assert.ok(Object.is(q.Shift, 0)); q.Centered = false; assert.equal(q.Shift, 1); assert.ok(Object.is(p.Shift, value));
  }
});
test('ACI JS-domain guards reject invalid Int16 inputs without changing automatic mode', () => {
  for (const key of ['Color1AciIndex', 'Color2AciIndex']) for (const value of [-32769, 32768, 0.5, NaN, Infinity, undefined, '1']) {
    const p = new HatchGradientPattern(), before = p[key];
    assert.throws(() => { p[key] = value; }, ArgumentOutOfRangeException);
    assert.equal(p[key], before); assert.equal(p[`Is${key}Automatic`], true);
  }
});
test('PAT name listing intentionally does not trim headers, while lookup does', () => {
  const text = ' *Indented, one\r\n0,0,0,0,1\r\n*MiXeD, desc, commas\r0,0,0,0,1\n';
  assert.deepEqual(HatchPattern.NamesFromText(text).ToArray(), ['MiXeD']);
  assert.equal(HatchPattern.LoadText(text, 'indented').Name, 'Indented');
  assert.equal(HatchPattern.LoadText(text, 'mixed').Description, 'desc, commas');
  assert.equal(HatchPattern.LoadText(text, null), null); assert.equal(HatchPattern.LoadText(text, 'missing'), null);
  assert.equal(HatchPattern.LoadText('*\u03c3,d\n0,0,0,0,1', '\u03c2').Name, '\u03c3');
});
test('PAT matching header requires a following line; blank, comment and next header terminate', () => {
  assert.throws(() => HatchPattern.LoadText('*P,desc', 'P', 'test.pat'), { name: 'FileLoadException', FileName: 'test.pat' });
  assert.throws(() => HatchPattern.LoadText('*P,desc\n', 'P'), FileLoadException);
  for (const stop of ['', '; stop', '*Q,next']) {
    const p = HatchPattern.LoadText(`*P,desc\n0,0,0,0,1\n${stop}\n90,0,0,1,1\n`, 'P');
    assert.equal(p.LineDefinitions.Count, 1);
  }
  assert.equal(HatchPattern.LoadText('*P,desc\n\n', 'P').LineDefinitions.Count, 0);
});
test('PAT malformed headers, rows and invalid invariant numbers are rejected', () => {
  for (const text of ['*bad', '*bad\n*P,ok\n0,0,0,0,1']) {
    assert.throws(() => HatchPattern.NamesFromText(text), ArgumentOutOfRangeException);
    assert.throws(() => HatchPattern.LoadText(text, 'P'), ArgumentOutOfRangeException);
  }
  assert.throws(() => HatchPattern.LoadText('*P,d\n0,0,0,1', 'P'), FileLoadException);
  for (const value of ['', '0x10', '1e', '.', '1 2', 'oops', '1_000']) assert.throws(() => HatchPattern.LoadText(`*P,d\n0,${value},0,0,1`, 'P'), FormatException);
});
test('PAT whitespace, dash values, negative zero, overflow and formatting retain authored values', () => {
  const p = HatchPattern.LoadText('\u0085*P, desc\u0085\n-90, -0 ,1e-5,1e309,-Infinity,0,-.5,+NaN\n', 'P');
  const line = p.LineDefinitions.get_Item(0);
  assert.equal(line.Angle, 270); assert.ok(Object.is(line.Origin.X, -0)); assert.equal(line.Origin.Y, 1e-5);
  assert.equal(line.Delta.X, Infinity); assert.equal(line.Delta.Y, -Infinity); assert.ok(Number.isNaN(line.DashPattern.get_Item(2)));
  assert.equal(p.ToPatString('\r\n'), '*P,desc\r\n270,-0,1E-05,Infinity,-Infinity,0,-0.5,NaN\r\n');
  assert.equal(HatchPattern.LoadText('\ufeff*P,d\n0,0,0,0,1', 'P'), null);
});
test('PAT Save appends UTF-8, preserves existing bytes and omits non-PAT metadata', () => temp(dir => {
  const file = path.join(dir, 'żółć-😀.pat'), p = HatchPattern.Line;
  p.Description = 'Zażółć 😀'; p.IsDouble = true; p.Scale = 3; p.Origin = new Vector2(4, 5);
  const newline = process.platform === 'win32' ? '\r\n' : '\n';
  p.Save(file); const first = fs.readFileSync(file); p.Save(file);
  assert.deepEqual(fs.readFileSync(file), Buffer.concat([first, first])); assert.equal(first.toString('utf8'), p.ToPatString(newline));
  const q = HatchPattern.Load(file, 'line'); assert.equal(q.Description, p.Description); assert.equal(q.IsDouble, false); assert.equal(q.Scale, 1);
  assert.deepEqual(HatchPattern.NamesFromFile(file).ToArray(), ['LINE', 'LINE']);
}));
test('PAT host checks arguments, missing paths and strict UTF-8 output', () => temp(dir => {
  assert.throws(() => HatchPattern.Load(null, 'P'), ArgumentNullException);
  assert.throws(() => HatchPattern.Load('', 'P'), ArgumentException);
  assert.throws(() => HatchPattern.Load(path.join(dir, 'absent.pat'), 'P'), FileNotFoundException);
  assert.throws(() => HatchPattern.Line.Save(path.join(dir, 'absent', 'test.pat')), DirectoryNotFoundException);
  const p = new HatchPattern('P', '\ud800'), file = path.join(dir, 'invalid.pat');
  assert.throws(() => p.Save(file), EncoderFallbackException);
  assert.throws(() => SetPatternFileSystem({}), ArgumentException);
}));
test('PAT reader detects UTF-8/16/32 BOMs and replaces malformed sequences', () => temp(dir => {
  const text = '*日本😀,desc\n0,0,0,0,1\n', le = Buffer.from(text, 'utf16le'), be = Buffer.from(le).swap16();
  const scalars = [...text].map(s => s.codePointAt(0));
  const utf32 = little => { const b = Buffer.alloc(scalars.length * 4); scalars.forEach((cp, i) => little ? b.writeUInt32LE(cp, i * 4) : b.writeUInt32BE(cp, i * 4)); return b; };
  for (const bytes of [Buffer.from(text), Buffer.concat([Buffer.from([239,187,191]), Buffer.from(text)]),
    Buffer.concat([Buffer.from([255,254]), le]), Buffer.concat([Buffer.from([254,255]), be]),
    Buffer.concat([Buffer.from([255,254,0,0]), utf32(true)]), Buffer.concat([Buffer.from([0,0,254,255]), utf32(false)])]) {
    assert.equal(DecodePatternText(bytes), text); const file = path.join(dir, 'unicode.pat'); fs.writeFileSync(file, bytes);
    assert.equal(HatchPattern.Load(file, '日本😀').Name, '日本😀');
  }
  assert.equal(DecodePatternText(Buffer.from([0xff])), '\ufffd');
  assert.equal(DecodePatternText(Buffer.from([0xff,0xfe,0,0,0,0,0x11,0,1])), '\ufffd\ufffd');
}));
test('both unchanged PAT support files load and portable save/load roundtrip every listed pattern', () => {
  for (const name of ['acad.pat', 'acadiso.pat']) {
    const file = path.join(root, 'TestDxfDocument', 'Support', name), names = HatchPattern.NamesFromFile(file);
    assert.ok(names.Count > 50);
    for (const patternName of names) {
      const pattern = HatchPattern.Load(file, patternName); assert.equal(pattern.Name, patternName);
      const restored = HatchPattern.LoadText(pattern.ToPatString() + '\n', patternName);
      assert.equal(restored.ToPatString(), pattern.ToPatString());
    }
  }
});
test('portable import does not load Node modules and unhosted file operations fail explicitly', () => {
  const code = `import {HatchPattern} from './index.js';\ntry { HatchPattern.Line.Save('unexpected.pat'); throw Error('Unhosted save accepted'); } catch(e) { if(e.name!=='NotSupportedException') throw e; }\nif(HatchPattern.LoadText('*P,d\\n0,0,0,0,1','P').Name!=='P') throw Error('Portable parse failed');`;
  const loader = 'data:text/javascript,' + encodeURIComponent("export async function resolve(s,c,next){if(s.startsWith('node:')||(!s.startsWith('.')&&!s.startsWith('/')&&!s.startsWith('file:')))throw Error('Nonportable import: '+s);return next(s,c);}");
  const result = spawnSync(process.execPath, ['--experimental-loader', loader, '--input-type=module', '-e', code], { cwd: path.join(root, 'javascript'), encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
});
