/** Coordinate non-modal details panels without moving focus away from the chat.
 * Escape dismisses the panel before the composer can interpret it as Stop. */
export function bindChatPopovers(root, menus) {
  const document = root.ownerDocument;
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
      if (menu.open) menus.filter(other => other !== menu).forEach(other => close(other));
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
  return Object.freeze({ close, closeAll, dispose() {
    closeAll(); toggles.forEach(remove => remove());
    document.removeEventListener('pointerdown', outside);
    document.removeEventListener('focusin', outside);
    root.removeEventListener('keydown', escape, true);
  } });
}
