import { el } from './ui.mjs';
import { WorkspaceProfiles, workspacePresets } from './workspace-profiles.mjs';
import { parseWorkspace, exportWorkspace, describeWorkspace, workspaceTransferLimit } from './workspace-transfer.mjs';

/** Host-independent UI layout manager. Drawing workspaces remain a separate feature. */
export function workspaceProfileTools({ paletteIds, storage, capture, restore, dialog, report, download, onApplied = () => {}, onChanged = () => {} }) {
  const store = new WorkspaceProfiles({ paletteIds, storage });
  const list = () => [...workspacePresets(paletteIds).map(entry => ({ ...entry, preset: true })), ...store.read().profiles];
  const apply = identity => {
    const current = list().find(entry => typeof identity === 'string' ? entry.name === identity : entry.name === identity?.name && !!entry.preset === !!identity.preset);
    if (!current) throw new Error('The UI workspace no longer exists.');
    restore(current.layout); onApplied(current.name, !!current.preset); return current.layout;
  };
  const show = async function show() {
    let alive = true, generation = 0, imported = null;
    const saved = store.read(), presets = workspacePresets(paletteIds);
    const choices = [...presets.map(entry => ({ ...entry, preset: true })), ...saved.profiles];
    const select = el('select', { 'aria-label': 'UI workspace' }, choices.map((entry, index) =>
      el('option', { value: String(index), text: `${entry.name}${entry.preset ? ' (preset)' : ''}` })));
    const name = el('input', { 'aria-label': 'Workspace name', maxLength: 80, placeholder: 'My drawing layout' });
    const status = el('p', { class: 'hint', 'aria-live': 'polite' });
    const importStatus = el('p', { class: 'hint', role: 'status' });
    const file = el('input', { type: 'file', accept: '.json,application/json', 'aria-label': 'Import UI workspace' });
    const importName = el('input', { 'aria-label': 'Imported workspace name', maxLength: 80 });
    const replace = el('input', { type: 'checkbox', 'aria-label': 'Replace matching saved workspace', checked: false });
    const run = action => {
      try { action(); } catch (error) { status.textContent = error.message; report?.(error); }
    };
    const chosen = () => choices[Number(select.value)];
    const buttons = [];
    const button = (label, action) => {
      const node = el('button', { type: 'button', text: label, onclick: () => run(action) }); buttons.push(node); return node;
    };
    const refresh = target => {
      choices.splice(0, choices.length, ...presets.map(entry => ({ ...entry, preset: true })), ...store.read().profiles);
      select.replaceChildren(...choices.map((entry, index) => el('option', { value: String(index), text: `${entry.name}${entry.preset ? ' (preset)' : ''}` })));
      select.value = String(Math.max(0, choices.findIndex(entry => !entry.preset && entry.name === target)));
      changed();
    };
    const changed = () => {
      const current = chosen();
      for (const index of [2, 3, 4]) buttons[index].disabled = !current || current.preset === true;
    };
    const importButton = el('button', { type: 'button', text: 'Save imported workspace', disabled: true, onclick: () => run(() => {
      if (!imported || !alive) return;
      store.change({ type: 'save', name: importName.value, replace: replace.checked, layout: imported.layout });
      refresh(importName.value.trim());
      status.textContent = `${importName.value.trim()} imported. Apply layout to use it.`;
    }) });
    file.addEventListener('change', async () => {
      const current = ++generation, selected = file.files?.[0];
      imported = null; importButton.disabled = true; importStatus.textContent = ''; replace.checked = false;
      if (!selected) return;
      try {
        if (selected.size > workspaceTransferLimit) throw new Error('UI workspace exceeds the 500 KiB transfer limit.');
        const next = parseWorkspace(await selected.text(), paletteIds);
        if (!alive || current !== generation) return;
        imported = next; importName.value = next.name; importButton.disabled = false;
        importStatus.textContent = describeWorkspace(next) + '. Only supported UI layout fields are imported; positions are fitted to this screen. Save imports the layout without activating it.';
      } catch (error) { if (alive && current === generation) importStatus.textContent = error.message; }
    });
    const content = [
      el('p', { text: 'Save and restore palette groups, sizes and transparency, ribbon display and customization, Quick Access commands and command line visibility. Saved layouts stay in this browser; export a Studio JSON file to transfer them.' }),
      el('label', { class: 'property-row' }, [el('span', { text: 'Restore layout' }), select]),
      el('label', { class: 'property-row' }, [el('span', { text: 'Name' }), name]),
      el('div', { class: 'toolbar' }, [
        button('Apply layout', () => { const current = chosen(); if (current) { apply(current); status.textContent = `${current.name} applied.`; } }),
        button('Save current as', () => { store.change({ type: 'save', name: name.value, layout: capture() }); const target = name.value.trim(); refresh(target); onApplied(target, false); status.textContent = `${target} saved.`; }),
        button('Update saved workspace', () => { const current = chosen(); if (!current || current.preset) return; store.change({ type: 'save', name: current.name, replace: true, layout: capture() }); refresh(current.name); status.textContent = `${current.name} updated.`; }),
        button('Rename workspace', () => { const current = chosen(); if (!current || current.preset) return; store.change({ type: 'rename', name: current.name, next: name.value }); onChanged({type:'rename',name:current.name,next:name.value.trim()});refresh(name.value.trim()); status.textContent = 'Workspace renamed.'; }),
        button('Delete workspace', () => { const current = chosen(); if (!current || current.preset) return; store.change({ type: 'delete', name: current.name });onChanged({type:'delete',name:current.name});refresh(); status.textContent = `${current.name} deleted.`; })
      ]),
      status,
      el('details', {}, [
        el('summary', { text: 'Transfer UI workspace' }),
        el('button', { type: 'button', text: 'Export selected workspace', disabled: !download, onclick: () => run(() => {
          const current = chosen(); if (!current || !download) return;
          download('shadow-studio-workspace.json', exportWorkspace(current, paletteIds), 'application/json');
          status.textContent = `${current.name} exported.`;
        }) }),
        el('label', { class: 'property-row' }, [el('span', { text: 'Studio JSON file' }), file]),
        importStatus,
        el('label', { class: 'property-row' }, [el('span', { text: 'Save as' }), importName]),
        el('label', { class: 'property-row' }, [replace, el('span', { text: 'Replace a saved workspace with the same name' })]),
        importButton
      ]),
      el('p', { class: 'hint', text: 'Saved UI layouts do not change drawings, discipline enablement, drafting aids or agent settings. Use Immutable workspaces for drawing branches.' })
    ];
    select.value = '0'; select.addEventListener('change', changed); changed();
    try { await dialog('UI workspaces', content, 'Close'); }
    finally { alive = false; generation++; }
  };
  return Object.assign(show, { list, apply });
}
