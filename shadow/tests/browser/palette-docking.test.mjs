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
  assert.deepEqual(result.panels.Graph, { open: true, mode: 'float', side: 'right', x: 0, y: 0, width: 900, height: 500 });
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
