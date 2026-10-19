import { changePaletteLayout, paletteDockWidths, paletteLayout, paletteRect, resizePaletteRect, paletteStorageKey } from './palette-layout.mjs';

const element = (tag, attributes = {}, children = []) => {
  const result = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    if (name === 'text') result.textContent = value;
    else if (name === 'class') result.className = value;
    else if (name === 'hidden') result.hidden = value;
    else result.setAttribute(name, String(value));
  }
  result.append(...children); return result;
};
const control = (label, text, callback, className = '') => {
  const result = element('button', { type: 'button', 'aria-label': label, title: label, text, class: className });
  result.addEventListener('click', callback); return result;
};
const firstControl = node => node.querySelector('button:not(:disabled),select:not(:disabled),input:not(:disabled),[tabindex="0"]');

/** Consume only an Escape that actually closes/cancels local palette UI. Studio shortcuts still bubble. */
export function handlePaletteEscape(event, close) {
  if (event.key !== 'Escape' || !close()) return false;
  event.preventDefault(); event.stopPropagation(); return true;
}

/** Reusable independent palettes. Content renderers keep their native controls and event handlers. */
export class PaletteDock {
  constructor({ workspace, legacyPalette, legacySplitter, palettes, legacySettings = {}, render,
    onActiveChange = () => {}, onLayoutChange = () => {}, onHide = () => {}, keepOpen = () => false, report = () => {}, storage, storageKey = paletteStorageKey, loadStyles = true }) {
    if (!workspace || typeof render !== 'function') throw new Error('A palette workspace and content renderer are required.');
    this.workspace = workspace; this.definitions = palettes.map(value => typeof value === 'string' ? { id: value, title: value } : value);
    this.ids = this.definitions.map(value => value.id); this.render = render; this.onActiveChange = onActiveChange; this.onLayoutChange = onLayoutChange; this.onHide = onHide; this.keepOpen = keepOpen; this.report = report;
    this.storageKey = storageKey; this.frames = new Map(); this.autoId = null; this.hideTimer = null; this.disposed = false; this.gesture = null; this.frame = 0;
    try { this.storage = storage === undefined ? globalThis.localStorage : storage; } catch { this.storage = null; }
    let saved;
    try { const text = this.storage?.getItem(this.storageKey); if (text && text.length < 65536) saved = JSON.parse(text); } catch { /* Storage is optional. */ }
    this.state = paletteLayout(saved, this.ids, legacySettings);
    legacyPalette?.remove(); legacySplitter?.remove(); workspace.classList.remove('left', 'hide-palette'); workspace.classList.add('pd-workspace');
    if (loadStyles && !document.querySelector('link[data-palette-docking]')) document.head.append(element('link', { rel: 'stylesheet', href: new URL('./palette-docking.css', import.meta.url).href, 'data-palette-docking': '' }));
    this.viewport = workspace.querySelector('.viewport') || workspace.firstElementChild;
    this.leftRail = this.createRail('left'); this.rightRail = this.createRail('right');
    this.leftDock = this.createDock('left'); this.rightDock = this.createDock('right');
    workspace.insertBefore(this.leftRail, this.viewport); workspace.insertBefore(this.leftDock, this.viewport);
    this.viewport.after(this.rightDock, this.rightRail);
    this.overlay = element('div', { class: 'pd-overlay' }); workspace.append(this.overlay);
    this.launcher = this.createLauncher(); workspace.append(this.launcher);
    this.live = element('span', { class: 'pd-sr-only', role: 'status', 'aria-live': 'polite' }); workspace.append(this.live);
    this.observer = new ResizeObserver(() => this.schedule()); this.observer.observe(workspace); this.observer.observe(this.viewport);
    this.outside = event => {
      for (const frame of this.frames.values()) if (!frame.menu.contains(event.target)) frame.menu.open = false;
      if (!this.launcher.contains(event.target)) this.launcher.open = false;
      if (this.autoId) { const frame = this.frames.get(this.autoId); if (!frame.element.contains(event.target) && !frame.edge.contains(event.target)) this.hideAuto({ implicit: true }); }
    };
    document.addEventListener('pointerdown', this.outside);
    this.escape = event => {
      if (event.key !== 'Escape') return;
      if (this.gesture) { event.preventDefault(); event.stopPropagation(); this.endGesture(true); }
      else if (this.autoId && this.keepOpen(this.autoId)) { event.preventDefault(); event.stopPropagation(); this.hideAuto(); }
    };
    document.addEventListener('keydown', this.escape, true);
    this.apply(); this.refresh(); this.persist(); this.onActiveChange(this.state.active);
  }

