/** Bounded, credential-free palette workspace preferences. */
export const paletteStorageKey = 'netdxf-cad-palettes-v2';
const number = (value, fallback, low, high) => Number.isFinite(value) ? Math.max(low, Math.min(high, value)) : fallback;
const sides = new Set(['left', 'right']), modes = new Set(['dock', 'float', 'auto']);

export function paletteLayout(saved, ids, legacy = {}) {
  if (!Array.isArray(ids) || ids.length === 0 || ids.length > 32 || new Set(ids).size !== ids.length || ids.some(id => typeof id !== 'string' || !id || id.length > 80))
    throw new Error('Register 1–32 uniquely named palettes.');
  const valid = !!(saved && typeof saved === 'object' && !Array.isArray(saved) && [2, 3].includes(saved.version));
  const panels = Object.create(null);
  ids.forEach((id, index) => {
    const value = valid && Object.hasOwn(saved.panels || {}, id) ? saved.panels[id] : null;
    const source = value && typeof value === 'object' ? value : {};
    panels[id] = {
      open: valid ? source.open === true : index === 0,
      allowDocking: source.allowDocking !== false,
      mode: source.allowDocking === false ? 'float' : modes.has(source.mode) ? source.mode : 'dock', side: sides.has(source.side) ? source.side : legacy.left === true ? 'left' : 'right',
      x: number(source.x, 32 + index * 22, 0, 32000), y: number(source.y, 24 + index * 18, 0, 32000),
      width: number(source.width, number(legacy.width, 310, 220, 640), 180, 1600), height: number(source.height, 440, 120, 1600)
    };
  });
  return { version: 3, active: valid && ids.includes(saved.active) ? saved.active : ids[0],
    hidden: valid ? saved.hidden === true : legacy.hidden === true, locked: valid && saved.locked === true, panels,
    groups: normalizeGroups(valid && saved.version === 3 ? saved.groups : null, panels) };
}

function normalizeGroups(saved, panels) {
  const groups = { left: [], right: [] }, assigned = new Set();
  for (const side of sides) {
    for (const source of (Array.isArray(saved?.[side]) ? saved[side].slice(0, 32) : [])) {
      const tabs = (Array.isArray(source?.tabs) ? source.tabs.slice(0, 32) : []).filter(id => {
        if (typeof id !== 'string' || assigned.has(id) || !Object.hasOwn(panels, id) || panels[id].mode !== 'dock' || panels[id].side !== side) return false;
        assigned.add(id); return true;
      });
      if (tabs.length) groups[side].push({ tabs, active: tabs.includes(source.active) && panels[source.active].open ? source.active : tabs.find(id => panels[id].open) || tabs[0],
        weight: number(source.weight, 1, 0.01, 32000) });
    }
    for (const [id, panel] of Object.entries(panels)) if (panel.mode === 'dock' && panel.side === side && !assigned.has(id)) {
      assigned.add(id); groups[side].push({ tabs: [id], active: id, weight: 1 });
    }
  }
  return groups;
}

/** Visible ordered groups retain closed tab membership for predictable reopen/restore. */
export function paletteGroups(layout, side) {
  if (layout.hidden || !sides.has(side)) return [];
  return (layout.groups?.[side] || []).flatMap(group => {
    const tabs = group.tabs.filter(id => layout.panels[id]?.open);
    return tabs.length ? [{ ...group, key: group.tabs[0], tabs, active: tabs.includes(group.active) ? group.active : tabs[0] }] : [];
  });
}

/** Weighted heights with a readable minimum. Short docks scroll instead of hiding controls. */
export function paletteStackHeights(groups, height, minimum = 160, gap = 5) {
  const available = Math.max(groups.length * minimum, number(height, 600, 0, 32000) - Math.max(0, groups.length - 1) * gap);
  const heights = groups.map(() => 0), pending = new Set(groups.map((_, index) => index));
  let rest = available;
  while (pending.size) {
    const sum = [...pending].reduce((total, i) => total + number(groups[i].weight, 1, 0.01, 32000), 0);
    const small = [...pending].filter(i => rest * number(groups[i].weight, 1, 0.01, 32000) / sum < minimum);
    if (!small.length) { for (const i of pending) heights[i] = rest * number(groups[i].weight, 1, 0.01, 32000) / sum; break; }
    for (const i of small) { heights[i] = minimum; rest -= minimum; pending.delete(i); }
  }
  return heights;
}

