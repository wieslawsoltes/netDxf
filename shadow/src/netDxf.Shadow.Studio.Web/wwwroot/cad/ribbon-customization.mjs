import { ribbonHostPreferences } from './ribbon-host-model.mjs';
/** Declarative workspace preferences. No command text, macros or executable content is accepted. */
export const ribbonCustomizationKey = 'netdxf-cad-ribbon-v1';
export const ribbonLimits = Object.freeze({ tabs: 80, panels: 256, customTabs: 8, customPanels: 24, commands: 32, floating: 16, bytes: 131072 });
const object = value => value && typeof value === 'object' && !Array.isArray(value);
const name = value => typeof value === 'string' ? value.trim().slice(0, 80) : '';
const key = value => typeof value === 'string' && value.length > 0 && value.length <= 100 && !/[\u0000-\u001f]/.test(value);
const userKey = value => typeof value === 'string' && /^user:[a-zA-Z0-9_-]{1,64}$/.test(value);
const commandKey = value => typeof value === 'string' && value.length <= 180 && /^(command|action|input):[a-zA-Z0-9_.: -]+$/.test(value);
const bounded = (values, limit) => Array.isArray(values) ? values.slice(0, limit) : [];
export const ribbonPanelKey = (tab, panel) => JSON.stringify([tab, panel]);
export const protectedRibbonPanel = (tab, panel) => panel === 'Block Editor' || tab === 'Block Editor' && panel === 'Definition';
const unique = (values, identity) => { const seen = new Set(); return values.filter(value => { const id = identity(value); if (seen.has(id)) return false; seen.add(id); return true; }); };

export function ribbonPreferences(value) {
  const source = object(value) && value.version === 1 ? value : {};
  return {
    version: 1,
    host: ribbonHostPreferences(source.host),
    tabs: unique(bounded(source.tabs, ribbonLimits.tabs).filter(value => object(value) && key(value.id))
      .map(value => ({ id: value.id, visible: ['Home', 'Block Editor'].includes(value.id) || value.visible !== false })), value => value.id),
    panels: unique(bounded(source.panels, ribbonLimits.panels).filter(value => object(value) && key(value.tab) && key(value.id))
      .map(value => ({ tab: value.tab, id: value.id, visible: protectedRibbonPanel(value.tab, value.id) || value.visible !== false })), value => ribbonPanelKey(value.tab, value.id)),
    customTabs: unique(bounded(source.customTabs, ribbonLimits.customTabs).filter(value => object(value) && userKey(value.id) && name(value.name))
      .map(value => ({ id: value.id, name: name(value.name) })), value => value.id),
    customPanels: unique(bounded(source.customPanels, ribbonLimits.customPanels).filter(value => object(value) && userKey(value.id) && key(value.tab) && name(value.name))
      .map(value => ({ id: value.id, tab: value.tab, name: name(value.name), commands: [...new Set(bounded(value.commands, 256).filter(commandKey))].slice(0, ribbonLimits.commands) })), value => value.id),
    floating: unique(bounded(source.floating, ribbonLimits.floating).filter(value => object(value) && key(value.tab) && key(value.panel) && !protectedRibbonPanel(value.tab, value.panel))
      .map(value => ({ tab: value.tab, panel: value.panel, x: Number.isFinite(value.x) ? Math.max(0, Math.min(100000, value.x)) : 24,
        y: Number.isFinite(value.y) ? Math.max(0, Math.min(100000, value.y)) : 160, orientation: value.orientation === 'vertical' ? 'vertical' : 'horizontal' })), value => ribbonPanelKey(value.tab, value.panel)),
    expanded: unique(bounded(source.expanded,64).filter(value=>object(value)&&key(value.tab)&&key(value.panel))
      .map(value=>({tab:value.tab,panel:value.panel})),value=>ribbonPanelKey(value.tab,value.panel))
  };
}

