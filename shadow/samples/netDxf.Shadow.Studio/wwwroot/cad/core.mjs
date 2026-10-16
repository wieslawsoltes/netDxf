import { authoringCommands } from './authoring-contracts.mjs';
/** Host-independent precision input, camera math and command state. No DOM, eval or network. */
export const point = (x = 0, y = 0, z = 0) => ({ x, y, z });
export const add = (a, b) => point(a.x + b.x, a.y + b.y, (a.z ?? 0) + (b.z ?? 0));
export const sub = (a, b) => point(a.x - b.x, a.y - b.y, (a.z ?? 0) - (b.z ?? 0));
export const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y, (a.z ?? 0) - (b.z ?? 0));
export const format = n => Number(n.toFixed(4)).toString();
export function finite(n) {
  if (!Number.isFinite(n) || Math.abs(n) > 1e12) throw new Error('Value is outside the finite drafting range (±1e12).');
  return n;
}
/** Bounded recursive-descent arithmetic, with explicit grammar and no JavaScript execution. */
export function number(text) {
  const source = String(text).trim().toLowerCase();
  if (!source || source.length > 128) throw new Error('Enter a number or an arithmetic expression.');
  const tokens = source.match(/(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?|pi|[()+*/-]|\S/g) || [];
  let i = 0, depth = 0;
  function atom() {
    if (++depth > 24) throw new Error('Expression nesting limit exceeded.');
    const t = tokens[i++]; let value;
    if (t === '+' || t === '-') value = (t === '-' ? -1 : 1) * atom();
    else if (t === '(') { value = sum(); if (tokens[i++] !== ')') throw new Error('Missing closing parenthesis.'); }
    else if (t === 'pi') value = Math.PI;
    else if (t && /^(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/.test(t)) value = Number(t);
    else throw new Error('Invalid number or arithmetic expression.');
    depth--; return finite(value);
  }
  function product() { let a = atom(); while (tokens[i] === '*' || tokens[i] === '/') { const op = tokens[i++], b = atom(); a = finite(op === '*' ? a * b : a / b); } return a; }
  function sum() { let a = product(); while (tokens[i] === '+' || tokens[i] === '-') { const op = tokens[i++], b = product(); a = finite(op === '+' ? a + b : a - b); } return a; }
  const result = sum(); if (i !== tokens.length) throw new Error('Unexpected expression token.'); return result;
}
export function parsePoint(text, anchor = point(), cursor = null) {
  let value = String(text).trim(), relative = value.startsWith('@');
  if (relative || value.startsWith('#')) value = value.slice(1);
  let result;
  if (value.includes('<')) {
    const pieces = value.split('<'); if (pieces.length !== 2) throw new Error('Polar input is distance<angle.');
    const r = number(pieces[0]), angle = number(pieces[1]) * Math.PI / 180;
    result = point(r * Math.cos(angle), r * Math.sin(angle));
  } else if (value.includes(',')) {
    const pieces = value.split(','); if (pieces.length < 2 || pieces.length > 3) throw new Error('Use X,Y or X,Y,Z coordinates.');
    result = point(...pieces.map(number));
  } else {
    if (!cursor) throw new Error('Use X,Y coordinates or point the cursor to specify a distance.');
    const d = sub(cursor, anchor), length = distance(cursor, anchor); if (length < 1e-12) throw new Error('Point the cursor away from the previous point.');
    const r = number(value); result = point(d.x / length * r, d.y / length * r, d.z / length * r); relative = true;
  }
  if (relative) result = add(anchor, result);
  for (const v of Object.values(result)) finite(v); return result;
}
export function screen(p, camera, size) { return point(size.w / 2 + (p.x - camera.x) * camera.scale, size.h / 2 - (p.y - camera.y) * camera.scale); }
export function world(p, camera, size) { return point(camera.x + (p.x - size.w / 2) / camera.scale, camera.y - (p.y - size.h / 2) / camera.scale); }
export function zoom(camera, size, pixel, factor) {
  const before = world(pixel, camera, size), scale = Math.min(1e8, Math.max(1e-8, camera.scale * factor));
  const after = world(pixel, { ...camera, scale }, size);
  return { x: camera.x + before.x - after.x, y: camera.y + before.y - after.y, scale };
}
export function fit(bounds, size) {
  if (!bounds || bounds.isEmpty) return { x: 0, y: 0, scale: Math.max(1, Math.min(size.w, size.h) / 150) };
  return { x: (bounds.minX + bounds.maxX) / 2, y: (bounds.minY + bounds.maxY) / 2,
    scale: Math.min(size.w / Math.max(20, bounds.maxX - bounds.minX), size.h / Math.max(20, bounds.maxY - bounds.minY)) * .82 };
}
export function constrain(p, anchor, settings) {
  let value = { ...p };
  if (anchor && (settings.ortho || settings.polar)) {
    const d = sub(p, anchor), angle = Math.atan2(d.y, d.x), step = (settings.ortho ? 90 : 15) * Math.PI / 180;
    const a = Math.round(angle / step) * step, r = settings.ortho ? Math.max(Math.abs(d.x), Math.abs(d.y)) : Math.hypot(d.x, d.y);
    value = add(anchor, point(Math.cos(a) * r, Math.sin(a) * r));
  } else if (settings.snap) {
    const step = Math.max(1e-9, settings.gridStep || 10); value = point(Math.round(p.x / step) * step, Math.round(p.y / step) * step, p.z);
  }
  return value;
}
export function polygonArea(points) { return Math.abs(points.reduce((sum, p, i) => { const q = points[(i + 1) % points.length]; return sum + p.x * q.y - p.y * q.x; }, 0)) / 2; }
const P = prompt => ({ kind: 'point', prompt });
const N = (key, prompt, defaultValue, min = 1e-9, max = 1e12, integer = false) => ({ kind: 'number', key, prompt, defaultValue, min, max, integer });
const T = (key, prompt, defaultValue = '') => ({ kind: 'text', key, prompt, defaultValue });
const spec = (aliases, label, steps = [], extra = {}) => ({ aliases: aliases.split(' '), label, steps, ...extra });
export const commands = Object.freeze({
  ...authoringCommands,
  LINE: spec('L', 'Line', [P('Specify first point')], { chain: 2 }),
  PLINE: spec('PL', 'Polyline', [P('Specify start point')], { chain: 2 }),
  RECTANG: spec('REC RECTANGLE', 'Rectangle', [P('Specify first corner'), P('Specify opposite corner')]),
  POLYGON: spec('POL', 'Polygon', [N('count', 'Number of sides', 6, 3, 1024, true), P('Specify center'), N('value', 'Radius', 10)]),
  CIRCLE: spec('C', 'Circle', [P('Specify center'), N('value', 'Radius', 10)]),
  ARC: spec('A', '3-point arc', [P('Specify start point'), P('Specify a point on the arc'), P('Specify end point')]),
  ELLIPSE: spec('EL', 'Ellipse', [P('Specify center'), P('Specify major-axis endpoint'), P('Specify minor-axis point')]),
  POINT: spec('PO', 'Point', [P('Specify point')]),
  SPLINE: spec('SPL', 'Cubic spline', [P('Specify first control point')], { chain: 4 }),
  TEXT: spec('T DT', 'Text', [P('Specify insertion point'), N('value', 'Text height', 3), N('rotation', 'Rotation in degrees', 0, -1e12), T('text', 'Enter text', 'Text')]),
  MTEXT: spec('MT', 'Multiline text', [P('Specify insertion point'), N('value', 'Text height', 3), N('secondary', 'Paragraph width', 40), T('text', 'Enter text (\\P for paragraphs)', 'Text')]),
  DIMALIGNED: spec('DAL', 'Aligned dimension', [P('First extension origin'), P('Second extension origin'), P('Dimension line location')]),
  DIMLINEAR: spec('DLI', 'Linear dimension', [P('First extension origin'), P('Second extension origin'), P('Dimension line location')]),
  INSERT: spec('I', 'Insert block', [T('block', 'Block name'), P('Insertion point'), N('value', 'Uniform scale', 1), N('rotation', 'Rotation in degrees', 0, -1e12)]),
  MOVE: spec('M', 'Move', [P('Base point'), P('Second point')], { selection: 1 }),
  COPY: spec('CO CP', 'Copy', [P('Base point'), P('Second point')], { selection: 1 }),
  ROTATE: spec('RO', 'Rotate', [P('Base point'), N('value', 'Rotation in degrees', 90, -1e12)], { selection: 1 }),
  SCALE: spec('SC', 'Scale', [P('Base point'), N('value', 'Scale factor', 2)], { selection: 1 }),
  MIRROR: spec('MI', 'Mirror', [P('First mirror-axis point'), P('Second mirror-axis point')], { selection: 1 }),
  ARRAY: spec('AR', 'Rectangular array', [N('count', 'Columns', 3, 1, 10000, true), N('value', 'Rows', 2, 1, 10000, true), P('Cell origin'), P('Next cell (X/Y spacing)')], { selection: 1 }),
  OFFSET: spec('O', 'Offset', [N('value', 'Offset distance', 10), P('Specify side')], { selection: 1, exactSelection: 1 }),
  TRIM: spec('TR', 'Trim endpoint', [P('Pick the target end to trim')], { selection: 2, exactSelection: 2 }),
  EXTEND: spec('EX', 'Extend endpoint', [P('Pick the target end to extend')], { selection: 2, exactSelection: 2 }),
  FILLET: spec('F', 'Fillet', [N('value', 'Fillet radius', 5, 0)], { selection: 2, exactSelection: 2 }),
  CHAMFER: spec('CHA', 'Chamfer', [N('value', 'Equal chamfer distance', 5, 0)], { selection: 2, exactSelection: 2 }),
  JOIN: spec('J', 'Join lines', [], { selection: 2 }),
  EXPLODE: spec('X', 'Explode polyline', [], { selection: 1 }),
  ERASE: spec('E DELETE', 'Erase', [], { selection: 1 }),
  DIST: spec('DI MEASURE', 'Distance', [P('First measurement point'), P('Second measurement point')], { measure: true }),
  AREA: spec('AA', 'Area', [P('First area point')], { chain: 3, measure: true })
});
export function commandName(text) {
  const name = text.trim().toUpperCase(); if (commands[name]) return name;
  return Object.keys(commands).find(key => commands[key].aliases.includes(name)) || null;
}
/** A command is detached from HTTP. A complete input produces one native transaction. */
export class CommandSession {
  constructor(name, layer = '0', selectionCount = 0) {
    this.name = commandName(name); if (!this.name) throw new Error(`Unknown command: ${name}`);
    this.spec = commands[this.name]; this.command = { name: this.name, points: [], layer };
    this.version = 0; this.index = 0; this.selecting = !!this.spec.selection && !this.validSelection(selectionCount); this.complete = false;
  }
  validSelection(count) { return this.spec.exactSelection ? count === this.spec.exactSelection : count >= (this.spec.selection || 0); }
  acceptSelection(count) { if (!this.validSelection(count)) throw new Error(`Select ${this.spec.exactSelection ? 'exactly ' : 'at least '}${this.spec.selection} source objects.`); this.selecting = false; this.complete = this.spec.steps.length === 0; this.version++; }
  get anchor() { return this.command.points.at(-1) || null; }
  get step() { return this.selecting ? { kind: 'selection', prompt: 'Select objects in order; Enter to continue' } : this.spec.steps[this.index] || { kind: 'point', prompt: 'Next point or [Undo/Close]; Enter to finish' }; }
  get prompt() { const s = this.step; return `${this.name} — ${s.prompt}${s.defaultValue !== undefined && s.defaultValue !== '' ? ` <${s.defaultValue}>` : ''}:`; }
  input(text, cursor = null) {
    if (this.selecting || this.complete) throw new Error('The command is not accepting coordinates.');
    const value = String(text).trim(), step = this.step;
    if (this.spec.chain && ['U', 'UNDO'].includes(value.toUpperCase())) {
      if (this.command.points.length) this.command.points.pop(); this.index = this.command.points.length ? 1 : 0; this.version++; return;
    }
    if (this.spec.chain && (!value || ['C', 'CLOSE'].includes(value.toUpperCase()))) {
      const closed = !!value;
      if (closed && this.name === 'SPLINE') throw new Error('This command constructs open clamped control-point splines.');
      const minimum = closed ? Math.max(3, this.spec.chain) : this.spec.chain;
      if (this.command.points.length < minimum) throw new Error(`Specify at least ${minimum} points.`);
      this.command.closed = closed; this.complete = true; this.version++; return;
    }
    if (step.kind === 'point') return this.pick(parsePoint(value, this.anchor || point(), cursor));
    if (step.kind === 'text') { const t = value || step.defaultValue; if (!t || t.length > 16384) throw new Error('Enter 1–16384 characters.'); this.command[step.key] = t; }
    else {
      const n = value ? number(value) : step.defaultValue;
      if (!Number.isFinite(n) || n < step.min || n > step.max || (step.integer && !Number.isInteger(n))) throw new Error(`Enter ${step.integer ? 'an integer' : 'a number'} from ${step.min} to ${step.max}.`);
      this.command[step.key] = n;
    }
    this.advance();
  }
  pick(p) {
    if (this.selecting || this.complete) throw new Error('Finish selecting objects first.');
    const step = this.step;
    for (const v of [p.x, p.y, p.z ?? 0]) finite(v);
    if (step.kind === 'number' && step.key === 'value' && ['CIRCLE', 'POLYGON'].includes(this.name)) {
      const rotation = Math.atan2(p.y - this.anchor.y, p.x - this.anchor.x) * 180 / Math.PI;
      this.input(String(distance(p, this.anchor)));
      if (this.name === 'POLYGON') this.command.rotation = rotation;
      return;
    }
    if (step.kind === 'number' && step.key === 'value' && this.name === 'ROTATE') return this.input(String(Math.atan2(p.y - this.anchor.y, p.x - this.anchor.x) * 180 / Math.PI));
    if (step.kind !== 'point') throw new Error('This prompt requires keyboard input.');
    if (this.anchor && distance(this.anchor, p) < 1e-9) throw new Error('The next point must be distinct.');
    this.command.points.push(point(p.x, p.y, p.z ?? 0)); this.advance();
  }
  advance() { this.version++; this.index++; if (!this.spec.chain && this.index >= this.spec.steps.length) this.complete = true; }
}
