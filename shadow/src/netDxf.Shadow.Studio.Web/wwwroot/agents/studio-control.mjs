import { graphOperations } from '../cad/graph-contracts.mjs';
/** Typed UI automation boundary. Model-produced text is never executed as JavaScript or HTML. */
const actionNames = new Set([
  'properties','layers','blocks','explorer','workspaces','diagnostics','agents','modules','health','edit-previews','workflows','block-edit','block-save','block-close','block-insert','block-library-import','block-library-export', 'layer-manager', 'layer-filters', 'layer-isolate', 'layer-unisolate', 'layer-states', 'attributes', 'attribute-sync', 'attribute-definitions', 'attribute-create', 'linetypes', 'text-styles',
  'new','demo','open','save','save-as','font','undo','redo','fit','regen','zoom-in','zoom-out','pan','palette','dock','clean',
  'settings','selection-cycling','layer-new','assign-layer','snapshot','snapshots','fork','annotation','linetype','graph','graph-analysis','export-png','export-svg',
  'ribbon-customize','ribbon-minimize','ribbon-cycle','ribbon-display','palette-picker','palette-lock','palette-reset','ui-workspaces','reference-graph','graph-load','graph-fit','graph-clear','command-line','command-history',
  'legacy','reset-ui','help','block-create','block-redefine','block-rename','hatch-create','hatch-edit',
  'command-search','quick-access-customize','reference-query:explore','reference-query:components','reference-query:path','reference-query:impact'
]);
const readRoutes = new Set(['state', 'graph', 'hatch/pattern', 'resources', 'layer-state/details', 'layer-state/export', 'attributes', 'attribute-definitions', 'modules', 'edit-previews', 'workflow/status', 'workflow/export', 'block-libraries']);
const readPostRoutes = new Set(['graph/query','modules/symbols','modules/analyze','modules/schedule','modules/connectivity','health','references/capture','references/validate','insert-preview','selection-candidates','layer-state/preview']);
const editRoutes = new Set([
  'command', 'insert', 'block/define', 'block/rename', 'hatch/create', 'hatch/pattern', 'select', 'block/create', 'block/begin',
  'block/finish', 'grip', 'layer', 'layer-isolation', 'layer-state', 'layer-state/import', 'attributes', 'attribute-definitions', 'assign-layer', 'property', 'history/undo', 'history/redo', 'layout', 'retain', 'fork',
  'workspace', 'merge/preview', 'merge/apply', 'annotation-view', 'linetype-view','resource',
  'modules/toggle','modules/command','edit-preview','edit-preview/apply','edit-preview/discard','health/select','workflow/start','workflow/stop','workflow/replay','workflow/roundtrip'
]);
const keys = ['grid', 'snap', 'ortho', 'polar', 'osnap', 'dynamic', 'tracking', 'selectionCycling'];
function finite(value, name, min = -1e12, max = 1e12) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max)
    throw new Error(`${name} must be finite and between ${min} and ${max}.`);
  return value;
}
function object(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('UI action arguments must be a JSON object.');
  return value;
}

