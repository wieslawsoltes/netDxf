import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateReferenceShape, referenceKey, splitReferencePrompt } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/drawing-reference-contracts.mjs';
import { AgentTranscript } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/contracts.mjs';
import { AgentClient } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/client.mjs';

const ref = { documentId: 'document-guid', workspaceId: 'live', revision: 7, viewVersion: 2, objectId: 'root', handle: 'A1', type: 'netDxf.Entities.Insert', layout: 'Model', occurrenceKey: 'root/leaf', insertPath: ['root'], definitionObjectId: 'leaf' };
const context = text => `${text}\n\n<shadow-drawing-references-v1>\n${JSON.stringify([ref])}\n</shadow-drawing-references-v1>`;
test('reference identity distinguishes drawing, workspace and occurrence without relying on handle', () => {
  assert.notEqual(referenceKey(ref), referenceKey({ ...ref, documentId: 'other' }));
  assert.notEqual(referenceKey(ref), referenceKey({ ...ref, workspaceId: 'branch' }));
  assert.notEqual(referenceKey(ref), referenceKey({ ...ref, occurrenceKey: 'another/leaf' }));
  assert.equal(referenceKey(ref), referenceKey({ ...ref, revision: 8 }));
});
test('reference validation clones data and rejects oversized/duplicate/invalid identities', () => {
  const copy = validateReferenceShape([ref]); copy[0].insertPath.push('changed'); assert.deepEqual(ref.insertPath, ['root']);
  assert.throws(() => validateReferenceShape([ref, ref])); assert.throws(() => validateReferenceShape(Array(33).fill(ref)));
  assert.throws(() => validateReferenceShape([{ ...ref, revision: -1 }])); assert.throws(() => validateReferenceShape([{ ...ref, objectId: '\nwrong' }]));
});
test('external restored history decodes only a complete final reference declaration', () => {
  assert.deepEqual(splitReferencePrompt(context('Explain this INSERT')), { text: 'Explain this INSERT', references: [ref] });
  const malformed = 'Prompt\n\n<shadow-drawing-references-v1>\ninvalid\n</shadow-drawing-references-v1>';
  assert.deepEqual(splitReferencePrompt(malformed), { text: malformed, references: [] });
  assert.equal(splitReferencePrompt(context('Prompt') + ' unrelated text').references.length, 0);
});
test('primary transcript keeps attached refs on streaming events and checkpoint hydration', () => {
  const transcript = new AgentTranscript();
  transcript.accept({ sequence: 1, type: 'user', message: 'Explain selection', data: { references: [ref] } });
  assert.equal(transcript.items[0].references[0].objectId, 'root');
  transcript.hydrate([{ role: 'user', contents: [{ kind: 'text', text: context('Explain selection') }], displayText: 'Explain selection', references: [ref] }], 'session', 1);
  assert.equal(transcript.items[0].text, 'Explain selection'); assert.equal(transcript.items[0].references[0].definitionObjectId, 'leaf');
});
test('reference start dispatch binds active drawing and uses typed prompt without resetting conversation', async () => {
  const calls = [];
  const client = new AgentClient({ getDocument: () => 'route-key', invoke: async (method, ...args) => { calls.push([method, ...args]); return {}; }, delay: () => 1, clearDelay() {} });
  client.configured = true;
  await client.start('Explain this selected object', [ref]);
  assert.equal(calls[0][0], 'AgentSetDocument'); assert.equal(calls[0][1], 'route-key');
  assert.equal(calls[1][0], 'AgentStartPrompt'); assert.deepEqual(JSON.parse(calls[1][1]), { text: 'Explain this selected object', references: [ref] });
  assert(!calls.some(([method]) => method === 'AgentReset'));
  client.dispose();
});
