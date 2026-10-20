import { el, group, icon } from './ui.mjs';
import { commandAvailability, resolveCommand, searchCommands } from './command-catalog.mjs';
import { ribbonPreferences, customizedRibbonTabs, customizedRibbonPanels, changeRibbonPreferences,
  parseRibbonCustomization, protectedRibbonPanel, ribbonLimits } from './ribbon-customization.mjs';

const button = (text, action, attributes = {}) => el('button', { type: 'button', text, ...attributes,
  onclick: event => { event.preventDefault(); event.stopPropagation(); action(); } });
const row = (text, input) => el('label', { class: 'form-row' }, [el('span', { text }), input]);
const choices = (label, entries, current) => {
  const select = el('select', { 'aria-label': label }, entries.map(entry => el('option', { value: entry.id, text: entry.name })));
  select.value = entries.some(entry => entry.id === current) ? current : entries[0]?.id || ''; return select;
};
let serial = 0;
const newId = () => `user:${Date.now().toString(36)}-${(++serial).toString(36)}`;

/** Custom commands always resolve current registration and availability immediately before dispatch. */
export function renderCustomizedRibbonGroups(store, tab, rawGroups, callbacks) {
  const entries = rawGroups.map(node => ({ id: node.dataset.ribbonPanel || node.dataset.panel, name: node.dataset.panel, node }));
  return store.panels(tab, entries).map(panel => {
    if (panel.node) { panel.node.dataset.ribbonPanel = panel.id; return panel.node; }
    const commands = panel.commands.map(id => {
      const entry = callbacks.catalog().find(command => command.id === id);
      const control = button('', () => {
        if (control.pending) return;
        control.pending = true;
        Promise.resolve().then(() => callbacks.execute(resolveCommand(id, callbacks.catalog(), callbacks.state())))
          .catch(callbacks.report).finally(() => { control.pending = false; refreshCustomRibbonCommands(node, callbacks); });
      }, { class: 'tool', 'data-ribbon-command-id': id });
      control.append(icon(entry?.target || 'settings'), el('span', { text: entry?.label || `Unavailable (${id})` }));
      return control;
    });
    const node = group(panel.name, commands.length ? commands : [el('span', { class: 'hint', text: 'Add commands in Customize ribbon.' })]);
    node.dataset.ribbonPanel = panel.id;
    refreshCustomRibbonCommands(node, callbacks); return node;
  });
}
export function refreshCustomRibbonCommands(root, { catalog, state }) {
  const entries = catalog(), current = state();
  for (const control of root.querySelectorAll('[data-ribbon-command-id]')) {
    const entry = entries.find(command => command.id === control.getAttribute('data-ribbon-command-id'));
    const reason = commandAvailability(entry, current);
    control.disabled = !!reason || !!control.pending;
    control.title = `${entry?.label || 'Unavailable command'}${reason ? ` — ${reason}` : ''}`;
  }
}

