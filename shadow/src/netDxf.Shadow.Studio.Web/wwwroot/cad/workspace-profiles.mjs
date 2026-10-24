import { ribbonPreferences } from './ribbon-customization.mjs';
import { chromePreferences } from './ribbon-layout.mjs';
import { paletteLayout } from './palette-layout.mjs';
import { quickAccessPreferences } from './command-catalog.mjs';
import { shellPreferences } from './workspace-shell-model.mjs';

export const workspaceProfileKey = 'netdxf-cad-workspaces-v1';
export const workspaceProfileLimit = 24;
export const workspaceProfileStorageLimit = 512000;
const record = value => value && typeof value === 'object' && !Array.isArray(value);
const nameOf = value => typeof value === 'string' ? value.trim().slice(0, 80) : '';

/** Resolve a workspace without conflating a built-in preset and an equally named saved layout. */
export function findWorkspaceProfile(entries, identity) {
  if (record(identity) && identity.detached) return undefined;
  const name = typeof identity === 'string' ? identity : identity?.name;
  if (typeof name !== 'string' || !name.trim()) return undefined;
  const key = name.trim().toLocaleLowerCase('en-US');
  return entries.find(entry => entry.name.toLocaleLowerCase('en-US') === key &&
    (typeof identity === 'string' || !!entry.preset === !!identity?.preset));
}

/** UI state only: never copies drawing identities, agent settings or arbitrary storage. */
export function workspaceProfileLayout(value, paletteIds) {
  const source = record(value) ? value : {};
  return {
    chrome: chromePreferences(record(source.chrome) ? source.chrome : {}),
    tab: typeof source.tab === 'string' && source.tab.length <= 100 ? source.tab : 'Home',
    commandHeight: Number.isFinite(source.commandHeight) ? Math.max(32, Math.min(600, source.commandHeight)) : 90,
    palettes: paletteLayout(source.palettes, paletteIds),
    quickAccess: quickAccessPreferences(source.quickAccess),
    ribbonCustomization: ribbonPreferences(source.ribbonCustomization),
    shell: shellPreferences(source.shell)
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
  const presets=[{ name: 'Drafting', layout: draft }, { name: 'Graph analysis', layout: graph }];
  if (paletteIds.includes('References')) {
    const dependencies=workspaceProfileLayout({},paletteIds);
    dependencies.tab='Graph';
    const tabs=['References','Graph'].filter(id=>paletteIds.includes(id));
    for(const id of tabs)dependencies.palettes.panels[id]={...dependencies.palettes.panels[id],open:true,side:'left',width:540};
    dependencies.palettes.active='References';
    dependencies.palettes.groups.left=[{tabs,active:'References',weight:1}];
    presets.push({name:'Dependency analysis',layout:workspaceProfileLayout(dependencies,paletteIds)});
  }
  if (paletteIds.includes('Modules')) {
    const discipline = workspaceProfileLayout({}, paletteIds);
    discipline.tab = 'Graph';
    const tabs = ['Modules', 'Graph', 'References'].filter(id => paletteIds.includes(id));
    for (const id of tabs) discipline.palettes.panels[id] = { ...discipline.palettes.panels[id], open: true, side: 'left', width: 540 };
    discipline.palettes.active = 'Modules';
    discipline.palettes.groups.left = [{ tabs, active: 'Modules', weight: 1 }];
    presets.push({ name: 'Discipline analysis', layout: workspaceProfileLayout(discipline, paletteIds) });
  }
  return presets;
}

/** Storage is injected so hosts can keep preferences private or use another persistence layer. */
export class WorkspaceProfiles {
  constructor({ paletteIds, storage, key = workspaceProfileKey }) {
    this.paletteIds = [...paletteIds]; this.storage = storage; this.key = key;
  }
  read() {
    try {
      const text = this.storage?.getItem(this.key);
      return workspaceProfiles(text && text.length <= workspaceProfileStorageLimit ? JSON.parse(text) : null, this.paletteIds);
    } catch { return workspaceProfiles(null, this.paletteIds); }
  }
  change(action) {
    const next = changeWorkspaceProfiles(this.read(), action, this.paletteIds);
    const text = JSON.stringify(next);
    if (text.length > workspaceProfileStorageLimit)
      throw new Error('Saved UI workspaces exceed the storage limit. Remove an unused workspace or simplify its ribbon customization.');
    try {
      if (!this.storage) throw new Error();
      this.storage.setItem(this.key, text);
    } catch { throw new Error('UI workspace could not be saved. Browser storage is unavailable or full.'); }
    return next;
  }
}
