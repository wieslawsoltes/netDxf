const object = value => value && typeof value === 'object' && !Array.isArray(value);
const number = (value, fallback, min, max) => Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : fallback;
export const ribbonPlacements = ['top', 'left', 'right', 'float'];

/** Portable geometry; native windows and executable customization are deliberately absent. */
export function ribbonHostPreferences(value = {}) {
  const source = object(value) ? value : {};
  const size = (entry, width, height) => ({ width: number(entry?.width, width, 200, 2400), height: number(entry?.height, height, 120, 1800) });
  return { placement: ribbonPlacements.includes(source.placement) ? source.placement : 'top',
    orientation: source.orientation === 'vertical' ? 'vertical' : 'horizontal',
    previousDock: ['top','left','right'].includes(source.previousDock) ? source.previousDock : 'top',
    allowDocking: source.allowDocking !== false, x: number(source.x, 40, 0, 100000), y: number(source.y, 90, 0, 100000),
    dockWidth: number(source.dockWidth, 340, 240, 520), horizontal: size(source.horizontal, 1000, 180), vertical: size(source.vertical, 340, 520) };
}
export function ribbonHostRect(value, viewport) {
  const state = ribbonHostPreferences(value), size = state[state.orientation];
  const width = Math.min(size.width, Math.max(160, viewport.width - 8)), height = Math.min(size.height, Math.max(100, viewport.height - 8));
  return { x: Math.max(4, Math.min(state.x, viewport.width - width - 4)), y: Math.max(4, Math.min(state.y, viewport.height - height - 4)), width, height };
}
export function ribbonDockTarget(point, bounds, { allowDocking = true, ctrlKey = false } = {}) {
  if (!allowDocking || ctrlKey || point.x < bounds.left - 12 || point.x > bounds.right + 12 || point.y < bounds.top - 12 || point.y > bounds.bottom + 12) return null;
  if (point.y <= bounds.top + 32) return 'top';
  if (bounds.width >= 720 && point.x <= bounds.left + 32) return 'left';
  if (bounds.width >= 720 && point.x >= bounds.right - 32) return 'right';
  return null;
}
export function ribbonLock(mask, floating, event) { return !!((Number(mask) & 15) & (floating ? 4 : 1)) && !event?.ctrlKey; }
