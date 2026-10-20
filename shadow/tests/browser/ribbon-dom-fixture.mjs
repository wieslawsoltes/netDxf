import { Element, all, installDom } from './ui-fixture.mjs';

const matches = (node, selector) => {
  if (selector.includes(' ')) { const parts = selector.split(/\s+/), last = parts.pop(); return matches(node, last) && !!node.parentNode?.closest(parts.join(' ')); }
  const enabled = !selector.includes(':not(:disabled)') || !node.disabled; selector = selector.replace(':not(:disabled)', '');
  const tag = selector.match(/^[a-zA-Z]+/), cls = selector.match(/\.([\w-]+)/), id = selector.match(/#([\w-]+)/);
  const attribute = [...selector.matchAll(/\[([\w-]+)(?:="?([^\]"]+)"?)?\]/g)];
  return enabled && (!tag || node.tagName.toLowerCase() === tag[0]) && (!cls || node.classList.contains(cls[1])) &&
    (!id || node.getAttribute('id') === id[1]) && attribute.every(([, key, value]) => value === undefined ? node.getAttribute(key) !== null : node.getAttribute(key) === value);
};
export class RibbonElement extends Element {
  constructor(tag) {
    super(tag); this.style = { removeProperty(name) { delete this[name]; } }; this.capture = null; this.clientWidth = 900;
    this.classList = {
      contains: name => (this.className || '').split(' ').includes(name),
      add: (...names) => names.forEach(name => this.classList.toggle(name, true)),
      remove: (...names) => names.forEach(name => this.classList.toggle(name, false)),
      toggle: (name, enabled) => { const names = new Set((this.className || '').split(' ').filter(Boolean)); enabled ??= !names.has(name); if (enabled) names.add(name); else names.delete(name); this.className = [...names].join(' '); }
    };
  }
  setAttribute(name, value) { super.setAttribute(name, value); if (name.startsWith('data-')) this.dataset[name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = String(value); }
  removeAttribute(name) { delete this.attributes[name]; }
  remove() { if (this.isConnected && this.contains(document.activeElement)) document.activeElement = document.body; super.remove(); }
  replaceChildren(...children) { if (this.children.some(child => child instanceof Element && child.contains(document.activeElement))) document.activeElement = document.body; super.replaceChildren(...children); }
  closest(selector) { return matches(this, selector) ? this : this.parentNode?.closest(selector) || null; }
  querySelectorAll(selector) { return all(this).slice(1).filter(node => selector.split(',').some(part => matches(node, part.trim()))); }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  before(value) { const parent = this.parentNode, index = parent.children.indexOf(this); value.remove(); value.parentNode = parent; parent.children.splice(index, 0, value); }
  replaceWith(value) { this.before(value); this.remove(); }
  removeEventListener(name, callback) { this.handlers.set(name, (this.handlers.get(name) || []).filter(value => value !== callback)); }
  getBoundingClientRect() {
    const width = this.rect?.width || 300, height = this.rect?.height || 120;
    const left = parseFloat(this.style.left) || this.rect?.left || 0, top = parseFloat(this.style.top) || this.rect?.top || 30;
    return { width, height, left, top, right: left + width, bottom: top + height };
  }
  setPointerCapture(id) { this.capture = id; }
  hasPointerCapture(id) { return this.capture === id; }
  releasePointerCapture(id) { this.capture = null; this.emit('lostpointercapture', { pointerId: id }); }
  emit(name, values = {}) {
    const event = { type: name, target: this, button: 0, pointerId: 1, clientX: 40, clientY: 110, preventDefault() {}, stopPropagation() {}, ...values };
    for (const callback of [...this.handlers.get(name) || []]) callback(event); return event;
  }
}
export function ribbonDom(t) {
  const cleanups = [], disposers = []; installDom({ after: callback => cleanups.push(callback) });
  const globals = ['window', 'innerWidth', 'innerHeight', 'ResizeObserver', 'requestAnimationFrame', 'cancelAnimationFrame'].map(key => [key, globalThis[key]]);
  const body = new RibbonElement('body'); body.attached = true;
  const host = new RibbonElement('main'); host.setAttribute('id', 'studio'); body.append(host);
  const tabs = new RibbonElement('div'); tabs.setAttribute('id', 'ribbon-tabs');
  const tab = new RibbonElement('button'); tab.setAttribute('aria-selected', 'true'); tabs.append(tab); host.append(tabs);
  const bar = new RibbonElement('div'); bar.className = 'ribbon-bar'; host.append(bar);
  const panel = new RibbonElement('div'); panel.setAttribute('id', 'ribbon'); panel.rect = { left: 0, top: 30, width: 900, height: 120 }; host.append(panel);
  const events = new RibbonElement('document'), windowEvents = new RibbonElement('window');
  Object.assign(document, { body, activeElement: body, createElement: tag => new RibbonElement(tag),
    createElementNS: (namespace, tag) => Object.assign(new RibbonElement(tag), { namespaceURI: namespace }), createComment: () => new RibbonElement('comment'),
    querySelector: selector => body.querySelector(selector), addEventListener: (...args) => events.addEventListener(...args),
    removeEventListener: (...args) => events.removeEventListener(...args) });
  Object.assign(globalThis, { window: windowEvents, innerWidth: 1280, innerHeight: 800,
    ResizeObserver: class { observe() {} disconnect() {} }, requestAnimationFrame: () => 0, cancelAnimationFrame() {} });
  t.after(() => { for (const dispose of disposers) dispose(); for (const cleanup of cleanups) cleanup(); for (const [key, value] of globals) if (value === undefined) delete globalThis[key]; else globalThis[key] = value; });
  return { body, host, panel, events, windowEvents, tab, disposeWith: callback => disposers.push(callback) };
}
