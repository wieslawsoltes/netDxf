import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PaletteDock, handlePaletteEscape } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-docking.mjs';
import { raiseWorkspaceWindow, releaseWorkspaceWindow } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-foreground.mjs';
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
    super(tag); this.style = { removeProperty(name) { delete this[name]; delete this[name.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())]; } }; this.capture = null;
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
function docking(t, { width = 1280, height = 600, chatWidth = 0, drawingArea = false } = {}) {
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
  workspace.rect = { left: 100, top: 50, width, height }; viewport.className = 'viewport';
  const center = drawingArea ? new DockElement('section') : viewport;
  if (drawingArea) { center.className = 'drawing-area'; center.append(viewport); }
  workspace.append(center); body.append(workspace);
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
  return { dock, workspace, viewport, center, events, graph, start, point, stored, active, hidden, cleaned, rendered };
}

test('docks surround the complete drawing column and exclude a separate agent pane from their bounds', t => {
  const { dock, workspace, center, viewport } = docking(t, { drawingArea: true, chatWidth: 350 });
  assert.equal(dock.center, center); assert.equal(dock.viewport, viewport); assert.equal(viewport.parentNode, center);
  assert.equal(center.parentNode, workspace); assert.equal(dock.leftDock.parentNode, workspace); assert.equal(dock.rightDock.parentNode, workspace);
  assert(workspace.children.indexOf(dock.leftDock) < workspace.children.indexOf(center));
  assert(workspace.children.indexOf(dock.rightDock) > workspace.children.indexOf(center));
  assert.deepEqual(dock.bounds(), { width: 930, height: 600 });
});

test('opening or focusing a compact palette raises it above a floating ribbon and allows ribbon reactivation', t => {
  const { dock, graph } = docking(t, { width: 390, height: 700 });
  const ribbon = new DockElement('section'); ribbon.style.zIndex = '38';
  t.after(() => releaseWorkspaceWindow(ribbon));
  raiseWorkspaceWindow(ribbon); dock.show('Graph');
  assert.equal(dock.overlay.style.zIndex, '42'); assert.equal(ribbon.style.zIndex, '38');
  raiseWorkspaceWindow(ribbon);
  assert.equal(ribbon.style.zIndex, '42'); assert.equal(dock.overlay.style.zIndex, undefined);
  graph().element.emit('focusin', { target: graph().selector });
  assert.equal(dock.overlay.style.zIndex, '42'); assert.equal(ribbon.style.zIndex, '38');
  dock.hideAll(); assert.equal(dock.overlay.style.zIndex, undefined);
  raiseWorkspaceWindow(ribbon); dock.restore(null); assert.equal(ribbon.style.zIndex, '42', 'background palette cleanup does not change ribbon foreground');
});

test('hide/show sessions preserve manual reopening and closing, including anchored tabs and saved restore', t => {
  const { dock, graph } = docking(t);
  dock.show('Graph', { mode: 'float' });
  dock.show('Layers', { mode: 'auto', side: 'left' });
  dock.setLocked(true); dock.hideAll();
  assert.equal(dock.allHidden, true); assert.equal(dock.state.hidden, true);
  assert.equal(dock.leftRail.hidden, true); assert.equal(dock.rightDock.hidden, true);
  assert.equal(document.activeElement, dock.launcher.querySelector('summary'));
  dock.show('Graph');
  assert.equal(dock.allHidden, false); assert.equal(dock.state.hidden, true);
  assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.isVisible('Properties'), false);
  assert.equal(dock.frames.get('Layers').edge.hidden, true);
  dock.close('Graph'); dock.show('Layers');
  assert.equal(dock.isVisible('Layers'), true); assert.equal(dock.frames.get('Layers').edge.hidden, false);
  assert.equal(graph().element.isConnected, false); assert.equal(dock.rightDock.hidden, true);
  const saved = dock.snapshot(); dock.restore(saved);
  assert.deepEqual(dock.snapshot(), saved); assert.equal(dock.allHidden, false);
  assert.equal(dock.frames.get('Layers').edge.hidden, false); assert.equal(dock.rightDock.hidden, true);
  dock.toggle();
  assert.equal(dock.state.hidden, false); assert.deepEqual(dock.state.hiddenRevealed, []);
  assert.equal(dock.isVisible('Properties'), true); assert.equal(dock.state.panels.Graph.open, false);
  assert.equal(dock.frames.get('Layers').edge.hidden, false); assert.equal(dock.state.locked, true);
});

