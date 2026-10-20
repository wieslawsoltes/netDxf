import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PaletteDock, handlePaletteEscape } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-docking.mjs';
import { Element, all, installDom } from './ui-fixture.mjs';

function manager(keepOpen) {
  const value = Object.create(PaletteDock.prototype);
  value.autoId = 'Graph'; value.keepOpen = keepOpen; value.hideTimer = null; value.applied = 0;
  value.apply = () => value.applied++;
  return value;
}

test('palette keyboard controllers forward Studio shortcuts and ordinary input instead of swallowing them', () => {
  for (const modifier of ['ctrlKey', 'metaKey']) for (const key of ['s', 'o', 'n', 'z', 'y', '0', '1', '9', ...Array.from({ length: 12 }, (_, index) => `F${index + 1}`), 'a', 'Tab']) {
    let stopped = false, prevented = false, closed = false;
    const event = { key, [modifier]: true, stopPropagation: () => stopped = true, preventDefault: () => prevented = true };
    assert.equal(handlePaletteEscape(event, () => { closed = true; return true; }), false);
    assert.equal(stopped, false, `${modifier}+${key} must bubble`); assert.equal(prevented, false); assert.equal(closed, false);
  }
});

test('Escape only stops propagation when a palette menu, anchor or gesture consumes it', () => {
  let stopped = 0, prevented = 0; const event = { key: 'Escape', stopPropagation: () => stopped++, preventDefault: () => prevented++ };
  assert.equal(handlePaletteEscape(event, () => false), false); assert.equal(stopped, 0);
  assert.equal(handlePaletteEscape(event, () => true), true); assert.equal(stopped, 1); assert.equal(prevented, 1);
});

test('active viewport picking keeps its anchored palette open on implicit outside dismissal', () => {
  let picking = true; const value = manager(id => id === 'Graph' && picking);
  assert.equal(value.hideAuto({ implicit: true }), false); assert.equal(value.autoId, 'Graph'); assert.equal(value.applied, 0);
  picking = false; assert.equal(value.hideAuto({ implicit: true }), true); assert.equal(value.autoId, null); assert.equal(value.applied, 1);
});

test('explicit Escape, close and mode changes may dismiss an anchored palette during picking', () => {
  const value = manager(() => true);
  assert.equal(value.hideAuto(), true); assert.equal(value.autoId, null); assert.equal(value.applied, 1);
});

test('hover then click and repeated edge clicks keep the palette open and focus its selector', () => {
  const value = manager(() => false); let focusCount = 0, refreshCount = 0;
  value.autoId = null; value.frames = new Map([['Graph', { selector: { focus: () => focusCount++ } }]]);
  value.reveal = id => { if (value.autoId !== id) refreshCount++; value.autoId = id; };
  value.reveal('Graph'); // Pointer hover reveals before the browser delivers click.
  value.activateAnchor('Graph'); value.activateAnchor('Graph');
  assert.equal(value.autoId, 'Graph'); assert.equal(focusCount, 2); assert.equal(refreshCount, 1); assert.equal(value.applied, 0);
});

test('delayed pointer or focus leave checks current pick state instead of the scheduling-time state', () => {
  const oldTimer = globalThis.setTimeout, oldClear = globalThis.clearTimeout, oldDocument = globalThis.document;
  let queued, picking = false;
  globalThis.setTimeout = callback => { queued = callback; return 1; }; globalThis.clearTimeout = () => {};
  globalThis.document = { activeElement: {} };
  try {
    const value = manager(() => picking);
    value.frames = new Map([['Graph', { hovered: false, edgeHovered: false, element: { contains: () => false }, edge: { contains: () => false } }]]);
    value.queueHide(); picking = true; queued(); assert.equal(value.autoId, 'Graph');
    value.queueHide(); picking = false; queued(); assert.equal(value.autoId, null);
  } finally { globalThis.setTimeout = oldTimer; globalThis.clearTimeout = oldClear; if (oldDocument === undefined) delete globalThis.document; else globalThis.document = oldDocument; }
});

