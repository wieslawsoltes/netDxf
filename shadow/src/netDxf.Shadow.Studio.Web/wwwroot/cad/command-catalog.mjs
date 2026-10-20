import { commands } from './core.mjs';
import { graphOperations } from './graph-contracts.mjs';

// Explicit action targets are shared by discovery and pinned buttons. No command
// strings, drawing payloads, or executable expressions are read from preferences.
const groups = {
  File: [
    ['new','New drawing','NEW QNEW','Ctrl+N'],['open','Open DXF / DWG','OPEN','Ctrl+O'],
    ['save','Save DXF','QSAVE','Ctrl+S'],['save-as','Save drawing as','SAVE SAVEAS'],
    ['demo','New sample drawing'],['export-png','Export PNG'],['export-svg','Export SVG']
  ],
  History: [['undo','Undo','U UNDO','Ctrl+Z'],['redo','Redo','REDO','Ctrl+Y']],
  Navigate: [['fit','Zoom extents','Z ZOOM'],['zoom-in','Zoom in'],['zoom-out','Zoom out'],['pan','Pan','P PAN'],['regen','Regenerate drawing','RE REGEN']],
  Resources: [
    ['layer-manager','Layer manager'],['layer-filters','Layer filters and isolation'],['layer-isolate','Isolate selected layers','LAYISO'],['layer-unisolate','Restore isolated layers','LAYUNISO'],['layer-states','Layer states','LAYERSTATE LAS'],['layers','Layers palette','LA LAYER'],
    ['layer-new','New layer'],['assign-layer','Assign current layer'],['linetypes','Linetypes'],['text-styles','Text styles'],
    ['font','Load SHX / SHP fonts'],['annotation','Model annotation scale'],['linetype','Linetype endpoint alignment']
  ],
  Blocks: [
    ['block-insert','Insert block dialog'],['blocks','Blocks palette'],['block-create','Create block from selection'],
    ['block-redefine','Redefine block from selection'],['block-rename','Rename block dialog'],
    ['block-edit','Edit block definition','BEDIT BE'],['block-save','Save block definition','BSAVE'],['block-close','Close / discard block edits','BCLOSE'],
    ['block-library-import','Import block library'],['block-library-export','Export block library'],
    ['attributes','Edit instance attributes','EATTEDIT ATTEDIT ATE'],['attribute-definitions','Manage attribute definitions','BATTMAN'],
    ['attribute-create','Define attribute','ATTDEF'],['attribute-sync','Synchronize attributes','ATTSYNC']
  ],
  Hatch: [['hatch-create','Hatch selected boundaries'],['hatch-edit','Edit hatch pattern']],
  Graph: [
    ['graph-analysis','Graph workspace','GRAPH TOPOLOGY'],['graph-load','Visualize topology'],['graph-fit','Fit topology'],
    ['graph-clear','Clear graph overlay'],['graph','Toggle topology overlay'],['reference-graph','Object references','REFGRAPH'],
    ['reference-query:explore','Object reference explorer'],['reference-query:components','Cyclic dependency groups'],
    ['reference-query:path','Shortest dependency path'],['reference-query:impact','Reference impact']
  ],
  Inspect: [
    ['properties','Properties','PROPERTIES PR'],['explorer','Drawing object explorer','LIST'],['diagnostics','Render diagnostics'],
    ['health','Drawing health'],['edit-previews','Edit previews'],['workflows','Workflow regressions'],['legacy','Advanced diagnostics']
  ],
  Workspace: [
    ['ribbon-customize','Customize ribbon','CUI'],['command-search','Search commands','','Ctrl+K'],['quick-access-customize','Customize Quick Access'],
    ['ui-workspaces','UI workspaces','WORKSPACE WSSAVE'],['workspaces','Immutable drawing workspaces'],
    ['fork','Fork drawing workspace'],['snapshot','Retain snapshot','SNAPSHOT'],['snapshots','Snapshot views'],
    ['palette-picker','Open drawing palette'],['palette','Show or hide palettes','','Ctrl+1'],['dock','Change palette docking side'],
    ['palette-lock','Lock or unlock palette layout'],['palette-reset','Reset palette layout'],
    ['clean','Clean screen','','Ctrl+0'],['ribbon-display','Ribbon display options'],['ribbon-cycle','Cycle ribbon display'],
    ['ribbon-minimize','Minimize or expand ribbon'],['command-line','Show or hide command line','','Ctrl+9'],
    ['command-history','Show or hide command history','','F2'],['reset-ui','Reset interface']
  ],
  Settings: [['settings','Drafting settings','OPTIONS OSNAP'],['selection-cycling','Selection cycling','','Shift+Space'],['modules','Discipline modules'],['agents','Shadow Agent'],['help','Command help','HELP','F1']]
};
const writeActions = new Set(['layer-isolate','layer-unisolate','save','save-as','font','undo','redo','layer-new','assign-layer','block-insert','block-create',
  'block-redefine','block-rename','block-edit','block-save','block-close','block-library-import','block-library-export',
  'attributes','attribute-definitions','attribute-create','attribute-sync','hatch-create','hatch-edit','snapshot','fork','annotation','linetype']);