test('hide/show cancels an in-progress layout gesture and toggles a fully closed layout predictably', t => {
  const { dock, workspace, start, point } = docking(t);
  start(); const initial = dock.snapshot(); workspace.emit('pointermove', point(650));
  dock.hideAll(); assert.equal(dock.gesture, null); assert.equal(workspace.capture, null);
  assert.deepEqual(dock.state.panels.Graph, initial.panels.Graph);
  dock.hideAll(); assert.equal(dock.allHidden, true);
  dock.showAll(); assert.equal(dock.isVisible('Graph'), true); assert.equal(dock.isVisible('Properties'), true);
  for (const id of dock.ids) dock.close(id);
  assert.equal(dock.allHidden, true); dock.showAll(); assert.equal(dock.allHidden, true);
  dock.toggle(); assert.equal(dock.allHidden, false); assert.equal(dock.state.panels[dock.state.active].open, true);
});

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

test('center drops form tab groups and top/bottom drops insert stacked groups only on release', t => {
  const { dock, workspace, start, point } = docking(t);
  const target = () => { dock.groups.get('right:Properties').element.rect = { left: 1070, top: 50, width: 310, height: 600 }; };
  start(); target(); workspace.emit('pointermove', point(1100, 250));
  assert.equal(dock.dropPreview.dataset.kind, 'group'); assert.match(dock.dropPreview.textContent, /group Graph with Properties/);
  assert.equal(dock.state.panels.Graph.mode, 'float');
  workspace.emit('pointerup', point(1100, 250));
  assert.deepEqual(dock.state.groups.right[0].tabs, ['Properties', 'Graph']); assert.equal(dock.state.groups.right[0].active, 'Graph');
  for (const [y, order] of [[10, ['Graph', 'Properties']], [590, ['Properties', 'Graph']]]) {
    dock.reset(); start(); target(); workspace.emit('pointermove', point(1100, y));
    assert.equal(dock.dropPreview.dataset.kind, 'stack');
    workspace.emit('pointerup', point(1100, y));
    assert.deepEqual(dock.state.groups.right.filter(group => group.tabs.some(id => dock.state.panels[id].open)).map(group => group.tabs[0]), order);
  }
});

test('tab drags detach inactive palettes, restore focus to their new frame, and cancel without losing group membership', t => {
  const { dock, workspace, point, events } = docking(t);
  dock.groupWith('Graph', 'Properties'); dock.show('Properties');
  const group = dock.groups.get('right:Properties'); group.element.rect = { left: 1070, top: 50, width: 310, height: 600 };
  const tab = group.tabs.get('Graph'); tab.focus();
  tab.emit('pointerdown', point(1100, 590)); workspace.emit('pointerup', point(1100, 590));
  assert.equal(dock.state.groups.right[0].active, 'Graph'); assert.equal(document.activeElement, tab);
  tab.emit('pointerdown', point(1100, 590)); const before = dock.snapshot();
  workspace.emit('pointermove', point(600, 300)); assert.equal(dock.state.panels.Graph.mode, 'float');
  events.emit('keydown', { key: 'Escape' }); assert.deepEqual(dock.snapshot(), before);
  tab.emit('pointerdown', point(1100, 590)); workspace.emit('pointerup', point(600, 300));
  assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(document.activeElement, dock.frames.get('Graph').selector);
  assert.deepEqual(dock.state.groups.right.find(item => item.tabs.includes('Properties')).tabs, ['Properties']);
});

test('group-drop cancellation restores the original layout and Ctrl bypass leaves a floating palette', t => {
  const { dock, workspace, start, point, events } = docking(t);
  start(); dock.groups.get('right:Properties').element.rect = { left: 1070, top: 50, width: 310, height: 600 };
  const before = dock.snapshot(); workspace.emit('pointermove', point(1100, 250));
  events.emit('keydown', { key: 'Escape' }); assert.deepEqual(dock.snapshot(), before);
  start(); workspace.emit('pointermove', point(1100, 250));
  workspace.emit('pointerup', point(1100, 250, { ctrlKey: true }));
  assert.equal(dock.state.panels.Graph.mode, 'float'); assert.equal(dock.dropZones.hidden, true);
});

