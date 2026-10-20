import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ribbonPreferences, ribbonLimits, customizedRibbonTabs, customizedRibbonPanels, changeRibbonPreferences,
  parseRibbonCustomization, RibbonCustomization, floatingRibbonPosition } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-customization.mjs';
import { buildCommandCatalog } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/command-catalog.mjs';
import { renderCustomizedRibbonGroups, refreshCustomRibbonCommands, ribbonCustomizationTools } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-customization-ui.mjs';
import { all, byLabel, byText, installDom, tick, gate } from './ui-fixture.mjs';

const entries = [{ key: 'Home', name: 'Home' }, { key: 'Graph', name: 'Graph' }, { key: 'View', name: 'View' }];
const panels = [{ id: 'Draw', name: 'Draw' }, { id: 'Modify', name: 'Modify' }, { id: 'Layers', name: 'Layers' }];
const catalog = buildCommandCatalog();
const context = { entries, panels, catalog };
const change = (value, action) => changeRibbonPreferences(value, action, context);
const storage = () => { const values = new Map(); return { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value), values }; };

test('ribbon preferences discard payloads and malformed records while protecting Home and block controls', () => {
  const value = ribbonPreferences({ version: 1, apiKey: 'discard', tabs: [{ id: 'Home', visible: false }, { id: 'Graph', visible: false }, { id: 'Graph' }, null],
    panels: [{ tab: 'Block Editor', id: 'Definition', visible: false }], floating: [{ tab: 'Block Editor', panel: 'Definition', x: 10 }],
    customPanels: [{ id: 'user:p', tab: 'Home', name: 'Tools', commands: ['command:LINE', '<script>', 'command:LINE'], code: 'discard' }] });
  assert.deepEqual(value.tabs, [{ id: 'Home', visible: true }, { id: 'Graph', visible: false }]);
  assert.equal(value.panels[0].visible, true); assert.deepEqual(value.floating, []);
  assert.deepEqual(value.customPanels[0].commands, ['command:LINE']); assert(!JSON.stringify(value).includes('discard'));
  assert.equal(ribbonPreferences({ version: 2 }).tabs.length, 0);
});
test('all preference dimensions are bounded and detached', () => {
  const raw = { version: 1, tabs: Array.from({ length: 1000 }, (_, i) => ({ id: `tab:${i}` })),
    customPanels: Array.from({ length: 100 }, (_, i) => ({ id: `user:p${i}`, tab: 'Home', name: 'x'.repeat(200), commands: Array.from({ length: 100 }, (_, j) => `action:${j}`) })),
    floating: Array.from({ length: 50 }, (_, i) => ({ tab: 'Home', panel: `p${i}`, x: 1e30, y: -20 })) };
  const value = ribbonPreferences(raw); assert.equal(value.tabs.length, ribbonLimits.tabs);
  assert.equal(value.customPanels.length, 24); assert.equal(value.customPanels[0].commands.length, 32);
  assert.equal(value.customPanels[0].name.length, 80); assert.equal(value.floating.length, 16);
  assert.equal(value.floating[0].x, 100000); assert.equal(value.floating[0].y, 0);
  value.customPanels[0].commands[0] = 'changed'; assert.notEqual(raw.customPanels[0].commands[0], 'changed');
});
test('visibility and order retain dormant module preferences and contextual availability', () => {
  let value = ribbonPreferences({ version: 1, tabs: [{ id: 'module:pid', visible: false }] });
  value = change(value, { type: 'tab-move', id: 'Graph', offset: -1 });
  value = change(value, { type: 'tab-visible', id: 'View', visible: false });
  assert.deepEqual(customizedRibbonTabs(value, entries).map(tab => tab.key), ['Graph', 'Home']);
  assert(value.tabs.some(tab => tab.id === 'module:pid' && !tab.visible));
  const editing = [...entries, { key: 'Block Editor', name: 'Block Editor', contextual: 'block' }];
  assert.equal(customizedRibbonTabs(value, editing).at(-1).contextual, 'block');
  assert(!customizedRibbonTabs(value, entries).some(tab => tab.key === 'Block Editor'));
});
test('panel order and visibility are independent across tabs', () => {
  let value = change(null, { type: 'panel-move', tab: 'Home', id: 'Layers', offset: -1 });
  value = change(value, { type: 'panel-visible', tab: 'Home', id: 'Modify', visible: false });
  assert.deepEqual(customizedRibbonPanels(value, 'Home', panels).map(panel => panel.id), ['Draw', 'Layers']);
  assert.deepEqual(customizedRibbonPanels(value, 'Graph', panels).map(panel => panel.id), ['Draw', 'Modify', 'Layers']);
  assert.equal(customizedRibbonPanels(value, 'Home', panels, { includeHidden: true }).length, 3);
});
test('custom tabs and panels contain registered command identities and delete their dependent layout state', () => {
  let value = change(null, { type: 'custom-tab', id: 'user:design', name: 'Design' });
  value = change(value, { type: 'custom-panel', id: 'user:tools', tab: 'user:design', name: 'My tools', commands: ['command:LINE', 'action:undo'] });
  value = change(value, { type: 'float', tab: 'user:design', panel: 'user:tools', x: 4, y: 80 });
  assert.equal(customizedRibbonTabs(value, entries).at(-1).name, 'Design');
  assert.deepEqual(customizedRibbonPanels(value, 'user:design', [])[0].commands, ['command:LINE', 'action:undo']);
  assert.throws(() => change(value, { type: 'custom-panel', id: 'user:tools', tab: 'Home', name: 'Bad', commands: ['action:not-registered'] }), /registered/);
  assert.throws(() => change(value, { type: 'custom-panel', id: 'user:tools', tab: 'not-registered', name: 'Bad', commands: [] }), /available ribbon tab/);
  value = change(value, { type: 'delete-tab', id: 'user:design' });
  assert.equal(value.customPanels.length, 0); assert.equal(value.floating.length, 0); assert.equal(value.customTabs.length, 0);
});
test('customization limits refuse changes rather than claiming silently dropped additions', () => {
  let value = null;
  for (let i = 0; i < 8; i++) value = change(value, { type: 'custom-tab', id: `user:t${i}`, name: `Tab ${i}` });
  assert.throws(() => change(value, { type: 'custom-tab', id: 'user:extra', name: 'Extra' }), /at most 8/);
  for (let i = 0; i < 16; i++) value = change(value, { type: 'float', tab: 'Home', panel: `p${i}` });
  assert.throws(() => change(value, { type: 'float', tab: 'Home', panel: 'extra' }), /at most 16/);
  assert.throws(() => change(value, { type: 'float', tab: 'Home', panel: 'Block Editor' }), /contextual/);
});
test('storage and portable JSON preserve only schema data and report write failures', () => {
  const memory = storage(), events = [];
  const store = new RibbonCustomization({ storage: memory, changed: value => events.push(value) });
  store.change({ type: 'tab-visible', id: 'Graph', visible: false }, context);
  assert.equal(new RibbonCustomization({ storage: memory }).tabs(entries).length, 2); assert.equal(events.length, 1);
  const exported = JSON.stringify(store.snapshot()); assert.deepEqual(parseRibbonCustomization(exported), store.snapshot());
  assert.throws(() => parseRibbonCustomization('{"version":9}'), /version/);
  assert.throws(() => parseRibbonCustomization(' '.repeat(131073)), /128 KiB/);
  assert.throws(() => parseRibbonCustomization(JSON.stringify({ version: 1, unknown: 'é'.repeat(70000) })), /128 KiB/);
  const broken = new RibbonCustomization({ storage: { getItem: () => null, setItem: () => { throw Error(); } } });
  assert.throws(() => broken.restore(store.snapshot()), /could not be saved/); assert.deepEqual(broken.snapshot(), ribbonPreferences(null));
  broken.restore(store.snapshot(), { strict: false }); assert.deepEqual(broken.snapshot(), store.snapshot(), 'workspace restore remains useful in memory when persistence is blocked');
});
test('floating positions clamp to compact viewports without losing the title controls', () => {
  assert.deepEqual(floatingRibbonPosition({ x: 1000, y: 900 }, { width: 200, height: 120 }, { width: 390, height: 600 }), { x: 186, y: 476 });
  assert.deepEqual(floatingRibbonPosition({ x: -20, y: -10 }, { width: 900, height: 800 }, { width: 390, height: 600 }), { x: 4, y: 4 });
});
test('custom command controls re-resolve availability before every dispatch and never dispatch imported unknown IDs', async t => {
  const host = installDom(t), store = new RibbonCustomization({ storage: storage() });
  store.restore({ version: 1, customPanels: [{ id: 'user:p', tab: 'Home', name: 'My tools', commands: ['command:LINE', 'action:unknown'] }] });
  let busy = false, calls = 0; const errors = [];
  const callbacks = { catalog: () => catalog, state: () => ({ hasDocument: true, busy }), execute: () => calls++, report: error => errors.push(error) };
  const groups = renderCustomizedRibbonGroups(store, 'Home', [], callbacks); host.append(...groups);
  const control = all(host).find(node => node.getAttribute('data-ribbon-command-id') === 'command:LINE');
  const unknown = all(host).find(node => node.getAttribute('data-ribbon-command-id') === 'action:unknown');
  busy = true; await control.dispatch('click', { stopPropagation() {} }); await tick();
  assert.equal(calls, 0); assert.match(errors[0].message, /Wait/);
  busy = false; await control.dispatch('click', { stopPropagation() {} }); await tick(); assert.equal(calls, 1);
  await unknown.dispatch('click', { stopPropagation() {} }); await tick(); assert.equal(calls, 1); assert.match(errors.at(-1).message, /registered/);
});
test('editor changes, reset and imports are detached drafts until Apply; Cancel retains preferences', async t => {
  installDom(t); const store = new RibbonCustomization({ storage: storage() });
  let body, modal = gate();
  const editor = ribbonCustomizationTools({ store, entries: () => entries, panels: () => panels, catalog: () => catalog,
    dialog: (title, contents) => { body = contents[0]; return modal.promise; } });
  const pending = editor.customize(); const tabs = byLabel(body, 'Customize ribbon tab'); tabs.value = 'Graph'; await tabs.dispatch('change');
  const visible = byLabel(body, 'Show ribbon tab'); visible.checked = false; await visible.dispatch('change');
  assert.equal(store.tabs(entries).length, 3); modal.resolve(false); await pending; assert.equal(store.tabs(entries).length, 3);
  modal = gate(); const apply = editor.customize(); const tab = byLabel(body, 'Customize ribbon tab'); tab.value = 'Graph'; await tab.dispatch('change');
  const show = byLabel(body, 'Show ribbon tab'); show.checked = false; await show.dispatch('change'); modal.resolve(true); await apply;
  assert.equal(store.tabs(entries).length, 2);
  modal = gate(); const reset = editor.customize(); await byText(body, 'Reset ribbon draft').dispatch('click', { stopPropagation() {} });
  assert.equal(store.tabs(entries).length, 2); modal.resolve(true); await reset; assert.equal(store.tabs(entries).length, 3);
});