  bounds() {
    const rect = this.workspace.getBoundingClientRect(), owned = [this.viewport, this.leftDock, this.rightDock, this.leftRail, this.rightRail, this.overlay, this.launcher, this.live];
    const other = [...this.workspace.children].filter(child => !owned.includes(child) && !child.hidden && !['absolute', 'fixed'].includes(getComputedStyle(child).position));
    return { width: Math.max(1, rect.width - other.reduce((sum, child) => sum + child.getBoundingClientRect().width, 0)), height: rect.height };
  }
  snapshot() { return JSON.parse(JSON.stringify(this.state)); }
  containsFocus() { return [...this.frames.values()].some(frame => frame.element.contains(document.activeElement)) || this.launcher.contains(document.activeElement); }
  contentFor(id) { return this.frames.get(id)?.content || null; }
  isVisible(id) { const panel = this.state.panels[id]; return !!panel?.open && !this.state.hidden && (!this.anchored(panel) || this.autoId === id); }
  anchored(panel) { return panel.mode === 'auto' || panel.mode === 'dock' && this.compact; }
  persist() { try { this.storage?.setItem(this.storageKey, JSON.stringify(this.state)); } catch { /* Private or full storage does not block the drawing. */ } }
  announce(text) { this.live.textContent = text; }
  commit(action, { announce = '', apply = true, save = true } = {}) {
    const previous = this.state; this.state = changePaletteLayout(previous, action, this.bounds());
    if (previous === this.state) { this.announce('Palette positions are locked. Unlock the layout to move or resize them.'); return; }
    if (apply) this.apply(); if (save) this.persist();
    if (previous.active !== this.state.active) this.onActiveChange(this.state.active);
    if (announce) this.announce(announce);
  }
  show(id = this.state.active, options = {}) {
    this.commit({ type: 'show', id });
    if (options.mode || options.side) this.commit({ type: 'place', id, ...options });
    const frame = this.frames.get(id); this.refresh(id);
    if (this.anchored(this.state.panels[id])) this.reveal(id);
    else this.raise(id);
    if (options.focus !== false) frame.selector.focus({ preventScroll: true });
    this.onActiveChange(id); return frame.content;
  }
  close(id = this.state.active) {
    const frame = this.frames.get(id), hadFocus = frame?.element.contains(document.activeElement);
    this.commit({ type: 'close', id }, { announce: `${this.title(id)} closed. Reopen it from Palettes.` });
    if (hadFocus) this.launcher.querySelector('summary').focus({ preventScroll: true });
  }
  toggle() { this.hideAuto(); this.commit({ type: 'toggle' }); if (!this.state.hidden) this.refresh(); }
  dockSide(side) {
    const id = this.state.active, panel = this.state.panels[id];
    this.place(id, 'dock', side || (panel.side === 'left' ? 'right' : 'left'));
  }
  setLocked(value) { this.endGesture(true); this.commit({ type: 'lock', value }, { announce: value ? 'Palette layout locked.' : 'Palette layout unlocked.' }); }
  reset() {
    this.endGesture(true); this.hideAuto(); this.state = paletteLayout(null, this.ids); this.apply(); this.refresh(); this.persist();
    this.onActiveChange(this.state.active); this.announce(`Palette layout reset. ${this.title(this.ids[0])} is docked on the right.`);
  }
  place(id, mode, side) {
    if (this.state.locked) { this.announce('Palette positions are locked. Unlock the layout to change docking.'); return; }
    this.hideAuto(); this.commit({ type: 'place', id, mode, side }, { announce: `${this.title(id)} ${mode === 'auto' ? 'auto-hidden' : mode === 'float' ? 'floating' : 'docked'}${mode !== 'float' ? ` on the ${side || this.state.panels[id].side}` : ''}.` });
    if (this.isVisible(id) && !this.frames.get(id).rendered) this.refresh(id);
  }
  title(id) { return this.definitions.find(value => value.id === id)?.title || id; }

