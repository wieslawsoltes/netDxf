/** Presentation preferences are independent of drawing state and drafting aids. */
export const shellStorageKey = 'netdxf-cad-shell-v1';
export const draftingIndicators = [
  { key:'snap', label:'SNAP', name:'Snap mode', shortcut:'F9' },
  { key:'grid', label:'GRID', name:'Drawing grid', shortcut:'F7' },
  { key:'ortho', label:'ORTHO', name:'Ortho mode', shortcut:'F8' },
  { key:'polar', label:'POLAR', name:'Polar tracking', shortcut:'F10' },
  { key:'osnap', label:'OSNAP', name:'Object snap', shortcut:'F3' },
  { key:'tracking', label:'OTRACK', name:'Object snap tracking', shortcut:'F11' },
  { key:'dynamic', label:'DYN', name:'Dynamic input', shortcut:'F12' },
  { key:'selectionCycling', label:'CYCLE', name:'Selection cycling', shortcut:'Shift+Space' }
];
const record = value => value && typeof value === 'object' && !Array.isArray(value);
const finite = (value, fallback) => Number.isFinite(value) ? value : fallback;
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));

export function commandWindowPreferences(value) {
  const source = record(value) ? value : {};
  return { mode:source.mode === 'float' ? 'float' : 'dock',
    x:clamp(finite(source.x, 80), 0, 20000), y:clamp(finite(source.y, 220), 0, 20000),
    width:clamp(finite(source.width, 640), 280, 2400), height:clamp(finite(source.height, 150), 68, 900) };
}

/** Fit to the actual host; even a restored window from a larger monitor stays reachable. */
export function commandWindowRect(value, bounds) {
  const source = commandWindowPreferences(value);
  const availableWidth = Math.max(1, finite(bounds?.width, 640) - 8);
  const availableHeight = Math.max(1, finite(bounds?.height, 480) - 8);
  const width = Math.min(availableWidth, source.width), height = Math.min(availableHeight, source.height);
  return { x:clamp(source.x, 4, Math.max(4, availableWidth - width + 4)),
    y:clamp(source.y, 4, Math.max(4, availableHeight - height + 4)), width, height };
}

export function shellPreferences(value) {
  const source = record(value) && (value.version === undefined || value.version === 1) ? value : {};
  const ids = new Set(draftingIndicators.map(entry => entry.key));
  return { version:1,
    indicators:Array.isArray(source.indicators) ? [...new Set(source.indicators.filter(id => ids.has(id)))] : [...ids],
    statusLabels:source.statusLabels === true, coordinates:source.coordinates !== false,
    workspaceName:typeof source.workspaceName === 'string' && source.workspaceName.trim() && !/[\u0000-\u001f\u007f]/u.test(source.workspaceName)
      ? source.workspaceName.trim().slice(0, 80) : 'Drafting',
    workspacePreset:source.workspacePreset !== false,
    workspaceDetached:source.workspaceDetached === true,
    commandWindow:commandWindowPreferences(source.commandWindow) };
}

export function adjacentTab(ids, active, key) {
  if (!ids.length) return null;
  if (key === 'Home') return ids[0];
  if (key === 'End') return ids.at(-1);
  const step = ['ArrowRight', 'PageDown'].includes(key) ? 1 : ['ArrowLeft', 'PageUp'].includes(key) ? -1 : 0;
  if (!step) return null;
  const index = ids.indexOf(active);
  return ids[(Math.max(0, index) + step + ids.length) % ids.length];
}