/** An editor works on a detached draft; only Apply commits preferences, including reset/import. */
export function ribbonCustomizationTools({ store, entries, panels, catalog, dialog, download, report = () => {} }) {
  async function customize() {
    let draft = store.snapshot(), selectedTab = '', selectedPanel = '', commandIndex = 0;
    const origin = document.activeElement, body = el('div', { class: 'ribbon-customization' });
    const status = el('p', { role: 'status', 'aria-live': 'polite', class: 'ribbon-customization-status' });
    const execute = (action, focusLabel) => {
      try {
        draft = changeRibbonPreferences(draft, action, { entries: entries(), panels: panels(action.tab || selectedTab), catalog: catalog() });
        status.textContent = 'Draft changed. Apply saves; Cancel keeps the current ribbon.';
        render(); if (focusLabel) [...body.querySelectorAll('select,input,button')].find(node => node.getAttribute('aria-label') === focusLabel)?.focus();
      } catch (error) { status.textContent = error.message; }
    };
    function render() {
      const tabEntries = customizedRibbonTabs(draft, entries(), { includeHidden: true });
      const tabs = choices('Customize ribbon tab', tabEntries.map(tab => ({ id: tab.key, name: `${tab.name}${tab.visible ? '' : ' (hidden)'}` })), selectedTab);
      selectedTab = tabs.value;
      tabs.addEventListener('change', () => { selectedTab = tabs.value; selectedPanel = ''; commandIndex = 0; render(); });
      const currentTab = tabEntries.find(tab => tab.key === selectedTab);
      const visibleTab = el('input', { type: 'checkbox', checked: currentTab?.visible, disabled: ['Home', 'Block Editor'].includes(selectedTab), 'aria-label': 'Show ribbon tab',
        onchange: event => execute({ type: 'tab-visible', id: selectedTab, visible: event.target.checked }, 'Show ribbon tab') });
      const tabName = el('input', { 'aria-label': 'Custom tab name', maxlength: 80, value: currentTab?.custom ? currentTab.name : '' });
      const panelEntries = customizedRibbonPanels(draft, selectedTab, panels(selectedTab), { includeHidden: true });
      const panelSelect = choices('Customize ribbon panel', panelEntries.map(panel => ({ id: panel.id, name: `${panel.name}${panel.visible ? '' : ' (hidden)'}` })), selectedPanel);
      selectedPanel = panelSelect.value;
      panelSelect.addEventListener('change', () => { selectedPanel = panelSelect.value; commandIndex = 0; render(); });
      const currentPanel = panelEntries.find(panel => panel.id === selectedPanel);
      const custom = draft.customPanels.find(panel => panel.id === selectedPanel);
      const visiblePanel = el('input', { type: 'checkbox', checked: currentPanel?.visible, 'aria-label': 'Show ribbon panel',
        disabled: !currentPanel || protectedRibbonPanel(selectedTab, selectedPanel),
        onchange: event => execute({ type: 'panel-visible', tab: selectedTab, id: selectedPanel, visible: event.target.checked }, 'Show ribbon panel') });
      const panelName = el('input', { 'aria-label': 'Custom panel name', maxlength: 80, value: custom?.name || '' });
      const editPanel = commands => execute({ type: 'custom-panel', id: custom.id, tab: custom.tab, name: custom.name, commands }, 'Custom panel commands');
      const commands = choices('Custom panel commands', (custom?.commands || []).map((id, index) => ({ id: String(index), name: catalog().find(entry => entry.id === id)?.label || `Unavailable (${id})` })), String(commandIndex));
      commands.disabled = !custom; commandIndex = Number(commands.value || 0);
      commands.addEventListener('change', () => { commandIndex = Number(commands.value); });
      const search = el('input', { type: 'search', 'aria-label': 'Find ribbon command', maxlength: 256, placeholder: 'Search registered commands', disabled: !custom });
      const found = el('select', { 'aria-label': 'Registered ribbon command', disabled: !custom });
      const searchResults = () => found.replaceChildren(...searchCommands(catalog(), search.value, 200).map(entry => el('option', { value: entry.id, text: `${entry.label} · ${entry.category}` })));
      search.addEventListener('input', searchResults); searchResults();
      const reorder = offset => {
        const ids = [...custom.commands], next = commandIndex + offset;
        if (next < 0 || next >= ids.length) return;
        [ids[commandIndex], ids[next]] = [ids[next], ids[commandIndex]]; commandIndex = next; editPanel(ids);
      };
      const file = el('input', { type: 'file', accept: 'application/json,.json', 'aria-label': 'Import ribbon customization' });
      file.addEventListener('change', async () => {
        try {
          const selected = file.files?.[0]; if (!selected) return;
          if (selected.size > ribbonLimits.bytes) throw new Error('Ribbon customization JSON exceeds 128 KiB.');
          draft = parseRibbonCustomization(await selected.text()); selectedTab = ''; selectedPanel = ''; render();
          status.textContent = 'Imported into the draft. Unregistered commands stay disabled. Apply to save.';
        } catch (error) { status.textContent = error.message; }
      });
      body.replaceChildren(el('p', { text: 'Choose visible tabs and panels, reorder them, or build custom panels from registered commands. Home and block save/discard controls remain available. This is Studio JSON customization, not CUIx file compatibility.' }),
        el('section', {}, [el('h3', { text: 'Tabs' }), row('Tab', tabs), row('Visible', visibleTab), el('div', { class: 'toolbar' }, [
          button('Move tab earlier', () => execute({ type: 'tab-move', id: selectedTab, offset: -1 }, 'Customize ribbon tab')),
          button('Move tab later', () => execute({ type: 'tab-move', id: selectedTab, offset: 1 }, 'Customize ribbon tab'))]),
        row('Custom tab name', tabName), el('div', { class: 'toolbar' }, [
          button('Add custom tab', () => { const id = newId(); selectedTab = id; execute({ type: 'custom-tab', id, name: tabName.value }, 'Customize ribbon tab'); }),
          button('Rename custom tab', () => execute({ type: 'custom-tab', id: selectedTab, name: tabName.value }), { disabled: !currentTab?.custom }),
          button('Delete custom tab', () => execute({ type: 'delete-tab', id: selectedTab }), { disabled: !currentTab?.custom })])]),
        el('section', {}, [el('h3', { text: 'Panels' }), row('Panel', panelSelect), row('Visible', visiblePanel), el('div', { class: 'toolbar' }, [
          button('Move panel earlier', () => execute({ type: 'panel-move', tab: selectedTab, id: selectedPanel, offset: -1 }, 'Customize ribbon panel'), { disabled: !currentPanel }),
          button('Move panel later', () => execute({ type: 'panel-move', tab: selectedTab, id: selectedPanel, offset: 1 }, 'Customize ribbon panel'), { disabled: !currentPanel })]),
        row('Custom panel name', panelName), el('div', { class: 'toolbar' }, [
          button('Add custom panel', () => { const id = newId(); selectedPanel = id; execute({ type: 'custom-panel', id, tab: selectedTab, name: panelName.value, commands: [] }, 'Customize ribbon panel'); }),
          button('Rename custom panel', () => execute({ type: 'custom-panel', id: selectedPanel, tab: selectedTab, name: panelName.value, commands: custom.commands }), { disabled: !custom }),
          button('Delete custom panel', () => execute({ type: 'delete-panel', id: selectedPanel }), { disabled: !custom })]),
        row('Commands', commands), el('div', { class: 'toolbar' }, [
          button('Move command earlier', () => reorder(-1), { disabled: !custom?.commands.length }),
          button('Move command later', () => reorder(1), { disabled: !custom?.commands.length }),
          button('Remove panel command', () => editPanel(custom.commands.filter((_, index) => index !== commandIndex)), { disabled: !custom?.commands.length })]),
        row('Search', search), row('Registered command', found),
        button('Add panel command', () => editPanel([...custom.commands, found.value]), { disabled: !custom })]),
        el('section', {}, [el('h3', { text: 'Portable preferences' }), row('Import JSON', file), el('div', { class: 'toolbar' }, [
          button('Export ribbon JSON', () => download?.('ribbon-customization.json', JSON.stringify(draft, null, 2), 'application/json'), { disabled: !download }),
          button('Reset ribbon draft', () => { draft = ribbonPreferences(null); selectedTab = ''; selectedPanel = ''; render(); status.textContent = 'Defaults prepared. Apply to reset the ribbon; Cancel keeps it.'; })])]), status);
    }
    render();
    try { if (await dialog('Customize ribbon', [body], 'Apply')) store.restore(draft); }
    catch (error) { report(error); }
    finally { if (origin?.isConnected) origin.focus({ preventScroll: true }); }
  }
  return { customize };
}