const selectionActions = new Set(['layer-isolate','assign-layer','block-create','block-redefine','hatch-create','hatch-edit','attributes']);
const blockActions = new Set(['block-insert','block-edit','block-rename','block-redefine','block-library-export']);
const globalActions = new Set(['new','open','demo','agents','help','settings','selection-cycling','legacy','palette','palette-picker','dock',
  'palette-lock','palette-reset','ui-workspaces','clean','ribbon-display','ribbon-cycle','ribbon-minimize','command-line','command-history','reset-ui',
  'command-search','quick-access-customize','ribbon-customize']);
const actionAliases = (value = '') => value.split(' ').filter(Boolean);
const moduleId = value => typeof value === 'string' && /^[a-zA-Z][a-zA-Z0-9_.-]{0,63}$/.test(value);
const text = (value, maximum = 160) => typeof value === 'string' ? value.slice(0, maximum) : '';
const baseActions = Object.entries(groups).flatMap(([category, entries]) => entries.map(([target,label,aliases,shortcut]) => Object.freeze({
  id:`action:${target}`,kind:'action',target,label,category,location:category,aliases:actionAliases(aliases),shortcut:shortcut || '',
  requiresDocument:!globalActions.has(target),requiresWrite:writeActions.has(target),requiresSelection:selectionActions.has(target),
  requiresIdle:['new','open','demo','font'].includes(target),
  requiresBlocks:blockActions.has(target),requiresBlockEditor:['block-save','block-close'].includes(target),
  excludesBlockEditor:['block-edit','layer-isolate','layer-unisolate'].includes(target)
})));
const categories = name => /^(TEXT|MTEXT|DIM)/.test(name) ? 'Annotate' :
  /^(BLOCK|INSERT)/.test(name) ? 'Blocks' : name.startsWith('HATCH') ? 'Hatch' :
  commands[name]?.selection ? 'Modify' : commands[name]?.measure ? 'Measure' : 'Draw';

/** Catalog metadata is declarative; callers dispatch only its registered kind/target. */
export function buildCommandCatalog({ modules = [] } = {}) {
  const entries = [
    ...Object.entries(commands).map(([target, spec]) => ({id:`command:${target}`,kind:'command',target,label:spec.label,
      aliases:spec.aliases.filter(alias=>alias!=='-INSERT'),category:categories(target),location:categories(target),shortcut:'',
      requiresDocument:true,requiresWrite:true,requiresBlocks:['INSERT','BLOCKRENAME','BLOCKREDEFINE'].includes(target)})),
    {id:'command:-INSERT',kind:'command',target:'-INSERT',label:'Insert block with command prompts',aliases:['-INSERT'],
      category:'Blocks',location:'Insert · Command prompts',shortcut:'',requiresDocument:true,requiresWrite:true,requiresBlocks:true},
    ...baseActions,
    ...Object.entries(graphOperations).map(([operation,label]) => ({id:`action:graph-query:${operation}`,kind:'action',
      target:`graph-query:${operation}`,label,aliases:[],category:'Graph',location:'Graph · Topology analysis',shortcut:'',requiresDocument:true})),
    ...['ATTREQ','ATTDIA'].map(target => ({id:`input:${target}`,kind:'input',target,
      label:target==='ATTREQ'?'Insertion attribute prompts':'Insertion attribute dialog',
      aliases:[target],category:'Blocks',location:'Insert · Attribute settings',shortcut:'',requiresIdleCommand:true}))
  ];
  const known = new Set(entries.map(entry=>entry.id));
  for (const state of (Array.isArray(modules) ? modules : []).slice(0,64)) {
    const descriptor = state?.descriptor;
    if (!descriptor || !moduleId(descriptor.id) || !text(descriptor.name)) continue;
    const module = descriptor.id, category = text(descriptor.name);
    const append = entry => {
      if (!known.has(entry.id)) { known.add(entry.id);entries.push({...entry,category,location:`${category} · Discipline`,shortcut:'',
        module,requiresDocument:true,excludesBlockEditor:true}); }
    };
    for (const [mode,label] of [['symbols','Symbol library'],['data','Data manager'],['validation','Validation'],['connectivity','Connectivity graph'],['structure','Network structure']]) {
      const target=`module-open:${module}:${mode}`;
      append({id:`action:${target}`,kind:'action',target,label:`${category} ${label}`,aliases:[category,mode]});
    }
    for (const command of (Array.isArray(descriptor.commands)?descriptor.commands:[]).slice(0,128)) {
      if (!moduleId(command?.id) || !text(command?.name)) continue;
      const target=`module-command:${module}:${command.id}`;
      append({id:`action:${target}`,kind:'action',target,label:`${category}: ${text(command.name)}`,
        description:text(command.description,512),aliases:[category,command.id],requiresWrite:true,
        requiresSelection:command.requiresSelection===true});
    }
  }
  return entries.map(entry=>Object.freeze(entry));
}