export const studioUiActions = Object.freeze([
  { name: 'snapshot', description: 'Read current document, selection, drawing camera, workspace, and visible controls.' },
  { name: 'inspect', description: 'Read public Studio controls and the currently open drawing dialog. Agent credentials are excluded.' },
  { name: 'command', description: 'Submit a CAD command line, including inline coordinates and values.', arguments: { text: 'LINE 0,0 10,10' } },
  { name: 'command.input', description: 'Supply the next CAD command input or an empty string to finish.', arguments: { text: '' } },
  { name: 'command.cancel', description: 'Cancel the active command; optionally clear selection.', arguments: { clearSelection: false } },
  { name: 'viewport.set', description: 'Set camera center and pixels per drawing unit.', arguments: { x: 0, y: 0, scale: 2 } },
  { name: 'viewport.pan', description: 'Pan by a delta in drawing units.', arguments: { dx: 10, dy: 0 } },
  { name: 'viewport.zoom', description: 'Zoom by a positive factor.', arguments: { factor: 1.5 } },
  { name: 'viewport.fit', description: 'Fit the current drawing bounds.' },
  { name: 'palette.show', description: 'Show Properties, Layers, Blocks, Explorer, Workspaces, Graph, References, Modules, Health, Edit previews, Workflows, or Diagnostics.', arguments: { name: 'Properties' } },
  { name: 'ribbon.show', description: 'Select a ribbon tab.', arguments: { name: 'Home' } },
  { name: 'settings.set', description: 'Set grid, snap, ortho, polar, osnap, dynamic, tracking, selectionCycling, gridStep, snapModes, polarAngle or polarTolerance.' },
  { name: 'document.activate', description: 'Activate an open drawing.', arguments: { id: 'document ID' } },
  { name: 'document.create', description: 'Create a new drawing or the sample drawing.', arguments: { demo: false } },
  { name: 'layout.set', description: 'Select a model or paper layout.', arguments: { name: 'Model' } },
  { name: 'workspace.set', description: 'Select a live document or isolated branch.', arguments: { id: 'live' } },
  { name: 'snapshot.show', description: 'Show a retained revision; null returns to the current drawing.', arguments: { revision: null } },
  { name: 'layer.set', description: 'Set the current drawing layer by name.', arguments: { name: '0' } },
  { name: 'selection.set', description: 'Select source entity IDs or clear selection.', arguments: { entities: [], extend: false } },
  { name: 'cad.read', description: 'Read CAD state, resources, graph queries, module discovery/analysis, health or drawing references. For attribute-definitions, use currentSpace:true for model/paper space or the open block editor, or block for a named definition. Supply body for typed query routes; discover schema first.', arguments: { path: 'graph' } },
  { name: 'cad.edit', description: 'Execute a supported native CAD transaction with revision fencing.', arguments: { path: 'command', body: {} } },
  { name: 'ui.action', description: 'Invoke a named Studio action. Returns a pending task when a dialog needs input.', arguments: { name: 'settings' } },
  { name: 'ui.control', description: 'Click a control or set a value using its ID from inspect.', arguments: { id: 'control-1', operation: 'click' } },
  { name: 'dialog.submit', description: 'Accept or cancel the current drawing dialog.', arguments: { accept: true } },
  { name: 'refresh', description: 'Refresh the visible drawing, document tabs, and render.' }
]);

