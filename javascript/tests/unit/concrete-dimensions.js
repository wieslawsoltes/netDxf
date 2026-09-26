import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import * as api from '../../index.js';
import { concreteDimensionCorpus } from '../../tools/concrete-dimension-corpus.mjs';
import { dimensionConstructors, dimensionProperties, dimensionBuildOverloads } from '../../tools/concrete-dimension-schema.mjs';
import { concreteDimensionWire, concreteDimensionInvoke } from '../../tools/concrete-dimension-wire.mjs';
import { jsGeometry } from '../../tools/foundations-wire.mjs';
const manifest = JSON.parse(fs.readFileSync(new URL('../../dimension-port-manifest.json', import.meta.url)));

test('concrete dimension corpus retains every unique deterministic input and no expected outputs', () => {
  const corpus = concreteDimensionCorpus();
  assert.deepEqual(corpus, concreteDimensionCorpus());
  assert.equal(corpus.length, 1141); assert.equal(new Set(corpus.map(x => x.name)).size, 1141);
  assert.equal(corpus.reduce((n, x) => n + x.request.steps.length, 0), 7959);
  assert.ok(corpus.every(x => !Object.hasOwn(x, 'expected')));
  assert.equal(dimensionConstructors.length, 56);
});
test('dimension observation schema retains the exact public constructor and Build signatures', () => {
  const constructors = manifest.files.flatMap(file => file.members.filter(member => member.kind === 'constructor' && member.accessibility === 'Public').map(ctor => ({
    type: file.source.replace('netDxf/', '').replaceAll('/', '.').replace('.cs', ''),
    signature: ctor.signature.split(',').filter(Boolean).map(type => type === 'double' ? 'Double' : type), parameters: ctor.parameters,
  })));
  // Parameter names/types are source metadata, not expected operation results.
  assert.equal(constructors.length, dimensionConstructors.length);
  for (const item of dimensionConstructors) assert.ok(constructors.some(c => c.type === item.type && c.signature.join(',') === item.signature.join(',')), JSON.stringify(item));
  const builder = manifest.files.find(file => file.source.endsWith('/DimensionBlock.cs'));
  assert.deepEqual(builder.members.filter(item => item.name === 'Build').map(({ signature, implementation }) => ({ signature, implementation })), dimensionBuildOverloads);
});
for (const name of Object.keys(dimensionProperties)) test(`${name} Matrix4 overload retains affine semantics and ignores the final row`, () => {
  const first = new api[name](), second = new api[name]();
  first.TransformBy(new api.Matrix4(2, 0, 0, 5, 0, 3, 0, -2, 0, 0, 4, 7, 9, 8, 7, 6));
  second.TransformBy(new api.Matrix3(2, 0, 0, 0, 3, 0, 0, 0, 4), new api.Vector3(5, -2, 7));
  const fields = new Set([...dimensionProperties[name], 'DefinitionPoint', 'TextReferencePoint', 'Elevation', 'Normal']);
  for (const field of fields) {
    const a = first[field], b = second[field];
    if (typeof a?.ToArray === 'function') assert.deepEqual(a.ToArray(), b.ToArray(), field);
    else assert.deepEqual(a, b, field);
  }
});
test('concrete observation adapter excludes non-dimensions and observes all declared model properties', () => {
  assert.equal(concreteDimensionWire(new api.Line(), {}, x => x), undefined);
  for (const [name, properties] of Object.entries(dimensionProperties)) {
    const dimension = new api[name](), observed = concreteDimensionWire(dimension, { marker: true }, x => x);
    assert.equal(observed.common.marker, true);
    assert.ok(Object.hasOwn(observed.fields, 'DefinitionPoint'));
    for (const property of properties) assert.ok(Object.hasOwn(observed.fields, property), `${name}.${property}`);
  }
});
test('exact builder transport dispatches every declared overload without a derived-type guess', () => {
  for (const item of dimensionBuildOverloads) {
    const type = item.signature.split(',')[0].split('.').at(-1);
    const value = new api[type === 'Dimension' ? 'AlignedDimension' : type]();
    const args = item.signature.includes(',') ? [value, 'Exact'] : [value];
    const result = concreteDimensionInvoke(api.DimensionBlock, item.signature, args);
    assert.equal(result.handled, true); assert.ok(result.value instanceof api.Block);
  }
  assert.throws(() => concreteDimensionInvoke(api.DimensionBlock, 'missing', []), /Unmapped exact/);
});
test('reflection transport preserves missing-method and readonly-property failures, not JavaScript TypeError', () => {
  const readonly = jsGeometry({ steps: [{ kind: 'new', type: 'Entities.Angular2LineDimension', id: 'd' },
    { kind: 'set', target: 'd', member: 'CenterPoint', value: { new: 'Vector2', args: [1, 2] } }] });
  assert.deepEqual(readonly.at(-1), { ok: false, error: 'ArgumentException', param: null });
  const missing = jsGeometry({ steps: [{ kind: 'new', type: 'Entities.OrdinateDimension', id: 'd' },
    { kind: 'call', target: 'd', member: 'SetDimensionLinePosition', args: [{ new: 'Vector2', args: [1, 2] }] }] });
  assert.deepEqual(missing.at(-1), { ok: false, error: 'InvalidOperationException', param: null });
});