  createLauncher() {
    const select = element('select', { 'aria-label': 'Open drawing palette' }, [element('option', { value: '', text: 'Choose a palette…' }), ...this.definitions.map(value => element('option', { value: value.id, text: value.title }))]);
    select.addEventListener('change', () => { if (select.value) { const id = select.value; select.value = ''; this.launcher.open = false; this.show(id); } });
    this.lock = element('input', { type: 'checkbox', 'aria-label': 'Lock palette layout' }); this.lock.addEventListener('change', () => this.setLocked(this.lock.checked));
    const result = element('details', { class: 'pd-launcher' }, [element('summary', { text: 'Palettes ▾', 'aria-label': 'Drawing palettes menu' }),
      element('div', { class: 'pd-launcher-body' }, [element('label', { text: 'Open a palette' }, [select]),
        element('label', { class: 'pd-lock-label' }, [this.lock, document.createTextNode('Lock palette layout')]),
        control('Reset palette layout', 'Reset palette layout', () => { result.open = false; this.reset(); }),
        element('p', { text: 'Open several palettes together. Drag a title grip to float; dock or auto-hide from its layout menu.' })])]);
    result.addEventListener('keydown', event => handlePaletteEscape(event, () => {
      if (!result.open) return false; result.open = false; result.querySelector('summary').focus(); return true;
    }));
    result.addEventListener('toggle', () => { if (result.open) this.positionMenu(result); });
    return result;
  }
  createRail(side) { return element('nav', { class: `pd-edge pd-edge-${side}`, 'aria-label': `${side} anchored palettes` }); }
  createDock(side) {
    const panes = element('div', { class: 'pd-dock-panes' });
    const splitter = element('div', { class: 'pd-splitter', role: 'separator', tabindex: '0', 'aria-label': `Resize ${side} docked palettes`, 'aria-orientation': 'vertical' });
    splitter.addEventListener('pointerdown', event => this.beginDockResize(event, side));
    splitter.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight'].includes(event.key) || this.state.locked) return;
      event.preventDefault(); event.stopPropagation(); const sign = side === 'left' ? 1 : -1;
      this.commit({ type: 'dock-width', side, width: this[`${side}Dock`].getBoundingClientRect().width + (event.key === 'ArrowRight' ? 20 : -20) * sign });
    });
    const dock = element('div', { class: `pd-dock pd-dock-${side}` }, side === 'left' ? [panes, splitter] : [splitter, panes]);
    dock.panes = panes; dock.splitter = splitter; return dock;
  }
  createFrame(id) {
    const title = this.title(id), key = `pd-${this.ids.indexOf(id)}`;
    const selector = element('select', { class: 'pd-selector', 'aria-label': `Palette shown in ${title}` }, this.definitions.map(value => element('option', { value: value.id, text: value.title })));
    selector.value = id;
    selector.addEventListener('change', () => {
      const selected = selector.value; selector.value = id;
      if (selected !== id) {
        const target = this.state.panels[selected];
        if (!target.open && !this.state.locked) this.state.panels[selected] = { ...this.state.panels[id], open: false };
        this.close(id); this.show(selected);
      }
    });
    const move = control(`Move ${title} palette`, '⠿', () => {}, 'pd-move');
    move.setAttribute('aria-describedby', `${key}-help`); move.addEventListener('pointerdown', event => this.beginMove(event, id));
    move.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || this.state.locked) return;
      event.preventDefault(); event.stopPropagation(); const panel = this.state.panels[id];
      if (panel.mode !== 'float') this.commit({ type: 'place', id, mode: 'float' });
      const current = paletteRect(panel, this.bounds()), step = event.shiftKey ? 1 : 20;
      this.commit({ type: 'rect', id, rect: { ...current, x: current.x + (event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0), y: current.y + (event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0) } });
    });
    const pin = control(`Auto-hide ${title} palette`, '⌖', () => { const panel = this.state.panels[id]; this.place(id, panel.mode === 'auto' ? 'dock' : 'auto', panel.side); });
    const close = control(`Close ${title} palette`, '×', () => this.close(id));
    const choices = [ ['Dock left', 'dock', 'left'], ['Dock right', 'dock', 'right'], ['Float palette', 'float'], ['Auto-hide left', 'auto', 'left'], ['Auto-hide right', 'auto', 'right'] ];
    const menu = element('details', { class: 'pd-menu' }, [element('summary', { text: '⋯', 'aria-label': `${title} palette layout` }),
      element('div', { class: 'pd-menu-body' }, choices.map(([label, mode, side]) => control(`${label}: ${title}`, label, () => { menu.open = false; this.place(id, mode, side); }))) ]);
    const content = element('div', { class: 'palette-content pd-content', id: `${key}-content` });
    const resize = element('div', { class: 'pd-resize', role: 'separator', tabindex: '0', 'aria-label': `Resize ${title} palette`, 'aria-orientation': 'horizontal', title: 'Drag to resize; arrow keys resize, Shift for one pixel' });
    resize.addEventListener('pointerdown', event => this.beginResize(event, id));
    resize.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || this.state.locked) return;
      event.preventDefault(); event.stopPropagation(); const current = paletteRect(this.state.panels[id], this.bounds()), step = event.shiftKey ? 1 : 20;
      this.commit({ type: 'rect', id, rect: resizePaletteRect(current, event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0, event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0, this.bounds()) });
    });
    const header = element('header', { class: 'pd-title' }, [move, selector, pin, menu, close]);
    const window = element('section', { class: 'pd-window', 'data-palette': id, 'aria-label': `${title} palette` }, [header, content, resize,
      element('span', { class: 'pd-sr-only', id: `${key}-help`, text: 'Drag to float and move. Arrow keys move a floating palette; Shift moves one pixel. Use the layout menu to dock or auto-hide.' })]);
    const edge = control(`Show ${title} palette`, title, () => this.activateAnchor(id), 'pd-edge-button');
    edge.setAttribute('aria-controls', `${key}-content`); edge.setAttribute('aria-expanded', 'false');
    const frame = { id, element: window, selector, header, content, resize, move, pin, menu, close, edge, rendered: false, visible: false, generation: 0, hovered: false, edgeHovered: false, cleanup: null };
    menu.addEventListener('toggle', () => { if (menu.open) this.positionMenu(menu); });
    edge.addEventListener('pointerenter', event => { if (event.pointerType !== 'touch') { frame.edgeHovered = true; this.reveal(id); } });
    edge.addEventListener('pointerleave', () => { frame.edgeHovered = false; this.queueHide(); });
    edge.addEventListener('focus', () => { if (!this.suppressReveal) this.reveal(id); });
    edge.addEventListener('blur', () => this.queueHide());
    edge.addEventListener('keydown', event => {
      if (handlePaletteEscape(event, () => this.autoId === id && this.hideAuto())) return;
      if (['ArrowLeft', 'ArrowRight', 'Enter', ' '].includes(event.key)) { event.preventDefault(); event.stopPropagation(); this.reveal(id); firstControl(window)?.focus({ preventScroll: true }); }
    });
    window.addEventListener('pointerenter', () => { frame.hovered = true; clearTimeout(this.hideTimer); });
    window.addEventListener('pointerleave', () => { frame.hovered = false; this.queueHide(); });
    window.addEventListener('focusout', () => this.queueHide());
    window.addEventListener('pointerdown', () => this.raise(id));
    window.addEventListener('focusin', () => this.raise(id));
    window.addEventListener('keydown', event => handlePaletteEscape(event, () => {
      if (this.gesture) this.endGesture(true);
      else if (menu.open) { menu.open = false; menu.querySelector('summary').focus(); }
      else if (this.autoId === id) { this.hideAuto(); this.suppressReveal = true; edge.focus({ preventScroll: true }); this.suppressReveal = false; }
      else return false;
      return true;
    }));
    this.frames.set(id, frame); return frame;
  }

  apply() {
    if (this.disposed) return;
    const bounds = this.bounds(); this.compact = bounds.width < 700;
    const activePanels = Object.entries(this.state.panels).filter(([, panel]) => panel.open && !this.state.hidden);
    const width = side => Math.max(0, ...activePanels.filter(([, panel]) => panel.mode === 'dock' && panel.side === side).map(([, panel]) => panel.width));
    const hasRail = side => activePanels.some(([, panel]) => this.anchored(panel) && panel.side === side);
    const railWidth = parseFloat(getComputedStyle(this.leftRail).width) || 28;
    const dockWidths = paletteDockWidths(width('left'), width('right'), bounds.width, (hasRail('left') + hasRail('right')) * railWidth);
    this.workspace.classList.toggle('pd-locked', this.state.locked); this.lock.checked = this.state.locked;
    for (const side of ['left', 'right']) {
      const dock = this[`${side}Dock`], rail = this[`${side}Rail`];
      dock.hidden = !dockWidths[side]; dock.style.width = `${dockWidths[side]}px`; rail.hidden = !hasRail(side);
      dock.splitter.setAttribute('aria-valuenow', String(Math.round(dockWidths[side]))); dock.splitter.setAttribute('aria-valuemin', '180'); dock.splitter.setAttribute('aria-valuemax', '640'); dock.splitter.setAttribute('aria-disabled', String(this.state.locked)); dock.splitter.tabIndex = this.state.locked ? -1 : 0;
    }
    if (this.autoId && (!this.state.panels[this.autoId].open || this.state.hidden || !this.anchored(this.state.panels[this.autoId]))) this.autoId = null;
    for (const id of this.ids) {
      const panel = this.state.panels[id], frame = this.frames.get(id) || (panel.open ? this.createFrame(id) : null); if (!frame) continue;
      const anchored = this.anchored(panel), visible = panel.open && !this.state.hidden && (!anchored || this.autoId === id);
      if (frame.visible && !visible) this.releaseFrame(frame);
      frame.visible = visible;
      frame.element.hidden = !visible; frame.edge.hidden = !panel.open || this.state.hidden || !anchored;
      frame.edge.setAttribute('aria-expanded', String(visible && anchored)); frame.pin.setAttribute('aria-pressed', String(panel.mode === 'auto'));
      frame.move.disabled = this.state.locked; frame.pin.disabled = this.state.locked;
      for (const item of frame.menu.querySelectorAll('button')) item.disabled = this.state.locked;
      frame.resize.hidden = panel.mode !== 'float' || this.state.locked;
      frame.element.classList.toggle('pd-floating', panel.mode === 'float'); frame.element.classList.toggle('pd-anchored', anchored);
      const host = panel.mode === 'float' || anchored ? this.overlay : this[`${panel.side}Dock`].panes;
      if (!visible) frame.element.remove(); else if (frame.element.parentNode !== host) host.append(frame.element);
      const rail = this[`${panel.side}Rail`]; if (frame.edge.parentNode !== rail) rail.append(frame.edge);
      if (panel.mode === 'float') this.position(frame, paletteRect(panel, bounds));
      else if (anchored) {
        const canvasWidth = bounds.width - dockWidths.left - dockWidths.right - (hasRail('left') + hasRail('right')) * railWidth;
        const rect = paletteRect({ ...panel, width: Math.min(panel.width, canvasWidth), height: bounds.height, y: 0 }, { width: Math.max(1, canvasWidth), height: bounds.height });
        rect.x = panel.side === 'left' ? (hasRail('left') ? railWidth : 0) + dockWidths.left : bounds.width - rect.width - (hasRail('right') ? railWidth : 0) - dockWidths.right;
        this.position(frame, paletteRect(rect, bounds));
      } else for (const property of ['left', 'top', 'width', 'height']) frame.element.style.removeProperty(property);
      if (frame.menu.open) this.positionMenu(frame.menu);
    }
    this.launcher.style.left = `${(dockWidths.left + bounds.width - dockWidths.right) / 2}px`;
    this.onLayoutChange(this.snapshot());
  }
  position(frame, rect) { for (const [property, value] of Object.entries({ left: rect.x, top: rect.y, width: rect.width, height: rect.height })) frame.element.style[property] = `${Math.round(value)}px`; }
  schedule() { if (!this.frame && !this.disposed) this.frame = requestAnimationFrame(() => { this.frame = 0; this.apply(); for (const [id, frame] of this.frames) if (this.isVisible(id) && !frame.rendered) this.refresh(id); }); }
  positionMenu(menu) {
    const body = menu.lastElementChild, anchor = menu.querySelector('summary').getBoundingClientRect();
    const width = Math.min(menu === this.launcher ? 250 : 180, innerWidth - 16), height = Math.min(body.scrollHeight, innerHeight - 16);
    body.style.width = `${Math.max(1, width)}px`; body.style.maxHeight = `${Math.max(1, innerHeight - 16)}px`;
    body.style.left = `${Math.max(8, Math.min(innerWidth - width - 8, anchor.right - width))}px`;
    body.style.top = `${Math.max(8, Math.min(innerHeight - height - 8, anchor.bottom + 4))}px`;
  }
  refresh(id) {
    for (const [key, frame] of this.frames) if ((!id || key === id) && this.isVisible(key)) {
      const generation = ++frame.generation; frame.cleanup?.(); frame.cleanup = null; frame.rendered = true;
      try {
        const result = this.render(key, frame.content);
        Promise.resolve(result).then(cleanup => {
          if (typeof cleanup !== 'function') return;
          if (this.disposed || generation !== frame.generation) cleanup(); else frame.cleanup = cleanup;
        }).catch(error => { if (!this.disposed && generation === frame.generation) this.report(error); });
      } catch (error) { this.report(error); }
    }
  }
  releaseFrame(frame) {
    frame.generation++; frame.cleanup?.(); frame.cleanup = null; frame.rendered = false; frame.hovered = false; frame.menu.open = false;
    this.onHide(frame.id, frame.content);
  }
  raise(id) {
    if (this.state.active !== id) this.commit({ type: 'activate', id }, { apply: false });
    this.order = [...(this.order || []).filter(value => value !== id), id];
    this.order.forEach((key, index) => { const frame = this.frames.get(key); if (frame) frame.element.style.zIndex = String(index + 1); });
  }
  reveal(id) {
    const panel = this.state.panels[id]; if (this.state.hidden || !panel.open || !this.anchored(panel)) return;
    clearTimeout(this.hideTimer); const changed = this.autoId !== id; this.autoId = id; this.apply();
    if (changed || !this.frames.get(id).rendered) this.refresh(id);
  }
  activateAnchor(id) {
    this.reveal(id);
    if (this.autoId === id) this.frames.get(id)?.selector.focus({ preventScroll: true });
  }
  hideAuto({ implicit = false } = {}) {
    clearTimeout(this.hideTimer);
    if (!this.autoId || implicit && this.keepOpen(this.autoId)) return false;
    this.autoId = null; this.apply(); return true;
  }
  queueHide() {
    clearTimeout(this.hideTimer); this.hideTimer = setTimeout(() => {
      const frame = this.frames.get(this.autoId); if (frame && !frame.hovered && !frame.edgeHovered && !frame.element.contains(document.activeElement) && !frame.edge.contains(document.activeElement)) this.hideAuto({ implicit: true });
    }, 320);
  }

  beginMove(event, id) {
    if (event.button !== 0 || this.state.locked) return;
    const window = this.frames.get(id).element.getBoundingClientRect(), workspace = this.workspace.getBoundingClientRect();
    const rect = { x: window.left - workspace.left, y: window.top - workspace.top, width: window.width, height: window.height };
    this.startGesture(event, (dx, dy) => {
      if (Math.abs(dx) + Math.abs(dy) < 4 && this.state.panels[id].mode !== 'float') return;
      if (this.state.panels[id].mode !== 'float') this.commit({ type: 'place', id, mode: 'float' }, { save: false });
      this.commit({ type: 'rect', id, rect: { ...rect, x: rect.x + dx, y: rect.y + dy } }, { save: false });
    });
  }
  beginResize(event, id) {
    if (event.button !== 0 || this.state.locked || this.state.panels[id].mode !== 'float') return;
    const rect = paletteRect(this.state.panels[id], this.bounds());
    this.startGesture(event, (dx, dy) => this.commit({ type: 'rect', id, rect: resizePaletteRect(rect, dx, dy, this.bounds()) }, { save: false }));
  }
  beginDockResize(event, side) {
    if (event.button !== 0 || this.state.locked) return;
    const width = this[`${side}Dock`].getBoundingClientRect().width;
    this.startGesture(event, dx => this.commit({ type: 'dock-width', side, width: width + dx * (side === 'left' ? 1 : -1) }, { save: false }));
  }
  startGesture(event, update) {
    this.endGesture(); event.preventDefault(); event.stopPropagation(); const target = this.workspace, before = this.snapshot(), x = event.clientX, y = event.clientY;
    this.launcher.open = false; for (const frame of this.frames.values()) frame.menu.open = false;
    target.setPointerCapture(event.pointerId);
    const move = current => { if (current.pointerId === event.pointerId) update(current.clientX - x, current.clientY - y); };
    const end = current => { if (current.pointerId === event.pointerId) this.endGesture(current.type === 'pointercancel' || current.type === 'lostpointercapture'); };
    this.gesture = { target, pointer: event.pointerId, before, move, end };
    target.addEventListener('pointermove', move); target.addEventListener('pointerup', end); target.addEventListener('pointercancel', end); target.addEventListener('lostpointercapture', end);
  }
  endGesture(cancel = false) {
    const value = this.gesture; if (!value) return; this.gesture = null;
    value.target.removeEventListener('pointermove', value.move); value.target.removeEventListener('pointerup', value.end); value.target.removeEventListener('pointercancel', value.end); value.target.removeEventListener('lostpointercapture', value.end);
    if (value.target.hasPointerCapture(value.pointer)) value.target.releasePointerCapture(value.pointer);
    if (cancel) { this.state = paletteLayout(value.before, this.ids); this.apply(); }
    this.persist();
  }
  dispose() {
    this.disposed = true; this.endGesture(true); clearTimeout(this.hideTimer); cancelAnimationFrame(this.frame); this.observer.disconnect(); document.removeEventListener('pointerdown', this.outside); document.removeEventListener('keydown', this.escape, true);
    for (const frame of this.frames.values()) this.releaseFrame(frame);
    for (const node of [this.leftDock, this.rightDock, this.leftRail, this.rightRail, this.overlay, this.launcher, this.live]) node.remove();
    this.workspace.classList.remove('pd-workspace', 'pd-locked');
  }
}