export function installStudioControl(host) {
  const { client, viewport } = host;
  const controlIds = new WeakMap(), controls = new Map(), pending = new Map();
  let nextControl = 1, nextOperation = 1;
  const root = host.document || globalThis.document;
  const knownAction = name => actionNames.has(name) || typeof name==='string' && name.startsWith('graph-query:') && Object.hasOwn(graphOperations,name.slice(12)) || typeof name==='string' &&
    /^module-(?:open|command):[a-z0-9-]+:[a-z0-9-]+$/.test(name) && client.data?.modules?.some(m=>m.enabled&&m.id===name.split(':')[1]);
  const publicNode = node => !!node && !node.closest('[data-agent-private], .agent-workspace') &&
    node.getAttribute('type') !== 'password' && node.getAttribute('type') !== 'file';
  const visible = node => !node.hidden && !node.closest('[hidden], [inert]') && (!node.getClientRects || node.getClientRects().length > 0);
  function activeModal() {
    let dialogs;
    try { dialogs = [...root.querySelectorAll('dialog:modal')]; }
    catch { dialogs = [...root.querySelectorAll('dialog[open]')]; }
    // Focus identifies the top dialog when several native modal dialogs are stacked.
    // Include private dialogs here: their background must remain inert even though
    // none of their controls or contents may be exposed to an agent.
    return dialogs.find(dialog => dialog.contains?.(root.activeElement)) || dialogs.at(-1) || null;
  }
  const actionable = (node, modal = activeModal()) => publicNode(node) && visible(node) && (!modal || modal.contains(node));
  function descriptor(node) {
    if (!publicNode(node)) return null;
    let id = controlIds.get(node);
    if (!id) { id = `control-${nextControl++}`; controlIds.set(node, id); }
    controls.set(id, node);
    const tag = node.tagName.toLowerCase();
    const result = {
      id, elementId: node.id || undefined, tag, role: node.getAttribute('role') || undefined,
      label: (node.getAttribute('aria-label') || node.getAttribute('title') || node.textContent || '').trim().slice(0, 180),
      action: node.dataset?.action, command: node.dataset?.command, disabled: !!node.disabled
    };
    if (['input', 'select', 'textarea'].includes(tag)) {
      result.value = String(node.value ?? '').slice(0, 4000);
      if (node.type === 'checkbox') result.checked = !!node.checked;
      if (tag === 'select') result.options = [...node.options].slice(0, 128).map(o => ({ value: o.value, text: o.textContent, disabled: o.disabled }));
    }
    if (node.getAttribute('aria-selected') !== null) result.selected = node.getAttribute('aria-selected') === 'true';
    if (node.getAttribute('aria-pressed') !== null) result.pressed = node.getAttribute('aria-pressed') === 'true';
    return result;
  }
  function inspect() {
    controls.clear();
    const modal = activeModal();
    const nodes = [...(modal || root).querySelectorAll(modal ? 'button, input, select, textarea' :
      '#studio button, #studio input, #studio select, #studio textarea, #dialog button, #dialog input, #dialog select, #dialog textarea, .command-access-dialog button, .command-access-dialog input, .command-access-dialog select')]
      .filter(node => actionable(node, modal));
    const legacyDialog = root.getElementById('dialog');
    const labelledBy = modal?.getAttribute('aria-labelledby')?.split(/\s+/).map(id => root.getElementById(id)).filter(publicNode)
      .map(node => node.textContent).join(' ');
    return {
      controls: nodes.slice(0, 400).map(descriptor), truncated: nodes.length > 400,
      dialog: modal && publicNode(modal) ? (modal === legacyDialog
        ? { title: root.getElementById('dialog-title')?.textContent, text: root.getElementById('dialog-body')?.textContent?.slice(0, 8000) }
        : { title: (labelledBy || modal.getAttribute('aria-label') || 'Dialog').slice(0, 180) }) : null,
      pending: [...pending].map(([id, item]) => ({ id, name: item.name, status: item.status, error: item.error }))
    };
  }
  function snapshot() {
    const data = client.data;
    return {
      documentId: client.id, document: host.documents().find(d => d.id === client.id) || null,
      documents: host.documents().map(d => ({ id: d.id, name: d.name, dirty: d.dirty })),
      state: data?.state ? {
        serverId: data.state.serverId, documentId: data.state.documentId, workspaceId: data.state.workspaceId,
        workspaceName: data.state.workspaceName, revision: data.state.revision, viewVersion: data.state.viewVersion,
        changeVersion: data.state.changeVersion, layout: data.state.layout, layouts: data.state.layouts,
        entities: data.state.entities, objects: data.state.objects, undoCount: data.state.undoCount, redoCount: data.state.redoCount,
        bounds: data.state.bounds
      } : null,
      selection: [...(data?.selectedEntities || [])], busy: !!client.busy, camera: { ...viewport.camera },
      settings: { ...viewport.settings }, backend: viewport.backend, layer: host.currentLayer(),
      ribbon: host.ribbon(), palette: host.palette(), snapshot: host.retainedSnapshot(),
      command: host.command(), ui: inspect(), availableActions: studioUiActions
    };
  }
  async function deferred(name, callback) {
    const id = `operation-${nextOperation++}`, item = { name, status: 'running' };
    const task = Promise.resolve().then(callback);
    pending.set(id, item);
    task.then(() => { item.status = 'completed'; }, error => { item.status = 'error'; item.error = error.message; host.report(error); });
    const result = await Promise.race([task.then(() => true), new Promise(resolve => setTimeout(() => resolve(false), 30))]);
    if (result) { pending.delete(id); return { completed: true, state: snapshot() }; }
    // Do not await a modal dialog from the agent tool loop: its next action supplies the answer.
    for (const [key, value] of pending) if (pending.size > 64 && value.status !== 'running') pending.delete(key);
    return { pending: true, operationId: id, state: snapshot() };
  }
  async function execute(name, input = {}, expectedDocumentId = null) {
    const args = object(input);
    if (expectedDocumentId && expectedDocumentId !== client.id && !['snapshot', 'inspect'].includes(name) && !(name === 'document.activate' && args.id === expectedDocumentId))
      throw new Error('The active drawing changed after the agent was bound. Return to the original drawing or start a new turn.');
    switch (name) {
      case 'snapshot': return snapshot();
      case 'inspect': return inspect();
      case 'command': case 'command.input':
        if (typeof args.text !== 'string' || args.text.length > 32000) throw new Error('Supply a CAD command string of at most 32,000 characters.');
        return deferred(name, () => host.submit(args.text));
      case 'command.cancel': host.cancel(args.clearSelection === true); break;
      case 'viewport.set': {
        const camera = { ...viewport.camera };
        for (const key of ['x', 'y']) if (args[key] !== undefined) camera[key] = finite(args[key], key);
        if (args.scale !== undefined) camera.scale = finite(args.scale, 'scale', 1e-8, 1e8);
        viewport.camera = camera; viewport.changed(); break;
      }
      case 'viewport.pan': {
        const dx = finite(args.dx ?? 0, 'dx'), dy = finite(args.dy ?? 0, 'dy');
        const x = finite(viewport.camera.x + dx, 'camera x'), y = finite(viewport.camera.y + dy, 'camera y');
        viewport.camera.x = x; viewport.camera.y = y; viewport.changed(); break;
      }
      case 'viewport.zoom':
        viewport.camera.scale = Math.max(1e-8, Math.min(1e8, viewport.camera.scale * finite(args.factor, 'factor', 1e-8, 1e8)));
        viewport.changed(); break;
      case 'viewport.fit': viewport.fit(); break;
      case 'palette.show':
        if (!['Properties', 'Layers', 'Blocks', 'Explorer', 'Workspaces', 'Graph', 'References', 'Modules', 'Health', 'Edit previews', 'Workflows', 'Diagnostics'].includes(args.name)) throw new Error('Unknown drawing palette.');
        host.showPalette(args.name); break;
      case 'ribbon.show':
        if (host.ribbonEntries ? !host.ribbonEntries().some(entry=>entry.key===args.name&&!entry.disabled) : !['Home', 'Insert', 'Annotate', 'Graph', 'View', 'Manage', 'Output'].includes(args.name) && !(args.name==='Block Editor'&&client.data?.blockEditor) && !client.data?.modules?.some(m=>m.enabled&&!client.data?.blockEditor&&`module:${m.id}`===args.name)) throw new Error('Unknown ribbon tab.');
        host.showRibbon(args.name); break;
      case 'settings.set': {
        const settings = { ...viewport.settings };
        for (const key of Object.keys(args)) if (![...keys, 'gridStep', 'snapModes', 'polarAngle', 'polarTolerance'].includes(key)) throw new Error(`Unknown drafting setting: ${key}`);
        for (const key of keys) if (args[key] !== undefined) {
          if (typeof args[key] !== 'boolean') throw new Error(`${key} must be a boolean.`);
          settings[key] = args[key];
        }
        if (args.gridStep !== undefined) settings.gridStep = finite(args.gridStep, 'gridStep', 1e-6, 1e9);
        if (args.polarAngle !== undefined) settings.polarAngle = finite(args.polarAngle, 'polarAngle', 1e-6, 90);
        if (args.polarTolerance !== undefined) settings.polarTolerance = finite(args.polarTolerance, 'polarTolerance', 0, 15);
        if (args.snapModes !== undefined) {
          if (!Number.isInteger(args.snapModes)) throw new Error('snapModes must be an integer bit mask.');
          settings.snapModes = finite(args.snapModes, 'snapModes', 0, 511);
        }
        if (args.ortho === true) settings.polar = false;
        else if (args.polar === true) settings.ortho = false;
        if (args.selectionCycling !== undefined) viewport.cancelSelectionCycle?.();
        Object.assign(viewport.settings, settings); host.persist(); host.modes(); viewport.invalidate(); break;
      }
      case 'document.activate':
        if (!host.documents().some(d => d.id === args.id)) throw new Error('The requested drawing is not open.');
        await host.activate(args.id); break;
      case 'document.create': await host.newDocument(args.demo === true); break;
      case 'layout.set':
        if (!client.state?.layouts.includes(args.name)) throw new Error('The requested layout does not exist.');
        host.cancel(false); host.setSnapshot(null); await host.edit('layout', { name: args.name }); break;
      case 'workspace.set': await host.switchWorkspace(String(args.id)); break;
      case 'snapshot.show':
        if (args.revision !== null && (!Number.isSafeInteger(args.revision) || !client.state?.snapshots.includes(args.revision))) throw new Error('The requested retained revision does not exist.');
        host.setSnapshot(args.revision); break;
      case 'layer.set':
        if (!client.data?.layers.some(layer => layer.name === args.name)) throw new Error('The requested layer does not exist.');
        await host.setLayer(args.name); break;
      case 'selection.set': {
        if (!Array.isArray(args.entities) || args.entities.length > 5000 || args.entities.some(id => typeof id !== 'string'))
          throw new Error('Supply an array of at most 5,000 source entity IDs.');
        await host.edit('select', { mode: 'entities', entities: [...new Set(args.entities)].map(value => ({ value })),
          extend: args.extend === true, remove: args.remove === true });
        break;
      }
      case 'cad.read': {
        if(readPostRoutes.has(args.path))return client.read(args.path,{body:{...object(args.body||{}),revision:client.state.revision}});
        if (!readRoutes.has(args.path)) throw new Error('Unsupported CAD read route. Discover the available routes first.');
        const query = new URLSearchParams({ revision: client.state.revision });
        if(['layer-state/details','layer-state/export'].includes(args.path)){
          if(typeof args.id!=='string'||!args.id||args.id.length>256)throw new Error('Choose a bounded saved layer-state identity.');
          query.set('id',args.id);
        }
        if(args.path==='attribute-definitions'&&args.block!==undefined){if(typeof args.block!=='string'||!args.block||args.block.length>1024)throw new Error('Choose a bounded block identity.');query.set('block',args.block);}
        if(args.path==='attribute-definitions'&&args.currentSpace!==undefined){if(typeof args.currentSpace!=='boolean')throw new Error('Current space must be a boolean.');query.set('currentSpace',String(args.currentSpace));}
        return client.read(`${args.path}?${query}`);
      }
      case 'cad.edit':
        if (!editRoutes.has(args.path)) throw new Error('Unsupported native CAD transaction route.');
        return host.edit(args.path, object(args.body || {}), { expectedKey: client.key });
      case 'ui.action':
        if (!knownAction(args.name)) throw new Error('Unknown Studio action.');
        return deferred(args.name, () => host.action(args.name));
      case 'ui.control': {
        const node = controls.get(args.id);
        if (!node || !node.isConnected || !actionable(node)) throw new Error('The control has changed. Inspect the UI again.');
        if (node.disabled) throw new Error('The requested control is disabled.');
        if (args.operation === 'click') {
          if (node.dataset.action && knownAction(node.dataset.action)) return deferred(node.dataset.action, () => host.action(node.dataset.action));
          if (node.dataset.command) return deferred(node.dataset.command, () => host.submit(node.dataset.command));
          node.click(); await Promise.resolve();
          return { dispatched: true, state: snapshot() };
        }
        if (args.operation !== 'set' || !['INPUT', 'SELECT', 'TEXTAREA'].includes(node.tagName)) throw new Error('Use click, or set on an input control.');
        if (node.type === 'checkbox') {
          if (typeof args.value !== 'boolean') throw new Error('Checkbox values must be booleans.');
          node.checked = args.value;
        } else {
          const value = String(args.value ?? '');
          if (value.length > 32000) throw new Error('Control values must be at most 32,000 characters.');
          if (node.tagName === 'SELECT' && ![...node.options].some(option => option.value === value && !option.disabled)) throw new Error('Choose an available option.');
          node.value = value;
        }
        node.dispatchEvent(new Event('input', { bubbles: true })); node.dispatchEvent(new Event('change', { bubbles: true }));
        if (args.submit === true) node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
        break;
      }
      case 'dialog.submit': {
        const dialog = root.getElementById('dialog');
        if (!dialog?.open || activeModal() !== dialog || !publicNode(dialog)) throw new Error('No active drawing dialog is open.');
        if (args.accept === true && !root.getElementById('dialog-form').reportValidity()) throw new Error('Complete the required dialog fields.');
        dialog.close(args.accept === true ? 'ok' : 'cancel'); await Promise.resolve(); break;
      }
      case 'refresh': await client.refresh(); await host.documentTabs(); viewport.requestFrame(true); break;
      default: throw new Error(`Unknown Studio UI action: ${name}`);
    }
    return snapshot();
  }
  const api = Object.freeze({ snapshot, inspect, execute, actions: studioUiActions });
  globalThis.shadowStudioAgentUi = api;
  return api;
}
