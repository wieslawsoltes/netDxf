import { changePaletteLayout, paletteAvailable, paletteDockWidths, paletteLayout, paletteRect, resizePaletteFromEdge, paletteDropTarget, paletteDropPreview, paletteStorageKey, paletteGroups, paletteGroupFor, paletteStackHeights } from './palette-layout.mjs';
import { openPaletteAppearance, paletteOpacity } from './palette-appearance.mjs';
import { raiseWorkspaceWindow, releaseWorkspaceWindow } from './workspace-foreground.mjs';

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
const chromeIcon = name => {
  const paths = {
    move: 'M5 3h1M10 3h1M5 7h1M10 7h1M5 11h1M10 11h1',
    pin: 'M5 2h6M6 2v5l-2 3h8l-2-3V2M8 10v4',
    close: 'M4 4l8 8M12 4l-8 8',
    float: 'M6 2h8v8H6zM3 6H2v8h8v-1',
    dock: 'M2 2h12v12H2zM9 2v12M11 5h1M11 8h1M11 11h1',
    menu: 'M4 6l4 4l4-4', expand: 'M4 6l4 4l4-4', collapse: 'M4 10l4-4l4 4'
  };
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 16 16'); svg.setAttribute('class', `pd-icon pd-icon-${name}`); svg.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', paths[name]); svg.append(path); return svg;
};
const iconControl = (label, name, callback, className = '') => {
  const button = control(label, '', callback, className); button.append(chromeIcon(name)); return button;
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
    this.storageKey = storageKey; this.frames = new Map(); this.groups = new Map(); this.autoId = null; this.hideTimer = null; this.disposed = false; this.gesture = null; this.frame = 0;
    this.floatingExpanded = new Set(); this.lockMask = 0; this.ctrlHeld = false;
    try { this.storage = storage === undefined ? globalThis.localStorage : storage; } catch { this.storage = null; }
    let saved;
    try { const text = this.storage?.getItem(this.storageKey); if (text && text.length < 65536) saved = JSON.parse(text); } catch { /* Storage is optional. */ }
    this.state = paletteLayout(saved, this.ids, legacySettings);
    legacyPalette?.remove(); legacySplitter?.remove(); workspace.classList.remove('left', 'hide-palette'); workspace.classList.add('pd-workspace');
    if (loadStyles && !document.querySelector('link[data-palette-docking]')) document.head.append(element('link', { rel: 'stylesheet', href: new URL('./palette-docking.css', import.meta.url).href, 'data-palette-docking': '' }));
    this.viewport = workspace.querySelector('.viewport') || workspace.firstElementChild;
    this.center = workspace.querySelector('.drawing-area') || this.viewport;
    this.leftRail = this.createRail('left'); this.rightRail = this.createRail('right');
    this.leftDock = this.createDock('left'); this.rightDock = this.createDock('right');
    workspace.insertBefore(this.leftRail, this.center); workspace.insertBefore(this.leftDock, this.center);
    this.center.after(this.rightDock, this.rightRail);
    this.overlay = element('div', { class: 'pd-overlay' }); workspace.append(this.overlay);
    this.dropPreview = element('div', { class: 'pd-drop-preview', hidden: true }, [element('span')]);
    this.dropZones = element('div', { class: 'pd-drop-zones', hidden: true, 'aria-hidden': 'true' }, [
      element('div', { class: 'pd-drop-zone pd-drop-left', text: 'Dock left' }), element('div', { class: 'pd-drop-zone pd-drop-right', text: 'Dock right' }), this.dropPreview]);
    workspace.append(this.dropZones);
    this.launcher = this.createLauncher(); workspace.append(this.launcher);
    this.live = element('span', { class: 'pd-sr-only', role: 'status', 'aria-live': 'polite' }); workspace.append(this.live);
    this.observer = new ResizeObserver(() => this.schedule()); this.observer.observe(workspace); this.observer.observe(this.viewport);
    this.outside = event => {
      for (const frame of this.frames.values()) if (!frame.menu.contains(event.target)) frame.menu.open = false;
      if (!this.launcher.contains(event.target)) this.launcher.open = false;
      if (this.autoId) { const frame = this.frames.get(this.autoId); if (!frame.element.contains(event.target) && !frame.edge.contains(event.target)) this.hideAuto({ implicit: true }); }
      for (const id of this.floatingExpanded) if (!this.frames.get(id)?.element.contains(event.target)) this.queueFloatingHide(id);
    };
    document.addEventListener('pointerdown', this.outside);
    this.escape = event => {
      if (event.key !== 'Escape') return;
      if (this.appearance?.dialog.open) return;
      if (this.gesture) { event.preventDefault(); event.stopPropagation(); this.endGesture(true); }
      else if (this.autoId && this.keepOpen(this.autoId)) { event.preventDefault(); event.stopPropagation(); this.hideAuto(); }
    };
    document.addEventListener('keydown', this.escape, true);
    this.modifierDown = event => { if (event.key === 'Control' && !this.ctrlHeld) { this.ctrlHeld = true; this.apply(); } };
    this.modifierUp = event => { if (event.key === 'Control' && this.ctrlHeld) { this.ctrlHeld = false; if (this.gesture?.temporaryUnlock) this.endGesture(); this.apply(); } };
    this.blur = () => { this.ctrlHeld = false; this.endGesture(true); this.apply(); };
    document.addEventListener('keydown', this.modifierDown, true); document.addEventListener('keyup', this.modifierUp, true);
    globalThis.window?.addEventListener('blur', this.blur);
    this.apply(); this.persist(); this.onActiveChange(this.state.active);
  }

  bounds() {
    const rect = this.workspace.getBoundingClientRect(), owned = [this.center, this.viewport, this.leftDock, this.rightDock, this.leftRail, this.rightRail, this.overlay, this.dropZones, this.launcher, this.live];
    const other = [...this.workspace.children].filter(child => !owned.includes(child) && !child.hidden && !['absolute', 'fixed'].includes(getComputedStyle(child).position));
    return { width: Math.max(1, rect.width - other.reduce((sum, child) => sum + child.getBoundingClientRect().width, 0)), height: rect.height };
  }
  snapshot() { return JSON.parse(JSON.stringify(this.state)); }
  containsFocus() { return this.leftDock.contains(document.activeElement) || this.rightDock.contains(document.activeElement) || [...this.frames.values()].some(frame => frame.element.contains(document.activeElement)) || this.launcher.contains(document.activeElement); }
  contentFor(id) { return this.frames.get(id)?.content || null; }
  isVisible(id) {
    const panel = this.state.panels[id];
    if (!paletteAvailable(this.state, id)) return false;
    if (this.anchored(panel)) return this.autoId === id;
    return panel.mode !== 'dock' || paletteGroups(this.state, panel.side).some(group => group.active === id);
  }
  anchored(panel) { return panel.mode === 'auto' || panel.mode === 'dock' && this.compact; }
  /** LOCKUI belongs to the shell; legacy palette locking remains an independent preference. */
  setLockMask(mask) { const value = Number.isInteger(mask) ? mask & 15 : 0; if (value === this.lockMask) return; this.endGesture(true); this.lockMask = value; this.apply(); }
  positionLocked(id = this.state.active) { return this.state.locked || !!(this.lockMask & (id !== null && this.state.panels[id]?.mode === 'float' ? 8 : 2)); }
  isLocked(id = this.state.active, event) {
    if (this.ctrlHeld || event?.ctrlKey || this.gesture?.temporaryUnlock) return false;
    // A drag is authorized against its starting placement. Undocking must not
    // suddenly freeze when the destination's LOCKUI category differs. Changing
    // either lock policy cancels the gesture before applying the new policy.
    if (this.gesture?.authorized && this.gesture.id === id) return false;
    return this.positionLocked(id);
  }
  persist() { try { this.storage?.setItem(this.storageKey, JSON.stringify(this.state)); } catch { /* Private or full storage does not block the drawing. */ } }
  announce(text) { this.live.textContent = text; }
  commit(action, { announce = '', apply = true, save = true, temporaryUnlock = this.ctrlHeld || this.gesture?.temporaryUnlock || false } = {}) {
    const id = action.type === 'dock-width' || action.type === 'resize-groups' ? null : action.id || this.state.active;
    if (!['lock', 'transparency-disabled', 'toggle', 'hide-all', 'show-all', 'show', 'close', 'activate', 'opacity'].includes(action.type) && this.isLocked(id) && !temporaryUnlock) {
      this.announce('Palette positions are locked. Hold Ctrl to move or resize temporarily.'); return;
    }
    const previous = this.state; this.state = changePaletteLayout(previous, action, this.bounds(), { temporaryUnlock });
    if (previous === this.state) {
      if (this.state.locked) this.announce('Palette positions are locked. Unlock the layout to move or resize them.');
      else if (this.state.panels[action.id]?.allowDocking === false) this.announce('Allow docking is off for this palette.');
      return;
    }
    if (apply) this.apply(); if (save) this.persist();
    if (previous.active !== this.state.active) this.onActiveChange(this.state.active);
    if (announce) this.announce(announce);
  }
  show(id = this.state.active, options = {}) {
    const wasRendered = this.isVisible(id) && this.frames.get(id)?.rendered;
    this.commit({ type: 'show', id });
    if (options.mode || options.side) this.commit({ type: 'place', id, ...options });
    // Explicit Show also refreshes out-of-band data (for example a prepared edit
    // preview). New visibility is rendered by apply(), so only refresh it once.
    const frame = this.frames.get(id); if (wasRendered || !frame.rendered) this.refresh(id);
    if (this.anchored(this.state.panels[id])) this.reveal(id);
    else this.raise(id);
    this.revealFloating(id);
    if (options.focus !== false) frame.selector.focus({ preventScroll: true });
    this.onActiveChange(id); return frame.content;
  }
  close(id = this.state.active) {
    const frame = this.frames.get(id), hadFocus = frame?.element.contains(document.activeElement) || [...this.groups.values()].some(group => group.tabs.get(id) === document.activeElement);
    this.commit({ type: 'close', id }, { announce: `${this.title(id)} closed. Reopen it from Palettes.` });
    if (hadFocus) {
      const group = paletteGroupFor(this.state, id), active = group && paletteGroups(this.state, group.side).find(value => value.key === group.group.tabs[0])?.active;
      if (active && this.isVisible(active)) this.frames.get(active)?.selector.focus({ preventScroll: true });
      else this.launcher.querySelector('summary').focus({ preventScroll: true });
    }
  }
  get allHidden() { return !this.ids.some(id => paletteAvailable(this.state, id)); }
  hideAll() {
    const focused = this.containsFocus();
    this.endGesture(true); this.hideAuto(); this.commit({ type: 'hide-all' });
    if (focused) this.launcher.querySelector('summary').focus({ preventScroll: true });
  }
  showAll() { this.endGesture(true); this.commit({ type: 'show-all' }); }
  toggle() {
    if (this.state.hidden) this.showAll();
    else if (!this.allHidden) this.hideAll();
    else this.commit({ type: 'toggle' });
  }
  dockSide(side) {
    const id = this.state.active, panel = this.state.panels[id];
    this.place(id, 'dock', side || (panel.side === 'left' ? 'right' : 'left'));
  }
  setLocked(value) { this.endGesture(true); this.commit({ type: 'lock', value }, { announce: value ? 'Palette layout locked.' : 'Palette layout unlocked.' }); }
  reset() {
    this.restore(null); this.announce(`Palette layout reset. ${this.title(this.ids[0])} is docked on the right.`);
  }
  /** Restore a detached snapshot or defaults. Explicit restoration also replaces the prior lock state. */
  restore(layout) {
    this.appearance?.close(); this.appearance = null;
    this.endGesture(true); clearTimeout(this.hideTimer); this.autoId = null; this.floatingExpanded.clear(); this.launcher.open = false;
    const focused = this.containsFocus();
    for (const frame of this.frames.values()) if (frame.visible) { this.releaseFrame(frame); frame.visible = false; }
    this.state = paletteLayout(layout, this.ids);
    const bounds = this.bounds();
    for (const [id, panel] of Object.entries(this.state.panels)) if (panel.mode === 'float') this.state.panels[id] = { ...panel, ...paletteRect(panel, bounds) };
    this.apply(); this.persist(); this.onActiveChange(this.state.active);
    if (focused) this.launcher.querySelector('summary').focus({ preventScroll: true });
    this.announce('Palette layout restored.'); return this.snapshot();
  }
  place(id, mode, side) {
    if (this.isLocked(id)) { this.announce('Palette positions are locked. Hold Ctrl to change docking temporarily.'); return; }
    if (!this.state.panels[id].allowDocking && mode !== 'float') { this.announce('Allow docking is off for this palette.'); return; }
    this.hideAuto(); this.commit({ type: 'place', id, mode, side }, { announce: `${this.title(id)} ${mode === 'auto' ? 'auto-hidden' : mode === 'float' ? 'floating' : 'docked'}${mode !== 'float' ? ` on the ${side || this.state.panels[id].side}` : ''}.` });
    if (this.isVisible(id) && !this.frames.get(id).rendered) this.refresh(id);
  }
  /** Dock groups are layout state only; palette renderers never need to know their container. */
  groupWith(id, target) { this.hideAuto(); this.commit({ type: 'group', id, target }); this.focusPalette(id); }
  detach(id) { this.commit({ type: 'detach', id }); this.focusPalette(id); }
  reorder(id, delta, tab = false) { this.commit({ type: tab ? 'move-tab' : 'move-group', id, delta }); this.focusPalette(id); }
  focusPalette(id) {
    const location = paletteGroupFor(this.state, id), group = location && this.groups.get(`${location.side}:${location.group.tabs[0]}`);
    const tab = group?.tabs.get(id);
    if (tab && !group.tablist.hidden && this.isVisible(id)) tab.focus({ preventScroll: true });
    else if (this.isVisible(id)) this.frames.get(id)?.selector.focus({ preventScroll: true });
  }
  setAllowDocking(id, value) {
    this.endGesture(true);
    const frame = this.frames.get(id), rect = frame?.element.getBoundingClientRect(), origin = this.workspace.getBoundingClientRect();
    this.commit({ type: 'allow-docking', id, value,
      rect: this.isVisible(id) && rect ? { x: rect.left - origin.left, y: rect.top - origin.top, width: rect.width, height: rect.height } : undefined });
    this.focusPalette(id);
  }
  title(id) { return this.definitions.find(value => value.id === id)?.title || id; }
  editAppearance(id) {
    if (this.appearance?.dialog.open) return;
    const frame = this.frames.get(id); if (!frame) return;
    this.appearance = openPaletteAppearance({ title: this.title(id), appearance: this.state.panels[id],
      disabled: this.state.transparencyDisabled, origin: frame.menu.querySelector('summary'),
      onApply: value => {
        if (this.disposed) return;
        this.commit({ type: 'opacity', id, ...value }, { announce: 'Palette transparency updated.' });
      } });
    this.appearanceId = this.appearance ? id : null;
  }
  updateOpacity(frame, focused = frame.element.contains(document.activeElement)) {
    frame.element.style.opacity = String(paletteOpacity(this.state.panels[frame.id], {
      floating: this.state.panels[frame.id].mode === 'float' || this.anchored(this.state.panels[frame.id]),
      disabled: this.state.transparencyDisabled, hovered: frame.hovered, focused, menuOpen: frame.menu.open
    }));
  }

  createLauncher() {
    const select = element('select', { 'aria-label': 'Open drawing palette' }, [element('option', { value: '', text: 'Choose a palette…' }), ...this.definitions.map(value => element('option', { value: value.id, text: value.title }))]);
    select.addEventListener('change', () => { if (select.value) { const id = select.value; select.value = ''; this.launcher.open = false; this.show(id); } });
    this.lock = element('input', { type: 'checkbox', 'aria-label': 'Lock palette layout' }); this.lock.addEventListener('change', () => this.setLocked(this.lock.checked));
    this.opaque = element('input', { type: 'checkbox', 'aria-label': 'Disable all palette transparency' });
    this.opaque.addEventListener('change', () => this.commit({ type: 'transparency-disabled', value: this.opaque.checked }));
    const result = element('details', { class: 'pd-launcher' }, [element('summary', { text: 'Palettes ▾', 'aria-label': 'Drawing palettes menu' }),
      element('div', { class: 'pd-launcher-body' }, [element('label', { text: 'Open a palette' }, [select]),
        element('label', { class: 'pd-lock-label' }, [this.lock, document.createTextNode('Lock palette layout')]),
        element('label', { class: 'pd-lock-label' }, [this.opaque, document.createTextNode('Disable all palette transparency')]),
        control('Reset palette layout', 'Reset palette layout', () => { result.open = false; this.reset(); }),
        element('p', { text: 'Drag a title grip or tab to move. Release over an edge to dock, a palette center to group, or its top or bottom to stack. Hold Ctrl to keep floating. Escape cancels.' })])]);
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
      if (!['ArrowLeft', 'ArrowRight'].includes(event.key) || this.isLocked(null, event)) return;
      event.preventDefault(); event.stopPropagation(); const sign = side === 'left' ? 1 : -1;
      this.commit({ type: 'dock-width', side, width: this[`${side}Dock`].getBoundingClientRect().width + (event.key === 'ArrowRight' ? 20 : -20) * sign }, { temporaryUnlock: event.ctrlKey });
    });
    const dock = element('div', { class: `pd-dock pd-dock-${side}` }, side === 'left' ? [panes, splitter] : [splitter, panes]);
    dock.panes = panes; dock.splitter = splitter; return dock;
  }
  createGroup(key) {
    const tablist = element('div', { class: 'pd-tabs', role: 'tablist', 'aria-label': 'Docked palette group' });
    const body = element('div', { class: 'pd-group-body' });
    const frame = element('div', { class: 'pd-group', 'data-group': key }, [body, tablist]);
    const splitter = element('div', { class: 'pd-stack-splitter', role: 'separator', tabindex: '0', 'aria-orientation': 'horizontal' });
    const group = { element: frame, body, tablist, splitter, tabs: new Map(), panels: new Map(), key };
    splitter.addEventListener('pointerdown', event => this.beginStackResize(event, group));
    splitter.addEventListener('keydown', event => {
      if (!['ArrowUp', 'ArrowDown'].includes(event.key) || this.isLocked(null, event)) return;
      event.preventDefault(); event.stopPropagation();
      this.commit({ type: 'resize-groups', id: group.id, nextId: group.nextId,
        firstHeight: group.height + (event.key === 'ArrowDown' ? 1 : -1) * (event.shiftKey ? 1 : 20), height: this.bounds().height }, { temporaryUnlock: event.ctrlKey });
    });
    this.groups.set(key, group); return group;
  }
  applyGroups(side, height) {
    const values = this.compact ? [] : paletteGroups(this.state, side), sizes = paletteStackHeights(values, height), nodes = [], hosts = new Map();
    values.forEach((value, index) => {
      const key = `${side}:${value.key}`, group = this.groups.get(key) || this.createGroup(key);
      group.id = value.key; group.active = value.active; group.side = side; group.nextId = values[index + 1]?.key; group.height = sizes[index]; group.visibleTabs = value.tabs;
      group.element.style.height = `${sizes[index]}px`; group.tablist.hidden = value.tabs.length < 2;
      const panels = [], tabs = value.tabs.map(id => {
        let tab = group.tabs.get(id);
        if (!tab) {
          tab = control(`Show ${this.title(id)} in palette group`, this.title(id), () => { this.show(id, { focus: false }); this.focusPalette(id); }, 'pd-tab');
          tab.setAttribute('role', 'tab'); tab.setAttribute('id', `pd-tab-${this.ids.indexOf(id)}`);
          tab.title = `${this.title(id)} — drag to detach or dock; right-click for layout`;
          tab.addEventListener('pointerdown', event => this.beginMove(event, id, group.element.getBoundingClientRect(), true));
          tab.addEventListener('contextmenu', event => this.openFrameMenu(id, event));
          tab.addEventListener('keydown', event => {
            if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
            event.preventDefault(); event.stopPropagation();
            const current = group.visibleTabs.indexOf(id), count = group.visibleTabs.length;
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? count - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + count) % count;
            this.show(group.visibleTabs[next], { focus: false }); this.focusPalette(group.visibleTabs[next]);
          });
          group.tabs.set(id, tab);
        }
        tab.setAttribute('aria-selected', String(id === value.active)); tab.tabIndex = id === value.active ? 0 : -1;
        const panelId = `pd-panel-${this.ids.indexOf(id)}`;
        tab.setAttribute('aria-controls', panelId);
        let panel = group.panels.get(id);
        if (!panel) { panel = element('div', { class: 'pd-tab-panel', id: panelId }); group.panels.set(id, panel); }
        panel.hidden = id !== value.active;
        if (value.tabs.length > 1) { panel.setAttribute('role', 'tabpanel'); panel.setAttribute('aria-labelledby', `pd-tab-${this.ids.indexOf(id)}`); }
        else { panel.removeAttribute('role'); panel.removeAttribute('aria-labelledby'); }
        panels.push(panel); if (id === value.active) hosts.set(id, panel);
        return tab;
      });
      this.orderNodes(group.tablist, tabs); this.orderNodes(group.body, panels);
      nodes.push(group.element);
      if (group.nextId) {
        group.splitter.setAttribute('aria-label', `Resize ${this.title(value.active)} and ${this.title(values[index + 1].active)} palette groups`);
        group.splitter.setAttribute('aria-valuenow', String(Math.round(group.height)));
        group.splitter.setAttribute('aria-valuemin', '160'); group.splitter.setAttribute('aria-valuemax', String(Math.round(group.height + sizes[index + 1] - 160)));
        group.splitter.setAttribute('aria-disabled', String(this.isLocked(null))); group.splitter.tabIndex = this.isLocked(null) ? -1 : 0;
        nodes.push(group.splitter);
      }
    });
    this.orderNodes(this[`${side}Dock`].panes, nodes); return hosts;
  }
  orderNodes(parent, nodes) {
    for (const child of [...parent.children]) if (!nodes.includes(child)) child.remove();
    nodes.forEach((node, index) => { if (parent.children[index] !== node) { if (parent.children[index]) parent.insertBefore(node, parent.children[index]); else parent.append(node); } });
  }
  createFrame(id) {
    const title = this.title(id), key = `pd-${this.ids.indexOf(id)}`;
    const selector = element('select', { class: 'pd-selector', 'aria-label': `Palette shown in ${title}` }, this.definitions.map(value => element('option', { value: value.id, text: value.title })));
    selector.value = id;
    selector.addEventListener('change', () => {
      const selected = selector.value; selector.value = id;
      if (selected !== id) {
        const target = this.state.panels[selected];
        if (!target.open && !this.isLocked(id)) this.state.panels[selected] = {
          ...this.state.panels[id], allowDocking: target.allowDocking,
          opacity: target.opacity, rolloverOpacity: target.rolloverOpacity,
          mode: target.allowDocking ? this.state.panels[id].mode : 'float', open: false
        };
        this.close(id); this.show(selected);
      }
    });
    const move = iconControl(`Move ${title} palette`, 'move', () => {}, 'pd-move');
    move.setAttribute('aria-describedby', `${key}-help`); move.addEventListener('pointerdown', event => this.beginMove(event, id));
    move.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || this.isLocked(id, event)) return;
      event.preventDefault(); event.stopPropagation(); const panel = this.state.panels[id];
      if (panel.mode !== 'float') this.commit({ type: 'place', id, mode: 'float' }, { temporaryUnlock: event.ctrlKey });
      const current = paletteRect(panel, this.bounds()), step = event.shiftKey ? 1 : 20;
      // One key gesture is authorized against its starting placement, even if
      // undocking changes the applicable LOCKUI category before the rect update.
      this.commit({ type: 'rect', id, rect: { ...current, x: current.x + (event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0), y: current.y + (event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0) } }, { temporaryUnlock: event.ctrlKey || panel.mode !== 'float' });
    });
    const pin = iconControl(`Auto-hide ${title} palette`, 'pin', () => { const panel = this.state.panels[id];
      if (panel.mode === 'float') this.setFloatingAutoHide(id, !panel.autoHide);
      else this.place(id, panel.mode === 'auto' ? 'dock' : 'auto', panel.side);
    }, 'pd-pin');
    const roll = iconControl(`Expand ${title} palette`, 'expand', () => {
      if (this.floatingExpanded.has(id)) this.hideFloating(id); else this.revealFloating(id);
    }, 'pd-roll');
    const float = iconControl(`Float ${title} palette`, 'float', () => this.toggleFloating(id), 'pd-float');
    const close = iconControl(`Close ${title} palette`, 'close', () => this.close(id), 'pd-close');
    const choices = [ ['Dock left', 'dock', 'left'], ['Dock right', 'dock', 'right'], ['Float palette', 'float'], ['Auto-hide left', 'auto', 'left'], ['Auto-hide right', 'auto', 'right'] ];
    const placementButtons = choices.map(([label, mode, side]) => {
      const button = control(`${label}: ${title}`, label, () => { menu.open = false; this.place(id, mode, side); });
      button.dataset.docking = String(mode !== 'float'); return button;
    });
    const allowDocking = control(`Allow docking: ${title}`, 'Allow docking', () => { menu.open = false; this.setAllowDocking(id, !this.state.panels[id].allowDocking); });
    allowDocking.setAttribute('role', 'menuitemcheckbox');
    const autoHide = control(`Auto-hide floating ${title} palette`, 'Auto-hide floating palette', () => { menu.open = false; this.setFloatingAutoHide(id, !this.state.panels[id].autoHide); });
    autoHide.setAttribute('role', 'menuitemcheckbox');
    const groupTarget = element('select', { 'aria-label': `Group ${title} with` });
    const groupButton = control(`Group ${title} with chosen palette`, 'Group with chosen palette', () => { menu.open = false; if (groupTarget.value) this.groupWith(id, groupTarget.value); });
    const detach = control(`Separate ${title} from group`, 'Separate from group', () => { menu.open = false; this.detach(id); });
    const groupUp = control(`Move ${title} group up`, 'Move group up', () => { menu.open = false; this.reorder(id, -1); });
    const groupDown = control(`Move ${title} group down`, 'Move group down', () => { menu.open = false; this.reorder(id, 1); });
    const tabLeft = control(`Move ${title} tab left`, 'Move tab left', () => { menu.open = false; this.reorder(id, -1, true); });
    const tabRight = control(`Move ${title} tab right`, 'Move tab right', () => { menu.open = false; this.reorder(id, 1, true); });
    const appearance = control(`Transparency: ${title}`, 'Transparency…', () => { menu.open = false; this.editAppearance(id); });
    const menu = element('details', { class: 'pd-menu' }, [element('summary', { 'aria-label': `${title} palette layout`, title: `${title} palette layout` }, [chromeIcon('menu')]),
      element('div', { class: 'pd-menu-body' }, [...placementButtons, allowDocking, autoHide, appearance, element('hr'), groupTarget, groupButton, detach, groupUp, groupDown, tabLeft, tabRight]) ]);
    const content = element('div', { class: 'palette-content pd-content', id: `${key}-content` });
    const edgeNames = { n: 'north', ne: 'north-east', e: 'east', se: 'south-east', s: 'south', sw: 'south-west', w: 'west', nw: 'north-west' };
    const resizeEdges = Object.keys(edgeNames).map(edgeName => {
      const grip = element('div', { class: edgeName === 'se' ? 'pd-resize' : 'pd-resize-edge', 'data-resize-edge': edgeName, role: 'separator',
        tabindex: edgeName === 'se' || edgeName.length === 1 ? '0' : '-1', 'aria-label': edgeName === 'se' ? `Resize ${title} palette` : `Resize ${title} palette from ${edgeNames[edgeName]} edge`,
        'aria-orientation': edgeName === 'n' || edgeName === 's' ? 'horizontal' : 'vertical', title: `Resize from ${edgeNames[edgeName]}; arrow keys resize, Shift for one pixel` });
      grip.addEventListener('pointerdown', event => this.beginResize(event, id, edgeName));
      grip.addEventListener('keydown', event => {
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || this.isLocked(id, event)) return;
        event.preventDefault(); event.stopPropagation(); const current = paletteRect(this.state.panels[id], this.bounds()), step = event.shiftKey ? 1 : 20;
        this.commit({ type: 'rect', id, rect: resizePaletteFromEdge(current, event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0, event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0, this.bounds(), edgeName) }, { temporaryUnlock: event.ctrlKey });
      }); return grip;
    });
    const resize = resizeEdges.find(grip => grip.getAttribute('data-resize-edge') === 'se');
    const header = element('header', { class: 'pd-title' }, [move, selector, roll, float, pin, menu, close]);
    header.addEventListener('pointerdown', event => { if (event.target === header) this.beginMove(event, id); });
    header.addEventListener('dblclick', event => {
      if ((event.target === header || move.contains(event.target)) && performance.now() - (this.lastGripToggle ?? -Infinity) > 100) this.toggleFloating(id);
    });
    header.addEventListener('contextmenu', event => this.openFrameMenu(id, event));
    const window = element('section', { class: 'pd-window', 'data-palette': id, 'aria-label': `${title} palette` }, [header, content, ...resizeEdges,
      element('span', { class: 'pd-sr-only', id: `${key}-help`, text: 'Drag to move. Release at an edge to dock, over a palette center to group, or at its top or bottom to stack. Hold Ctrl to keep floating. Escape cancels. Double-click the grip to dock or float. Arrow keys move a floating palette; Shift moves one pixel. Right-click the title for layout options.' })]);
    const edge = control(`Show ${title} palette`, title, () => this.activateAnchor(id), 'pd-edge-button');
    edge.setAttribute('aria-controls', `${key}-content`); edge.setAttribute('aria-expanded', 'false');
    const frame = { id, element: window, selector, header, content, resize, resizeEdges, move, pin, roll, float, menu, close, edge,
      placementButtons, allowDocking, autoHide, groupTarget, groupButton, detach, groupUp, groupDown, tabLeft, tabRight,
      rendered: false, visible: false, generation: 0, hovered: false, edgeHovered: false, cleanup: null };
    menu.addEventListener('toggle', () => {
      if (menu.open) { this.positionMenu(menu); this.raise(id); }
      else { menu.contextPoint = null; this.queueFloatingHide(id); releaseWorkspaceWindow(frame.element); }
      this.updateOpacity(frame);
    });
    menu.addEventListener('keydown', event => {
      if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key) || event.target.tagName === 'SELECT' || event.altKey || event.ctrlKey || event.metaKey) return;
      if (!menu.open) return;
      const buttons = [...menu.querySelectorAll('button')].filter(button => !button.disabled);
      if (!buttons.length) return;
      event.preventDefault(); event.stopPropagation();
      const current = buttons.indexOf(document.activeElement), next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 :
        current < 0 ? event.key === 'ArrowUp' ? buttons.length - 1 : 0 : (current + (event.key === 'ArrowUp' ? -1 : 1) + buttons.length) % buttons.length;
      buttons[next].focus({ preventScroll: true });
    });
    edge.addEventListener('pointerenter', event => { if (event.pointerType !== 'touch') { frame.edgeHovered = true; this.reveal(id); } });
    edge.addEventListener('pointerleave', () => { frame.edgeHovered = false; this.queueHide(); });
    edge.addEventListener('focus', () => { if (!this.suppressReveal) this.reveal(id); });
    edge.addEventListener('blur', () => this.queueHide());
    edge.addEventListener('keydown', event => {
      if (handlePaletteEscape(event, () => this.autoId === id && this.hideAuto())) return;
      if (['ArrowLeft', 'ArrowRight', 'Enter', ' '].includes(event.key)) { event.preventDefault(); event.stopPropagation(); this.reveal(id); firstControl(window)?.focus({ preventScroll: true }); }
    });
    window.addEventListener('pointerenter', event => { frame.hovered = true; clearTimeout(this.hideTimer); clearTimeout(frame.hideTimer); if (event.pointerType !== 'touch') this.revealFloating(id); this.updateOpacity(frame); });
    window.addEventListener('pointerleave', () => { frame.hovered = false; this.queueHide(); this.queueFloatingHide(id); this.updateOpacity(frame); });
    window.addEventListener('focusout', event => { this.queueHide(); this.queueFloatingHide(id); this.updateOpacity(frame, frame.element.contains(event.relatedTarget)); });
    window.addEventListener('pointerdown', () => this.raise(id));
    window.addEventListener('focusin', event => { this.raise(id); if (event.target !== roll) this.revealFloating(id); this.updateOpacity(frame, true); });
    window.addEventListener('keydown', event => handlePaletteEscape(event, () => {
      if (this.gesture) this.endGesture(true);
      else if (menu.open) { menu.open = false; menu.querySelector('summary').focus(); }
      else if (this.autoId === id) { this.hideAuto(); this.suppressReveal = true; edge.focus({ preventScroll: true }); this.suppressReveal = false; }
      else if (this.floatingExpanded.has(id)) { roll.focus({ preventScroll: true }); this.hideFloating(id); }
      else return false;
      return true;
    }));
    this.frames.set(id, frame); return frame;
  }

  toggleFloating(id) {
    const panel = this.state.panels[id];
    this.place(id, panel.mode === 'float' && panel.allowDocking ? 'dock' : 'float', panel.side);
    this.focusPalette(id);
  }
  setFloatingAutoHide(id, value) {
    if (this.state.panels[id]?.mode !== 'float') { this.announce('Float the palette before enabling floating auto-hide.'); return; }
    if (this.isLocked(id)) { this.announce('Palette positions are locked. Hold Ctrl to change auto-hide temporarily.'); return; }
    if (value) this.floatingExpanded.add(id); else this.floatingExpanded.delete(id);
    this.commit({ type: 'auto-hide', id, value }, { announce: value ? `${this.title(id)} will roll up to its floating title bar when not in use.` : `${this.title(id)} stays expanded.` });
    this.queueFloatingHide(id);
  }
  revealFloating(id) {
    const panel = this.state.panels[id];
    if (panel?.mode !== 'float' || !panel.autoHide || !paletteAvailable(this.state, id) || this.floatingExpanded.has(id)) return;
    this.floatingExpanded.add(id); clearTimeout(this.frames.get(id)?.hideTimer); this.apply(); raiseWorkspaceWindow(this.overlay);
  }
  hideFloating(id, { implicit = false } = {}) {
    const frame = this.frames.get(id); if (!frame || !this.floatingExpanded.has(id)) return false;
    if (implicit && (frame.hovered || frame.menu.open || frame.element.contains(document.activeElement) || this.gesture?.id === id ||
      this.keepOpen(id) || this.appearance?.dialog.open && this.appearanceId === id)) return false;
    clearTimeout(frame.hideTimer); this.floatingExpanded.delete(id); this.apply(); return true;
  }
  queueFloatingHide(id) {
    const frame = this.frames.get(id); if (!frame) return;
    clearTimeout(frame.hideTimer);
    if (this.state.panels[id].mode === 'float' && this.state.panels[id].autoHide)
      frame.hideTimer = setTimeout(() => this.hideFloating(id, { implicit: true }), 320);
  }
  openFrameMenu(id, event) {
    if (this.gesture) return;
    event.preventDefault(); event.stopPropagation();
    // An inactive group's tab must activate its frame before its menu can be displayed.
    if (!this.isVisible(id)) this.show(id, { focus: false });
    const frame = this.frames.get(id); if (!frame) return;
    for (const other of this.frames.values()) if (other !== frame) other.menu.open = false;
    this.launcher.open = false;
    frame.menu.contextPoint = { x: event.clientX, y: event.clientY }; frame.menu.open = true;
    this.positionMenu(frame.menu);
    frame.placementButtons.find(button => !button.disabled)?.focus({ preventScroll: true });
  }

  apply() {
    if (this.disposed) return;
    const bounds = this.bounds(); this.compact = bounds.width < 700;
    const activePanels = Object.entries(this.state.panels).filter(([id]) => paletteAvailable(this.state, id));
    const width = side => Math.max(0, ...activePanels.filter(([, panel]) => panel.mode === 'dock' && panel.side === side).map(([, panel]) => panel.width));
    const hasRail = side => activePanels.some(([, panel]) => this.anchored(panel) && panel.side === side);
    const railWidth = parseFloat(getComputedStyle(this.leftRail).width) || 28;
    const dockWidths = paletteDockWidths(width('left'), width('right'), bounds.width, (hasRail('left') + hasRail('right')) * railWidth);
    this.workspace.classList.toggle('pd-locked', this.isLocked(null)); this.lock.checked = this.state.locked;
    this.opaque.checked = this.state.transparencyDisabled;
    for (const side of ['left', 'right']) {
      const dock = this[`${side}Dock`], rail = this[`${side}Rail`];
      dock.hidden = !dockWidths[side]; dock.style.width = `${dockWidths[side]}px`; rail.hidden = !hasRail(side);
      dock.splitter.setAttribute('aria-valuenow', String(Math.round(dockWidths[side]))); dock.splitter.setAttribute('aria-valuemin', '180'); dock.splitter.setAttribute('aria-valuemax', '640'); dock.splitter.setAttribute('aria-disabled', String(this.isLocked(null))); dock.splitter.tabIndex = this.isLocked(null) ? -1 : 0;
    }
    if (this.autoId && (!paletteAvailable(this.state, this.autoId) || !this.anchored(this.state.panels[this.autoId]))) this.autoId = null;
    const groupHosts = new Map([...this.applyGroups('left', bounds.height), ...this.applyGroups('right', bounds.height)]);
    const toRender = [];
    for (const id of this.ids) {
      const panel = this.state.panels[id], frame = this.frames.get(id) || (panel.open ? this.createFrame(id) : null); if (!frame) continue;
      const anchored = this.anchored(panel), visible = this.isVisible(id);
      const rolling = panel.mode === 'float' && panel.autoHide, expanded = !rolling || this.floatingExpanded.has(id), locked = this.isLocked(id);
      if (!rolling || !visible) this.floatingExpanded.delete(id);
      if (frame.visible && !visible) this.releaseFrame(frame);
      frame.visible = visible;
      frame.element.hidden = !visible; frame.edge.hidden = !paletteAvailable(this.state, id) || !anchored;
      frame.edge.setAttribute('aria-expanded', String(visible && anchored)); frame.pin.setAttribute('aria-pressed', String(panel.mode === 'auto' || rolling));
      frame.pin.title = panel.mode === 'auto' ? `Pin ${this.title(id)} palette` : rolling ? `Keep ${this.title(id)} palette expanded` : `Auto-hide ${this.title(id)} palette`;
      frame.move.disabled = locked; frame.float.disabled = locked || panel.mode === 'float' && !panel.allowDocking;
      frame.pin.disabled = locked || panel.mode !== 'float' && !panel.allowDocking;
      const floatAction = panel.mode === 'float' ? 'Dock' : 'Float';
      frame.float.setAttribute('aria-label', `${floatAction} ${this.title(id)} palette`); frame.float.title = `${floatAction} ${this.title(id)} palette`;
      if (frame.floatAction !== floatAction) { frame.float.replaceChildren(chromeIcon(floatAction === 'Dock' ? 'dock' : 'float')); frame.floatAction = floatAction; }
      this.updateGroupMenu(frame);
      const floatingRect = paletteRect(panel, bounds);
      for (const handle of frame.resizeEdges) {
        handle.hidden = panel.mode !== 'float' || locked || !expanded;
        const edge = handle.getAttribute('data-resize-edge'), verticalSize = edge === 'n' || edge === 's';
        handle.setAttribute('aria-valuenow', String(Math.round(verticalSize ? floatingRect.height : floatingRect.width)));
        handle.setAttribute('aria-valuemin', String(Math.min(verticalSize ? 160 : 220, verticalSize ? bounds.height : bounds.width)));
        handle.setAttribute('aria-valuemax', String(Math.round(verticalSize ? edge === 'n' ? floatingRect.y + floatingRect.height : bounds.height - floatingRect.y : edge.includes('w') ? floatingRect.x + floatingRect.width : bounds.width - floatingRect.x)));
        handle.setAttribute('aria-valuetext', `${Math.round(floatingRect.width)} by ${Math.round(floatingRect.height)} pixels`);
      }
      frame.content.hidden = !expanded; frame.element.classList.toggle('pd-rolled', !expanded);
      frame.roll.hidden = !rolling; frame.roll.setAttribute('aria-expanded', String(expanded)); frame.roll.setAttribute('aria-controls', frame.content.getAttribute('id'));
      const rollAction = expanded ? 'Collapse' : 'Expand'; frame.roll.setAttribute('aria-label', `${rollAction} ${this.title(id)} palette`); frame.roll.title = `${rollAction} ${this.title(id)} palette`;
      if (frame.rollAction !== rollAction) { frame.roll.replaceChildren(chromeIcon(expanded ? 'collapse' : 'expand')); frame.rollAction = rollAction; }
      frame.element.classList.toggle('pd-floating', panel.mode === 'float'); frame.element.classList.toggle('pd-anchored', anchored);
      const host = panel.mode === 'float' || anchored ? this.overlay : groupHosts.get(id);
      if (!visible) frame.element.remove(); else if (frame.element.parentNode !== host) host.append(frame.element);
      const rail = this[`${panel.side}Rail`]; if (frame.edge.parentNode !== rail) rail.append(frame.edge);
      if (panel.mode === 'float') { const rect = floatingRect; if (!expanded) rect.height = frame.header.getBoundingClientRect().height || 27; this.position(frame, rect); }
      else if (anchored) {
        const canvasWidth = bounds.width - dockWidths.left - dockWidths.right - (hasRail('left') + hasRail('right')) * railWidth;
        const rect = paletteRect({ ...panel, width: Math.min(panel.width, canvasWidth), height: bounds.height, y: 0 }, { width: Math.max(1, canvasWidth), height: bounds.height });
        rect.x = panel.side === 'left' ? (hasRail('left') ? railWidth : 0) + dockWidths.left : bounds.width - rect.width - (hasRail('right') ? railWidth : 0) - dockWidths.right;
        this.position(frame, paletteRect(rect, bounds));
      } else for (const property of ['left', 'top', 'width', 'height']) frame.element.style.removeProperty(property);
      if (frame.menu.open) this.positionMenu(frame.menu);
      this.updateOpacity(frame);
      if (visible && !frame.rendered) toRender.push(id);
    }
    if (![...this.frames.values()].some(frame => frame.visible && frame.element.parentNode === this.overlay)) releaseWorkspaceWindow(this.overlay);
    this.launcher.style.left = `${(dockWidths.left + bounds.width - dockWidths.right) / 2}px`;
    this.onLayoutChange(this.snapshot());
    for (const id of toRender) this.refresh(id);
  }
  updateGroupMenu(frame) {
    const { id } = frame, panel = this.state.panels[id], locked = this.isLocked(id), found = paletteGroupFor(this.state, id);
    frame.allowDocking.disabled = locked; frame.allowDocking.setAttribute('aria-checked', String(panel.allowDocking));
    frame.autoHide.disabled = locked || panel.mode !== 'float'; frame.autoHide.setAttribute('aria-checked', String(panel.autoHide));
    for (const button of frame.placementButtons) button.disabled = locked || !panel.allowDocking && button.dataset.docking === 'true';
    const candidates = this.ids.filter(other => other !== id && paletteAvailable(this.state, other) && this.state.panels[other].mode === 'dock' && this.state.panels[other].allowDocking && !found?.group.tabs.includes(other));
    const current = frame.groupTarget.value;
    // Keep the native select and its options stable while the menu is being used.
    const signature = candidates.join('\0');
    if (frame.groupSignature !== signature) {
      frame.groupTarget.replaceChildren(...candidates.map(other => element('option', { value: other, text: this.title(other) })));
      if (candidates.includes(current)) frame.groupTarget.value = current;
      frame.groupSignature = signature;
    }
    frame.groupTarget.disabled = frame.groupButton.disabled = locked || !panel.allowDocking || !candidates.length;
    frame.detach.disabled = locked || !found || found.group.tabs.length < 2;
    const groups = found ? paletteGroups(this.state, found.side) : [], index = groups.findIndex(group => group.tabs.includes(id));
    frame.groupUp.disabled = locked || index <= 0; frame.groupDown.disabled = locked || index < 0 || index >= groups.length - 1;
    const tabs = found?.group.tabs.filter(tab => paletteAvailable(this.state, tab)) || [], tabIndex = tabs.indexOf(id);
    frame.tabLeft.disabled = locked || tabIndex <= 0; frame.tabRight.disabled = locked || tabIndex < 0 || tabIndex >= tabs.length - 1;
  }
  position(frame, rect) { for (const [property, value] of Object.entries({ left: rect.x, top: rect.y, width: rect.width, height: rect.height })) frame.element.style[property] = `${Math.round(value)}px`; }
  schedule() { if (!this.frame && !this.disposed) this.frame = requestAnimationFrame(() => { this.frame = 0; this.apply(); for (const [id, frame] of this.frames) if (this.isVisible(id) && !frame.rendered) this.refresh(id); }); }
  positionMenu(menu) {
    const body = menu.lastElementChild, anchor = menu.querySelector('summary').getBoundingClientRect();
    const width = Math.min(menu === this.launcher ? 250 : 180, innerWidth - 16);
    const below = Math.max(1, innerHeight - anchor.bottom - 12), above = Math.max(1, anchor.top - 12);
    const openAbove = !menu.contextPoint && body.scrollHeight > below && above > below;
    const availableHeight = menu.contextPoint ? innerHeight - 16 : openAbove ? above : below;
    const height = Math.min(body.scrollHeight, availableHeight);
    body.style.width = `${Math.max(1, width)}px`; body.style.maxHeight = `${Math.max(1, availableHeight)}px`;
    body.style.left = `${Math.max(8, Math.min(innerWidth - width - 8, menu.contextPoint?.x ?? anchor.right - width))}px`;
    body.style.top = `${Math.max(8, Math.min(innerHeight - height - 8, menu.contextPoint?.y ?? (openAbove ? anchor.top - height - 4 : anchor.bottom + 4)))}px`;
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
    clearTimeout(frame.hideTimer); this.floatingExpanded.delete(frame.id);
    releaseWorkspaceWindow(frame.element);
    frame.generation++; frame.cleanup?.(); frame.cleanup = null; frame.rendered = false; frame.hovered = false; frame.menu.open = false;
    this.onHide(frame.id, frame.content);
  }
  raise(id) {
    if (this.state.active !== id) this.commit({ type: 'activate', id }, { apply: false });
    this.order = [...(this.order || []).filter(value => value !== id), id];
    this.order.forEach((key, index) => { const frame = this.frames.get(key); if (frame) frame.element.style.zIndex = String(index + 1); });
    const frame = this.frames.get(id), panel = this.state.panels[id];
    if (this.isVisible(id) && (panel.mode === 'float' || this.anchored(panel))) raiseWorkspaceWindow(this.overlay);
    else if (frame?.menu.open) raiseWorkspaceWindow(frame.element);
  }
  reveal(id) {
    const panel = this.state.panels[id]; if (!paletteAvailable(this.state, id) || !this.anchored(panel)) return;
    clearTimeout(this.hideTimer); this.autoId = id; this.apply();
    if (!this.frames.get(id).rendered) this.refresh(id);
    raiseWorkspaceWindow(this.overlay);
  }
  activateAnchor(id) {
    this.reveal(id);
    if (this.autoId === id) this.frames.get(id)?.selector.focus({ preventScroll: true });
  }
  hideAuto({ implicit = false } = {}) {
    clearTimeout(this.hideTimer);
    if (!this.autoId || implicit && (this.keepOpen(this.autoId) ||
      this.appearance?.dialog.open && this.appearanceId === this.autoId)) return false;
    this.autoId = null; this.apply(); return true;
  }
  queueHide() {
    clearTimeout(this.hideTimer); this.hideTimer = setTimeout(() => {
      const frame = this.frames.get(this.autoId); if (frame && !frame.hovered && !frame.edgeHovered && !frame.element.contains(document.activeElement) && !frame.edge.contains(document.activeElement)) this.hideAuto({ implicit: true });
    }, 320);
  }

  beginMove(event, id, sourceRect = null, fromTab = false) {
    if (event.button !== 0 || this.isLocked(id, event)) return;
    const window = sourceRect || this.frames.get(id).element.getBoundingClientRect(), workspace = this.workspace.getBoundingClientRect();
    const rect = { x: window.left - workspace.left, y: window.top - workspace.top, width: window.width,
      height: this.state.panels[id].mode === 'float' && this.state.panels[id].autoHide ? this.state.panels[id].height : window.height };
    this.startGesture(event, (dx, dy, current) => {
      if (!this.gesture.moved && Math.abs(dx) + Math.abs(dy) < 4) return;
      if (!this.gesture.moved) this.commit({ type: 'activate', id }, { apply: false, save: false });
      this.gesture.moved = true;
      if (this.state.panels[id].mode !== 'float') this.commit({ type: 'place', id, mode: 'float' }, { save: false });
      this.commit({ type: 'rect', id, rect: { ...rect, x: rect.x + dx, y: rect.y + dy } }, { save: false });
      raiseWorkspaceWindow(this.overlay);
      this.updateDropPreview(id, current);
    }, { kind: 'move', id, fromTab });
  }
  updateDropPreview(id, event) {
    const bounds = this.bounds(), origin = this.workspace.getBoundingClientRect();
    const groups = [...this.groups.values()].filter(group => group.element.isConnected && !group.element.hidden).map(group => {
      const rect = group.element.getBoundingClientRect(), tabs = !group.tablist.hidden && group.tablist.getBoundingClientRect();
      return { target: group.active, side: group.side, x: rect.left - origin.left, y: rect.top - origin.top, width: rect.width, height: rect.height,
        tabTop: tabs ? tabs.top - origin.top : undefined };
    });
    const target = event.ctrlKey ? null : paletteDropTarget(this.state, id, { x: event.clientX - origin.left, y: event.clientY - origin.top }, bounds, groups);
    this.dropZones.hidden = !this.state.panels[id].allowDocking; this.dropZones.style.width = `${bounds.width}px`;
    const zoneWidth = Math.min(40, bounds.width / 2);
    for (const zone of this.dropZones.querySelectorAll('.pd-drop-zone')) zone.style.width = `${zoneWidth}px`;
    const rect = target?.type === 'side' ? paletteDropPreview(this.state, id, target.side, bounds, parseFloat(getComputedStyle(this.leftRail).width) || 28) : target?.rect;
    this.dropPreview.hidden = !rect;
    let message = 'Release to leave the palette floating';
    if (rect) {
      for (const key of ['x', 'y', 'width', 'height']) this.dropPreview.style[{ x: 'left', y: 'top' }[key] || key] = `${rect[key]}px`;
      message = target.type === 'group' ? `Release to group ${this.title(id)} with ${this.title(target.target)}` :
        target.type === 'stack' ? `Release to dock ${this.title(id)} ${target.position === 'before' ? 'above' : 'below'} ${this.title(target.target)}` :
        `Release to ${rect.compact ? 'anchor' : 'dock'} ${this.title(id)} on the ${target.side}`;
      this.dropPreview.dataset.kind = target.type; this.dropPreview.firstElementChild.textContent = message;
    }
    const key = target ? `${target.type}:${target.side}:${target.target || ''}:${target.position || ''}` : '';
    if (this.gesture.dropKey !== key) this.announce(`${message}. Escape cancels.`);
    this.gesture.dropKey = key; this.gesture.dropTarget = target;
  }
  beginResize(event, id, edge = 'se') {
    if (event.button !== 0 || this.isLocked(id, event) || this.state.panels[id].mode !== 'float') return;
    raiseWorkspaceWindow(this.overlay);
    const rect = paletteRect(this.state.panels[id], this.bounds());
    this.startGesture(event, (dx, dy) => this.commit({ type: 'rect', id, rect: resizePaletteFromEdge(rect, dx, dy, this.bounds(), edge) }, { save: false }), { kind: 'resize', id });
  }
  beginDockResize(event, side) {
    if (event.button !== 0 || this.isLocked(null, event)) return;
    const width = this[`${side}Dock`].getBoundingClientRect().width;
    this.startGesture(event, dx => this.commit({ type: 'dock-width', side, width: width + dx * (side === 'left' ? 1 : -1) }, { save: false }));
  }
  beginStackResize(event, group) {
    if (event.button !== 0 || this.isLocked(null, event) || !group.nextId) return;
    const firstHeight = group.height, height = this.bounds().height, id = group.id, nextId = group.nextId;
    this.startGesture(event, (_, dy) => this.commit({ type: 'resize-groups', id, nextId, firstHeight: firstHeight + dy, height }, { save: false }));
  }
  startGesture(event, update, options = {}) {
    this.endGesture(true); event.preventDefault(); event.stopPropagation(); const target = this.workspace, before = this.snapshot(), x = event.clientX, y = event.clientY;
    this.launcher.open = false; for (const frame of this.frames.values()) frame.menu.open = false;
    target.setPointerCapture(event.pointerId);
    const move = current => { if (current.pointerId === event.pointerId) update(current.clientX - x, current.clientY - y, current); };
    const end = current => { if (current.pointerId === event.pointerId) { if (current.type === 'pointerup') move(current); this.endGesture(current.type !== 'pointerup'); } };
    this.gesture = { ...options, id: options.id ?? null, authorized: true, temporaryUnlock: !!(event.ctrlKey || this.ctrlHeld) && this.positionLocked(options.id ?? null),
      target, pointer: event.pointerId, x, y, before, autoId: this.autoId, floatingExpanded: [...this.floatingExpanded], focus: document.activeElement, moved: false, dropTarget: null, move, end };
    target.addEventListener('pointermove', move); target.addEventListener('pointerup', end); target.addEventListener('pointercancel', end); target.addEventListener('lostpointercapture', end);
  }
  endGesture(cancel = false) {
    const value = this.gesture; if (!value) return; this.gesture = null;
    const previousActive = this.state.active;
    value.target.removeEventListener('pointermove', value.move); value.target.removeEventListener('pointerup', value.end); value.target.removeEventListener('pointercancel', value.end); value.target.removeEventListener('lostpointercapture', value.end);
    if (value.target.hasPointerCapture(value.pointer)) value.target.releasePointerCapture(value.pointer);
    this.dropZones.hidden = true; this.dropPreview.hidden = true;
    if (cancel) { this.state = value.before; this.autoId = value.autoId; this.floatingExpanded = new Set(value.floatingExpanded); this.apply(); }
    else if (value.fromTab && !value.moved) { this.show(value.id, { focus: false }); this.focusPalette(value.id); }
    else if (value.kind === 'move' && value.moved && value.dropTarget && (value.authorized || !this.isLocked(value.id) || value.temporaryUnlock)) {
      const destination = value.dropTarget;
      const action = destination.type === 'side' ? { type: 'place', id: value.id, mode: 'dock', side: destination.side } :
        { type: destination.type, id: value.id, target: destination.target, position: destination.position };
      this.state = changePaletteLayout(this.state, action, this.bounds(), { temporaryUnlock: value.temporaryUnlock || this.ctrlHeld });
      if (this.bounds().width < 700) this.autoId = value.id;
      this.apply(); this.announce(destination.type === 'group' ? `${this.title(value.id)} grouped with ${this.title(destination.target)}.` :
        destination.type === 'stack' ? `${this.title(value.id)} docked ${destination.position === 'before' ? 'above' : 'below'} ${this.title(destination.target)}.` :
        `${this.title(value.id)} docked on the ${destination.side}.`);
    }
    // Workspace capture preserves the drag across reparenting, but it also makes
    // native click/dblclick target the workspace. Recognize grip double-clicks
    // from completed, stationary gestures without conflating a drag or tab click.
    if (!cancel && value.kind === 'move' && !value.fromTab && !value.moved) {
      const now = performance.now(), previous = this.lastGripClick;
      if (previous?.id === value.id && now - previous.time < 500 && Math.abs(previous.x - value.x) + Math.abs(previous.y - value.y) < 5) {
        this.lastGripClick = null; this.lastGripToggle = now; this.toggleFloating(value.id);
      } else this.lastGripClick = { id: value.id, time: now, x: value.x, y: value.y };
    } else this.lastGripClick = null;
    if (!this.disposed && value.authorized && this.positionLocked(value.id) && !this.ctrlHeld) this.apply();
    if (this.state.active !== previousActive) this.onActiveChange(this.state.active);
    if (!this.disposed && !cancel && value.fromTab) this.focusPalette(value.id);
    else if (!this.disposed && value.focus?.isConnected && document.activeElement !== value.focus) value.focus.focus({ preventScroll: true });
    else if (!this.disposed && !cancel && value.kind === 'move' && value.moved && !value.focus?.isConnected) this.focusPalette(value.id);
    this.persist();
  }
  dispose() {
    this.appearance?.close(); this.appearance = null;
    this.disposed = true; this.endGesture(true); clearTimeout(this.hideTimer); cancelAnimationFrame(this.frame); this.observer.disconnect(); document.removeEventListener('pointerdown', this.outside); document.removeEventListener('keydown', this.escape, true);
    document.removeEventListener('keydown', this.modifierDown, true); document.removeEventListener('keyup', this.modifierUp, true); globalThis.window?.removeEventListener('blur', this.blur);
    for (const frame of this.frames.values()) this.releaseFrame(frame);
    releaseWorkspaceWindow(this.overlay);
    for (const node of [this.leftDock, this.rightDock, this.leftRail, this.rightRail, this.overlay, this.dropZones, this.launcher, this.live]) node.remove();
    this.workspace.classList.remove('pd-workspace', 'pd-locked');
  }
}
