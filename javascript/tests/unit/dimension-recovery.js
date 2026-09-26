import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { DimensionChecks } from '../../runtime/DimensionStyleFields.js';

const names = ['AlignedDimension', 'LinearDimension', 'Angular2LineDimension', 'Angular3PointDimension',
  'ArcLengthDimension', 'DiametricDimension', 'RadialDimension', 'OrdinateDimension'];
for (const name of names) {
  test(`${name} is exported as the standalone model and builds an owned block`, async () => {
    const standalone = await import(`../../netDxf/Entities/${name}.js`);
    assert.equal(api[name], standalone[name]);
    const dimension = new api[name]();
    assert.ok(dimension instanceof api.Dimension);
    const block = api.DimensionBlock.Build(dimension, 'Recovered');
    assert.equal(block.Name, 'Recovered');
    assert.ok(block.Entities.Count > 0);
    for (const entity of block.Entities) assert.equal(entity.Owner, block);
    assert.equal(dimension.Block, null); // Build returns a block; it does not adopt it.
  });
  test(`${name} clones independently and regenerates an assigned block`, () => {
    const dimension = new api[name]();
    dimension.Block = api.DimensionBlock.Build(dimension, 'Original');
    const copy = dimension.Clone();
    assert.notEqual(copy.Style, dimension.Style);
    assert.equal(copy.Block, null);
    const old = dimension.Block;
    let events = 0;
    dimension.DimensionBlockChanged.Add((sender, args) => {
      assert.equal(sender, dimension); assert.equal(args.OldValue, old); events++;
    });
    dimension.Update();
    assert.equal(events, 1);
    assert.notEqual(dimension.Block, old);
    assert.equal(dimension.Block.Name, 'Original');
  });
}
for (const [name, prefix] of [['RadialDimension', 'R'], ['DiametricDimension', 'Ø']]) {
  test(`${name} keeps its default label for an explicitly boxed empty prefix`, () => {
    const dimension = new api[name](), box = new api.BoxedString('');
    const item = new api.DimensionStyleOverride(api.DimensionStyleOverrideType.DimPrefix, box);
    dimension.StyleOverrides.Add(item);
    const texts = Array.from(api.DimensionBlock.Build(dimension).Entities)
      .filter(entity => entity instanceof api.MText).map(entity => entity.Value);
    assert.ok(texts.length > 0);
    assert.ok(texts.every(text => text.startsWith(prefix)), JSON.stringify(texts));
    assert.equal(item.Value, box); // The original object-valued override retains identity.
  });
}
test('dimension typed string fields unwrap text without changing the input reference adapter', () => {
  const box = new api.BoxedString('custom');
  assert.equal(DimensionChecks.EmptyString(box), 'custom');
  assert.equal(DimensionChecks.EmptyString(null), '');
  const style = api.DimensionStyle.Default;
  style.DimPrefix = box; style.DimSuffix = new api.BoxedString('mm');
  assert.equal(style.DimPrefix, 'custom'); assert.equal(style.DimSuffix, 'mm');
  assert.equal(box.Value, 'custom');
});
