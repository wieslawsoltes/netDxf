import { test } from 'node:test';
import assert from 'node:assert/strict';
import { AgentTranscript, configuration, defaultConfiguration, preferences, preferenceRecord, restorePreferences } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/contracts.mjs';
import { AgentClient } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/client.mjs';
import { presentRun, recoveredRunSelection } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/run-state.mjs';

const state = (run, rest = {}) => ({ run, running: false, status: run?.phase || 'idle', items: [{ kind: 'user', text: 'Complete the task' }], ...rest });
const snapshot = (run, rest = {}) => ({ sessionId: 'session', events: [], running: false, status: run.phase, run, ...rest });

test('execution configuration supports unbounded runs with bounded optional turn and stall budgets', () => {
  const value = configuration(defaultConfiguration);
  assert.equal(value.mode, 'normal'); assert.equal(value.maxTurns, 0); assert.equal(value.maxNoProgressTurns, 8);
  assert.equal(configuration({ ...value, maxTurns: 10000, maxNoProgressTurns: 100 }).maxTurns, 10000);
  for (const maxTurns of [-1, 10001, 2.5, NaN]) assert.throws(() => configuration({ ...value, maxTurns }), /Turn budget/);
  for (const maxNoProgressTurns of [0, 1, 101, 2.5]) assert.throws(() => configuration({ ...value, maxNoProgressTurns }), /No-progress budget/);
});

test('modes and criteria remain separate from the explicit tool permission choice', () => {
  const plan = configuration({ ...defaultConfiguration, mode: 'Plan', permissionMode: 'fullAccess' });
  assert.equal(plan.mode, 'plan'); assert.equal(plan.permissionMode, 'fullAccess');
  const goal = configuration({ ...plan, mode: 'goal', successCriteria: '  Tests pass\nDrawing units are retained  ' });
  assert.equal(goal.successCriteria, 'Tests pass\nDrawing units are retained');
  assert.equal(configuration({ ...goal, successCriteria: '' }).successCriteria, '');
  assert.throws(() => configuration({ ...goal, successCriteria: 'x'.repeat(16001) }), /16,000/);
  assert.throws(() => configuration({ ...goal, mode: 'unrestricted' }), /Normal, Plan, or Goal/);
  const saved = preferences({ ...goal, apiKey: 'secret', endpoint: 'https://example.test' });
  assert.equal(saved.mode, 'goal'); assert(!('successCriteria' in saved)); assert(!('apiKey' in saved)); assert(!('permissionMode' in saved));
});

test('legacy default turn caps migrate once while explicit and custom budgets survive', () => {
  const migrated = restorePreferences({ provider: 'google', model: 'custom-model', maxTurns: 40 });
  assert.equal(migrated.maxTurns, 0); assert.equal(migrated.model, 'custom-model'); assert.equal(migrated.provider, 'google');
  assert.equal(restorePreferences({ schemaVersion: 1, maxTurns: '40' }).maxTurns, 0);
  assert.equal(restorePreferences({ maxTurns: 25 }).maxTurns, 25);
  assert.equal(restorePreferences({ maxTurns: 1 }).maxTurns, 1);
  assert.equal(restorePreferences(preferenceRecord({ ...defaultConfiguration, maxTurns: 40 })).maxTurns, 40);
  const saved = preferenceRecord({ ...migrated, apiKey: 'credential', successCriteria: 'Task content', permissionMode: 'fullAccess' });
  assert.equal(saved.schemaVersion, 2); assert(!JSON.stringify(saved).includes('credential')); assert(!('successCriteria' in saved));
  assert.throws(() => restorePreferences({ schemaVersion: 99 }), /Unknown/);
});

test('fresh recovery stages Goal criteria for explicit connection without overriding an existing host policy', () => {
  const checkpoint = JSON.stringify({ run: { mode: 'Goal', successCriteria: 'Existing checks pass and the drawing units are preserved.' } });
  const staged = recoveredRunSelection(checkpoint, null);
  assert.equal(staged.mode, 'goal'); assert.match(staged.successCriteria, /drawing units/);
  const connected = configuration({ ...defaultConfiguration, ...staged, apiKey: 'new-provider-key' });
  assert.equal(connected.mode, 'goal'); assert.equal(connected.successCriteria, staged.successCriteria);
  assert.equal(connected.permissionMode, 'ask');
  assert.equal(recoveredRunSelection(checkpoint, { mode: 'plan', apiKey: '', successCriteria: '' }), null);
  assert.equal(recoveredRunSelection(checkpoint, { mode: 'goal', successCriteria: 'Current task' }), null);
  assert.equal(recoveredRunSelection('{broken', null), null);
  assert.equal(recoveredRunSelection({ run: { mode: 'goal', successCriteria: 'x'.repeat(16001) } }, null), null);
});

test('poll run snapshots normalize enum casing and retain authoritative completion evidence', () => {
  const transcript = new AgentTranscript();
  transcript.apply(snapshot({ mode: 'Goal', phase: 'completed', resumable: false, turnCount: 9, totalTurns: 12,
    continuationCount: 3, outputLimitCount: 1, completionSummary: 'Checks passed', verification: ['Workspace test receipt: passed'], successCriteria: 'All tests pass' }));
  assert.equal(transcript.run.mode, 'goal');
  const view = presentRun(transcript);
  assert.equal(view.title, 'Goal completed'); assert.equal(view.resumable, false);
  assert.equal(view.summary, 'Checks passed'); assert.deepEqual(view.verification, ['Workspace test receipt: passed']);
  assert.match(view.progress, /9 model turns/); assert.match(view.progress, /12 total/); assert.match(view.progress, /3 continuations/); assert.match(view.progress, /1 output limit/);
});