// Exercise the real controller and layout application with pointer capture, reparenting focus loss,
// and renderer lifetimes. Browser tests separately verify actual CSS and hit testing.
class DockElement extends Element {
  constructor(tag) {
    super(tag); this.style = { removeProperty(name) { delete this[name]; } }; this.capture = null;
    this.classList = {
      contains: name => (this.className || '').split(' ').includes(name),
      add: (...names) => { for (const name of names) this.classList.toggle(name, true); },
      remove: (...names) => { for (const name of names) this.classList.toggle(name, false); },
      toggle: (name, enabled) => { const names = new Set((this.className || '').split(' ').filter(Boolean)); enabled ??= !names.has(name); if (enabled) names.add(name); else names.delete(name); this.className = [...names].join(' '); }
    };
  }
  get firstElementChild() { return this.children.find(value => value instanceof Element); }
  get lastElementChild() { return this.children.findLast(value => value instanceof Element); }
  remove() { if (this.isConnected && this.contains(document.activeElement)) document.activeElement = document.body; super.remove(); }
  insertBefore(value, before) { value.remove(); value.parentNode = this; this.children.splice(this.children.indexOf(before), 0, value); }
  after(...values) { for (const value of values.reverse()) { value.remove(); value.parentNode = this.parentNode; this.parentNode.children.splice(this.parentNode.children.indexOf(this) + 1, 0, value); } }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  removeAttribute(name) { delete this.attributes[name]; }
  querySelectorAll(selector) {
    return all(this).slice(1).filter(node => selector.startsWith('.') ? node.classList.contains(selector.slice(1)) : node.tagName.toLowerCase() === selector);
  }
  getBoundingClientRect() { return this.rect || { left: 100, top: 50, width: parseFloat(this.style.width) || 310, height: 440 }; }
  removeEventListener(name, callback) { this.handlers.set(name, (this.handlers.get(name) || []).filter(value => value !== callback)); }
  setPointerCapture(id) { this.capture = id; }
  hasPointerCapture(id) { return this.capture === id; }
  releasePointerCapture(id) { this.capture = null; this.emit('lostpointercapture', { pointerId: id }); }
  emit(type, value = {}) {
    const event = { type, target: this, button: 0, pointerId: 7, clientX: 420, clientY: 180, preventDefault() {}, stopPropagation() {}, ...value };
    for (const callback of [...(this.handlers.get(type) || [])]) callback(event);
    return event;
  }
}
function docking(t, { width = 1280, height = 600, chatWidth = 0 } = {}) {
  const cleanups = []; installDom({ after: callback => cleanups.push(callback) });
  const globals = ['getComputedStyle', 'ResizeObserver', 'requestAnimationFrame', 'cancelAnimationFrame'].map(key => [key, globalThis[key]]);
  let dock;
  t.after(() => {
    try { dock?.dispose(); } finally {
      for (const cleanup of cleanups) cleanup();
      for (const [key, value] of globals) if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  });
  const body = new DockElement('main'); body.attached = true; const workspace = new DockElement('div'), viewport = new DockElement('div');
  workspace.rect = { left: 100, top: 50, width, height }; viewport.className = 'viewport'; workspace.append(viewport); body.append(workspace);
  if (chatWidth) { const chat = new DockElement('aside'); chat.rect = { width: chatWidth }; workspace.append(chat); }
  const events = new DockElement('document');
  Object.assign(document, { body, activeElement: body, createElement: tag => new DockElement(tag), createTextNode: text => String(text),
    addEventListener: (...args) => events.addEventListener(...args), removeEventListener: (...args) => events.removeEventListener(...args) });
  globalThis.getComputedStyle = node => ({ width: '28px', position: node.classList.contains('pd-overlay') ? 'absolute' : 'static' });
  globalThis.ResizeObserver = class { observe() {} disconnect() {} };
  globalThis.requestAnimationFrame = () => 0; globalThis.cancelAnimationFrame = () => {};
  const stored = [], active = [], hidden = [], cleaned = [], rendered = [];
  dock = new PaletteDock({ workspace, palettes: ['Properties', 'Graph', 'Layers'], loadStyles: false,
    storage: { getItem: () => null, setItem: (key, text) => stored.push(JSON.parse(text)) },
    render: id => { rendered.push(id); return () => cleaned.push(id); }, onActiveChange: id => active.push(id), onHide: id => hidden.push(id) });
  const graph = () => dock.frames.get('Graph');
  function start({ mode = 'float', x = 420, y = 180, focus = true } = {}) {
    dock.show('Graph', { mode, focus: false }); if (mode === 'auto') dock.reveal('Graph');
    graph().element.rect = { left: 400, top: 150, width: 310, height: 350 };
    if (focus) graph().move.focus();
    graph().move.emit('pointerdown', { clientX: x, clientY: y });
  }
  const point = (x, y = 150, extra = {}) => ({ clientX: x + 100, clientY: y + 50, ...extra });
  return { dock, workspace, viewport, events, graph, start, point, stored, active, hidden, cleaned, rendered };
}

test('dragging displays a prospective column then docks on either side only on release', t => {
  const value = docking(t), { dock, workspace, start, point } = value;
  for (const side of ['left', 'right']) {
    start(); const other = dock.snapshot().panels.Properties, x = side === 'left' ? 5 : 1275;
    workspace.emit('pointermove', point(x));
    assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(dock.dropZones.hidden, false);
    assert.equal(dock.dropPreview.hidden, false); assert.match(dock.dropPreview.textContent, new RegExp(`Release to dock Graph on the ${side}`));
    workspace.emit('pointerup', point(x));
    assert.equal(dock.state.panels.Graph.mode, 'dock'); assert.equal(dock.state.panels.Graph.side, side);
    assert.deepEqual(dock.state.panels.Properties, other); assert.equal(dock.dropZones.hidden, true);
    assert.equal(dock.dropPreview.hidden, true); assert.equal(workspace.capture, null);
    assert.deepEqual(value.stored.at(-1), dock.snapshot()); assert.equal(dock.gesture, null);
  }
});

test('release coordinates and Ctrl bypass determine the final drop, without a last pointermove', t => {
  const { dock, workspace, start, point } = docking(t);
  start(); workspace.emit('pointermove', point(500)); workspace.emit('pointerup', point(5));
  assert.equal(dock.state.panels.Graph.side, 'left'); assert.equal(dock.state.panels.Graph.mode, 'dock');
  start(); workspace.emit('pointermove', point(1275, 150, { ctrlKey: true }));
  assert.equal(dock.dropPreview.hidden, true); workspace.emit('pointerup', point(1275, 150, { ctrlKey: true }));
  assert.equal(dock.state.panels.Graph.mode, 'float');
  start(); workspace.emit('pointermove', point(5)); assert.equal(dock.dropPreview.hidden, false);
  workspace.emit('pointerup', point(5, 150, { ctrlKey: true })); assert.equal(dock.state.panels.Graph.mode, 'float');
});

test('Escape, pointer cancellation and capture loss restore state, anchored visibility, active palette and focus', t => {
  const { dock, workspace, events, graph, start, point, active } = docking(t);
  for (const cancel of ['Escape', 'pointercancel', 'lostpointercapture']) {
    start({ mode: 'auto' }); const before = dock.snapshot(), focus = graph().move;
    workspace.emit('pointermove', point(1275)); assert.equal(dock.state.panels.Graph.mode, 'float');
    if (cancel === 'Escape') events.emit('keydown', { key: 'Escape' }); else workspace.emit(cancel);
    assert.deepEqual(dock.snapshot(), before); assert.equal(dock.autoId, 'Graph'); assert(dock.isVisible('Graph'));
    assert.equal(document.activeElement, focus); assert.equal(dock.dropZones.hidden, true); assert.equal(dock.gesture, null);
  }
  start({ focus: false }); dock.state.active = 'Properties'; dock.gesture.before.active = 'Properties';
  workspace.emit('pointermove', point(5)); assert.equal(active.at(-1), 'Graph');
  workspace.emit('pointercancel'); assert.equal(active.at(-1), 'Properties');
});

test('locking cancels an in-progress move, blocks new gestures and still permits showing palettes', t => {
  const { dock, workspace, graph, start, point } = docking(t);
  start(); const before = dock.snapshot(); workspace.emit('pointermove', point(5)); dock.setLocked(true);
  assert.deepEqual(dock.snapshot(), { ...before, locked: true }); assert.equal(dock.dropZones.hidden, true); assert.equal(workspace.capture, null);
  graph().move.emit('pointerdown'); assert.equal(dock.gesture, null);
  dock.close('Graph'); dock.show('Graph'); assert.equal(dock.state.panels.Graph.open, true); assert.equal(dock.state.locked, true);
});

test('click jitter and other pointers never dock or move palettes', t => {
  const { dock, workspace, start } = docking(t);
  start(); const before = dock.snapshot();
  workspace.emit('pointermove', { clientX: 105, pointerId: 99 }); assert.deepEqual(dock.snapshot(), before);
  workspace.emit('pointermove', { clientX: 422, clientY: 181 }); assert.equal(dock.dropZones.hidden, true);
  workspace.emit('pointerup', { clientX: 422, clientY: 181 }); assert.deepEqual(dock.snapshot(), before);
});

test('cancellation retains live geometry even when larger than persisted preference bounds', t => {
  const { dock, workspace, start, point } = docking(t, { width: 3000, height: 2400 });
  dock.show('Graph', { mode: 'float' }); dock.commit({ type: 'rect', id: 'Graph', rect: { width: 2000, height: 1800 } });
  start(); const before = dock.snapshot(); workspace.emit('pointermove', point(5)); workspace.emit('pointercancel');
  assert.deepEqual(dock.snapshot(), before); assert.equal(dock.state.panels.Graph.width, 2000);
});

test('drop targets exclude a docked chat and successful reparenting restores the original focused control', t => {
  const { dock, workspace, start, graph, point } = docking(t, { chatWidth: 400 });
  start(); const focus = graph().move;
  workspace.emit('pointermove', point(1000)); assert.equal(dock.dropPreview.hidden, true); assert.equal(dock.dropZones.style.width, '880px');
  workspace.emit('pointerup', point(875)); assert.equal(dock.state.panels.Graph.side, 'right'); assert.equal(dock.state.panels.Graph.mode, 'dock');
  assert.equal(document.activeElement, focus);
});

test('compact edge release anchors a visible palette and leaves the viewport free of permanent dock columns', t => {
  const { dock, workspace, start, point } = docking(t, { width: 390, height: 600 });
  start(); workspace.emit('pointermove', point(5)); assert.match(dock.dropPreview.textContent, /Release to anchor Graph on the left/);
  workspace.emit('pointerup', point(5));
  assert.equal(dock.state.panels.Graph.mode, 'dock'); assert.equal(dock.state.panels.Graph.side, 'left');
  assert.equal(dock.autoId, 'Graph'); assert.equal(dock.isVisible('Graph'), true);
  assert.equal(dock.leftDock.hidden, true); assert.equal(dock.rightDock.hidden, true); assert.equal(dock.leftRail.hidden, false);
});

test('resize gestures never activate docking targets or convert a floating palette', t => {
  const { dock, workspace, start, graph, point } = docking(t);
  start(); workspace.emit('pointercancel');
  graph().resize.emit('pointerdown'); workspace.emit('pointermove', point(5)); workspace.emit('pointerup', point(5));
  assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(dock.dropZones.hidden, true);
});

test('explicit restore replaces a locked layout, sanitizes and clamps the snapshot, refreshes renderers and persists', async t => {
  const { dock, graph, stored, active, cleaned, hidden } = docking(t, { width: 900, height: 500 });
  dock.show('Graph', { mode: 'float' }); await Promise.resolve();
  const previousGeneration = graph().generation, profile = dock.snapshot(); dock.setLocked(true);
  profile.active = 'Layers'; profile.locked = false; profile.hidden = false;
  profile.panels.Properties.open = false; profile.panels.Graph = { ...profile.panels.Graph, x: 3000, y: 3000, width: 1500, height: 1500 };
  profile.panels.Layers.open = true; profile.panels.Unregistered = { open: true }; profile.secret = 'discard-this';
  const source = JSON.stringify(profile), result = dock.restore(profile);
  assert.equal(JSON.stringify(profile), source, 'restore never mutates the caller snapshot');
  assert.equal(dock.state.locked, false); assert.equal(dock.state.active, 'Layers'); assert.equal(active.at(-1), 'Layers');
  assert.equal(dock.isVisible('Properties'), false); assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.isVisible('Layers'), true);
  assert.deepEqual(result.panels.Graph, { open: true, allowDocking: true, mode: 'float', side: 'right', x: 0, y: 0, width: 900, height: 500 });
  assert(!JSON.stringify(result).includes('discard-this')); assert(!Object.hasOwn(result.panels, 'Unregistered'));
  assert(graph().generation > previousGeneration); assert(cleaned.includes('Graph')); assert(hidden.includes('Graph'));
  assert.equal(document.activeElement, dock.launcher.querySelector('summary'));
  assert.deepEqual(stored.at(-1), result); result.panels.Graph.open = false; assert.equal(dock.isVisible('Graph'), true);
  dock.restore({ ...profile, locked: true }); assert.equal(dock.state.locked, true);
});

