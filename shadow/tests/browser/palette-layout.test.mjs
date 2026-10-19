import { test } from 'node:test';
import assert from 'node:assert/strict';
import { changePaletteLayout, paletteDockWidths, paletteLayout, paletteRect, resizePaletteRect } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-layout.mjs';

const ids = ['Properties', 'Graph', 'Layers'], bounds = { width: 1280, height: 600 };
const update = (value, action) => changePaletteLayout(value, action, bounds);

test('legacy left/right, hidden and width settings migrate once into a bounded independent layout', () => {
  const value = paletteLayout(null, ids, { left: true, hidden: true, width: 500 });
  assert.equal(value.version, 2); assert.equal(value.hidden, true); assert.equal(value.active, 'Properties');
  assert.equal(value.panels.Properties.open, true); assert.equal(value.panels.Graph.open, false);
  assert.equal(value.panels.Properties.side, 'left'); assert.equal(value.panels.Properties.width, 500);
  const saved = paletteLayout({ ...value, hidden: false }, ids, { left: false, hidden: true, width: 220 });
  assert.equal(saved.panels.Properties.side, 'left'); assert.equal(saved.panels.Properties.width, 500); assert.equal(saved.hidden, false);
});

test('serialized preferences retain only registered palette layout fields', () => {
  const source = { version: 2, active: 'unknown', locked: 'true', hidden: 'true', apiKey: 'never-store', panels: {
    Properties: { open: true, mode: 'unsafe', side: 'middle', x: Infinity, y: -10000, width: 9e10, height: NaN, token: 'never-store' },
    Unregistered: { open: true }, Graph: null } };
  const value = paletteLayout(source, ids);
  assert.equal(value.locked, false); assert.equal(value.hidden, false); assert.equal(value.active, 'Properties');
  assert.deepEqual(Object.keys(value.panels), ids); assert.equal(value.panels.Properties.mode, 'dock');
  assert.equal(value.panels.Properties.side, 'right'); assert.equal(value.panels.Properties.y, 0);
  assert.equal(value.panels.Properties.width, 1600); assert.equal(value.panels.Properties.height, 440);
  assert(!JSON.stringify(value).includes('never-store')); assert(!JSON.stringify(value).includes('Unregistered'));
  assert.throws(() => paletteLayout(null, [])); assert.throws(() => paletteLayout(null, ['same', 'same']));
});

test('several palettes open independently and preserve separate sides, floating positions and closed state', () => {
  let value = paletteLayout(null, ids);
  value = update(value, { type: 'show', id: 'Graph' }); value = update(value, { type: 'place', id: 'Graph', mode: 'dock', side: 'left' });
  assert.equal(value.panels.Properties.open, true); assert.equal(value.panels.Properties.side, 'right'); assert.equal(value.panels.Graph.side, 'left');
  value = update(value, { type: 'place', id: 'Layers', mode: 'float' }); value = update(value, { type: 'rect', id: 'Layers', rect: { x: 480, y: 100, width: 320, height: 340 } });
  value = update(value, { type: 'close', id: 'Layers' }); value = paletteLayout(JSON.parse(JSON.stringify(value)), ids);
  assert.equal(value.panels.Layers.open, false); value = update(value, { type: 'show', id: 'Layers' });
  assert.equal(value.panels.Layers.mode, 'float'); assert.equal(value.panels.Layers.x, 480); assert.equal(value.panels.Layers.height, 340);
});

test('auto-hide remembers its side without changing other palettes or losing reopen geometry', () => {
  let value = paletteLayout(null, ids);
  value = update(value, { type: 'place', id: 'Graph', mode: 'auto', side: 'left' });
  assert.equal(value.panels.Graph.mode, 'auto'); assert.equal(value.panels.Graph.side, 'left'); assert.equal(value.panels.Properties.mode, 'dock');
  value = update(value, { type: 'close', id: 'Graph' }); value = update(value, { type: 'show', id: 'Graph' });
  assert.equal(value.panels.Graph.mode, 'auto'); assert.equal(value.panels.Graph.side, 'left');
});

