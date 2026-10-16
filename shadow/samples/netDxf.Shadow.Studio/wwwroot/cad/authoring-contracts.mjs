/** Pure authoring contracts shared by keyboard, ribbon and other UI adapters. */
const text = (key, prompt, defaultValue = '') => ({ kind: 'text', key, prompt, defaultValue });
const number = (key, prompt, defaultValue, min = 1e-9) => ({ kind: 'number', key, prompt, defaultValue, min, max: 1e12 });
const point = prompt => ({ kind: 'point', prompt });
const hatchSteps = () => [text('patternName', 'Pattern name', 'SOLID'), number('value', 'Pattern scale', 1),
  number('rotation', 'Pattern angle in degrees', 0, -1e12), text('islands', 'Islands [Normal/Outer/Ignore]', 'Normal')];
export const authoringCommands = Object.freeze({
  BLOCK: { aliases: ['B'], label: 'Create block', selection: 1,
    steps: [text('text', 'New block name'), point('Specify block base point')] },
  BLOCKREDEFINE: { aliases: ['BREDEFINE'], label: 'Redefine block', selection: 1,
    steps: [text('text', 'Existing block name'), point('Replacement base point')] },
  BLOCKRENAME: { aliases: ['BRENAME'], label: 'Rename block',
    steps: [text('block', 'Existing block name'), text('text', 'New block name')] },
  HATCH: { aliases: ['H', 'BHATCH'], label: 'Hatch selection', selection: 1, steps: hatchSteps() },
  HATCHEDIT: { aliases: ['HE'], label: 'Edit hatch', selection: 1, steps: hatchSteps() },
  HATCHPOLY: { aliases: ['HP'], label: 'Solid hatch polygon', chain: 3, steps: [point('First polygon corner')] }
});
export const hatchPatterns = Object.freeze(['SOLID', 'LINE', 'CROSS', 'DOTS', 'DASH']);
export const islandStyles = Object.freeze(['Normal', 'Outer', 'Ignore']);
const objectId = value => ({ value });
export function blockName(value) {
  if (typeof value !== 'string' || !value.trim() || value.length > 255 || /[\x00-\x1f\x7f<>/\\":;?*|,=`]/u.test(value))
    throw new Error('Enter a 1–255 character block name without reserved DXF characters.');
  return value;
}
export function islandStyle(value) {
  const style = islandStyles.find(s => s.toLowerCase() === String(value).toLowerCase());
  if (!style) throw new Error('Island policy must be Normal, Outer or Ignore.');
  return islandStyles.indexOf(style); // Native enum: Normal=0, Outer=1, Ignore=2.
}
function checkedPoint(p) {
  if (!p || ![p.x, p.y, p.z ?? 0].every(n => Number.isFinite(n) && Math.abs(n) <= 1e12)) throw new Error('Specify a finite base point.');
  return { x: p.x, y: p.y, z: p.z ?? 0 };
}
export function authoringRequest(command, data) {
  if (!Object.hasOwn(authoringCommands, command.name)) return null;
  const selection = [...new Set(data.selectedEntities || [])].map(objectId);
  if (authoringCommands[command.name].selection && !selection.length) throw new Error('Select source objects before authoring.');
  const findBlock = name => {
    const block = data.blocks.find(b => b.name.toLowerCase() === String(name).toLowerCase());
    if (!block) throw new Error(`Unknown block: ${name}`);
    return block;
  };
  if (command.name === 'BLOCKRENAME') return { path: 'block/rename', body: { block: objectId(findBlock(command.block).id), name: blockName(command.text) } };
  if (command.name === 'BLOCK' || command.name === 'BLOCKREDEFINE') {
    const target = command.name === 'BLOCKREDEFINE' ? findBlock(command.text) : null;
    return { path: 'block/define', body: { definition: { name: target?.name || blockName(command.text),
      basePoint: checkedPoint(command.points[0]), entities: selection, disposition: target ? 0 : 1,
      redefine: target ? objectId(target.id) : null, insertLayer: command.layer || '0' } } };
  }
  if (command.name === 'HATCHPOLY') {
    if (command.points.length < 3 || command.points.length > 10000) throw new Error('A hatch polygon requires 3–10,000 points.');
    return { path: 'hatch/create', body: { hatch: { polygons: [command.points.map(checkedPoint)], pattern: { name: 'SOLID' }, layer: command.layer || '0' } } };
  }
  const name = String(command.patternName || 'SOLID').toUpperCase();
  if (!hatchPatterns.includes(name)) throw new Error('Choose a built-in pattern, or import a custom PAT through Hatch settings.');
  if (!Number.isFinite(command.value) || command.value < 1e-9 || command.value > 1e9 || !Number.isFinite(command.rotation))
    throw new Error('Pattern scale must be between 1e-9 and 1e9, with a finite angle.');
  const pattern = { name, scale: command.value, angle: command.rotation, islands: islandStyle(command.islands || 'Normal') };
  return command.name === 'HATCHEDIT' ? { path: 'hatch/pattern', body: { pattern, entities: selection } } :
    { path: 'hatch/create', body: { hatch: { boundaries: selection, pattern, layer: command.layer || '0' } } };
}
/** Strict bounded PAT line-family reader. No includes, URLs, expression evaluation or resource I/O. */
export function parsePat(source) {
  if (typeof source !== 'string' || source.length > 1_048_576) throw new Error('PAT input is limited to 1 MiB of text.');
  const patterns = [], names = new Set(); let current = null, lineNumber = 0;
  const fail = message => { throw new Error(`PAT line ${lineNumber}: ${message}`); };
  const numeric = value => {
    if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(value)) fail('Expected a finite decimal number.');
    const n = Number(value); if (!Number.isFinite(n) || Math.abs(n) > 1e12) fail('Number exceeds the drafting range.'); return n;
  };
  for (const raw of source.replace(/^\uFEFF/, '').split(/\r?\n/u)) {
    lineNumber++; if (raw.length > 8192 || lineNumber > 32768) fail('Input line budget exceeded.');
    const line = raw.split(';', 1)[0].trim(); if (!line) continue;
    if (line.startsWith('*')) {
      if (current && !current.lines.length) fail('Previous pattern has no line families.');
      const comma = line.indexOf(','), name = line.slice(1, comma < 0 ? undefined : comma).trim().toUpperCase();
      if (!/^[A-Z0-9_-]{1,64}$/.test(name) || name === 'SOLID' || names.has(name)) fail('Invalid, reserved or duplicate pattern name.');
      if (patterns.length >= 128) fail('At most 128 patterns are accepted per import.');
      current = { name, description: comma < 0 ? '' : line.slice(comma + 1).trim(), lines: [] };
      patterns.push(current); names.add(name); continue;
    }
    if (!current) fail('A pattern header is required before line families.');
    const pieces = line.split(',').map(v => v.trim());
    if (pieces.length < 5 || pieces.length > 133 || current.lines.length >= 128) fail('Expected 5–133 columns within 128 line families.');
    const [angle, x, y, dx, dy, ...dashes] = pieces.map(numeric);
    if (Math.abs(dy) < 1e-9 || dashes.length && !(dashes.reduce((s, n) => s + Math.abs(n), 0) > 0)) fail('Nonzero perpendicular spacing and dash period are required.');
    current.lines.push({ angle, origin: { x, y, z: 0 }, delta: { x: dx, y: dy, z: 0 }, dashes });
  }
  if (!patterns.length || !current.lines.length) throw new Error('PAT import needs at least one complete pattern.');
  return patterns;
}