test('restore cancels gestures and stale async renderers, and defaults remain available from a locked profile', async t => {
  const { dock, workspace, start, point } = docking(t);
  let finish, released = 0;
  start(); dock.render = () => new Promise(resolve => { finish = resolve; }); dock.refresh('Graph');
  const finishGraph = finish;
  workspace.emit('pointermove', point(5)); dock.setLocked(true); dock.restore(null);
  finishGraph(() => released++); await Promise.resolve();
  assert.equal(released, 1, 'a renderer belonging to the old layout is disposed when it completes');
  assert.equal(dock.gesture, null); assert.equal(workspace.capture, null); assert.equal(dock.dropZones.hidden, true);
  assert.equal(dock.state.locked, false); assert.equal(dock.state.active, 'Properties');
  assert.equal(dock.state.panels.Properties.open, true); assert.equal(dock.state.panels.Graph.open, false);
});

const visibleGroup = (dock, id) => [...dock.groups.values()].find(group => group.element.isConnected && group.visibleTabs.includes(id));

test('explicit Show refreshes already-visible out-of-band content while new visibility renders only once', t => {
  const { dock } = docking(t);
  let proposal = 'No pending preview';
  const calls = [];
  dock.render = (id, content) => { calls.push(id); content.textContent = proposal; };
  dock.show('Properties');
  assert.equal(dock.frames.get('Properties').content.textContent, 'No pending preview');
  proposal = 'Prepared edit preview'; const before = dock.snapshot();
  dock.show('Properties');
  assert.equal(dock.frames.get('Properties').content.textContent, 'Prepared edit preview');
  assert.deepEqual(dock.snapshot(), before, 'no drawing or layout mutation is needed to refresh external data');
  assert.deepEqual(calls, ['Properties', 'Properties']);
  dock.show('Graph'); assert.equal(calls.filter(id => id === 'Graph').length, 1);
  dock.close('Graph'); dock.show('Graph'); assert.equal(calls.filter(id => id === 'Graph').length, 2);
  dock.groupWith('Graph', 'Properties'); dock.show('Properties');
  const prior = calls.filter(id => id === 'Graph').length;
  dock.show('Graph'); assert.equal(calls.filter(id => id === 'Graph').length, prior + 1);
});

