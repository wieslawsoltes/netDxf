import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PaletteDock, handlePaletteEscape } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-docking.mjs';

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
