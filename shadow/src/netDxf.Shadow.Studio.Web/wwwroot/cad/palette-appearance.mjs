/** Palette opacity is UI state, independent of drawing/entity transparency. */
export function paletteAppearance(value = {}) {
  value = value && typeof value === 'object' ? value : {};
  const percent = (number, fallback) => Number.isFinite(number) ? Math.round(Math.max(0, Math.min(100, number))) : fallback;
  const opacity = percent(value.opacity, 100);
  return { opacity, rolloverOpacity: Math.max(opacity, percent(value.rolloverOpacity, 100)) };
}

export function paletteOpacity(value, { floating = false, disabled = false, hovered = false, focused = false, menuOpen = false } = {}) {
  if (!floating || disabled || focused || menuOpen) return 1;
  const appearance = paletteAppearance(value);
  return (hovered ? appearance.rolloverOpacity : appearance.opacity) / 100;
}

let sequence = 0;
/** Standalone reusable modal. Draft changes and previews never mutate host preferences. */
export function openPaletteAppearance({ title, appearance, disabled = false, onApply, host = document.body, origin = document.activeElement }) {
  if (document.querySelector('dialog[open]')) return null;
  const node = (tag, attributes = {}, children = []) => {
    const value = document.createElement(tag);
    for (const [key, text] of Object.entries(attributes)) {
      if (key === 'text') value.textContent = text;
      else value.setAttribute(key, String(text));
    }
    value.append(...children); return value;
  };
  const id = `palette-appearance-${++sequence}`, saved = paletteAppearance(appearance);
  const normal = node('input', { type: 'range', min: 0, max: 100, step: 1, value: saved.opacity, 'aria-label': 'Palette opacity' });
  const rollover = node('input', { type: 'range', min: 0, max: 100, step: 1, value: saved.rolloverOpacity, 'aria-label': 'Rollover opacity' });
  const normalText = node('output'), rolloverText = node('output');
  const global = node('input', { type: 'checkbox', 'aria-label': 'Disable all palette transparency' }); global.checked = disabled;
  const all = node('input', { type: 'checkbox', 'aria-label': 'Apply transparency to all palettes' });
  const preview = node('div', { class: 'pd-opacity-sample', text: `${title} · Properties and tools` });
  const status = node('p', { class: 'pd-opacity-status', role: 'status' });
  const button = (text, action) => { const result = node('button', { type: 'button', text }); result.addEventListener('click', action); return result; };
  const row = (text, input, output) => node('label', { class: 'pd-opacity-row' }, [node('span', { text }), input, ...(output ? [output] : [])]);
  let closed = false, previewRollover = false;
  const close = () => {
    if (closed) return; closed = true; dialog.close(); dialog.remove();
    if (origin?.isConnected) origin.focus({ preventScroll: true });
  };
  function refresh() {
    const normalValue = Number(normal.value), rolloverValue = Math.max(normalValue, Number(rollover.value));
    rollover.value = String(rolloverValue); rollover.min = String(normalValue);
    normalText.textContent = `${normalValue}%`; rolloverText.textContent = `${rolloverValue}%`;
    normal.disabled = rollover.disabled = global.checked;
    preview.style.opacity = String(global.checked ? 1 : (previewRollover ? rolloverValue : normalValue) / 100);
    status.textContent = global.checked ? 'All palettes will be opaque.' : `${previewRollover ? 'Rollover' : 'Normal'} preview. Docked palettes and keyboard-focused controls remain opaque.`;
  }
  const hold = button('Hold to preview rollover', () => {});
  const start = () => { previewRollover = true; refresh(); }, stop = () => { previewRollover = false; refresh(); };
  hold.addEventListener('pointerdown', event => { if (event.button === 0) { hold.setPointerCapture(event.pointerId); start(); } });
  for (const event of ['pointerup', 'pointercancel', 'lostpointercapture', 'blur']) hold.addEventListener(event, stop);
  hold.addEventListener('keydown', event => { if ([' ', 'Enter'].includes(event.key)) { event.preventDefault(); start(); } });
  hold.addEventListener('keyup', event => { if ([' ', 'Enter'].includes(event.key)) { event.preventDefault(); stop(); } });
  const dialog = node('dialog', { id, class: 'pd-appearance-dialog', 'aria-labelledby': `${id}-title` }, [
    node('header', {}, [node('h2', { id: `${id}-title`, text: `${title} transparency` }), button('Close', close)]),
    node('p', { text: 'Opacity: 0% is clear; 100% is solid. Rollover cannot be more transparent than the resting palette.' }),
    row('Palette opacity', normal, normalText), row('Rollover opacity', rollover, rolloverText),
    node('div', { class: 'pd-opacity-preview', 'aria-hidden': 'true' }, [preview]), hold, status,
    row('Apply these settings to all palettes', all), row('Disable all palette transparency', global),
    node('footer', {}, [button('Cancel', close), button('Apply transparency', () => {
      try { onApply({ ...paletteAppearance({ opacity: Number(normal.value), rolloverOpacity: Number(rollover.value) }), all: all.checked, disabled: global.checked }); close(); }
      catch (error) { status.textContent = error.message || String(error); }
    })])
  ]);
  for (const input of [normal, rollover, global]) input.addEventListener('input', refresh);
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  dialog.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(); } });
  refresh(); host.append(dialog); dialog.showModal(); normal.focus();
  return { dialog, close };
}