test('global hide and reveal render each visible palette once after attachment and retain its new ownership', async t => {
  const { dock } = docking(t);
  dock.groupWith('Graph', 'Properties'); await Promise.resolve();
  const lifecycle = [];
  dock.render = (id, content) => {
    lifecycle.push({ event: 'render', id, connected: content.isConnected });
    return () => lifecycle.push({ event: 'cleanup', id });
  };
  dock.onHide = id => lifecycle.push({ event: 'hide', id });
  dock.refresh('Graph'); await Promise.resolve(); lifecycle.length = 0;
  for (let cycle = 0; cycle < 2; cycle++) {
    dock.toggle();
    assert.equal(dock.contentFor('Graph').isConnected, false);
    dock.toggle(); await Promise.resolve();
    assert.equal(dock.contentFor('Graph').isConnected, true);
    assert.deepEqual(lifecycle, [
      { event: 'cleanup', id: 'Graph' }, { event: 'hide', id: 'Graph' },
      { event: 'render', id: 'Graph', connected: true }
    ], 'revealing an attached palette neither duplicates rendering nor hides or cleans up the new renderer');
    lifecycle.length = 0;
  }
});

test('tab groups reuse frames, expose linked accessible tabs and release inactive renderer ownership', async t => {
  const { dock, cleaned, rendered } = docking(t);
  dock.show('Graph'); const properties = dock.frames.get('Properties'), graph = dock.frames.get('Graph');
  await Promise.resolve(); dock.groupWith('Graph', 'Properties'); await Promise.resolve();
  const group = visibleGroup(dock, 'Graph');
  assert(group); assert.equal(group.tablist.hidden, false); assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.isVisible('Properties'), false);
  assert.equal(properties.element.isConnected, false); assert(cleaned.includes('Properties'));
  const tab = group.tabs.get('Graph');
  assert.equal(tab.getAttribute('aria-controls'), group.panels.get('Graph').getAttribute('id'));
  assert.equal(group.panels.get('Graph').getAttribute('aria-labelledby'), tab.getAttribute('id'));
  assert.equal(group.panels.get('Graph').getAttribute('role'), 'tabpanel'); assert.equal(tab.getAttribute('aria-selected'), 'true');
  assert.equal(group.panels.get('Properties').isConnected, true); assert.equal(group.panels.get('Properties').hidden, true);
  assert.equal(group.tabs.get('Properties').getAttribute('aria-controls'), group.panels.get('Properties').getAttribute('id'));
  assert.equal(tab.tabIndex, 0); assert.equal(group.tabs.get('Properties').tabIndex, -1);
  tab.emit('keydown', { key: 'ArrowLeft' }); await Promise.resolve();
  assert.equal(dock.isVisible('Properties'), true); assert.equal(dock.isVisible('Graph'), false);
  assert.equal(document.activeElement, group.tabs.get('Properties')); assert(dock.containsFocus()); assert(cleaned.includes('Graph'));
  assert.equal(dock.frames.get('Properties'), properties); assert(rendered.filter(id => id === 'Properties').length >= 2);
  group.tabs.get('Properties').emit('keydown', { key: 'End' }); assert.equal(dock.isVisible('Graph'), true);
  group.tabs.get('Graph').emit('keydown', { key: 'Home' }); assert.equal(dock.isVisible('Properties'), true);
});

