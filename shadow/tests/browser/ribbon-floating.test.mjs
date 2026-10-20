import { test } from 'node:test';
import assert from 'node:assert/strict';
import { RibbonLayout } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-layout.mjs';
import { RibbonCustomization, ribbonPanelKey } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-customization.mjs';
import { group, el } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ui.mjs';
import { ribbonDom } from './ribbon-dom-fixture.mjs';

function fixture(t) {
  const dom = ribbonDom(t), saved = [], errors = [], store = new RibbonCustomization({ storage: { getItem: () => null, setItem: (_, text) => saved.push(JSON.parse(text)) } });
  const layout = new RibbonLayout(dom.panel, { customization: store, report: error => errors.push(error) });
  const sources = new Map(), entries = [{ key: 'Home' }, { key: 'Graph' }, { key: 'module:pid' }];
  const source = (tab, name = tab === 'Home' ? 'Draw' : 'Analysis', callback = () => {}) => {
    const command = el('button', { 'data-command': 'LINE', onclick: callback }, ['Line']);
    const layer = el('select', { 'aria-label': 'Current layer', onchange: callback }, [el('option', { value: 'A', text: 'A' })]);
    const node = group(name, [command, layer]); sources.set(tab, [node]); return { node, command, layer };
  };
  const draw = (tab = 'Home') => layout.set(sources.get(tab) || [], { tab, entries, resolveGroups: tab => sources.get(tab) || [] });
  const first = source('Home'); source('Graph'); source('module:pid');
  draw(); dom.disposeWith(() => layout.dispose());
  return { ...dom, layout, store, saved, errors, sources, source, entries, draw, ...first,
    frame: (tab = 'Home', name = 'Draw') => layout.floating.frames.get(ribbonPanelKey(tab, name)) };
}
test('tear-off reuses the actual command and resource controls, returns them, and never duplicates handlers', t => {
  const { layout, node, command, layer, frame, panel, store } = fixture(t); let actions = 0;
  command.addEventListener('click', () => actions++); layer.addEventListener('change', () => actions++);
  node.querySelector('.ribbon-tear-button').emit('click');
  assert.equal(frame().content.children[0], node); assert.equal(command.isConnected, true); assert.equal(panel.children.length, 0);
  command.emit('click'); layer.emit('change'); assert.equal(actions, 2); assert.equal(store.snapshot().floating.length, 1);
  frame().back.emit('click'); assert.equal(node.parentNode, panel); assert.equal(store.snapshot().floating.length, 0);
  assert.equal(document.activeElement, command, 'return focuses a visible command rather than the hidden full-mode launcher');
  command.emit('click'); assert.equal(actions, 3); layout.set([node], layout.context);
  node.querySelector('.ribbon-tear-button').emit('click'); assert.equal(store.snapshot().floating.length, 1);
  assert.equal(node.querySelectorAll('.ribbon-tear-button').length, 1);
});
test('floating panels survive tab switches and redraw with current context callbacks instead of stale copies', t => {
  const { layout, node, draw, source, frame, panel } = fixture(t); let stale = 0, current = 0;
  node.querySelector('[data-command]').addEventListener('click', () => stale++);
  layout.floatPanel(node); draw('Graph');
  assert.equal(frame().group, node); assert.equal(node.isConnected, true); assert.equal(panel.children[0].dataset.panel, 'Analysis');
  const next = source('Home', 'Draw', () => current++); draw('Graph');
  assert.equal(frame().group, next.node); assert.equal(node.isConnected, false);
  next.command.emit('click'); assert.equal(current, 1); assert.equal(stale, 0);
});
test('floating source controls have unique label targets and custom-panel renames update window names', t => {
  const { layout, node, layer, frame, source, draw } = fixture(t);
  node.dataset.ribbonPanel = 'user:tools'; layer.setAttribute('id', 'current-layer');
  const label = el('label', { for: 'current-layer', text: 'Layer' }); node.querySelector('.ribbon-tools').append(label);
  layout.floatPanel(node);
  const window = frame('Home', 'user:tools');
  assert.notEqual(layer.getAttribute('id'), 'current-layer'); assert.equal(label.getAttribute('for'), layer.getAttribute('id'));
  const renamed = source('Home', 'Renamed'); renamed.node.dataset.ribbonPanel = 'user:tools'; draw('Graph');
  assert.equal(window.element.getAttribute('aria-label'), 'Renamed floating ribbon panel');
  assert.equal(window.grip.getAttribute('aria-label'), 'Move floating Renamed panel');
  assert.equal(window.back.getAttribute('aria-label'), 'Return Renamed panel to ribbon');
});
test('disabled modules and removed contextual tabs suspend windows, preserve preferences and recover focus', t => {
  const { layout, entries, draw, frame, store, tab } = fixture(t);
  draw('module:pid'); const node = layout.groups[0]; layout.floatPanel(node);
  frame('module:pid', 'Analysis').grip.focus(); assert(layout.containsFocus());
  entries.find(entry => entry.key === 'module:pid').disabled = true; draw('Home');
  assert.equal(frame('module:pid', 'Analysis'), undefined); assert.equal(store.snapshot().floating.length, 1);
  assert.equal(document.activeElement, tab);
  entries.find(entry => entry.key === 'module:pid').disabled = false; draw('Home');
  assert(frame('module:pid', 'Analysis')); entries.pop(); draw('Home'); assert.equal(frame('module:pid', 'Analysis'), undefined);
});
test('hidden source tabs suspend their floating windows and retain positions for explicit re-show', t => {
  const { layout, node, entries, draw, frame, store } = fixture(t);
  layout.floatPanel(node); const saved = store.snapshot();
  entries[0].visible = false; draw('Graph'); assert.equal(frame(), undefined); assert.deepEqual(store.snapshot(), saved);
  entries[0].visible = true; draw('Graph'); assert(frame()); assert.deepEqual(store.snapshot(), saved);
});
test('dragging previews outside the ribbon and persists only on release', t => {
  const { node, layout, events, frame, store, panel } = fixture(t), before = store.snapshot();
  const title = node.querySelector('.ribbon-group-title');
  title.emit('pointerdown'); events.emit('pointermove', { clientX: 80, clientY: 120 });
  assert.equal(frame(), undefined, 'moving within the ribbon is not a tear-off');
  events.emit('pointermove', { clientX: 180, clientY: 330 });
  assert(frame()); assert.deepEqual(store.snapshot(), before); assert.equal(panel.capture, 1);
  events.emit('pointerup', { clientX: 200, clientY: 350 });
  assert.equal(store.snapshot().floating.length, 1); assert.equal(panel.capture, null); assert.equal(layout.floating.gesture, null);
});
test('Escape, pointercancel and lost capture restore exact pre-drag preferences and original attachment', t => {
  const { node, events, panel, frame, store } = fixture(t), before = store.snapshot();
  for (const type of ['keydown', 'pointercancel', 'lostpointercapture']) {
    const title = node.querySelector('.ribbon-group-title'); title.emit('pointerdown');
    events.emit('pointermove', { clientX: 180, clientY: 330 }); assert(frame());
    (type === 'lostpointercapture' ? panel : events).emit(type, { key: 'Escape' });
    assert.equal(frame(), undefined); assert.equal(node.parentNode, panel); assert.deepEqual(store.snapshot(), before); assert.equal(panel.capture, null);
  }
});
test('floating movement clamps keyboard and pointer coordinates, forwards shortcuts and cancels safely', t => {
  const { node, layout, events, frame, store, windowEvents } = fixture(t);
  layout.floatPanel(node); frame().grip.focus(); const before = store.snapshot();
  frame().grip.emit('pointerdown'); events.emit('pointermove', { clientX: 9000, clientY: 9000 }); events.emit('keydown', { key: 'Escape' });
  assert.deepEqual(store.snapshot(), before);
  let prevented = 0; frame().grip.emit('keydown', { key: 'ArrowRight', ctrlKey: true, preventDefault: () => prevented++ });
  assert.equal(prevented, 0); frame().grip.emit('keydown', { key: 'ArrowRight', shiftKey: true });
  assert.equal(store.snapshot().floating[0].x, before.floating[0].x + 1);
  assert.equal(document.activeElement, frame().grip);
  globalThis.innerWidth = 390; globalThis.innerHeight = 300; windowEvents.emit('resize');
  const box = frame().element.getBoundingClientRect(); assert(box.right <= 386); assert(box.bottom <= 296);
  frame().grip.emit('pointerdown'); events.emit('pointermove', { clientX: -999, clientY: -999 }); events.emit('pointerup', { clientX: -999, clientY: -999 });
  assert.equal(store.snapshot().floating[0].x, 4); assert.equal(store.snapshot().floating[0].y, 4);
});
test('all compact modes expose tear-off from an opened panel flyout, preserving the control objects', t => {
  const { layout, node, command, frame } = fixture(t);
  for (const mode of ['buttons', 'titles', 'tabs']) {
    layout.setMode(mode); layout.open(node);
    const float = layout.popup.floatButton; assert(float); float.emit('click');
    assert.equal(layout.popup, null); assert.equal(frame().group, node); assert.equal(command.isConnected, true);
    frame().back.emit('click');
  }
});
test('contextual save/discard panels cannot be hidden in floating windows and dispose removes transient UI', t => {
  const { layout, source, draw, entries, store, body, events } = fixture(t);
  entries.push({ key: 'Block Editor' }); const { node } = source('Block Editor', 'Definition'); draw('Block Editor');
  assert.equal(node.querySelector('.ribbon-launcher').getAttribute('aria-keyshortcuts'),'ArrowDown','protected controls must not advertise an unavailable float shortcut');
  assert.equal(node.querySelector('.ribbon-tear-button'), null); layout.floatPanel(node); assert.equal(store.snapshot().floating.length, 0);
  draw('Home'); layout.floatPanel(layout.groups[0]); assert.equal(body.querySelectorAll('.ribbon-floating').length, 1);
  layout.dispose(); assert.equal(body.querySelectorAll('.ribbon-floating').length, 0);
  assert.equal(events.handlers.get('pointermove').length, 0);
});