test('layout lock blocks dragging, resizing and mode/side changes while close and reopen stay available', () => {
  let value = update(paletteLayout(null, ids), { type: 'lock', value: true });
  for (const action of [{ type: 'place', id: 'Properties', mode: 'float' }, { type: 'rect', id: 'Properties', rect: { x: 200 } }, { type: 'dock-width', side: 'right', width: 600 }])
    assert.equal(update(value, action), value);
  value = update(value, { type: 'close', id: 'Properties' }); assert.equal(value.panels.Properties.open, false);
  value = update(value, { type: 'show', id: 'Properties' }); assert.equal(value.panels.Properties.open, true); assert.equal(value.locked, true);
  value = update(value, { type: 'lock', value: false }); value = update(value, { type: 'place', mode: 'float' }); assert.equal(value.panels.Properties.mode, 'float');
});

test('toggle hides all windows without forgetting their modes and reopens the active palette after all close', () => {
  let value = update(paletteLayout(null, ids), { type: 'show', id: 'Graph' });
  value = update(value, { type: 'toggle' }); assert(value.hidden); assert(value.panels.Graph.open); assert(value.panels.Properties.open);
  value = update(value, { type: 'toggle' }); assert(!value.hidden);
  value = update(value, { type: 'close', id: 'Properties' }); value = update(value, { type: 'close', id: 'Graph' });
  value = update(value, { type: 'toggle' }); assert(value.panels.Graph.open); assert.equal(value.hidden, false);
});

test('restored floating frames stay inside desktop, narrow, rotated and temporarily zero-sized workspaces', () => {
  for (const size of [{ width: 390, height: 844 }, { width: 1200, height: 290 }, { width: 0, height: 0 }, { width: 110, height: 95 }]) {
    const rect = paletteRect({ x: 3000, y: 3000, width: 1400, height: 1300 }, size);
    assert(rect.width > 0 && rect.height > 0); assert(rect.x >= 0 && rect.y >= 0);
    assert(rect.x + rect.width <= Math.max(1, size.width)); assert(rect.y + rect.height <= Math.max(1, size.height));
  }
});

test('floating resizing holds the top-left corner while bounding the right and bottom handles', () => {
  const initial = { x: 100, y: 80, width: 300, height: 260 };
  assert.deepEqual(resizePaletteRect(initial, 9000, 9000, { width: 800, height: 600 }), { x: 100, y: 80, width: 700, height: 520 });
  assert.deepEqual(resizePaletteRect(initial, -9000, -9000, { width: 800, height: 600 }), { x: 100, y: 80, width: 220, height: 160 });
  assert.deepEqual(resizePaletteRect(initial, 1, 1, { width: 0, height: 0 }), { x: 0, y: 0, width: 1, height: 1 });
});

test('two dock columns share a bounded budget and narrow workspaces switch to edge overlays', () => {
  const value = paletteDockWidths(550, 600, 1000, 56);
  assert.equal(value.overlay, false); assert(value.left + value.right + 56 <= 720); assert(value.left > 0 && value.right > 0);
  assert.deepEqual(paletteDockWidths(310, 310, 390, 56), { left: 0, right: 0, overlay: true });
  assert.deepEqual(paletteDockWidths(0, 310, 1200), { left: 0, right: 310, overlay: false });
});

test('dock width changes affect that column while preserving floating and opposite-side geometry', () => {
  let value = update(paletteLayout(null, ids), { type: 'show', id: 'Graph' });
  value = update(value, { type: 'place', id: 'Layers', mode: 'float' });
  value = update(value, { type: 'dock-width', side: 'right', width: 480 });
  assert.equal(value.panels.Properties.width, 480); assert.equal(value.panels.Graph.width, 480); assert.equal(value.panels.Layers.width, 310);
  assert.throws(() => update(value, { type: 'show', id: 'toString' }), /Unknown/);
  assert.throws(() => update(value, { type: 'place', id: 'Graph', mode: 'script' }), /supported/);
});
