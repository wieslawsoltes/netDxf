import { test } from 'node:test';
import assert from 'node:assert/strict';
import { acpCapabilities, acpConfiguration, acpConfigurationCall, acpSessionIdentity, mergeAcpToolCall, createAcpTranscriptIds, agentSignInUrl, codexHistoryEntries, codexQuestionAnswer } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/external-agent-contracts.mjs';

test('ACP actions follow advertised capabilities rather than an agent brand or missing fields', () => {
  const absent = acpCapabilities({ agentInfo: { name: 'Gemini' } });
  for (const name of ['load', 'resume', 'list', 'close', 'delete', 'logout', 'httpMcp']) assert.equal(absent[name], false);
  const supported = acpCapabilities({ agentCapabilities: { loadSession: true, sessionCapabilities: { resume: {}, list: {}, close: {}, delete: null }, auth: { logout: {} }, mcpCapabilities: { http: false } } });
  assert.equal(supported.load, true); assert.equal(supported.resume, true); assert.equal(supported.close, true);
  assert.equal(supported.logout, true); assert.equal(supported.delete, false); assert.equal(supported.httpMcp, false);
});
test('only advertised agent-managed authentication is offered as a protocol login', () => {
  const auth = acpCapabilities({ authMethods: [{ id: 'legacy', name: 'Login' }, { id: 'managed', type: 'agent' }, { id: 'external', type: 'terminal' }, { id: 'future', type: '_custom' }] }).auth;
  assert.deepEqual(auth.map(a => a.protocolLogin), [true, true, false, false]);
  assert.equal(acpCapabilities().auth.length, 0);
  assert.equal(acpCapabilities(null).auth.length, 0);
});
test('session configuration supports advertised model, reasoning, mode, grouped choices and boolean controls', () => {
  const options = acpConfiguration({ configOptions: [
    { id: 'model', category: 'model', name: 'Model', type: 'select', currentValue: 'fast', options: [{ group: 'vendor', name: 'Provider', options: [{ value: 'fast', name: 'Fast' }, { value: 'deep', name: 'Deep' }] }] },
    { id: 'reasoning', category: 'thought_level', type: 'select', currentValue: 'high', options: [{ value: 'high', name: 'High' }] },
    { id: 'enabled', category: '_unknown', type: 'boolean', currentValue: true },
    { id: 'future', type: '_new_type' }
  ], modes: { currentModeId: 'old', availableModes: [{ id: 'old' }] } });
  assert.equal(options.length, 4); assert.equal(options[0].choices[1].group, 'Provider');
  assert.equal(options[2].supported, true); assert.equal(options[3].supported, false);
  assert.deepEqual(acpConfigurationCall('session-1', options[0], 'deep'), { method: 'session/set_config_option', parameters: { sessionId: 'session-1', configId: 'model', value: 'deep' } });
  assert.deepEqual(acpConfigurationCall('session-1', options[2], false).parameters, { sessionId: 'session-1', configId: 'enabled', type: 'boolean', value: false });
  assert.throws(() => acpConfigurationCall('session-1', options[0], 'invented'));
  assert.throws(() => acpConfigurationCall('session-1', options[2], 'true'));
  assert.throws(() => acpConfigurationCall('session-1', options[3], 'value'));
});
test('legacy modes are used only when configOptions are unavailable', () => {
  const mode = acpConfiguration({ modes: { currentModeId: 'ask', availableModes: [{ id: 'ask', name: 'Ask' }, { id: 'edit', name: 'Edit' }] } })[0];
  assert.equal(mode.currentValue, 'ask');
  assert.deepEqual(acpConfigurationCall('s', mode, 'edit'), { method: 'session/set_mode', parameters: { sessionId: 's', modeId: 'edit' } });
  assert.deepEqual(acpConfiguration(), []);
});
test('ACP load/resume responses may omit the existing session ID', () => {
  assert.equal(acpSessionIdentity({ configOptions: [] }, 'saved-session'), 'saved-session');
  assert.equal(acpSessionIdentity({ sessionId: 'new-session' }), 'new-session');
  assert.throws(() => acpSessionIdentity({}));
});
test('tool call patches retain earlier title, arguments and output for omitted/null fields', () => {
  const first = mergeAcpToolCall({}, { sessionUpdate: 'tool_call', toolCallId: 'tool-1', title: 'Edit layer', rawInput: { id: 'layer' }, rawOutput: 'old', content: ['old'] });
  const updated = mergeAcpToolCall(first, { sessionUpdate: 'tool_call_update', toolCallId: 'tool-1', status: 'completed', title: null, rawInput: null, rawOutput: false, content: [] });
  assert.equal(updated.title, 'Edit layer'); assert.deepEqual(updated.rawInput, { id: 'layer' });
  assert.equal(updated.rawOutput, false); assert.deepEqual(updated.content, []); assert.equal(updated.status, 'completed');
  assert.equal('sessionUpdate' in updated, false);
});
test('loaded history keeps distinct message groups instead of collapsing every assistant message into one null turn', () => {
  const ids = createAcpTranscriptIds(), replay = { sessionId: 'saved', replay: true };
  const user1 = ids.next('user', replay), assistant1 = ids.next('assistant', replay);
  assert.equal(ids.next('assistant', replay), assistant1);
  const user2 = ids.next('user', replay), assistant2 = ids.next('assistant', replay);
  assert.notEqual(user1, user2); assert.notEqual(assistant1, assistant2);
  ids.boundary(); assert.notEqual(ids.next('assistant', replay), assistant2);
  assert.equal(ids.next('assistant', { sessionId: 'saved', turnId: 'live' }), 'acp-saved-live-assistant');
  ids.boundary();
  const afterTool = ids.next('assistant', { sessionId: 'saved', turnId: 'live' });
  assert.notEqual(afterTool, 'acp-saved-live-assistant');
  assert.equal(ids.next('assistant', { sessionId: 'saved', turnId: 'live' }), afterTool);
});
test('sign-in links accept HTTPS and reject executable schemes and embedded credentials', () => {
  assert.equal(agentSignInUrl('https://example.invalid/login?state=opaque'), 'https://example.invalid/login?state=opaque');
  for (const url of ['javascript:alert(1)', 'data:text/html,test', 'http://example.invalid/login', 'https://user:secret@example.invalid/']) assert.throws(() => agentSignInUrl(url));
});
test('Codex full history pages retain ordered transcript IDs, text and tool receipts without rendering reasoning', () => {
  const entries = codexHistoryEntries({ data: [{ id: 'turn-1', items: [
    { id: 'u1', type: 'userMessage', content: [{ type: 'text', text: 'Draw a circle' }, { type: 'image', url: 'opaque' }] },
    { id: 'r1', type: 'reasoning', text: 'private reasoning' },
    { id: 'tool1', type: 'mcpToolCall', status: 'completed' },
    { id: 'a1', type: 'agentMessage', text: 'Created.' }
  ] }, { id: 'turn-2', items: [{ id: 'a2', type: 'agentMessage', text: 'Next.' }] }] });
  assert.deepEqual(entries.map(e => e.id), ['u1', 'tool1', 'a1', 'a2']);
  assert.equal(entries[0].text, 'Draw a circle\n[image]'); assert.equal(entries[1].value.status, 'completed');
  assert.equal(entries[2].role, 'assistant'); assert.deepEqual(codexHistoryEntries({ data: [{}] }), []);
});
test('Codex questions expose freeform answers only when advertised and preserve selected answers', () => {
  const question = { options: [{ label: 'One' }, { label: 'Two' }], isOther: true };
  assert.equal(codexQuestionAnswer(question, 'Two', 'ignored'), 'Two');
  assert.equal(codexQuestionAnswer(question, 'sentinel', ' My response ', true), 'My response');
  assert.throws(() => codexQuestionAnswer({ ...question, isOther: false }, 'sentinel', 'Other', true));
  assert.throws(() => codexQuestionAnswer(question, 'sentinel', '   ', true));
  assert.throws(() => codexQuestionAnswer(question, 'invented'));
  assert.equal(codexQuestionAnswer({}, 'Plain answer'), 'Plain answer');
});