test('closing and reopening an active grouped palette selects its neighbor without losing membership or focus', t => {
  const { dock } = docking(t);
  dock.groupWith('Graph', 'Properties'); const group = visibleGroup(dock, 'Graph');
  group.tabs.get('Graph').focus(); dock.close('Graph');
  assert.equal(dock.isVisible('Properties'), true); assert.equal(document.activeElement, dock.frames.get('Properties').selector);
  assert.deepEqual(dock.state.groups.right[0].tabs, ['Properties', 'Graph']);
  dock.show('Graph'); assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.isVisible('Properties'), false);
});

test('explicit reorder and separation retain controls and new dock order in storage', t => {
  const { dock, stored } = docking(t);
  dock.groupWith('Graph', 'Properties'); dock.reorder('Graph', -1, true);
  assert.deepEqual(dock.state.groups.right[0].tabs, ['Graph', 'Properties']);
  assert.equal(document.activeElement, visibleGroup(dock, 'Graph').tabs.get('Graph'));
  dock.detach('Graph');
  assert(dock.isVisible('Graph') && dock.isVisible('Properties'));
  dock.reorder('Graph', -1);
  const visible = [...dock.rightDock.panes.children].filter(child => child.classList.contains('pd-group'));
  assert.equal(visible[0], visibleGroup(dock, 'Graph').element);
  assert.deepEqual(stored.at(-1), dock.snapshot());
});

