import { node, button, code } from './render.mjs';
import { BrowserFolder, importPlan, lineDiff, parseWorkspaceBundle, readSelectedFiles, readWorkspaceZip, workspaceBundle, workspaceZip } from './file-transfer.mjs';

export function saveDownload(content, name, type = 'application/json') {
  const url = URL.createObjectURL(new Blob([content], { type })), link = node('a', { href: url, download: name });
  document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function renderFileDiff(before, after) {
  const diff = lineDiff(before, after), element = node('div', { class: 'agent-file-diff', 'aria-label': 'File changes' });
  if (!diff.lines.length) return node('p', { class: 'agent-muted', text: 'No content changes.' });
  element.append(node('div', { class: 'agent-diff-caption', text: `+${diff.added} lines · −${diff.removed} lines${diff.omitted ? ` · ${diff.omitted} lines outside this preview` : ''}` }));
  const content = node('pre');
  for (const line of diff.lines) content.append(node('div', { class: `agent-diff-line ${line.kind}` }, [node('span', { class: 'agent-diff-marker', text: line.kind === 'added' ? '+' : line.kind === 'removed' ? '−' : ' ' }),
    node('span', { class: 'agent-diff-number', text: line.line }), node('span', { text: line.text || ' ' })]));
  element.append(content); return element;
}

/** File import and folder publishing are reviewable operations; async work never replays a write. */
export function mountBrowserFileTransfer({ client, host, assertFileClean, onImported, notice }) {
  let folder = null, pending = null, busy = false, generation = 0;
  const controls = [], status = node('p', { class: 'agent-muted', role: 'status', text: 'UTF-8 text · 512 files · 16 MiB. Generated directories and binary files are skipped.' });
  const review = node('section', { class: 'agent-import-review', hidden: true, 'aria-label': 'Review workspace file changes' });
  const heading = node('h3'), summary = node('p'), rowsNode = node('div', { class: 'agent-import-rows' });
  const applyButton = button('Apply reviewed changes', () => run(apply), { icon: 'check', class: 'agent-primary' });
  const discardButton = button('Discard review', () => { pending = null; review.hidden = true; });
  review.append(heading, summary, rowsNode, node('div', { class: 'agent-row' }, [applyButton, discardButton]));
  const run = callback => Promise.resolve().then(callback).catch(error => notice(error.message || String(error), 'error'));
  function ensureReady() { client.ensureIdle(); assertFileClean(); if (busy) throw new Error('Wait for the current browser file operation to finish.'); }
  async function operation(callback) {
    ensureReady(); busy = true; update(); const version = ++generation;
    try { return await callback(version); } finally { busy = false; update(); }
  }
  function check(version) { if (version !== generation) throw new Error('The file operation was cancelled.'); }
  function add(label, callback, options = {}) { const control = button(label, callback, options); controls.push(control); return control; }
  const input = node('input', { type: 'file', multiple: '', hidden: true, 'aria-label': 'Import workspace text files' });
  const directoryInput = node('input', { type: 'file', multiple: '', webkitdirectory: '', hidden: true, 'aria-label': 'Import workspace folder' });
  const archiveInput = node('input', { type: 'file', accept: '.zip,.json,application/zip,application/json', hidden: true, 'aria-label': 'Import workspace ZIP or bundle' });
  for (const [control, directory] of [[input, false], [directoryInput, true]]) control.addEventListener('change', () => {
    const selected = Array.from(control.files || []); control.value = ''; if (!selected.length) return;
    run(() => operation(async version => { const imported = await readSelectedFiles(selected, { directory }); check(version); await prepareImport(imported, directory ? 'Folder import' : 'File import'); }));
  });
  archiveInput.addEventListener('change', () => {
    const selected = archiveInput.files?.[0]; archiveInput.value = ''; if (!selected) return;
    run(() => operation(async version => {
      if (selected.size > 20 * 1024 * 1024) throw new Error('Workspace archives must be smaller than 20 MiB.');
      const imported = /\.zip$/i.test(selected.name) ? await readWorkspaceZip(await selected.arrayBuffer()) : { files: parseWorkspaceBundle(await selected.text()), skipped: [] };
      check(version); await prepareImport(imported, selected.name);
    }));
  });
  const importRow = node('div', { class: 'agent-row' }, [add('Import files', () => input.click(), { icon: 'upload' }), add('Import folder', () => directoryInput.click(), { icon: 'folder' }), add('Import ZIP / bundle', () => archiveInput.click(), { icon: 'archive' })]);
  const exportRow = node('div', { class: 'agent-row' }, [add('Download ZIP', () => run(() => operation(async () => saveDownload(workspaceZip(await client.exportFiles()), 'shadow-workspace.zip', 'application/zip'))), { icon: 'download' }),
    add('Download bundle', () => run(() => operation(async () => saveDownload(workspaceBundle(await client.exportFiles()), 'shadow-workspace.json'))), { icon: 'download' })]);
  const folderStatus = node('p', { class: 'agent-muted', role: 'status', text: 'Folder access stays in this tab. Reconnect after reloading to read or write local files.' });
  const chooseFolder = add('Connect folder', () => {
    // Request the picker within the user gesture. File reads and writes use this
    // explicit browser handle and are never inferred from a filesystem path.
    try {
      ensureReady();
      const picker = globalThis.showDirectoryPicker({ mode: 'read', id: 'netdxf-agent-workspace' });
      run(() => operation(async version => {
        let handle; try { handle = await picker; } catch (error) { if (error.name === 'AbortError') return; throw error; }
        check(version); const candidate = new BrowserFolder(handle), result = await candidate.read(); check(version);
        folder = candidate; folderStatus.textContent = `Connected to ${folder.name}. Read changes before importing; review writes before saving back.`;
        await prepareImport(result, folder.name); update();
      }));
    } catch (error) { notice(error.message, 'error'); }
  }, { icon: 'folder' });
  chooseFolder.hidden = typeof globalThis.showDirectoryPicker !== 'function';
  const readFolder = add('Read folder changes', () => run(() => operation(async version => {
    const result = await folder.read(); check(version); await prepareImport(result, folder.name);
  })), { icon: 'refresh' });
  const writeFolder = add('Review folder writes', () => run(() => operation(async version => {
    const rows = await folder.planWrite(await client.exportFiles()); check(version);
    pending = { kind: 'folder', rows, label: folder.name, selected: new Set(rows.filter(row => !row.conflict).map(row => row.path)) };
    renderReview();
  })), { icon: 'download' });
  const disconnectFolder = add('Disconnect folder', () => { folder = null; if (pending?.kind === 'folder') { pending = null; review.hidden = true; } folderStatus.textContent = 'Folder disconnected. Workspace files remain in this browser.'; update(); });
  const details = node('details', { class: 'agent-file-transfer' }, [node('summary', { text: 'Project import, export & folder access' }), importRow, exportRow, status,
    node('div', { class: 'agent-row' }, [chooseFolder, readFolder, writeFolder, disconnectFolder]), folderStatus, input, directoryInput, archiveInput]);
  if (chooseFolder.hidden) folderStatus.textContent = 'Use Import folder and Download ZIP in this browser. Direct folder write access is available when the browser provides a folder picker.';
  host.append(details, review);
  function update() {
    const unavailable = busy || client.busy || client.transcript.running;
    for (const control of controls) control.disabled = unavailable;
    for (const control of [readFolder, writeFolder, disconnectFolder]) control.hidden = !folder;
    applyButton.disabled = unavailable || !pending?.selected.size; discardButton.disabled = busy;
  }
  async function prepareImport(imported, label) {
    const plan = importPlan(await client.exportFiles(), imported.files);
    pending = { ...plan, kind: 'workspace', label, selected: new Set(plan.changes.map(row => row.path)) };
    const skipped = imported.skipped || [];
    status.textContent = skipped.length ? `${skipped.length} binary or generated entries skipped. ${skipped.slice(0, 3).map(entry => entry.path).join(', ')}${skipped.length > 3 ? '…' : ''}` : `${imported.files.length} UTF-8 files read. Existing workspace paths not present in the import are retained.`;
    renderReview();
  }
  function renderReview() {
    if (!pending) { review.hidden = true; return; }
    const changed = pending.rows.filter(row => row.kind !== 'unchanged'), conflicts = changed.filter(row => row.conflict);
    heading.textContent = pending.kind === 'folder' ? `Write to ${pending.label}` : `Import ${pending.label}`;
    summary.textContent = pending.kind === 'folder' ? `${changed.length} changed files. ${conflicts.length ? `${conflicts.length} files changed outside this tab; inspect their diff and select them explicitly to replace their current contents. ` : ''}Each file is checked again before writing. Folder saves are atomic per file; a later failure reports already saved paths.`
      : `${pending.added} new · ${pending.modified} changed · ${pending.unchanged} unchanged. Select files below. The runtime validates all versions before applying the selected changes together.`;
    rowsNode.replaceChildren(...changed.map(row => {
      const checkbox = node('input', { type: 'checkbox', checked: pending.selected.has(row.path), 'aria-label': `Include ${row.path}` });
      checkbox.addEventListener('change', () => { checkbox.checked ? pending.selected.add(row.path) : pending.selected.delete(row.path); update(); });
      const detail = node('details', { class: `agent-import-file${row.conflict ? ' conflict' : ''}` }, [node('summary', {}, [node('span', { class: `agent-file-kind ${row.kind}`, text: row.conflict ? 'Conflict' : row.kind === 'added' ? 'New' : 'Changed' }), node('code', { text: row.path })])]);
      detail.addEventListener('toggle', () => { if (detail.open && !detail.dataset.rendered) { detail.dataset.rendered = 'true'; detail.append(renderFileDiff(row.before, row.content)); } });
      return node('div', { class: 'agent-import-file-row' }, [checkbox, detail]);
    }));
    if (!changed.length) rowsNode.append(node('p', { class: 'agent-muted', text: 'All selected file contents already match.' }));
    applyButton.querySelector('span').textContent = pending.kind === 'folder' ? 'Save selected files to folder' : 'Apply selected files';
    review.hidden = false; update();
  }
  async function apply() {
    ensureReady(); if (!pending?.selected.size) throw new Error('Select at least one file change.');
    const plan = pending, selected = plan.rows.filter(row => plan.selected.has(row.path));
    await operation(async version => {
      if (plan.kind === 'folder') {
        const lease = client.beginOperation('lifecycle');
        try {
          const current = new Map((await client.exportFiles()).map(file => [file.path, file]));
          for (const row of selected) {
            const file = current.get(row.path);
            if (!file || file.content !== row.content || file.version !== row.version) throw new Error(`The workspace file changed after review: ${row.path}. Review folder writes again.`);
          }
          await folder.write(selected); check(version);
          notice(`Saved ${selected.length} file${selected.length === 1 ? '' : 's'} to ${folder.name}.`);
        } finally { client.finishOperation(lease); }
      } else {
        await client.applyFiles(selected.map(({ path, content, expectedVersion }) => ({ path, content, expectedVersion }))); check(version);
        await onImported(); notice(`Imported ${selected.length} file${selected.length === 1 ? '' : 's'} into this browser workspace.`);
      }
      pending = null; review.hidden = true;
    });
  }
  client.addEventListener('busy', update); client.addEventListener('change', update); update();
  return Object.freeze({ reviewImport: imported => run(() => operation(() => prepareImport(imported, 'Dropped files'))),
    state: () => ({ folderConnected: !!folder, folderName: folder?.name || null, reviewing: !!pending }),
    dispose: () => { generation++; folder = null; pending = null; client.removeEventListener('busy', update); client.removeEventListener('change', update); } });
}
