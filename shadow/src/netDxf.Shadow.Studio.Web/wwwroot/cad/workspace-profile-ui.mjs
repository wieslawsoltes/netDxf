import { el } from './ui.mjs';
import { WorkspaceProfiles, workspacePresets } from './workspace-profiles.mjs';

/** Host-independent UI layout manager. Drawing workspaces remain a separate feature. */
export function workspaceProfileTools({ paletteIds, storage, capture, restore, dialog, report }) {
  const store = new WorkspaceProfiles({ paletteIds, storage });
  return async function show() {
    const saved = store.read(), presets = workspacePresets(paletteIds);
    const choices = [...presets.map(entry => ({ ...entry, preset: true })), ...saved.profiles];
    const select = el('select', { 'aria-label': 'UI workspace' }, choices.map((entry, index) =>
      el('option', { value: String(index), text: `${entry.name}${entry.preset ? ' (preset)' : ''}` })));
    const name = el('input', { 'aria-label': 'Workspace name', maxLength: 80, placeholder: 'My drawing layout' });
    const status = el('p', { class: 'hint', 'aria-live': 'polite' });
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
    const content = [
      el('p', { text: 'Save and restore palette groups, sizes and transparency, ribbon display and customization, Quick Access commands and command line visibility. UI layouts are stored in this browser.' }),
      el('label', { class: 'property-row' }, [el('span', { text: 'Restore layout' }), select]),
      el('label', { class: 'property-row' }, [el('span', { text: 'Name' }), name]),
      el('div', { class: 'toolbar' }, [
        button('Apply layout', () => { const current = chosen(); if (current) { restore(current.layout); status.textContent = `${current.name} applied.`; } }),
        button('Save current as', () => { store.change({ type: 'save', name: name.value, layout: capture() }); const target = name.value.trim(); refresh(target); status.textContent = `${target} saved.`; }),
        button('Update saved workspace', () => { const current = chosen(); if (!current || current.preset) return; store.change({ type: 'save', name: current.name, replace: true, layout: capture() }); refresh(current.name); status.textContent = `${current.name} updated.`; }),
        button('Rename workspace', () => { const current = chosen(); if (!current || current.preset) return; store.change({ type: 'rename', name: current.name, next: name.value }); refresh(name.value.trim()); status.textContent = 'Workspace renamed.'; }),
        button('Delete workspace', () => { const current = chosen(); if (!current || current.preset) return; store.change({ type: 'delete', name: current.name }); refresh(); status.textContent = `${current.name} deleted.`; })
      ]),
      status,
      el('p', { class: 'hint', text: 'Saved UI layouts do not change drawings, discipline enablement, drafting aids or agent settings. Use Immutable workspaces for drawing branches.' })
    ];
    select.value = '0'; select.addEventListener('change', changed); changed();
    await dialog('UI workspaces', content, 'Close');
  };
}
