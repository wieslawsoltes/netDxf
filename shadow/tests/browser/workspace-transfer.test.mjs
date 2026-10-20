import test from 'node:test';
import assert from 'node:assert/strict';
import { exportWorkspace, parseWorkspace, describeWorkspace, workspaceTransferLimit } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-transfer.mjs';
import { workspaceProfileTools } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profile-ui.mjs';
import { installDom, byText, byLabel, gate } from './ui-fixture.mjs';

const ids = ['Properties', 'Graph', 'References'];
const entry = () => ({ name: 'Analysis', layout: {
  tab: 'Graph', chrome: { ribbonMode: 'titles', commandLineVisible: false },
  palettes: { version: 3, panels: { Graph: { open: true, mode: 'float', opacity: 70 } } },
  ribbonCustomization: { version: 1, customTabs: [{ id: 'user:qa', name: 'QA' }],
    customPanels: [{ id: 'user:inspect', tab: 'user:qa', name: 'Inspect', commands: ['action:reference-graph'] }] },
  documentId: 'private-drawing', agent: { credential: 'must-not-copy' }
} });

test('portable workspace roundtrip keeps UI configuration and removes unrelated data', () => {
  const text = exportWorkspace(entry(), ids);
  assert(!text.includes('private-drawing')); assert(!text.includes('must-not-copy'));
  const value = parseWorkspace(text, ids);
  assert.equal(value.name, 'Analysis'); assert.equal(value.layout.tab, 'Graph');
  assert.equal(value.layout.palettes.panels.Graph.opacity, 70);
  assert.equal(value.layout.chrome.commandLineVisible, false);
  assert.deepEqual(value.layout.ribbonCustomization.customPanels[0].commands, ['action:reference-graph']);
  assert.match(describeWorkspace(value), /1 floating/);
  assert.equal(exportWorkspace(value, ids), text);
  const otherHost = parseWorkspace(text, ['Properties']);
  assert(!Object.hasOwn(otherHost.layout.palettes.panels, 'Graph'));
});

test('workspace transfer rejects unknown envelopes and invalid field versions before storage', () => {
  for (const value of [null, [], {}, { version: 2, format: 'shadow-studio-ui-workspace' }])
    assert.throws(() => parseWorkspace(JSON.stringify(value), ids), /workspace file/);
  assert.throws(() => parseWorkspace('{', ids), /valid JSON/);
  assert.throws(() => exportWorkspace({ ...entry(), name: 'bad\nname' }, ids), /printable/);
  assert.throws(() => exportWorkspace({ ...entry(), layout: [] }, ids), /UI layout/);
  for (const layout of [{ palettes: { version: 99 } }, { chrome: [] }, { quickAccess: { version: 2 } }, { ribbonCustomization: { version: 0 } }])
    assert.throws(() => exportWorkspace({ name: 'Invalid', layout }, ids));
  assert.throws(() => parseWorkspace(' '.repeat(workspaceTransferLimit + 1), ids), /transfer limit/);
  assert.throws(() => parseWorkspace('é'.repeat(workspaceTransferLimit / 2 + 1), ids), /transfer limit/);
});

test('workspace import previews then saves without activation, rejects conflicts and ignores stale reads', async t => {
  const root = installDom(t), closed = gate(), errors = [], applied = [], downloads = [];
  let stored = null;
  const show = workspaceProfileTools({ paletteIds: ids, storage: { getItem: () => stored, setItem: (_, text) => { stored = text; } },
    capture: () => entry().layout, restore: value => applied.push(value), report: error => errors.push(error),
    download: (...args) => downloads.push(args), dialog: (_, content) => { root.replaceChildren(...content); return closed.promise; } });
  const showing = show(), file = byLabel(root, 'Import UI workspace');
  const text = exportWorkspace(entry(), ids);
  file.files = [{ size: text.length, text: async () => text }];
  await file.dispatch('change');
  assert.equal(stored, null); assert.equal(applied.length, 0);
  assert.equal(byLabel(root, 'Imported workspace name').value, 'Analysis');
  await byText(root, 'Save imported workspace').click();
  const initial = stored;
  assert.equal(JSON.parse(stored).profiles[0].name, 'Analysis'); assert.equal(applied.length, 0);
  await byText(root, 'Save imported workspace').click();
  assert.equal(stored, initial); assert.match(errors.at(-1).message, /already exists/);
  byLabel(root, 'Replace matching saved workspace').checked = true;
  await byText(root, 'Save imported workspace').click();
  assert.equal(JSON.parse(stored).profiles.length, 1);
  await byText(root, 'Export selected workspace').click();
  assert.equal(parseWorkspace(downloads[0][1], ids).name, 'Analysis');
  await byText(root, 'Apply layout').click(); assert.equal(applied.length, 1);
  const slow = gate();
  file.files = [{ size: 1, text: () => slow.promise }];
  const pending = file.dispatch('change');
  file.files = [{ size: 3, text: async () => 'bad' }];
  await file.dispatch('change');
  slow.resolve(text); await pending;
  assert(byText(root, 'Save imported workspace').disabled);
  const last = gate(); file.files = [{ size: 1, text: () => last.promise }];
  const finalRead = file.dispatch('change'); closed.resolve(); await showing;
  last.resolve(text); await finalRead;
  assert(byText(root, 'Save imported workspace').disabled); assert.equal(stored, initial);
});
