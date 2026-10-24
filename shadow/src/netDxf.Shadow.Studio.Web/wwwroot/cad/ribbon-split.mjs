import { bindRovingToolbar, nativeRibbonInput } from './ribbon-keyboard.mjs';

let serial = 0;

/** A split tool owns the real command controls, including their current disabled state. */
export function splitTool(primary, alternatives, label) {
  const root = document.createElement('div'); root.className = 'ribbon-split';
  root.setAttribute('role', 'group'); root.setAttribute('aria-label', label);
  const toggle = document.createElement('button'); toggle.type = 'button'; toggle.className = 'ribbon-split-toggle';
  toggle.setAttribute('data-ribbon-split-toggle', ''); toggle.setAttribute('aria-label', `${label} options`);
  toggle.setAttribute('aria-haspopup', 'menu'); toggle.setAttribute('aria-expanded', 'false');
  toggle.setAttribute('aria-keyshortcuts', 'ArrowDown'); toggle.title = `${label} options`;
  const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  arrow.setAttribute('viewBox', '0 0 10 10'); arrow.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(arrow.namespaceURI, 'path'); path.setAttribute('d', 'M2 3.5L5 6.5L8 3.5'); arrow.append(path); toggle.append(arrow);
  const menu = document.createElement('div'); menu.className = 'ribbon-split-menu'; menu.hidden = true;
  menu.setAttribute('id', `ribbon-split-${++serial}`); menu.setAttribute('role', 'menu'); menu.setAttribute('aria-label', `${label} options`);
  toggle.setAttribute('aria-controls', menu.getAttribute('id'));
  for (const command of alternatives) { command.setAttribute('role', 'menuitem'); menu.append(command); }
  root.append(primary, toggle, menu); return root;
}

export function splitMenuPosition(anchor, size, viewport) {
  const bottom = anchor.bottom + 3;
  return { left: Math.max(4, Math.min(anchor.left, viewport.width - size.width - 4)),
    top: Math.max(4, Math.min(bottom + size.height <= viewport.height - 4 ? bottom : anchor.top - size.height - 3, viewport.height - size.height - 4)) };
}

/** One delegated lifetime per ribbon, including panels moved into floating windows. */
export class RibbonSplitMenus {
  constructor({ onChoose = () => {}, changed = () => {} } = {}) {
    this.groups = new Set(); this.popup = null; this.onChoose = onChoose; this.changed = changed;
    this.click = event => {
      const toggle = event.target.closest?.('[data-ribbon-split-toggle]');
      const command = event.target.closest?.('button');
      if (toggle && this.owns(toggle)) {
        event.preventDefault(); event.stopPropagation();
        if (this.popup?.toggle === toggle) this.close(true); else this.open(toggle);
      } else if (this.popup?.menu.contains(event.target) && ['data-command','data-action','data-ribbon-command-id'].some(name=>command?.getAttribute(name)!==null&&command?.getAttribute(name)!==undefined)) {
        // Restore the very same command before the host's delegated routing runs.
        this.close(); this.onChoose();
      }
    };
    this.keydown = event => {
      if (event.defaultPrevented || event.isComposing || event.ctrlKey || event.metaKey || nativeRibbonInput(event.target)) return;
      if (event.key === 'Escape' && this.popup) { event.preventDefault(); event.stopPropagation(); this.close(true); return; }
      const toggle = event.target.closest?.('[data-ribbon-split-toggle]');
      if (toggle && this.owns(toggle) && ['ArrowDown', 'ArrowUp'].includes(event.key) && !event.shiftKey && !event.altKey) {
        event.preventDefault(); event.stopPropagation(); this.open(toggle, event.key === 'ArrowUp');
      }
    };
    this.outside = event => { if (this.popup && !this.contains(event.target) && !this.popup.toggle.contains(event.target)) this.close(); };
    this.resize = () => this.close();
    this.scroll = event => { if (this.popup && !this.popup.menu.contains(event.target)) this.close(); };
    document.addEventListener('click', this.click); document.addEventListener('keydown', this.keydown, true);
    document.addEventListener('pointerdown', this.outside); document.addEventListener('focusin', this.outside);
    document.addEventListener('scroll', this.scroll, true); window.addEventListener('resize', this.resize);
  }
  owns(node) { return this.groups.has(node.closest('.ribbon-group')); }
  contains(node) { return !!this.popup?.menu.contains(node); }
  set(groups) { this.close(); this.groups = new Set(groups); }
  open(toggle, last = false) {
    if (this.popup?.toggle === toggle) { this.popup.keyboard.focus(last); return; }
    this.close();
    const root = toggle.closest('.ribbon-split'), menu = root?.querySelector('.ribbon-split-menu');
    if (!menu || toggle.disabled || root.classList.contains('ribbon-split-inline')) return;
    const marker = document.createComment('split-tool-menu'); menu.before(marker); document.body.append(menu);
    menu.hidden = false; menu.classList.add('ribbon-command-menu'); toggle.setAttribute('aria-expanded', 'true');
    this.popup = { root, menu, marker, toggle };
    const position = splitMenuPosition(root.getBoundingClientRect(), menu.getBoundingClientRect(), { width: innerWidth, height: innerHeight });
    menu.style.left = `${position.left}px`; menu.style.top = `${position.top}px`;
    this.popup.keyboard = bindRovingToolbar(menu, { orientation: 'vertical', onEscape: () => this.close(true), onTab: () => this.close(true) });
    this.changed(); this.popup.keyboard.focus(last);
  }
  close(focus = false) {
    const popup = this.popup; if (!popup) return; this.popup = null;
    popup.keyboard?.dispose(); popup.menu.hidden = true; popup.menu.classList.remove('ribbon-command-menu');
    popup.menu.style.removeProperty('left'); popup.menu.style.removeProperty('top'); popup.marker.replaceWith(popup.menu);
    popup.toggle.setAttribute('aria-expanded', 'false'); this.changed();
    if (focus && popup.toggle.isConnected) popup.toggle.focus({ preventScroll: true });
  }
  /** Compact panel flyouts present every command without a second nested popup. */
  expose(tools, value) {
    this.close();
    for (const root of tools.querySelectorAll('.ribbon-split')) {
      root.classList.toggle('ribbon-split-inline', value);
      const toggle = root.querySelector('[data-ribbon-split-toggle]'), menu = root.querySelector('.ribbon-split-menu');
      toggle.hidden = value; menu.hidden = !value; menu.setAttribute('role', value ? 'group' : 'menu');
      for (const command of menu.querySelectorAll('button')) {
        if (value) command.removeAttribute('role'); else command.setAttribute('role', 'menuitem');
      }
    }
  }
  dispose() {
    this.close(); this.groups.clear();
    document.removeEventListener('click', this.click); document.removeEventListener('keydown', this.keydown, true);
    document.removeEventListener('pointerdown', this.outside); document.removeEventListener('focusin', this.outside);
    document.removeEventListener('scroll', this.scroll, true); window.removeEventListener('resize', this.resize);
  }
}
