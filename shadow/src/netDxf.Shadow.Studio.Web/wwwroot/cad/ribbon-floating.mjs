import { floatingRibbonPosition, protectedRibbonPanel, ribbonPanelKey } from './ribbon-customization.mjs';

const button = (text, label) => {
  const node = document.createElement('button'); node.type = 'button'; node.textContent = text;
  node.setAttribute('aria-label', label); node.title = label; return node;
};
let floatingSerial = 0;
/** Floating windows retain the actual panel controls, never copies of their event handlers. */
export class RibbonFloatingPanels {
  constructor({ panel, customization, changed, report = () => {} }) {
    this.panel = panel; this.customization = customization; this.changed = changed; this.report = report;
    this.frames = new Map(); this.context = {}; this.groups = []; this.gesture = null;
    this.host = panel.closest('#studio') || document.body;
    this.move = event => this.moveGesture(event);
    this.up = event => { if (this.gesture?.pointerId === event.pointerId) this.endGesture(false, event); };
    this.cancel = event => { if (this.gesture?.pointerId === event.pointerId) this.endGesture(true); };
    this.escape = event => { if (event.key === 'Escape' && this.gesture) { event.preventDefault(); event.stopPropagation(); this.endGesture(true); } };
    this.resize = () => { this.endGesture(true); for (const frame of this.frames.values()) this.position(frame, frame.position); };
    document.addEventListener('pointermove', this.move); document.addEventListener('pointerup', this.up);
    document.addEventListener('pointercancel', this.cancel); document.addEventListener('keydown', this.escape, true);
    panel.addEventListener('lostpointercapture', this.cancel); window.addEventListener('resize', this.resize);
  }
  containsFocus() { return [...this.frames.values()].some(frame => frame.element.contains(document.activeElement)); }
  isFloating(tab, panel) { return this.frames.has(ribbonPanelKey(tab, panel)); }
  reconcile(groups, context) {
    if (this.gesture) this.endGesture(true, null, false);
    this.groups = groups; this.context = context; this.focusLost = false;
    const prefs = this.customization.snapshot(), keep = new Set(), attached = new Set(), available = context.entries || [];
    const byTab = new Map([[context.tab, groups]]);
    for (const spec of prefs.floating) {
      if (!available.some(entry => entry.key === spec.tab && !entry.disabled && entry.visible !== false)) continue;
      if (!byTab.has(spec.tab)) byTab.set(spec.tab, context.resolveGroups?.(spec.tab) || []);
      const group = byTab.get(spec.tab).find(node => (node.dataset.ribbonPanel || node.dataset.panel) === spec.panel);
      if (!group || protectedRibbonPanel(spec.tab, spec.panel)) continue;
      const id = ribbonPanelKey(spec.tab, spec.panel); keep.add(id);
      this.attach(group, spec); if (spec.tab === context.tab) attached.add(group);
    }
    for (const [id, frame] of this.frames) if (!keep.has(id)) {
      this.focusLost ||= frame.element.contains(document.activeElement); frame.element.remove(); this.frames.delete(id);
    }
    return groups.filter(group => !attached.has(group));
  }
  attach(group, spec) {
    const id = ribbonPanelKey(spec.tab, spec.panel); let frame = this.frames.get(id);
    if (!frame) {
      const element = document.createElement('section'); element.className = 'ribbon-floating';
      element.setAttribute('role', 'region'); element.setAttribute('aria-label', `${group.dataset.panel} floating ribbon panel`);
      element.dataset.ribbonSource = id;
      const header = document.createElement('header'); header.className = 'ribbon-floating-header';
      const grip = button(group.dataset.panel, `Move floating ${group.dataset.panel} panel`); grip.className = 'ribbon-floating-grip';
      const back = button('↩', `Return ${group.dataset.panel} panel to ribbon`);
      header.append(grip, back); const content = document.createElement('div'); content.className = 'ribbon-floating-content';
      element.append(header, content); this.host.append(element);
      frame = { element, content, grip, back, spec, group, position: spec, prefix: `floating-ribbon-${++floatingSerial}`, scoped: new WeakSet() }; this.frames.set(id, frame);
      grip.addEventListener('pointerdown', event => this.beginGesture(event, frame.group, frame.spec));
      grip.addEventListener('keydown', event => {
        if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || event.ctrlKey || event.metaKey || event.altKey) return;
        event.preventDefault(); event.stopPropagation(); const step = event.shiftKey ? 1 : 10;
        const next = { ...frame.position, x: frame.position.x + (event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0),
          y: frame.position.y + (event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0) };
        this.position(frame, next); this.persist(frame.spec, frame.position); this.frames.get(id)?.grip.focus({ preventScroll: true });
      });
      back.addEventListener('click', event => { event.preventDefault(); event.stopPropagation(); this.returnPanel(frame.spec); });
    }
    frame.group = group; frame.spec = spec;
    frame.element.setAttribute('aria-label', `${group.dataset.panel} floating ribbon panel`);
    frame.grip.textContent = group.dataset.panel; frame.grip.setAttribute('aria-label', `Move floating ${group.dataset.panel} panel`);
    frame.back.setAttribute('aria-label', `Return ${group.dataset.panel} panel to ribbon`);
    // Live controls from different source tabs may share local IDs (for example
    // the current-layer selector). Keep label/ARIA relationships unique when both
    // tabs' controls are present at once.
    if (!frame.scoped.has(group)) {
      const ids = new Map([...group.querySelectorAll('[id]')].map(node => [node.getAttribute('id'), node]));
      for (const [original, node] of ids) node.setAttribute('id', `${frame.prefix}-${original}`);
      for (const node of group.querySelectorAll('[for],[aria-controls],[aria-labelledby],[aria-describedby]'))
        for (const attribute of ['for', 'aria-controls', 'aria-labelledby', 'aria-describedby']) {
          const value = node.getAttribute(attribute); if (value) node.setAttribute(attribute, value.split(/\s+/).map(id => ids.has(id) ? `${frame.prefix}-${id}` : id).join(' '));
        }
      frame.scoped.add(group);
    }
    group.classList.remove('collapsed');
    if (group.parentNode !== frame.content) {
      const focused = frame.content.contains(document.activeElement);
      frame.content.replaceChildren(group); if (focused) frame.grip.focus({ preventScroll: true });
    }
    this.position(frame, spec); return frame;
  }
  position(frame, value) {
    frame.position = floatingRibbonPosition(value, frame.element.getBoundingClientRect(), { width: innerWidth, height: innerHeight });
    frame.element.style.left = `${frame.position.x}px`; frame.element.style.top = `${frame.position.y}px`;
  }
  persist(spec, position) {
    try { this.customization.change({ type: 'float', tab: spec.tab, panel: spec.panel, ...position }); this.changed(); }
    catch (error) { this.report(error); this.changed(); }
  }
  floatPanel(group, tab) {
    if (protectedRibbonPanel(tab, group.dataset.ribbonPanel || group.dataset.panel)) return;
    const bounds = group.getBoundingClientRect(), spec = { tab, panel: group.dataset.ribbonPanel || group.dataset.panel };
    const frame = this.attach(group, { ...spec, x: bounds.left, y: Math.min(bounds.bottom + 8, innerHeight - 100) });
    this.persist(spec, frame.position); this.frames.get(ribbonPanelKey(tab, spec.panel))?.grip.focus({ preventScroll: true });
  }
  returnPanel(spec) {
    this.endGesture(true);
    try { this.customization.change({ type: 'return', tab: spec.tab, panel: spec.panel }); this.changed(); }
    catch (error) { this.report(error); return; }
    const group = this.groups.find(node => (node.dataset.ribbonPanel || node.dataset.panel) === spec.panel && this.context.tab === spec.tab);
    (group?.classList.contains('collapsed') ? group.querySelector('.ribbon-launcher') :
      group?.querySelector('.ribbon-tools button:not(:disabled),.ribbon-tools select:not(:disabled)') ||
      document.querySelector('#ribbon-tabs [aria-selected=true]'))?.focus({ preventScroll: true });
  }
  beginGesture(event, group, spec) {
    if (event.button !== 0 || protectedRibbonPanel(spec.tab, spec.panel) || this.gesture) return;
    event.preventDefault(); event.stopPropagation();
    const id = ribbonPanelKey(spec.tab, spec.panel), existing = this.frames.get(id);
    const bounds = (existing?.element || group).getBoundingClientRect();
    this.gesture = { pointerId: event.pointerId, group, spec, id, existing: !!existing, original: { x: bounds.left, y: bounds.top },
      start: { x: event.clientX, y: event.clientY }, focus: document.activeElement, moved: false };
    this.panel.setPointerCapture(event.pointerId);
  }
  moveGesture(event) {
    const gesture = this.gesture; if (!gesture || gesture.pointerId !== event.pointerId) return;
    const dx = event.clientX - gesture.start.x, dy = event.clientY - gesture.start.y;
    if (!gesture.moved && Math.hypot(dx, dy) < 4) return;
    const ribbon = this.panel.getBoundingClientRect();
    if (!gesture.existing && !gesture.moved && event.clientX >= ribbon.left && event.clientX <= ribbon.right && event.clientY >= ribbon.top && event.clientY <= ribbon.bottom) return;
    gesture.moved = true;
    const frame = this.frames.get(gesture.id) || this.attach(gesture.group, { ...gesture.spec, ...gesture.original });
    this.position(frame, { x: gesture.original.x + dx, y: gesture.original.y + dy });
  }
  endGesture(cancelled, event = null, redraw = true) {
    const gesture = this.gesture; if (!gesture) return;
    if (!cancelled && event) this.moveGesture(event);
    this.gesture = null;
    if (this.panel.hasPointerCapture(gesture.pointerId)) this.panel.releasePointerCapture(gesture.pointerId);
    const frame = this.frames.get(gesture.id);
    if (cancelled) {
      if (gesture.existing && frame) this.position(frame, gesture.original);
      else if (frame) { frame.element.remove(); this.frames.delete(gesture.id); }
      if (redraw) this.changed();
    } else if (gesture.moved && frame) this.persist(gesture.spec, frame.position);
    if (gesture.focus?.isConnected) gesture.focus.focus({ preventScroll: true });
  }
  dispose() {
    this.endGesture(true, null, false); for (const frame of this.frames.values()) frame.element.remove(); this.frames.clear();
    document.removeEventListener('pointermove', this.move); document.removeEventListener('pointerup', this.up);
    document.removeEventListener('pointercancel', this.cancel); document.removeEventListener('keydown', this.escape, true);
    this.panel.removeEventListener('lostpointercapture', this.cancel); window.removeEventListener('resize', this.resize);
  }
}
