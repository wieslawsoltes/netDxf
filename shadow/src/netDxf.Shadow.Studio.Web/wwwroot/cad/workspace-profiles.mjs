import { chromePreferences } from './ribbon-layout.mjs';
import { paletteLayout } from './palette-layout.mjs';

export const workspaceProfileKey = 'netdxf-cad-workspaces-v1';
export const workspaceProfileLimit = 24;
const record = value => value && typeof value === 'object' && !Array.isArray(value);
const nameOf = value => typeof value === 'string' ? value.trim().slice(0, 80) : '';

/** UI state only: never copies drawing identities, agent settings or arbitrary storage. */
export function workspaceProfileLayout(value, paletteIds) {
  const source = record(value) ? value : {};
  return {
    chrome: chromePreferences(record(source.chrome) ? source.chrome : {}),
    tab: typeof source.tab === 'string' && source.tab.length <= 100 ? source.tab : 'Home',
    commandHeight: Number.isFinite(source.commandHeight) ? Math.max(32, Math.min(600, source.commandHeight)) : 90,
    palettes: paletteLayout(source.palettes, paletteIds)
  };
}

export function workspaceProfiles(value, paletteIds) {
  const entries = record(value) && value.version === 1 && Array.isArray(value.profiles) ? value.profiles : [];
  const names = new Set(), profiles = [];
  for (const entry of entries.slice(0, workspaceProfileLimit)) {
    if (!record(entry)) continue;
    const name = nameOf(entry.name), key = name.toLocaleLowerCase('en-US');
    if (!name || names.has(key)) continue;
    names.add(key);
    profiles.push({ name, layout: workspaceProfileLayout(entry.layout, paletteIds) });
  }
  return { version: 1, profiles };
}

export function changeWorkspaceProfiles(saved, action, paletteIds) {
  const value = workspaceProfiles(saved, paletteIds), name = nameOf(action.name);
  if (!name || typeof action.name !== 'string' || action.name.trim().length > 80)
    throw new Error('Choose a workspace name containing 1–80 characters.');
  const matches = candidate => candidate.name.toLocaleLowerCase('en-US') === name.toLocaleLowerCase('en-US');
  const index = value.profiles.findIndex(matches);
  if (action.type === 'save') {
    if (index >= 0 && !action.replace) throw new Error('A UI workspace with this name already exists. Use Update saved workspace.');
    if (index < 0 && value.profiles.length >= workspaceProfileLimit) throw new Error(`Keep at most ${workspaceProfileLimit} saved UI workspaces.`);
    const entry = { name, layout: workspaceProfileLayout(action.layout, paletteIds) };
    if (index >= 0) value.profiles[index] = entry; else value.profiles.push(entry);
  } else if (action.type === 'delete') {
    if (index < 0) throw new Error('The saved UI workspace no longer exists.');
    value.profiles.splice(index, 1);
  } else if (action.type === 'rename') {
    const next = nameOf(action.next);
    if (!next || typeof action.next !== 'string' || action.next.trim().length > 80) throw new Error('Choose a workspace name containing 1–80 characters.');
    if (index < 0) throw new Error('The saved UI workspace no longer exists.');
    if (value.profiles.some((entry, position) => position !== index && entry.name.toLocaleLowerCase('en-US') === next.toLocaleLowerCase('en-US')))
      throw new Error('A UI workspace with this name already exists.');
    value.profiles[index] = { ...value.profiles[index], name: next };
  } else throw new Error('Unknown UI workspace action.');
  return value;
}

export function workspacePresets(paletteIds) {
  const draft = workspaceProfileLayout({}, paletteIds);
  const graph = workspaceProfileLayout({}, paletteIds);
  graph.tab = 'Graph';
  if (graph.palettes.panels.Graph) {
    graph.palettes.panels.Graph = { ...graph.palettes.panels.Graph, open: true, side: 'left', width: 350 };
    graph.palettes.active = 'Graph';
  }
  return [{ name: 'Drafting', layout: draft }, { name: 'Graph analysis', layout: graph }];
}

/** Storage is injected so hosts can keep preferences private or use another persistence layer. */
export class WorkspaceProfiles {
  constructor({ paletteIds, storage, key = workspaceProfileKey }) {
    this.paletteIds = [...paletteIds]; this.storage = storage; this.key = key;
  }
  read() {
    try {
      const text = this.storage?.getItem(this.key);
      return workspaceProfiles(text && text.length <= 512000 ? JSON.parse(text) : null, this.paletteIds);
    } catch { return workspaceProfiles(null, this.paletteIds); }
  }
  change(action) {
    const next = changeWorkspaceProfiles(this.read(), action, this.paletteIds);
    try {
      if (!this.storage) throw new Error();
      this.storage.setItem(this.key, JSON.stringify(next));
    } catch { throw new Error('UI workspace could not be saved. Browser storage is unavailable or full.'); }
    return next;
  }
}