test('stack separators resize by keyboard and pointer, retain focus ownership and cancel exactly', t => {
  const { dock, workspace, events } = docking(t);
  dock.show('Graph'); const group = visibleGroup(dock, 'Properties'), before = group.height;
  group.splitter.focus(); assert(dock.containsFocus());
  group.splitter.emit('keydown', { key: 'ArrowDown' }); assert(Math.abs(group.height - before - 20) < 1e-8);
  assert.equal(group.splitter.getAttribute('aria-orientation'), 'horizontal');
  const snapshot = dock.snapshot();
  group.splitter.emit('pointerdown'); workspace.emit('pointermove', { clientY: 240 });
  assert.notDeepEqual(dock.snapshot(), snapshot); assert.equal(dock.dropZones.hidden, true);
  events.emit('keydown', { key: 'Escape' }); assert.deepEqual(dock.snapshot(), snapshot);
  assert.equal(document.activeElement, group.splitter);
  group.splitter.emit('pointerdown'); workspace.emit('pointermove', { clientY: 230 }); workspace.emit('pointerup', { clientY: 230 });
  assert.notDeepEqual(dock.snapshot(), snapshot);
  dock.setLocked(true); const locked = dock.snapshot(); group.splitter.emit('keydown', { key: 'ArrowUp' }); group.splitter.emit('pointerdown');
  assert.deepEqual(dock.snapshot(), locked); assert.equal(dock.gesture, null); assert.equal(group.splitter.tabIndex, -1);
});

