import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bindChatPopovers, chatPopoverRect } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/chat-popovers.mjs';

class Target {
  constructor() { this.listeners = new Map(); this.attributes = {}; this.dataset = {}; this.style = {}; }
  addEventListener(type, callback) { if (!this.listeners.has(type)) this.listeners.set(type, new Set()); this.listeners.get(type).add(callback); }
  removeEventListener(type, callback) { this.listeners.get(type)?.delete(callback); }
  emit(type, event = {}) { for (const callback of this.listeners.get(type) || []) callback(event); }
  setAttribute(name, value) { this.attributes[name] = value; }
  focus() { this.focused = true; }
  getBoundingClientRect() { return this.rect; }
}
function fixture() {
  const document = new Target(), root = new Target(), container = new Target(), bounds = new Target();
  document.defaultView = new Target(); root.ownerDocument = document;
  bounds.rect = { left: 120, top: 150, width: 440, height: 350 }; container.rect = { left: 120, top: 118, bottom: 600 };
  const menus = Array.from({ length: 3 }, (_, index) => {
    const menu = new Target(), summary = new Target(), body = new Target();
    menu.open = false; menu.dataset.placement = index ? 'above' : 'below';
    summary.rect = index ? { right: 550, top: 550, bottom: 580 } : { right: 460, top: 118, bottom: 150 };
    menu.querySelector = selector => selector === 'summary' ? summary : body;
    menu.contains = value => [menu, summary, body].includes(value);
    menu.toggle = value => { menu.open = value; menu.emit('toggle'); };
    return Object.assign(menu, { summary, body });
  });
  return { root, document, menus, bounds, controller: bindChatPopovers(root, menus, { bounds, container }) };
}

test('all chat overlays remain inside the transcript at mobile, docked and short dimensions', () => {
  for (const width of [240, 390, 440, 1200]) for (const height of [32, 120, 350, 640]) for (const above of [true, false]) {
    const bounds = { left: 75, top: 110, width, height };
    const anchor = above ? { right: 75 + width, top: 150 + height, bottom: 180 + height } : { right: 145, top: 70, bottom: 100 };
    const value = chatPopoverRect(bounds, anchor, { above });
    assert(value.left >= bounds.left); assert(value.left + value.width <= bounds.left + width);
    assert(value.top >= bounds.top); assert(value.top + value.maxHeight <= bounds.top + height);
    if (above) assert(value.bottom <= bounds.top + height);
  }
});

test('opening one chat overlay closes the others and announces expansion on its trigger', () => {
  const f = fixture();
  for (const menu of f.menus) {
    menu.toggle(true); assert.equal(menu.summary.attributes['aria-expanded'], 'true');
    for (const other of f.menus.filter(value => value !== menu)) assert.equal(other.open, false);
    assert.equal(menu.body.style.width, '420px');
  }
  f.controller.dispose();
});

test('Escape closes overlays and restores their trigger before composer Stop can run', () => {
  const f = fixture(); let stopped = 0, prevented = 0;
  f.menus[1].toggle(true);
  f.root.emit('keydown', { key: 'Escape', stopPropagation: () => stopped++, preventDefault: () => prevented++ });
  assert.equal(f.menus[1].open, false); assert.equal(f.menus[1].summary.focused, true); assert.equal(stopped, 1); assert.equal(prevented, 1);
  f.root.emit('keydown', { key: 'Escape', stopPropagation: () => stopped++ });
  assert.equal(stopped, 1, 'Escape without an open overlay remains available for Stop');
  f.controller.dispose();
});

test('outside pointer and focus dismiss without stealing focus; inner controls and other shortcuts remain usable', () => {
  const f = fixture(), menu = f.menus[2]; menu.toggle(true);
  f.document.emit('pointerdown', { target: menu.body }); assert.equal(menu.open, true);
  f.root.emit('keydown', { key: 'Enter', stopPropagation: () => assert.fail('ordinary keys must bubble') });
  f.document.emit('focusin', { target: {} }); assert.equal(menu.open, false); assert.equal(menu.summary.focused, undefined);
  menu.toggle(true); f.document.emit('pointerdown', { target: {} }); assert.equal(menu.open, false);
  f.controller.dispose();
});

test('open overlay bounds update on resize and disposal removes external listeners', () => {
  const f = fixture(), menu = f.menus[0]; menu.toggle(true);
  f.bounds.rect.width = 260; f.bounds.rect.height = 90;
  f.document.defaultView.emit('resize'); assert.equal(menu.body.style.width, '248px'); assert.equal(menu.body.style.maxHeight, '78px');
  f.controller.dispose(); assert.equal(menu.open, false);
  assert.equal(f.document.listeners.get('pointerdown').size, 0); assert.equal(f.document.listeners.get('focusin').size, 0);
  assert.equal(f.document.defaultView.listeners.get('resize').size, 0); assert.equal(f.root.listeners.get('keydown').size, 0);
});
