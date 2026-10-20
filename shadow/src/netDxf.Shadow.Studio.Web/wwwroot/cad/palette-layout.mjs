/** Bounded, credential-free palette workspace preferences. */
export const paletteStorageKey = 'netdxf-cad-palettes-v2';
const number = (value, fallback, low, high) => Number.isFinite(value) ? Math.max(low, Math.min(high, value)) : fallback;
const sides = new Set(['left', 'right']), modes = new Set(['dock', 'float', 'auto']);

export function paletteLayout(saved, ids, legacy = {}) {
  if (!Array.isArray(ids) || ids.length === 0 || ids.length > 32 || new Set(ids).size !== ids.length || ids.some(id => typeof id !== 'string' || !id || id.length > 80))
    throw new Error('Register 1–32 uniquely named palettes.');
  const valid = !!(saved && typeof saved === 'object' && !Array.isArray(saved) && saved.version === 2);
  const panels = Object.create(null);
  ids.forEach((id, index) => {
    const value = valid && Object.hasOwn(saved.panels || {}, id) ? saved.panels[id] : null;
    const source = value && typeof value === 'object' ? value : {};
    panels[id] = {
      open: valid ? source.open === true : index === 0,
      mode: modes.has(source.mode) ? source.mode : 'dock', side: sides.has(source.side) ? source.side : legacy.left === true ? 'left' : 'right',
      x: number(source.x, 32 + index * 22, 0, 32000), y: number(source.y, 24 + index * 18, 0, 32000),
      width: number(source.width, number(legacy.width, 310, 220, 640), 180, 1600), height: number(source.height, 440, 120, 1600)
    };
  });
  return { version: 2, active: valid && ids.includes(saved.active) ? saved.active : ids[0],
    hidden: valid ? saved.hidden === true : legacy.hidden === true, locked: valid && saved.locked === true, panels };
}

/** Keep the complete palette, including its controls, inside the current workspace. */
export function paletteRect(rect, bounds) {
  const availableWidth = Math.max(1, number(bounds.width, 1024, 0, 32000)), availableHeight = Math.max(1, number(bounds.height, 768, 0, 32000));
  const width = number(rect.width, 310, Math.min(220, availableWidth), availableWidth);
  const height = number(rect.height, 440, Math.min(160, availableHeight), availableHeight);
  return { x: number(rect.x, 0, 0, availableWidth - width), y: number(rect.y, 0, 0, availableHeight - height), width, height };
}

export function resizePaletteRect(rect, dx, dy, bounds) {
  bounds = { width: Math.max(1, number(bounds.width, 1024, 0, 32000)), height: Math.max(1, number(bounds.height, 768, 0, 32000)) };
  const current = paletteRect(rect, bounds);
  return { ...current,
    width: number(current.width + dx, current.width, Math.min(220, bounds.width - current.x), bounds.width - current.x),
    height: number(current.height + dy, current.height, Math.min(160, bounds.height - current.y), bounds.height - current.y) };
}

/** Side drop zones use workspace-local pointer coordinates and never extend into another dock such as chat. */
export function paletteDropSide(point, bounds, zoneSize = 40) {
  if (![point.x, point.y, bounds.width, bounds.height].every(Number.isFinite) || bounds.width <= 0 || bounds.height <= 0 ||
      point.x < 0 || point.y < 0 || point.x > bounds.width || point.y > bounds.height) return null;
  const width = Math.min(number(zoneSize, 40, 16, 80), bounds.width / 2);
  if (point.x <= width) return 'left';
  if (point.x >= bounds.width - width) return 'right';
  return null;
}

/** Preview the destination column, or its compact anchored overlay, without changing layout state. */
export function paletteDropPreview(layout, id, side, bounds, railWidth = 28) {
  if (!sides.has(side) || !Object.hasOwn(layout.panels, id) || layout.locked) return null;
  railWidth = number(railWidth, 28, 0, Math.min(64, Math.max(0, bounds.width / 2)));
  const panel = layout.panels[id], compact = bounds.width < 700;
  const panels = Object.entries(layout.panels).filter(([key, value]) => key !== id && value.open && !layout.hidden).map(([, value]) => value);
  panels.push({ ...panel, mode: 'dock', side });
  const rail = edge => panels.some(value => value.side === edge && (value.mode === 'auto' || compact && value.mode === 'dock')) ? railWidth : 0;
  const width = edge => Math.max(0, ...panels.filter(value => value.mode === 'dock' && value.side === edge).map(value => value.width));
  const docks = paletteDockWidths(width('left'), width('right'), bounds.width, rail('left') + rail('right'));
  const available = Math.max(1, bounds.width - docks.left - docks.right - rail('left') - rail('right'));
  const size = compact ? Math.min(panel.width, available) : docks[side];
  const rect = paletteRect({ x: 0, y: 0, width: size, height: bounds.height }, { width: compact ? available : size, height: bounds.height });
  rect.x = side === 'left' ? rail('left') : bounds.width - rail('right') - rect.width;
  return { ...paletteRect(rect, bounds), side, compact };
}

/** Dock columns consume space while keeping a usable drawing area. Small workspaces use anchored overlays. */
export function paletteDockWidths(left, right, availableWidth, railWidth = 0) {
  left = number(left, 0, 0, 1600); right = number(right, 0, 0, 1600); railWidth = number(railWidth, 0, 0, 128);
  const width = number(availableWidth, 1024, 0, 32000), reserve = 280;
  if (width < 700) return { left: 0, right: 0, overlay: true };
  const budget = Math.max(0, width - reserve - railWidth), total = left + right;
  if (!total || total <= budget) return { left, right, overlay: false };
  return { left: Math.floor(budget * left / total), right: Math.floor(budget * right / total), overlay: false };
}

export function changePaletteLayout(layout, action, bounds = { width: 1024, height: 768 }) {
  const panels = { ...layout.panels }, id = action.id || layout.active, panel = Object.hasOwn(panels, id) ? panels[id] : null;
  const result = { ...layout, panels };
  if (action.type === 'lock') { result.locked = action.value === undefined ? !layout.locked : action.value === true; return result; }
  if (action.type === 'toggle') {
    const anyOpen = Object.values(panels).some(value => value.open);
    result.hidden = anyOpen ? !layout.hidden : false;
    if (!result.hidden && !anyOpen && panel) panels[id] = { ...panel, open: true };
    return result;
  }
  if (!panel) throw new Error('Unknown drawing palette.');
  if (action.type === 'show') { panels[id] = { ...panel, open: true }; result.active = id; result.hidden = false; return result; }
  if (action.type === 'close') { panels[id] = { ...panel, open: false }; return result; }
  if (action.type === 'activate') { result.active = id; return result; }
  if (layout.locked) return layout;
  if (action.type === 'place') {
    if (action.mode !== undefined && !modes.has(action.mode) || action.side !== undefined && !sides.has(action.side)) throw new Error('Choose a supported palette position.');
    panels[id] = { ...panel, open: true, mode: action.mode || panel.mode, side: action.side || panel.side };
    result.active = id; result.hidden = false;
  } else if (action.type === 'rect') panels[id] = { ...panel, ...paletteRect({ ...panel, ...action.rect }, bounds) };
  else if (action.type === 'dock-width') {
    if (!sides.has(action.side)) throw new Error('Choose a docking side.');
    const width = number(action.width, 310, 180, Math.min(640, Math.max(180, bounds.width - 280)));
    for (const [key, value] of Object.entries(panels)) if (value.mode === 'dock' && value.side === action.side) panels[key] = { ...value, width };
  } else throw new Error('Unknown palette layout action.');
  return result;
}