test('a group with its original tab closed remains resizable through its visible neighbor', t => {
  const { dock } = docking(t);
  dock.groupWith('Layers', 'Graph'); dock.close('Graph'); dock.reorder('Layers', -1);
  const group = visibleGroup(dock, 'Layers'), height = group.height;
  assert.equal(group.id, 'Graph');
  group.splitter.emit('keydown', { key: 'ArrowDown', shiftKey: true });
  assert(Math.abs(group.height - height - 1) < 1e-8);
});

test('Allow Docking off floats an active tab and disables grouping, anchoring and drop targets, not movement', t => {
  const { dock, workspace, point } = docking(t);
  dock.groupWith('Graph', 'Properties'); const frame = dock.frames.get('Graph');
  dock.setAllowDocking('Graph', false);
  assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(dock.state.panels.Graph.allowDocking, false);
  assert(dock.isVisible('Properties') && dock.isVisible('Graph')); assert.equal(frame.pin.disabled, true);
  assert.equal(frame.allowDocking.getAttribute('aria-checked'), 'false'); assert.equal(frame.groupButton.disabled, true);
  assert(frame.placementButtons.filter(button => button.dataset.docking === 'true').every(button => button.disabled));
  assert.equal(frame.placementButtons.find(button => button.dataset.docking === 'false').disabled, false);
  frame.element.rect = { left: 400, top: 150, width: 310, height: 350 }; frame.move.emit('pointerdown');
  workspace.emit('pointermove', point(5)); assert.equal(dock.dropZones.hidden, true); workspace.emit('pointerup', point(5));
  assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(dock.state.panels.Graph.x, 0);
  dock.setAllowDocking('Graph', true); assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(frame.pin.disabled, false);
});

test('palette replacement inherits placement but never transfers another palette docking permission', t => {
  const { dock } = docking(t);
  dock.show('Graph'); dock.setAllowDocking('Graph', false); dock.close('Graph');
  const properties = dock.frames.get('Properties');
  properties.selector.value = 'Graph'; properties.selector.emit('change');
  assert.equal(dock.state.panels.Graph.allowDocking, false); assert.equal(dock.state.panels.Graph.mode, 'float');
  assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.state.panels.Properties.open, false);
  const graph = dock.frames.get('Graph');
  graph.selector.value = 'Properties'; graph.selector.emit('change');
  assert.equal(dock.state.panels.Properties.allowDocking, true); assert.equal(dock.state.panels.Properties.mode, 'float');
  assert.equal(dock.isVisible('Properties'), true);
  dock.place('Properties', 'dock', 'right'); assert.equal(dock.state.panels.Properties.mode, 'dock');
});

test('group layouts restore on compact screens as independent edge controls and return to the saved group', t => {
  const { dock, workspace } = docking(t);
  dock.groupWith('Graph', 'Properties'); const saved = dock.snapshot();
  workspace.rect.width = 390; dock.restore(saved);
  assert.equal(dock.leftDock.hidden, true); assert.equal(dock.rightDock.hidden, true);
  assert.equal(dock.frames.get('Properties').edge.hidden, false); assert.equal(dock.frames.get('Graph').edge.hidden, false);
  dock.activateAnchor('Graph'); assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.isVisible('Properties'), false);
  workspace.rect.width = 1280; dock.apply();
  assert.deepEqual(dock.state.groups, saved.groups); assert.equal(dock.isVisible('Graph'), true);
  assert.equal(visibleGroup(dock, 'Graph').tablist.hidden, false);
});

test('restore handles tab focus and late renderer completions after switching tabs', async t => {
  const { dock } = docking(t);
  dock.groupWith('Graph', 'Properties');
  let resolve, cleanup = 0; dock.render = () => new Promise(done => { resolve = done; });
  dock.refresh('Graph'); const old = resolve;
  visibleGroup(dock, 'Graph').tabs.get('Properties').emit('click');
  old(() => cleanup++); await Promise.resolve(); assert.equal(cleanup, 1);
  const saved = dock.snapshot(); visibleGroup(dock, 'Properties').tabs.get('Properties').focus();
  dock.restore(saved); assert.equal(document.activeElement, dock.launcher.querySelector('summary'));
});
