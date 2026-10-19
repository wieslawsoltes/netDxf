/** Keep an overlay inside the available transcript, even in a short dock. */
export function chatPopoverRect(bounds, anchor, { above = false, width = 420, gap = 6 } = {}) {
  const inset = Math.min(6, Math.max(0, bounds.width / 4), Math.max(0, bounds.height / 4));
  const left = bounds.left + inset, right = bounds.left + bounds.width - inset;
  const top = bounds.top + inset, bottom = bounds.top + bounds.height - inset;
  const size = Math.max(1, Math.min(width, right - left));
  const x = Math.max(left, Math.min(anchor.right - size, right - size));
  const edge = above ? Math.max(top, Math.min(anchor.top - gap, bottom)) : Math.min(bottom, Math.max(anchor.bottom + gap, top));
  return { left: x, top: above ? top : edge, width: size, maxHeight: Math.max(1, above ? edge - top : bottom - edge), bottom: above ? edge : null };
}

/** Coordinate non-modal details panels without moving focus away from the chat.
 * Escape dismisses the panel before the composer can interpret it as Stop.
 * Optional geometry bounds every overlay to the transcript, never over Stop. */
export function bindChatPopovers(root, menus, { bounds, container = root } = {}) {
  const document = root.ownerDocument;
  let disposed = false;
  const position = menu => {
    if (!menu.open || !bounds || disposed) return;
    const summary = menu.querySelector('summary'), popover = menu.querySelector('.agent-chat-popover');
    if (!popover) return;
    const rect = chatPopoverRect(bounds.getBoundingClientRect(), summary.getBoundingClientRect(), { above: menu.dataset.placement === 'above' });
    const origin = container.getBoundingClientRect();
    Object.assign(popover.style, { left: `${rect.left - origin.left}px`, right: 'auto', width: `${rect.width}px`, maxHeight: `${rect.maxHeight}px`,
      top: rect.bottom === null ? `${rect.top - origin.top}px` : 'auto', bottom: rect.bottom === null ? 'auto' : `${origin.bottom - rect.bottom}px` });
  };
  const reposition = () => menus.forEach(position);
  const close = (menu, restoreFocus = false) => {
    if (!menu.open) return;
    menu.open = false;
    menu.querySelector('summary').setAttribute('aria-expanded', 'false');
    if (restoreFocus) menu.querySelector('summary').focus();
  };
  const closeAll = () => menus.forEach(menu => close(menu));
  const toggles = menus.map(menu => {
    const summary = menu.querySelector('summary');
    summary.setAttribute('aria-expanded', String(menu.open));
    const toggle = () => {
      summary.setAttribute('aria-expanded', String(menu.open));
      if (menu.open) { menus.filter(other => other !== menu).forEach(other => close(other)); position(menu); }
    };
    menu.addEventListener('toggle', toggle);
    return () => menu.removeEventListener('toggle', toggle);
  });
  const outside = event => menus.forEach(menu => { if (!menu.contains(event.target)) close(menu); });
  const escape = event => {
    const open = menus.find(menu => menu.open);
    if (event.key !== 'Escape' || !open) return;
    event.preventDefault(); event.stopPropagation(); close(open, true);
  };
  document.addEventListener('pointerdown', outside);
  document.addEventListener('focusin', outside);
  root.addEventListener('keydown', escape, true);
  const observer = bounds && typeof ResizeObserver !== 'undefined' ? new ResizeObserver(reposition) : null;
  if (observer) { observer.observe(bounds); observer.observe(root); }
  document.defaultView?.addEventListener('resize', reposition);
  return Object.freeze({ close, closeAll, dispose() {
    disposed = true; closeAll(); toggles.forEach(remove => remove()); observer?.disconnect();
    document.defaultView?.removeEventListener('resize', reposition);
    document.removeEventListener('pointerdown', outside);
    document.removeEventListener('focusin', outside);
    root.removeEventListener('keydown', escape, true);
  } });
}