export function paletteGroupFor(layout, id) {
  for (const side of sides) {
    const index = layout.groups?.[side]?.findIndex(group => group.tabs.includes(id)) ?? -1;
    if (index >= 0) return { side, index, group: layout.groups[side][index] };
  }
  return null;
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
  if (!sides.has(side) || !Object.hasOwn(layout.panels, id) || layout.locked || layout.panels[id].allowDocking === false) return null;
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
  const result = { ...layout, panels, groups: {
    left: layout.groups.left.map(group => ({ ...group, tabs: [...group.tabs] })),
    right: layout.groups.right.map(group => ({ ...group, tabs: [...group.tabs] })) } };
  const finish = () => { result.groups = normalizeGroups(result.groups, panels); return result; };
  const remove = target => {
    for (const side of sides) result.groups[side] = result.groups[side].map(group => ({ ...group, tabs: group.tabs.filter(tab => tab !== target) })).filter(group => group.tabs.length);
  };
  const activate = target => { const found = paletteGroupFor(result, target); if (found) found.group.active = target; };
  if (action.type === 'lock') { result.locked = action.value === undefined ? !layout.locked : action.value === true; return result; }
  if (action.type === 'toggle') {
    const anyOpen = Object.values(panels).some(value => value.open);
    result.hidden = anyOpen ? !layout.hidden : false;
    if (!result.hidden && !anyOpen && panel) panels[id] = { ...panel, open: true };
    return result;
  }
  if (!panel) throw new Error('Unknown drawing palette.');
  if (action.type === 'show') { panels[id] = { ...panel, open: true }; result.active = id; result.hidden = false; activate(id); return finish(); }
  if (action.type === 'close') { panels[id] = { ...panel, open: false }; return finish(); }
  if (action.type === 'activate') { result.active = id; activate(id); return finish(); }
  if (layout.locked) return layout;
  if (action.type === 'place') {
    if (action.mode !== undefined && !modes.has(action.mode) || action.side !== undefined && !sides.has(action.side)) throw new Error('Choose a supported palette position.');
    if (panel.allowDocking === false && (action.mode || panel.mode) !== 'float') return layout;
    panels[id] = { ...panel, open: true, mode: action.mode || panel.mode, side: action.side || panel.side };
    result.active = id; result.hidden = false;
    if (panels[id].mode !== panel.mode || panels[id].side !== panel.side) remove(id);
    activate(id);
  } else if (action.type === 'allow-docking') {
    const allowDocking = action.value === undefined ? panel.allowDocking === false : action.value === true;
    panels[id] = { ...panel, allowDocking };
    if (!allowDocking && panel.mode !== 'float') {
      panels[id] = { ...panels[id], mode: 'float', ...paletteRect({ ...panel, ...action.rect }, bounds) }; remove(id);
    }
  } else if (action.type === 'group') {
    const target = panels[action.target], found = paletteGroupFor(result, action.target);
    if (!target || !found || action.target === id || panel.allowDocking === false || target.allowDocking === false) throw new Error('Choose another docked palette that allows docking.');
    remove(id);
    const destination = paletteGroupFor(result, action.target).group;
    destination.tabs.push(id); destination.active = id;
    panels[id] = { ...panel, open: true, mode: 'dock', side: found.side };
    result.active = id; result.hidden = false;
  } else if (action.type === 'detach') {
    const found = paletteGroupFor(result, id);
    if (!found || found.group.tabs.length < 2) return layout;
    const weight = Math.max(0.01, found.group.weight / 2);
    found.group.tabs = found.group.tabs.filter(tab => tab !== id); found.group.weight = weight;
    result.groups[found.side].splice(found.index + 1, 0, { tabs: [id], active: id, weight });
  } else if (action.type === 'move-group' || action.type === 'move-tab') {
    const found = paletteGroupFor(result, id), delta = action.delta;
    if (!found || ![-1, 1].includes(delta)) throw new Error('Choose a neighboring palette position.');
    const items = action.type === 'move-group' ? result.groups[found.side] : found.group.tabs;
    const index = action.type === 'move-group' ? found.index : items.indexOf(id);
    let next = index + delta;
    while (next >= 0 && next < items.length && (action.type === 'move-group' ? !items[next].tabs.some(tab => panels[tab].open) : !panels[items[next]].open)) next += delta;
    if (next < 0 || next >= items.length) return layout;
    [items[index], items[next]] = [items[next], items[index]];
  } else if (action.type === 'resize-groups') {
    const found = paletteGroupFor(result, id), visible = found && paletteGroups(result, found.side), index = visible?.findIndex(group => group.key === found.group.tabs[0]);
    if (!found || index < 0 || visible[index + 1]?.key !== action.nextId || !Number.isFinite(action.firstHeight)) throw new Error('Choose adjacent visible palette groups.');
    const heights = paletteStackHeights(visible, action.height ?? bounds.height), pair = heights[index] + heights[index + 1];
    heights[index] = number(action.firstHeight, heights[index], 160, pair - 160); heights[index + 1] = pair - heights[index];
    visible.forEach((group, i) => { paletteGroupFor(result, group.key).group.weight = heights[i]; });
  } else if (action.type === 'rect') panels[id] = { ...panel, ...paletteRect({ ...panel, ...action.rect }, bounds) };
  else if (action.type === 'dock-width') {
    if (!sides.has(action.side)) throw new Error('Choose a docking side.');
    const width = number(action.width, 310, 180, Math.min(640, Math.max(180, bounds.width - 280)));
    for (const [key, value] of Object.entries(panels)) if (value.mode === 'dock' && value.side === action.side) panels[key] = { ...value, width };
  } else throw new Error('Unknown palette layout action.');
  return finish();
}