/** Missing module identities remain saved but cannot create available commands or tabs. */
export function customizedRibbonTabs(preferences, entries, { includeHidden = false } = {}) {
  const value = ribbonPreferences(preferences);
  const available = unique([...entries, ...value.customTabs.map(tab => ({ key: tab.id, name: tab.name, custom: true }))], tab => tab.key);
  const ordered = value.tabs.map(tab => available.find(entry => entry.key === tab.id)).filter(Boolean);
  for (const entry of available) if (!ordered.some(tab => tab.key === entry.key)) ordered.push(entry);
  return ordered.map(entry => ({ ...entry, visible: value.tabs.find(tab => tab.id === entry.key)?.visible !== false }))
    .filter(entry => includeHidden || entry.visible);
}
export function customizedRibbonPanels(preferences, tab, entries, { includeHidden = false } = {}) {
  const value = ribbonPreferences(preferences), panels = unique([...entries,
    ...value.customPanels.filter(panel => panel.tab === tab).map(panel => ({ id: panel.id, name: panel.name, custom: true, commands: panel.commands }))], panel => panel.id);
  const ordered = value.panels.filter(panel => panel.tab === tab).map(panel => panels.find(entry => entry.id === panel.id)).filter(Boolean);
  for (const entry of panels) if (!ordered.some(panel => panel.id === entry.id)) ordered.push(entry);
  return ordered.map(entry => ({ ...entry, visible: value.panels.find(panel => panel.tab === tab && panel.id === entry.id)?.visible !== false }))
    .filter(entry => includeHidden || entry.visible);
}
export function floatingRibbonPosition(value, size, viewport) {
  return { x: Math.max(4, Math.min(Number.isFinite(value.x) ? value.x : 24, viewport.width - Math.min(size.width, viewport.width - 8) - 4)),
    y: Math.max(4, Math.min(Number.isFinite(value.y) ? value.y : 160, viewport.height - Math.min(size.height, viewport.height - 8) - 4)) };
}
const move = (items, index, offset) => { if (![-1, 1].includes(offset) || index < 0 || index + offset < 0 || index + offset >= items.length) return; [items[index], items[index + offset]] = [items[index + offset], items[index]]; };
const reorder = (items,index,beforeId) => { if(index<0||items[index].id===beforeId)return; const [item]=items.splice(index,1),before=items.findIndex(value=>value.id===beforeId);items.splice(before<0?items.length:before,0,item); };
export function changeRibbonPreferences(preferences, action, { entries = [], panels = [], catalog = [] } = {}) {
  const value = ribbonPreferences(preferences);
  if (action.type === 'reset') return ribbonPreferences(null);
  if (action.type === 'host') { value.host=ribbonHostPreferences(action.value); }
  else if (action.type === 'panel-expanded') {
    if(!key(action.tab)||!key(action.panel))throw new Error('Choose a ribbon panel.');
    value.expanded=value.expanded.filter(panel=>panel.tab!==action.tab||panel.panel!==action.panel);
    if(action.expanded){if(value.expanded.length>=64)throw new Error('Keep at most 64 expanded panels.');value.expanded.push({tab:action.tab,panel:action.panel});}
  }
  else if (action.type === 'tab-visible' || action.type === 'tab-move' || action.type === 'tab-reorder') {
    const dormant = value.tabs.filter(tab => !entries.some(entry => entry.key === tab.id) && !value.customTabs.some(entry => entry.id === tab.id));
    value.tabs = [...customizedRibbonTabs(value, entries, { includeHidden: true }).map(entry => ({ id: entry.key, visible: entry.visible })), ...dormant];
    const index = value.tabs.findIndex(tab => tab.id === action.id);
    if (index < 0) throw new Error('Choose an available ribbon tab.');
    if (action.type === 'tab-visible') value.tabs[index].visible = ['Home', 'Block Editor'].includes(action.id) || action.visible !== false;
    else if(action.type==='tab-reorder')reorder(value.tabs,index,action.beforeId);
    else move(value.tabs, index, action.offset);
  } else if (action.type === 'panel-visible' || action.type === 'panel-move' || action.type === 'panel-reorder') {
    const ordered = customizedRibbonPanels(value, action.tab, panels, { includeHidden: true }).map(panel => ({ tab: action.tab, id: panel.id, visible: panel.visible }));
    const index = ordered.findIndex(panel => panel.id === action.id);
    if (index < 0) throw new Error('Choose an available ribbon panel.');
    if (action.type === 'panel-visible') ordered[index].visible = protectedRibbonPanel(action.tab, action.id) || action.visible !== false;
    else if(action.type==='panel-reorder')reorder(ordered,index,action.beforeId);
    else move(ordered, index, action.offset);
    const dormant = value.panels.filter(panel => panel.tab === action.tab && !ordered.some(entry => entry.id === panel.id));
    value.panels = [...value.panels.filter(panel => panel.tab !== action.tab), ...ordered, ...dormant];
  } else if (action.type === 'custom-tab') {
    if (!userKey(action.id) || !name(action.name)) throw new Error('Choose a tab name containing 1–80 characters.');
    const old = value.customTabs.find(tab => tab.id === action.id);
    if (!old && value.customTabs.length >= ribbonLimits.customTabs) throw new Error('Keep at most 8 custom ribbon tabs.');
    if (old) old.name = name(action.name); else value.customTabs.push({ id: action.id, name: name(action.name) });
  } else if (action.type === 'custom-panel') {
    if (!userKey(action.id) || !key(action.tab) || !name(action.name)) throw new Error('Choose a valid tab and panel name.');
    if (!customizedRibbonTabs(value, entries, { includeHidden: true }).some(tab => tab.key === action.tab)) throw new Error('Choose an available ribbon tab.');
    if (!Array.isArray(action.commands) || action.commands.length > ribbonLimits.commands) throw new Error('Keep at most 32 commands per custom panel.');
    const known = new Set(catalog.map(entry => entry.id));
    if (action.commands.some(id => !known.has(id))) throw new Error('Choose only registered commands for a custom panel.');
    const next = { id: action.id, tab: action.tab, name: name(action.name), commands: [...new Set(action.commands)] };
    const index = value.customPanels.findIndex(panel => panel.id === action.id);
    if (index < 0 && value.customPanels.length >= ribbonLimits.customPanels) throw new Error('Keep at most 24 custom ribbon panels.');
    if (index >= 0) value.customPanels[index] = next; else value.customPanels.push(next);
  } else if (action.type === 'delete-tab' || action.type === 'delete-panel') {
    if (action.type === 'delete-tab') {
      value.customTabs = value.customTabs.filter(tab => tab.id !== action.id);
      value.customPanels = value.customPanels.filter(panel => panel.tab !== action.id);
      value.tabs = value.tabs.filter(tab => tab.id !== action.id);
      value.panels = value.panels.filter(panel => panel.tab !== action.id);
      value.floating = value.floating.filter(panel => panel.tab !== action.id);
      value.expanded = value.expanded.filter(panel => panel.tab !== action.id);
    } else {
      value.customPanels = value.customPanels.filter(panel => panel.id !== action.id);
      value.panels = value.panels.filter(panel => panel.id !== action.id);
      value.floating = value.floating.filter(panel => panel.panel !== action.id);
      value.expanded = value.expanded.filter(panel => panel.panel !== action.id);
    }
  } else if (action.type === 'float' || action.type === 'return' || action.type === 'float-orientation') {
    if (!key(action.tab) || !key(action.panel) || protectedRibbonPanel(action.tab, action.panel)) throw new Error('This contextual panel must remain on its ribbon.');
    const index = value.floating.findIndex(panel => panel.tab === action.tab && panel.panel === action.panel);
    if (action.type === 'return') { if (index >= 0) value.floating.splice(index, 1); }
    else if(action.type==='float-orientation') { if(index<0)throw new Error('Float the panel before changing its orientation.');value.floating[index].orientation=action.orientation; }
    else {
      if (index < 0 && value.floating.length >= ribbonLimits.floating) throw new Error('Keep at most 16 floating ribbon panels.');
      const next = { tab: action.tab, panel: action.panel, x: action.x, y: action.y, orientation: action.orientation || value.floating[index]?.orientation || 'horizontal' };
      if (index >= 0) value.floating[index] = next; else value.floating.push(next);
    }
  } else throw new Error('Unknown ribbon customization action.');
  return ribbonPreferences(value);
}

