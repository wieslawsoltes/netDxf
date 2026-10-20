import { workspaceProfileLayout } from './workspace-profiles.mjs';
import { ribbonLimits } from './ribbon-customization.mjs';

export const workspaceTransferLimit = 512000;
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const bytes = value => new TextEncoder().encode(value).length;
const nameOf = value => {
  if (typeof value !== 'string' || !value.trim() || value.trim().length > 80 || /[\u0000-\u001f\u007f]/u.test(value))
    throw new Error('Choose a workspace name containing 1–80 printable characters.');
  return value.trim();
};

/** A portable UI-only envelope. Host identities, drawing contents and account data are never copied. */
export function workspaceTransfer(entry, paletteIds) {
  if (!record(entry) || !record(entry.layout)) throw new Error('A workspace requires a name and UI layout.');
  const layout = entry.layout;
  for (const key of ['chrome', 'palettes', 'quickAccess', 'ribbonCustomization'])
    if (layout[key] !== undefined && !record(layout[key])) throw new Error(`Workspace ${key} must be an object.`);
  if (layout.palettes !== undefined && ![1, 2, 3].includes(layout.palettes.version))
    throw new Error('Unsupported palette layout version.');
  for (const key of ['quickAccess', 'ribbonCustomization'])
    if (layout[key] !== undefined && layout[key].version !== 1) throw new Error(`Unsupported ${key} version.`);
  const normalized = workspaceProfileLayout(layout, paletteIds);
  if (bytes(JSON.stringify(normalized.ribbonCustomization)) > ribbonLimits.bytes)
    throw new Error('Workspace ribbon customization exceeds 128 KiB.');
  return { format: 'shadow-studio-ui-workspace', version: 1, name: nameOf(entry.name), layout: normalized };
}

export function exportWorkspace(entry, paletteIds) {
  const text = JSON.stringify(workspaceTransfer(entry, paletteIds), null, 2);
  if (bytes(text) > workspaceTransferLimit) throw new Error('UI workspace exceeds the 500 KiB transfer limit.');
  return text;
}

export function parseWorkspace(text, paletteIds) {
  if (typeof text !== 'string' || text.length > workspaceTransferLimit || bytes(text) > workspaceTransferLimit)
    throw new Error('UI workspace exceeds the 500 KiB transfer limit.');
  let value;
  try { value = JSON.parse(text); } catch { throw new Error('UI workspace file is not valid JSON.'); }
  if (!record(value) || value.format !== 'shadow-studio-ui-workspace' || value.version !== 1)
    throw new Error('Choose a Shadow Studio UI workspace file (version 1).');
  return workspaceTransfer(value, paletteIds);
}

export function describeWorkspace(entry) {
  const layout = entry.layout, panels = Object.values(layout.palettes.panels);
  return `${panels.filter(panel => panel.open).length} open palettes · ${panels.filter(panel => panel.open && panel.mode === 'float').length} floating · ` +
    `${layout.ribbonCustomization.customTabs.length} custom ribbon tabs · ${layout.quickAccess.pinned.length} Quick Access commands · ${layout.chrome.ribbonMode} ribbon`;
}