test('palette title context menus are bounded and keyboard navigation skips disabled controls', t => {
  const { dock } = docking(t), oldWidth = globalThis.innerWidth, oldHeight = globalThis.innerHeight;
  globalThis.innerWidth = 1280; globalThis.innerHeight = 800;
  t.after(() => { if (oldWidth === undefined) delete globalThis.innerWidth; else globalThis.innerWidth = oldWidth;
    if (oldHeight === undefined) delete globalThis.innerHeight; else globalThis.innerHeight = oldHeight; });
  const frame = dock.frames.get('Properties'); frame.menu.lastElementChild.scrollHeight = 400;
  frame.header.emit('contextmenu', { clientX: 1250, clientY: 770 });
  assert.equal(frame.menu.open, true); assert.equal(frame.menu.lastElementChild.style.left, '1092px');
  assert.equal(frame.menu.lastElementChild.style.top, '392px'); assert.equal(document.activeElement, frame.placementButtons[0]);
  frame.menu.emit('keydown', { key: 'End' });
  assert.equal(document.activeElement, frame.menu.querySelectorAll('button').filter(button => !button.disabled).at(-1));
  frame.menu.emit('keydown', { key: 'Home' }); assert.equal(document.activeElement, frame.placementButtons[0]);
  frame.element.emit('keydown', { key: 'Escape' }); assert.equal(frame.menu.open, false);
  assert.equal(document.activeElement, frame.menu.querySelector('summary'));
});

test('a palette launcher in the status bar opens above its trigger and keeps that trigger clickable', t => {
  const { dock } = docking(t), oldWidth = globalThis.innerWidth, oldHeight = globalThis.innerHeight;
  globalThis.innerWidth = 1280; globalThis.innerHeight = 800;
  t.after(() => { if (oldWidth === undefined) delete globalThis.innerWidth; else globalThis.innerWidth = oldWidth;
    if (oldHeight === undefined) delete globalThis.innerHeight; else globalThis.innerHeight = oldHeight; });
  dock.launcher.querySelector('summary').rect = { left: 1000, top: 770, right: 1028, bottom: 795, width: 28, height: 25 };
  const popup = dock.launcher.lastElementChild; popup.scrollHeight = 400;
  dock.positionMenu(dock.launcher);
  assert.equal(popup.style.top, '366px'); assert.equal(popup.style.maxHeight, '758px');
  assert(parseFloat(popup.style.top) + popup.scrollHeight < 770);
  popup.scrollHeight = 1200; dock.positionMenu(dock.launcher);
  assert.equal(popup.style.top, '8px'); assert.equal(popup.style.maxHeight, '758px');
  dock.launcher.querySelector('summary').rect = { left: 1000, top: 20, right: 1028, bottom: 45, width: 28, height: 25 };
  dock.positionMenu(dock.launcher);
  assert.equal(popup.style.top, '49px'); assert.equal(popup.style.maxHeight, '743px');
});

test('float affordance and title double-click toggle docking without losing the remembered side or bypassing the lock', t => {
  const { dock } = docking(t), frame = dock.frames.get('Properties');
  frame.float.emit('click'); assert.equal(dock.state.panels.Properties.mode, 'float');
  assert.equal(frame.float.getAttribute('aria-label'), 'Dock Properties palette');
  frame.header.emit('dblclick', { target: frame.move });
  assert.equal(dock.state.panels.Properties.mode, 'dock'); assert.equal(dock.state.panels.Properties.side, 'right');
  dock.setLocked(true); frame.header.emit('dblclick', { target: frame.move });
  assert.equal(dock.state.panels.Properties.mode, 'dock'); assert.equal(frame.float.disabled, true);
  frame.close.emit('click'); assert.equal(dock.state.panels.Properties.open, false);
});