export function parseRibbonCustomization(text) {
  if (typeof text !== 'string' || text.length > ribbonLimits.bytes || new TextEncoder().encode(text).length > ribbonLimits.bytes)
    throw new Error('Ribbon customization JSON exceeds 128 KiB.');
  const value = JSON.parse(text);
  if (!object(value) || value.version !== 1) throw new Error('Unsupported ribbon customization version.');
  return ribbonPreferences(value);
}
/** Persistence is injected and failures remain visible. Hosts own command dispatch and redraw. */
export class RibbonCustomization {
  constructor({ storage, key = ribbonCustomizationKey, changed = () => {}, report = () => {} } = {}) {
    this.storage = storage; this.key = key; this.changed = changed; this.report = report;
    try { const text = storage?.getItem(key); this.value = text ? parseRibbonCustomization(text) : ribbonPreferences(null); }
    catch { this.value = ribbonPreferences(null); }
  }
  snapshot() { return ribbonPreferences(this.value); }
  tabs(entries, options) { return customizedRibbonTabs(this.value, entries, options); }
  panels(tab, entries, options) { return customizedRibbonPanels(this.value, tab, entries, options); }
  restore(value, { notify = true, strict = true } = {}) {
    const next = ribbonPreferences(value), text = JSON.stringify(next);
    if (new TextEncoder().encode(text).length > ribbonLimits.bytes) throw new Error('Ribbon customization JSON exceeds 128 KiB.');
    try { if (!this.storage) throw new Error(); this.storage.setItem(this.key, text); }
    catch {
      const error = new Error('Ribbon customization could not be saved. Browser storage is unavailable or full.');
      if (strict) throw error; this.report(error);
    }
    this.value = next; if (notify) this.changed(this.snapshot()); return this.snapshot();
  }
  change(action, context) { return this.restore(changeRibbonPreferences(this.value, action, context)); }
}