export function commandAvailability(entry, state = {}) {
  if (!entry) return 'This command is no longer registered.';
  if (entry.requiresDocument && !state.hasDocument) return 'Open a drawing first.';
  if ((entry.requiresDocument || entry.requiresIdle) && state.busy) return 'Wait for the current drawing operation.';
  if (entry.requiresWrite && state.readOnly) return 'Return to the current drawing before editing.';
  if (entry.module && !(state.modules || []).some(module=>module.id===entry.module && module.enabled===true))
    return 'Enable this discipline module first.';
  if (entry.requiresBlockEditor && !state.blockEditing) return 'Open a block definition first.';
  if (entry.excludesBlockEditor && state.blockEditing) return 'Close the block editor first.';
  if (entry.requiresSelection && !state.selectionCount) return 'Select source objects first.';
  if (entry.requiresBlocks && !state.blockCount) return 'Create or import a block definition first.';
  if (entry.requiresIdleCommand && state.commandActive) return 'Finish or cancel the current drawing command first.';
  if (entry.target==='undo' && !state.undoCount) return 'Nothing to undo.';
  if (entry.target==='redo' && !state.redoCount) return 'Nothing to redo.';
  return '';
}

/** Re-resolve IDs at execution time; displayed metadata is never an authorization receipt. */
export function resolveCommand(id, catalog, state) {
  const entry = catalog.find(value=>value.id===id), reason = commandAvailability(entry,state);
  if (reason) throw new Error(reason);
  return entry;
}

const normalize = value => text(value,256).normalize('NFKD').replace(/[\u0300-\u036f]/g,'').toLowerCase().replace(/[^a-z0-9]+/g,' ').trim();
export function searchCommands(catalog, query = '', limit = 60) {
  const raw=text(query,256).trim().toLowerCase(),needle=normalize(query),words=needle.split(/\s+/).filter(Boolean);
  const ranked=[];
  for (const [index,entry] of catalog.entries()) {
    const label=normalize(entry.label),target=normalize(entry.target),aliases=(entry.aliases||[]).map(normalize);
    const haystack=[label,target,...aliases,normalize(entry.category),normalize(entry.location),normalize(entry.description)].join(' ');
    if (words.some(word=>!haystack.includes(word))) continue;
    const exactAlias=(entry.aliases||[]).some(alias=>alias.toLowerCase()===raw);
    const score=!needle?0:exactAlias?1300:entry.target.toLowerCase()===raw?1200:target===needle||aliases.includes(needle)?1000:label===needle?900:
      label.startsWith(needle)?800:target.startsWith(needle)?700:aliases.some(alias=>alias.startsWith(needle))?650:
      label.includes(needle)?600:words.filter(word=>label.includes(word)).length*30;
    ranked.push({entry,score,index});
  }
  ranked.sort((a,b)=>b.score-a.score || a.index-b.index);
  return ranked.slice(0,Math.max(1,Math.min(200,Number.isInteger(limit)?limit:60))).map(value=>value.entry);
}

export const quickAccessKey = 'netdxf-cad-quick-access-v1';
export const quickAccessLimit = 16;
export const defaultQuickAccess = Object.freeze(['new','open','save','undo','redo'].map(value=>`action:${value}`));
const knownIds = new Set(buildCommandCatalog().map(entry=>entry.id));
const validPreferenceId = value => typeof value==='string' && value.length<=180 && (knownIds.has(value) ||
  /^action:module-(?:command:[a-zA-Z][a-zA-Z0-9_.-]{0,63}:[a-zA-Z][a-zA-Z0-9_.-]{0,63}|open:[a-zA-Z][a-zA-Z0-9_.-]{0,63}:(?:symbols|data|validation|connectivity|structure))$/.test(value));
/** Preserve unloaded module IDs so disabling a module never silently erases its pin. */
export function quickAccessPreferences(value) {
  if (!value || value.version!==1 || !Array.isArray(value.pinned)) return {version:1,pinned:[...defaultQuickAccess]};
  return {version:1,pinned:[...new Set(value.pinned.slice(0,256).filter(validPreferenceId))].slice(0,quickAccessLimit)};
}
export function changeQuickAccess(value, action, catalog = []) {
  const next=quickAccessPreferences(value), index=next.pinned.indexOf(action.id);
  if (action.type==='reset') return quickAccessPreferences(null);
  if (action.type==='pin') {
    if (!validPreferenceId(action.id) || !catalog.some(entry=>entry.id===action.id)) throw new Error('Choose a registered command to pin.');
    if (index>=0) return next;
    if (next.pinned.length>=quickAccessLimit) throw new Error(`Keep at most ${quickAccessLimit} Quick Access commands.`);
    next.pinned.push(action.id);
  } else if (action.type==='remove') {
    if (index>=0) next.pinned.splice(index,1);
  } else if (action.type==='move') {
    const destination=index+action.offset;
    if (![-1,1].includes(action.offset) || index<0 || destination<0 || destination>=next.pinned.length) return next;
    [next.pinned[index],next.pinned[destination]]=[next.pinned[destination],next.pinned[index]];
  } else throw new Error('Unknown Quick Access change.');
  return next;
}
export function quickAccessCapacity(width) { return Math.max(0,Math.min(quickAccessLimit,Math.floor((Math.max(0,width)-90)/28))); }