test('run events update persistent progress without creating duplicate timeline cards', () => {
  const transcript = new AgentTranscript();
  const events = Array.from({ length: 50 }, (_, index) => ({ sequence: index + 1, type: 'run', message: 'Working', data: { mode: 'Normal', phase: 'running', turnCount: index + 1 } }));
  transcript.apply({ sessionId: 'session', events, running: true, status: 'running' });
  transcript.apply({ sessionId: 'session', events, running: true, status: 'running' });
  assert.equal(transcript.items.length, 0); assert.equal(transcript.sequence, 50); assert.equal(transcript.run.turnCount, 50);
});

test('text-only assistant output does not mark a persistent task complete', () => {
  const transcript = new AgentTranscript();
  transcript.apply(snapshot({ mode: 'Normal', phase: 'continuing', resumable: false, continuationCount: 1 }, {
    running: true, events: [{ sequence: 1, type: 'assistant', message: 'I have another step to do.' }]
  }));
  const view = presentRun(transcript);
  assert.equal(view.title, 'Continuing the task'); assert.equal(view.phase, 'continuing'); assert.equal(view.resumable, false);
  assert.equal(transcript.items.filter(item => item.kind === 'assistant').length, 1);
});

test('Plan completion offers explicit execution without making normal completion resumable', () => {
  const plan = presentRun(state({ mode: 'Plan', phase: 'completed', resumable: false, completionSummary: 'A three-step plan is ready.' }));
  assert.equal(plan.title, 'Plan ready'); assert.equal(plan.executePlan, true); assert.equal(plan.resumable, false);
  const normal = presentRun(state({ mode: 'Normal', phase: 'completed', resumable: false }));
  assert.equal(normal.title, 'Task completed'); assert.equal(normal.executePlan, false); assert.equal(normal.resumable, false);
});

test('budget, stall, blocker and cancellation retain exact details and a Resume action', () => {
  for (const [phase, stopReason, detail] of [
    ['paused', 'turn_limit', 'The configured budget of 4 turns was reached.'],
    ['paused', 'no_progress', 'Repeated tool calls did not advance the task.'],
    ['blocked', 'blocked', 'The required drawing has not been opened.'],
    ['cancelled', 'cancelled', 'Stopped by the user.']
  ]) {
    const view = presentRun(state({ mode: 'Goal', phase, stopReason, detail, resumable: true, turnCount: 4, turnLimit: 4 }));
    assert.equal(view.resumable, true); assert.equal(view.detail, detail); assert.equal(view.stopReason, stopReason);
    assert.match(view.progress, /4 \/ 4 model turns/);
  }
});

test('a just-resumed task cannot retain actionable paused controls or a stale completion claim', () => {
  const view = presentRun(state({ mode: 'Normal', phase: 'paused', resumable: true, detail: 'Old pause', stopReason: 'turn_limit',
    completionSummary: 'Old outcome', verification: ['Old evidence'] }, { running: true, status: 'running' }));
  assert.equal(view.phase, 'running'); assert.equal(view.resumable, false); assert.equal(view.detail, '');
  assert.equal(view.summary, ''); assert.equal(view.stopReason, ''); assert.deepEqual(view.verification, []);
});

test('imported legacy tasks remain resumable while an empty idle session remains unobtrusive', () => {
  const idle = presentRun(state({ mode: 'Normal', phase: 'idle', resumable: false }, { items: [] }));
  assert.equal(idle.visible, false);
  const restored = presentRun(state({ mode: 'Normal', phase: 'idle', resumable: true, detail: 'Restored task ready to resume.' }));
  assert.equal(restored.visible, true); assert.equal(restored.title, 'Task ready to resume'); assert.equal(restored.resumable, true);
  assert.equal(presentRun(state(null, { status: 'paused' })).resumable, true);
  assert.equal(presentRun(state(null, { status: 'completed' })).resumable, false);
});

test('checkpoint hydration keeps the latest run snapshot after skipping old events', () => {
  const transcript = new AgentTranscript();
  transcript.apply(snapshot({ mode: 'Goal', phase: 'paused', resumable: true, turnCount: 6, stopReason: 'restored' }, {
    latestSequence: 100, messages: [{ role: 'user', contents: [{ kind: 'text', text: 'Task' }] }],
    events: [{ sequence: 99, type: 'run', data: { mode: 'Normal', phase: 'running', turnCount: 1 } }]
  }));
  assert.equal(transcript.sequence, 100); assert.equal(transcript.run.mode, 'goal'); assert.equal(transcript.run.turnCount, 6);
  assert.equal(presentRun(transcript).resumable, true);
  transcript.reset(); assert.equal(transcript.run, null); assert.equal(presentRun(transcript).visible, false);
});

test('applying Plan without credentials configures the runtime without enabling a provider', async () => {
  const calls = [];
  const client = new AgentClient({ getDocument: () => 'drawing', invoke: async (method, ...args) => {
    calls.push([method, ...args]);
    if (method === 'AgentConfigure') return { configured: false };
    if (method === 'AgentPoll') return snapshot({ mode: 'Plan', phase: 'idle', resumable: false });
  }, delay: () => 1, clearDelay() {} });
  await client.configure({ ...defaultConfiguration, mode: 'plan', permissionMode: 'fullAccess' });
  assert.equal(client.configured, false); assert.equal(client.config.mode, 'plan'); assert.equal(client.transcript.run.mode, 'plan');
  const sent = calls.find(call => call[0] === 'AgentConfigure')[1];
  assert.equal(sent.apiKey, ''); assert.equal(sent.mode, 'plan'); assert.equal(sent.maxTurns, 0);
  assert.equal(sent.maxNoProgressTurns, 8); assert.equal(sent.permissionMode, 'fullAccess');
  client.dispose();
});
