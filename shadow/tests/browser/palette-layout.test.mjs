import { test } from 'node:test';
import assert from 'node:assert/strict';
import { changePaletteLayout, paletteDockWidths, paletteDropSide, paletteDropPreview, paletteLayout, paletteRect, resizePaletteRect, paletteGroups, paletteGroupFor, paletteStackHeights } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-layout.mjs';
import { workspacePresets, workspaceProfileLayout } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profiles.mjs';

const ids = ['Properties', 'Graph', 'Layers'], bounds = { width: 1280, height: 600 };
const update = (value, action) => changePaletteLayout(value, action, bounds);

test('legacy left/right, hidden and width settings migrate once into a bounded independent layout', () => {
  const value = paletteLayout(null, ids, { left: true, hidden: true, width: 500 });
  assert.equal(value.version, 3); assert.equal(value.hidden, true); assert.equal(value.active, 'Properties');
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

test('drop targets accept only workspace-local side edges, never another dock or outside the workspace', () => {
  assert.equal(paletteDropSide({ x: 0, y: 0 }, bounds), 'left');
  assert.equal(paletteDropSide({ x: 40, y: 300 }, bounds), 'left');
  assert.equal(paletteDropSide({ x: 1240, y: 300 }, bounds), 'right');
  assert.equal(paletteDropSide({ x: 1280, y: 600 }, bounds), 'right');
  for (const point of [{ x: 41, y: 300 }, { x: 900, y: 300 }, { x: -1, y: 300 }, { x: 1281, y: 300 }, { x: 5, y: -1 }, { x: 5, y: 601 }, { x: NaN, y: 5 }])
    assert.equal(paletteDropSide(point, bounds), null);
  assert.equal(paletteDropSide({ x: 1, y: 1 }, { width: 0, height: 100 }), null);
  assert.equal(paletteDropSide({ x: 1, y: 1 }, { width: Infinity, height: 100 }), null);
  assert.equal(paletteDropSide({ x: 49, y: 1 }, { width: 50, height: 100 }), 'right');
});

test('drop preview projects the shared column budget without mutating independent palette state', () => {
  let layout = update(paletteLayout(null, ids), { type: 'place', id: 'Graph', mode: 'float' });
  layout = update(layout, { type: 'place', id: 'Layers', mode: 'auto', side: 'left' });
  layout.panels.Properties.width = 600; layout.panels.Graph.width = 550;
  const before = JSON.stringify(layout), size = { width: 1000, height: 600 };
  const preview = paletteDropPreview(layout, 'Graph', 'left', size);
  const columns = paletteDockWidths(550, 600, size.width, 28);
  assert.deepEqual(preview, { x: 28, y: 0, width: columns.left, height: 600, side: 'left', compact: false });
  assert.equal(JSON.stringify(layout), before);
  assert.equal(paletteDropPreview(layout, 'Graph', 'top', size), null);
  assert.equal(paletteDropPreview(layout, 'Missing', 'left', size), null);
  assert.equal(paletteDropPreview({ ...layout, locked: true }, 'Graph', 'left', size), null);
});

test('compact drop previews fit between edge strips without reserving permanent drawing columns', () => {
  const layout = update(paletteLayout(null, ids), { type: 'place', id: 'Graph', mode: 'float' });
  for (const width of [390, 699, 110, 1]) for (const side of ['left', 'right']) {
    const preview = paletteDropPreview(layout, 'Graph', side, { width, height: 240 }, 32);
    assert.equal(preview.compact, true); assert.equal(preview.y, 0); assert.equal(preview.height, 240);
    assert(preview.width > 0 && preview.x >= 0 && preview.x + preview.width <= width);
    if (width === 390) assert.equal(preview.x, side === 'left' ? 32 : 48);
  }
});

test('v2 snapshots and named profiles migrate to ordered singleton groups without changing placement', () => {
  const old = { version: 2, active: 'Graph', locked: true, hidden: false, panels: {
    Properties: { open: true, mode: 'dock', side: 'right', width: 480 },
    Graph: { open: true, mode: 'dock', side: 'left' }, Layers: { open: true, mode: 'float', x: 420, y: 80 }
  } };
  const migrated = workspaceProfileLayout({ palettes: old }, ids).palettes;
  assert.equal(migrated.version, 3); assert.equal(migrated.locked, true); assert.equal(migrated.active, 'Graph');
  assert.deepEqual(migrated.groups.left[0].tabs, ['Graph']); assert.deepEqual(migrated.groups.right[0].tabs, ['Properties']);
  assert.equal(migrated.panels.Properties.width, 480); assert.equal(migrated.panels.Layers.x, 420);
  assert.equal(paletteGroupFor(migrated, 'Layers'), null); assert.equal(migrated.panels.Graph.allowDocking, true);
});

test('group normalization rejects duplicate, unknown, floating and wrong-side membership with bounded metadata', () => {
  const source = paletteLayout(null, ids);
  source.panels.Graph.open = true; source.panels.Graph.side = 'left';
  source.panels.Layers.allowDocking = false;
  source.groups = { left: [{ tabs: ['Graph', 'Graph', 'Missing', 'Properties', 'Layers'], active: 'Missing', weight: Infinity }],
    right: [{ tabs: ['Graph', 'Properties'], active: 'Graph', weight: -10 }] };
  const value = paletteLayout(source, ids);
  assert.deepEqual(value.groups.left, [{ tabs: ['Graph'], active: 'Graph', weight: 1 }]);
  assert.deepEqual(value.groups.right, [{ tabs: ['Properties'], active: 'Properties', weight: 0.01 }]);
  assert.equal(value.panels.Layers.mode, 'float');
  const preset = workspacePresets(ids)[1], restored = paletteLayout(preset.layout.palettes, ids);
  assert.equal(paletteGroupFor(restored, 'Graph').side, 'left', 'direct preset panel-side overrides remain authoritative');
});

test('grouping moves into the target side, switches the active tab, and retains closed-tab placement', () => {
  let value = update(paletteLayout(null, ids), { type: 'place', id: 'Graph', mode: 'dock', side: 'left' });
  value = update(value, { type: 'group', id: 'Properties', target: 'Graph' });
  assert.equal(value.panels.Properties.side, 'left');
  assert.deepEqual(paletteGroups(value, 'left')[0].tabs, ['Graph', 'Properties']);
  assert.equal(paletteGroups(value, 'left')[0].active, 'Properties');
  value = update(value, { type: 'close', id: 'Properties' });
  assert.equal(paletteGroups(value, 'left')[0].active, 'Graph');
  assert.deepEqual(value.groups.left[0].tabs, ['Graph', 'Properties']);
  value = update(value, { type: 'show', id: 'Properties' });
  assert.equal(paletteGroups(value, 'left')[0].active, 'Properties');
  assert.equal(JSON.stringify(paletteLayout(JSON.parse(JSON.stringify(value)), ids)), JSON.stringify(value));
});

test('group and tab ordering skip closed panes and detaching preserves independent membership', () => {
  let value = update(paletteLayout(null, ids), { type: 'show', id: 'Layers' });
  value = update(value, { type: 'move-group', id: 'Layers', delta: -1 });
  assert.deepEqual(paletteGroups(value, 'right').map(group => group.key), ['Layers', 'Properties']);
  value = update(value, { type: 'group', id: 'Graph', target: 'Layers' });
  value = update(value, { type: 'move-tab', id: 'Graph', delta: -1 });
  assert.deepEqual(paletteGroups(value, 'right')[0].tabs, ['Graph', 'Layers']);
  value = update(value, { type: 'detach', id: 'Layers' });
  assert.deepEqual(paletteGroups(value, 'right').map(group => group.tabs), [['Graph'], ['Layers'], ['Properties']]);
  value = update(value, { type: 'place', id: 'Graph', mode: 'float' });
  assert.equal(paletteGroupFor(value, 'Graph'), null);
});

test('stack sizing conserves adjacent height, preserves other groups and scrolls when minimum sizes cannot fit', () => {
  let value = update(update(paletteLayout(null, ids), { type: 'show', id: 'Graph' }), { type: 'show', id: 'Layers' });
  const height = 900, before = paletteStackHeights(paletteGroups(value, 'right'), height);
  value = update(value, { type: 'resize-groups', id: 'Properties', nextId: 'Graph', firstHeight: before[0] + 80, height });
  const after = paletteStackHeights(paletteGroups(value, 'right'), height);
  assert(Math.abs(after[0] - before[0] - 80) < 1e-8); assert(Math.abs(after[1] - before[1] + 80) < 1e-8);
  assert(Math.abs(after[2] - before[2]) < 1e-8); assert(Math.abs(after.reduce((a, b) => a + b) + 10 - height) < 1e-8);
  value = update(value, { type: 'resize-groups', id: 'Properties', nextId: 'Graph', firstHeight: -9000, height });
  assert.equal(paletteStackHeights(paletteGroups(value, 'right'), height)[0], 160);
  assert.deepEqual(paletteStackHeights(paletteGroups(value, 'right'), 100), [160, 160, 160]);
});

test('Allow Docking disables dock, anchor and grouping while preserving floating movement and size', () => {
  let value = update(paletteLayout(null, ids), { type: 'allow-docking', id: 'Properties', value: false });
  assert.equal(value.panels.Properties.mode, 'float'); assert.equal(paletteGroupFor(value, 'Properties'), null);
  for (const mode of ['dock', 'auto']) assert.equal(update(value, { type: 'place', id: 'Properties', mode }), value);
  assert.equal(paletteDropPreview(value, 'Properties', 'left', bounds), null);
  assert.throws(() => update(value, { type: 'group', id: 'Properties', target: 'Graph' }), /allows docking/);
  value = update(value, { type: 'rect', id: 'Properties', rect: { x: 200, y: 100 } });
  assert.equal(value.panels.Properties.x, 200);
  value = update(value, { type: 'allow-docking', id: 'Properties', value: true });
  assert.equal(value.panels.Properties.mode, 'float', 'reenabling permission does not unexpectedly dock');
  value = update(value, { type: 'place', id: 'Properties', mode: 'auto' }); assert.equal(value.panels.Properties.mode, 'auto');
});

test('layout lock protects group membership, ordering, weights and docking permissions but permits tab selection', () => {
  let value = update(paletteLayout(null, ids), { type: 'group', id: 'Graph', target: 'Properties' });
  value = update(value, { type: 'lock', value: true });
  for (const action of [{ type: 'group', id: 'Layers', target: 'Graph' }, { type: 'detach', id: 'Graph' },
    { type: 'move-tab', id: 'Graph', delta: -1 }, { type: 'move-group', id: 'Graph', delta: 1 },
    { type: 'resize-groups', id: 'Graph', firstHeight: 200 }, { type: 'allow-docking', id: 'Graph', value: false }])
    assert.equal(update(value, action), value);
  value = update(value, { type: 'show', id: 'Properties' });
  assert.equal(paletteGroups(value, 'right')[0].active, 'Properties'); assert.equal(value.locked, true);
});