test('stationary captured grip clicks recognize double-click without treating a drag or tab as one', t => {
  const { dock, workspace } = docking(t), frame = dock.frames.get('Properties');
  for (let index = 0; index < 2; index++) { frame.move.emit('pointerdown'); workspace.emit('pointerup'); }
  assert.equal(dock.state.panels.Properties.mode, 'float');
  for (let index = 0; index < 2; index++) { frame.move.emit('pointerdown'); workspace.emit('pointerup'); }
  assert.equal(dock.state.panels.Properties.mode, 'dock');
  frame.move.emit('pointerdown'); workspace.emit('pointerup');
  frame.move.emit('pointerdown'); workspace.emit('pointermove', { clientX: 600, clientY: 300 }); workspace.emit('pointercancel');
  frame.move.emit('pointerdown'); workspace.emit('pointerup');
  assert.equal(dock.state.panels.Properties.mode, 'dock', 'a cancelled drag breaks the click sequence');
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
  assert.deepEqual(result.panels.Graph, { opacity: 100, rolloverOpacity: 100, open: true, allowDocking: true, autoHide: false, mode: 'float', side: 'right', x: 0, y: 0, width: 900, height: 500 });
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
  assert(dock.isVisible('Properties') && dock.isVisible('Graph')); assert.equal(frame.pin.disabled, false, 'floating auto-hide remains available without docking permission');
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

test('floating opacity responds to hover and focus while docked panes and recovery override remain opaque', t => {
  const {dock,graph,stored,rendered}=docking(t);
  dock.show('Graph',{mode:'float',focus:false});const frame=graph();
  dock.commit({type:'opacity',id:'Graph',opacity:20,rolloverOpacity:70});
  const count=rendered.length;
  assert.equal(frame.element.style.opacity,'0.2');
  frame.element.emit('pointerenter');assert.equal(frame.element.style.opacity,'0.7');
  frame.element.emit('focusin');assert.equal(frame.element.style.opacity,'1');
  frame.element.emit('focusout',{relatedTarget:document.body});assert.equal(frame.element.style.opacity,'0.7');
  frame.element.emit('pointerleave');assert.equal(frame.element.style.opacity,'0.2');
  dock.opaque.checked=true;dock.opaque.emit('change');
  assert.equal(frame.element.style.opacity,'1');assert.equal(stored.at(-1).transparencyDisabled,true);
  dock.commit({type:'transparency-disabled',value:false});assert.equal(frame.element.style.opacity,'0.2');
  dock.place('Graph','dock','right');assert.equal(frame.element.style.opacity,'1');
  assert.equal(dock.state.panels.Graph.opacity,20);assert.equal(rendered.length,count);
});

test('palette selector preserves the replacement palette appearance preference',t=>{
  const {dock}=docking(t);
  dock.commit({type:'opacity',id:'Graph',opacity:20,rolloverOpacity:70});
  const properties=dock.frames.get('Properties');
  properties.selector.value='Graph';properties.selector.emit('change');
  assert.equal(dock.state.panels.Graph.opacity,20);assert.equal(dock.state.panels.Graph.rolloverOpacity,70);
  const saved=dock.snapshot();dock.restore(saved);
  assert.equal(dock.state.panels.Graph.opacity,20);assert.equal(dock.state.panels.Properties.opacity,100);
});

test('an appearance modal owns its anchored palette until close and survives repeated requests',t=>{
  const {dock}=docking(t);
  dock.show('Graph',{mode:'auto'});dock.reveal('Graph');
  const owned={dialog:{open:true},close(){this.dialog.open=false;}};
  dock.appearance=owned;dock.appearanceId='Graph';
  assert.equal(dock.hideAuto({implicit:true}),false);
  dock.outside({target:document.body});
  assert.equal(dock.autoId,'Graph');
  dock.editAppearance('Properties');assert.equal(dock.appearance,owned);
  dock.keepOpen=()=>true;
  dock.escape({key:'Escape',preventDefault(){throw new Error('The modal must own Escape.');}});
  assert.equal(dock.autoId,'Graph');
  owned.close();dock.keepOpen=()=>false;
  assert.equal(dock.hideAuto({implicit:true}),true);
});

test('floating auto-hide retains its renderer, full rectangle and content while rolling up and revealing', t => {
  const { dock, graph, rendered, hidden } = docking(t);
  dock.show('Graph', { mode: 'float', focus: false }); const frame = graph(); frame.header.rect = { height: 27 };
  const body = frame.content, before = dock.snapshot().panels.Graph, generation = frame.generation, renderCount = rendered.length;
  dock.setFloatingAutoHide('Graph', true); assert.equal(frame.content.hidden, false);
  dock.hideFloating('Graph'); assert.equal(frame.content.hidden, true); assert.equal(frame.element.style.height, '27px');
  assert.equal(frame.content, body); assert.equal(frame.generation, generation); assert.equal(rendered.length, renderCount); assert(!hidden.includes('Graph'));
  assert.deepEqual(dock.snapshot().panels.Graph, { ...before, autoHide: true });
  frame.element.emit('pointerenter', { pointerType: 'mouse' }); assert.equal(frame.content.hidden, false);
  assert.equal(frame.element.style.height, `${before.height}px`);
  dock.setFloatingAutoHide('Graph', false); assert.equal(frame.content.hidden, false); assert.equal(frame.roll.hidden, true);
});

test('floating roll-up respects keyboard focus, active picking and appearance while Escape returns to the header', t => {
  const { dock, graph } = docking(t);
  dock.show('Graph', { mode: 'float', focus: false }); dock.setFloatingAutoHide('Graph', true); const frame = graph();
  frame.selector.focus(); assert.equal(dock.hideFloating('Graph', { implicit: true }), false);
  document.body.focus(); dock.keepOpen = id => id === 'Graph'; assert.equal(dock.hideFloating('Graph', { implicit: true }), false);
  dock.keepOpen = () => false; dock.appearance = { dialog: { open: true }, close() { this.dialog.open = false; } }; dock.appearanceId = 'Graph';
  assert.equal(dock.hideFloating('Graph', { implicit: true }), false); dock.appearance.close();
  assert.equal(dock.hideFloating('Graph', { implicit: true }), true);
  frame.element.emit('focusin', { target: frame.selector }); assert.equal(frame.content.hidden, false);
  frame.element.emit('keydown', { key: 'Escape' }); assert.equal(frame.content.hidden, true); assert.equal(document.activeElement, frame.roll);
  frame.roll.emit('click'); assert.equal(frame.content.hidden, false);
});

test('moving a rolled floating title retains expanded height and cancellation restores its expansion state', t => {
  const { dock, workspace, graph, point } = docking(t);
  dock.show('Graph', { mode: 'float', focus: false }); dock.setFloatingAutoHide('Graph', true); dock.hideFloating('Graph');
  const frame = graph(), before = dock.snapshot(); frame.element.rect = { left: 200, top: 100, width: 310, height: 27 };
  frame.move.emit('pointerdown', { clientX: 210, clientY: 112 }); workspace.emit('pointermove', { clientX: 280, clientY: 150 });
  assert.equal(dock.state.panels.Graph.height, before.panels.Graph.height);
  workspace.emit('pointercancel'); assert.deepEqual(dock.snapshot(), before); assert.equal(frame.content.hidden, true);
  const restored = dock.snapshot(); dock.restore(restored); assert.equal(frame.content.hidden, true); assert.equal(dock.state.panels.Graph.autoHide, true);
  dock.show('Graph'); assert.equal(frame.content.hidden, false, 'explicit Show reveals a restored rolling palette');
});

test('floating auto-hide remains independent of anchoring and is usable with Allow Docking disabled', t => {
  const { dock, graph } = docking(t);
  dock.show('Graph', { mode: 'float', focus: false }); dock.setAllowDocking('Graph', false);
  const frame = graph(); assert.equal(frame.pin.disabled, false); assert.equal(frame.autoHide.disabled, false);
  frame.pin.emit('click'); assert.equal(dock.state.panels.Graph.autoHide, true); assert.equal(dock.state.panels.Graph.mode, 'float');
  dock.setAllowDocking('Graph', true); dock.place('Graph', 'auto', 'left');
  assert.equal(frame.autoHide.disabled, true); assert.equal(frame.roll.hidden, true); assert.equal(dock.isVisible('Graph'), false);
  dock.reveal('Graph'); assert.equal(frame.content.hidden, false); assert.equal(frame.roll.hidden, true);
});

test('floating edge and corner resizing retains opposite edges, handles final release coordinates and cancels exactly', t => {
  const { dock, workspace, graph } = docking(t);
  dock.show('Graph', { mode: 'float' }); dock.commit({ type: 'rect', id: 'Graph', rect: { x: 200, y: 140, width: 320, height: 300 } });
  const handle = direction => graph().resizeEdges.find(node => node.getAttribute('data-resize-edge') === direction);
  assert.equal(graph().resizeEdges.length, 8);
  handle('nw').emit('pointerdown', { clientX: 300, clientY: 190 });
  workspace.emit('pointerup', { clientX: 280, clientY: 170 });
  assert.deepEqual(Object.fromEntries(['x', 'y', 'width', 'height'].map(key => [key, dock.state.panels.Graph[key]])), { x: 180, y: 120, width: 340, height: 320 });
  handle('n').emit('keydown', { key: 'ArrowUp', shiftKey: true }); assert.equal(dock.state.panels.Graph.y, 119); assert.equal(dock.state.panels.Graph.height, 321);
  const before = dock.snapshot(); handle('w').emit('pointerdown'); workspace.emit('pointermove', { clientX: 470, clientY: 180 }); workspace.emit('pointercancel');
  assert.deepEqual(dock.snapshot(), before);
});

test('LOCKUI window bits distinguish docked and floating palettes without overwriting the legacy lock', t => {
  const { dock, graph, events, stored } = docking(t);
  dock.show('Graph', { mode: 'float' }); const properties = dock.frames.get('Properties');
  dock.setLockMask(2); assert.equal(properties.move.disabled, true); assert.equal(graph().move.disabled, false); assert.equal(dock.state.locked, false);
  dock.setLockMask(8); assert.equal(properties.move.disabled, false); assert.equal(graph().move.disabled, true);
  assert.equal(dock.rightDock.splitter.getAttribute('aria-disabled'), 'false');
  dock.setLocked(true); events.emit('keydown', { key: 'Control', ctrlKey: true });
  assert.equal(properties.move.disabled, false); assert.equal(graph().move.disabled, false); assert.equal(dock.state.locked, true);
  const x = dock.state.panels.Graph.x; graph().move.emit('keydown', { key: 'ArrowRight', ctrlKey: true });
  assert.equal(dock.state.panels.Graph.x, x + 20); assert.equal(stored.at(-1).locked, true);
  events.emit('keyup', { key: 'Control' }); assert.equal(properties.move.disabled, true); assert.equal(graph().move.disabled, true);
  dock.setLocked(false); dock.setLockMask(0); assert.equal(graph().move.disabled, false);
});

test('Ctrl temporarily unlocks pointer dragging and release relocks without changing persisted lock state', t => {
  const { dock, workspace, graph, events, point, stored } = docking(t);
  dock.show('Graph', { mode: 'float' }); graph().element.rect = { left: 400, top: 150, width: 310, height: 350 };
  dock.setLocked(true); const before = dock.snapshot();
  events.emit('keydown', { key: 'Control', ctrlKey: true });
  graph().move.emit('pointerdown', { clientX: 420, clientY: 180, ctrlKey: true }); workspace.emit('pointermove', point(520, 230, { ctrlKey: true }));
  assert.notEqual(dock.state.panels.Graph.x, before.panels.Graph.x); assert.equal(dock.state.locked, true);
  events.emit('keyup', { key: 'Control' }); assert.equal(dock.gesture, null); assert.equal(graph().move.disabled, true);
  assert.equal(stored.at(-1).locked, true); assert.equal(workspace.capture, null);
  const after = dock.snapshot(); graph().move.emit('pointerdown'); assert.equal(dock.gesture, null); assert.deepEqual(dock.snapshot(), after);
});

test('a permitted docked drag completes across LOCKUI categories then respects its new floating lock', t => {
  const { dock, workspace, graph, point } = docking(t);
  dock.show('Graph', { mode: 'dock', focus: false }); graph().element.rect = { left: 900, top: 100, width: 310, height: 350 };
  dock.setLockMask(8); graph().move.emit('pointerdown', { clientX: 910, clientY: 112 });
  workspace.emit('pointermove', point(500, 200)); const first = dock.state.panels.Graph.x;
  workspace.emit('pointermove', point(600, 240)); assert(dock.state.panels.Graph.x > first);
  workspace.emit('pointerup', point(650, 260)); assert.equal(dock.state.panels.Graph.mode, 'float');
  assert.equal(graph().move.disabled, true); assert.equal(dock.state.locked, false);
  dock.setLockMask(0); dock.place('Graph', 'dock', 'right'); dock.setLockMask(8);
  const x = dock.state.panels.Graph.x; graph().move.emit('keydown', { key: 'ArrowRight' });
  assert.equal(dock.state.panels.Graph.x, x + 20); assert.equal(dock.state.panels.Graph.mode, 'float');
  assert.equal(graph().move.disabled, true); assert.equal(dock.state.locked, false);
});
