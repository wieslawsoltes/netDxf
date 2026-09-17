// Supplemental operation corpus. No production behavior or expected outputs live here.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const Pattern = 'Entities.HatchPattern', Gradient = 'Entities.HatchGradientPattern', Line = 'Entities.HatchPatternLineDefinition';
const gradientKind = value => E('Entities.HatchGradientPatternType', value);
const color = property => ({ static: 'AciColor', property });
const create = (type, args = [], id = 'p', signature) => ({ kind: 'new', type, args, id, ...(signature ? { signature } : {}) });
const get = (target, member, id) => ({ kind: 'get', target, member, ...(id ? { id } : {}) });
const set = (target, member, value) => ({ kind: 'set', target, member, value });
const call = (target, member, args = [], id, signature) => ({ kind: 'call', target, member, args, ...(id ? { id } : {}), ...(signature ? { signature } : {}) });
const snap = target => ({ kind: 'snapshot', target });
export function hatchCorpus(patternFiles = [], newLine = '\n') {
  const corpus = [], add = (name, category, steps) => corpus.push({ name, category, request: { steps } });
  for (const name of [null, '', 'solid', 'SOLID', 'MiXeD']) for (const description of [null, '', 'Zażółć 😀'])
    add(`pattern/constructor/${JSON.stringify(name)}/${JSON.stringify(description)}`, 'pattern-model', [
      create(Pattern, [name, description], 'p', ['String', 'String']), call('p', 'Clone', [], 'q'), snap('p'), snap('q'), { kind: 'pat-save', target: 'p', newLine }
    ]);
  for (const preset of ['Solid', 'Line', 'Net', 'Dots']) add(`pattern/preset/${preset}`, 'pattern-model', [
    { kind: 'get', type: Pattern, member: preset, id: 'p' }, set('p', 'IsDouble', true), set('p', 'Origin', V('Vector2', 4, 5)),
    set('p', 'Angle', D(-450)), set('p', 'Scale', D(2)), call('p', 'Clone', [], 'q'), set('q', 'IsDouble', false),
    get('q', 'LineDefinitions', 'lines'), call('lines', 'Clear'), snap('p'), snap('q'), { kind: 'pat-save', target: 'p', newLine }
  ]);
  add('pattern/collection-and-clone', 'pattern-model', [
    create(Line, [], 'line'), set('line', 'Angle', D(37)), set('line', 'Delta', V('Vector2', 1, 2)), get('line', 'DashPattern', 'dash'), call('dash', 'Add', [D(-0.5)]),
    create(Pattern, ['P', A(Line, [R('line')]), 'description'], 'p', ['String', `IEnumerable<${Line}>`, 'String']),
    call('p', 'Clone', [], 'q'), set('line', 'Angle', D(71)), call('dash', 'Add', [D(0)]), snap('p'), snap('q'),
    set('p', 'Description', null), call('p', 'Clone'), get('p', 'LineDefinitions', 'lines'), call('lines', 'Add', [null]), call('p', 'Clone'), { kind: 'pat-save', target: 'p', newLine }
  ]);
  for (const scale of [0, -0, -1, -Infinity, NaN, Infinity, Number.MIN_VALUE]) add(`pattern/scale/${Object.is(scale, -0) ? '-0' : scale}`, 'pattern-model', [
    create(Pattern, ['P']), set('p', 'Scale', D(scale)), snap('p'), call('p', 'Clone')
  ]);
  for (let kind = 0; kind < 9; kind++) for (const mode of ['default', 'single', 'dual']) {
    const args = mode === 'default' ? [] : [color('Red'), mode === 'single' ? D(0.35) : color('Blue'), gradientKind(kind), 'Description'];
    const signature = mode === 'single' ? ['AciColor', 'Double', 'Entities.HatchGradientPatternType', 'String'] : mode === 'dual' ? ['AciColor', 'AciColor', 'Entities.HatchGradientPatternType', 'String'] : [];
    add(`gradient/${kind}/${mode}`, 'gradient-model', [create(Gradient, args, 'p', signature),
      set('p', 'Shift', D(0.375)), set('p', 'IsDouble', true), set('p', 'Color1AciIndex', { short: 17 }), set('p', 'Color2AciIndex', null),
      set('p', 'Color1', color('Green')), call('p', 'Clone', [], 'q'), snap('q'), set('q', 'Tint', D(0.75)), snap('q'), snap('p'),
      set('q', 'Color2', color('Yellow')), snap('q'), set('q', 'SingleColor', true), snap('q'), call('q', 'ResetColor2AciIndex'), snap('q')]);
  }
  for (let first = 0; first < 3; first++) for (let second = 0; second < 3; second++) {
    const steps = [create(Gradient)];
    if (first) steps.push(set('p', 'Color1AciIndex', first === 1 ? { short: -32768 } : null));
    if (second) steps.push(set('p', 'Color2AciIndex', second === 1 ? { short: 32767 } : null));
    steps.push(call('p', 'Clone', [], 'q'), get('q', 'Color1', 'c1'), get('q', 'Color2', 'c2'), set('c1', 'Index', { short: 41 }), set('c2', 'Index', { short: 42 }),
      snap('p'), snap('q'), call('q', 'ResetColor1AciIndex'), call('q', 'ResetColor2AciIndex'), snap('q'), snap('p'));
    add(`gradient/aci/${first}/${second}`, 'gradient-model', steps);
  }
  for (const value of [-0, 0, Number.MIN_VALUE, 0.125, 0.5, 0.875, 0.9999999999999999, 1, NaN, Infinity, -Infinity, -Number.MIN_VALUE, -1, 1.0000000000000002, 2])
    for (const member of ['Shift', 'Tint']) add(`gradient/${member}/${Object.is(value, -0) ? '-0' : value}`, 'gradient-model', [
      create(Gradient, [color('Red'), D(0.4), gradientKind(0)]), set('p', member, D(value)), snap('p'), call('p', 'Clone', [], 'q'),
      set('q', 'Centered', true), snap('q'), set('q', 'Centered', false), snap('q'), snap('p')]);
  for (const member of ['Color1', 'Color2']) add(`gradient/null-${member}`, 'gradient-model', [
    create(Gradient, [color('Red'), D(0.4), gradientKind(0)]), set('p', member, null), snap('p')]);
  add('gradient/null-description-clone', 'gradient-model', [create(Gradient), set('p', 'Description', null), call('p', 'Clone')]);
  for (const value of [NaN, Infinity, -1, 2]) add(`gradient/rejected-constructor/${value}`, 'gradient-model', [create(Gradient, [color('Red'), D(value), gradientKind(0)])]);
  for (const [label, args, signature] of [
    ['single', [null, D(0.4), gradientKind(0)], ['AciColor', 'Double', 'Entities.HatchGradientPatternType']],
    ['first', [null, color('Blue'), gradientKind(0)], ['AciColor', 'AciColor', 'Entities.HatchGradientPatternType']],
    ['second', [color('Red'), null, gradientKind(0)], ['AciColor', 'AciColor', 'Entities.HatchGradientPatternType']]
  ]) add(`gradient/null-constructor/${label}`, 'gradient-model', [create(Gradient, args, 'p', signature)]);

  let seed = 0x48544348;
  const next = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed; };
  for (let i = 0; i < 64; i++) {
    const first = next() & 0xffffff, second = next() & 0xffffff, tint = (next() & 0xffff) / 65536, shift = (next() & 0xffff) / 65536;
    add(`gradient/seeded/${i}`, 'gradient-seeded', [
      { kind: 'call', type: 'AciColor', member: 'FromTrueColor', args: [I(first)], signature: ['Int32'], id: 'first' },
      { kind: 'call', type: 'AciColor', member: 'FromTrueColor', args: [I(second)], signature: ['Int32'], id: 'second' },
      create(Gradient, [R('first'), R('second'), gradientKind(i % 9)]), set('p', 'SingleColor', i % 2 === 0),
      set('p', 'Tint', D(tint)), set('p', 'Shift', D(shift)), set('p', 'Color1AciIndex', { short: (next() & 0xffff) - 32768 }),
      set('p', 'Color2AciIndex', i % 3 === 0 ? null : { short: (next() & 0xffff) - 32768 }), call('p', 'Clone', [], 'q'),
      set('q', 'SingleColor', true), set('q', 'Tint', D(1 - tint)), snap('p'), snap('q'), call('q', 'ResetColor1AciIndex'), snap('q')
    ]);
  }

  const texts = ['', '*P,desc', '*P,desc\n', '*P,desc\n\n', '*P,desc\n;stop\n', '*P,desc\n*Q,next\n', '*bad', '*bad\n*P,d\n0,0,0,0,1',
    '*P,d\n0,0,0,1', ' *P,  desc, commas\r\n-90,-0,1e-5,.1,1e-300,0,-.5\r\n', '*P,d\n0,0,0,0,1\n;stop\n90,0,0,1,1',
    '\u0085*P,desc\u0085\n0,0,0,0,1', '*P,d\n0,0,0,0,1\n\n90,0,0,1,1', '*P,first\n0,0,0,0,1\n*P,second\n90,0,0,1,1'];
  for (const token of ['', '0x10', '1e', '.', '1 2', 'oops', '1_000', '-0', '.5', '1.', '1e309', '-1e309', 'NaN', '+NaN', '-NaN', 'Infinity', '+Infinity', '-Infinity', ' \t1.25\r ', '\u00851\u0085']) texts.push(`*P,d\n0,${token},0,0,1`);
  texts.forEach((text, i) => add(`pat/syntax/${i}`, 'pat-text', [{ kind: 'pat-names', text }, { kind: 'pat-load', text, patternName: 'p', id: 'p' }]));
  for (const [name, lookup] of [['MiXeD', 'mixed'], ['σ', 'ς'], ['日本😀', '日本😀'], ['P', null], ['P', 'absent']])
    add(`pat/name/${name}/${lookup}`, 'pat-text', [{ kind: 'pat-load', text: `*${name},description\n0,0,0,0,1`, patternName: lookup }]);
  for (const fixture of patternFiles) {
    add(`pat/fixture/${fixture.name}/names`, 'pat-fixtures', [{ kind: 'pat-names', text: fixture.text }]);
    // Header enumeration is input discovery, not an implementation of PAT parsing.
    const names = fixture.text.split(/\r\n|\n|\r/).filter(line => line.startsWith('*')).map(line => line.slice(1, line.indexOf(',')));
    names.forEach((name, i) => add(`pat/fixture/${fixture.name}/${i}/${name}`, 'pat-fixtures', [
      { kind: 'pat-load', text: fixture.text, patternName: name, id: 'p' }, call('p', 'Clone', [], 'q'), { kind: 'pat-save', target: 'q', newLine }
    ]));
  }
  return corpus;
}
