import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ToolDisclosure } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/agents/tool-disclosure.mjs';

class Detail extends EventTarget { open = false; toggle(value) { this.open = value; this.dispatchEvent(new Event('toggle')); } }
const item = status => ({ id: 'event-1', callId: 'call-1', status });

test('initial errors and running-to-error updates remain collapsed by default', () => {
  const choices = new ToolDisclosure(); let detail = new Detail(), binding = choices.bind(detail, item('running'));
  assert.equal(detail.open, false); binding.dispose(); detail = new Detail(); binding = choices.bind(detail, item('error'));
  assert.equal(detail.open, false); binding.dispose();
  const another = new Detail(); choices.bind(another, { ...item('error'), callId: 'new-error' }); assert.equal(another.open, false);
});

test('explicit user expansion and collapse survive results and restored history IDs', () => {
  const choices = new ToolDisclosure(); let detail = new Detail(), binding = choices.bind(detail, item('running'));
  detail.toggle(true); binding.dispose(); detail = new Detail(); binding = choices.bind(detail, { ...item('error'), id: 'history-52' });
  assert.equal(detail.open, true); detail.toggle(false); binding.dispose(); detail = new Detail(); choices.bind(detail, item('error'));
  assert.equal(detail.open, false);
});

test('pending approvals open even a previously collapsed card and retain mandatory actions', () => {
  const choices = new ToolDisclosure(); let detail = new Detail(), binding = choices.bind(detail, item('running'));
  detail.toggle(true); detail.toggle(false); binding.dispose(); detail = new Detail(); binding = choices.bind(detail, item('approval'));
  assert.equal(detail.open, true); detail.toggle(false); assert.equal(detail.open, true); binding.dispose();
  detail = new Detail(); choices.bind(detail, item('error')); assert.equal(detail.open, false);
});

test('a pending native toggle is captured on replacement and choices are bounded and cleared per conversation', () => {
  const choices = new ToolDisclosure(2); let detail = new Detail(), binding = choices.bind(detail, item('running'));
  detail.open = true; binding.dispose(); detail = new Detail(); choices.bind(detail, item('error')); assert.equal(detail.open, true);
  for (const callId of ['next', 'last']) { const extra = new Detail(); choices.bind(extra, { callId, status: 'complete' }); extra.toggle(true); }
  assert.equal(choices.choices.size, 2); assert.equal(choices.choices.has('call-1'), false);
  choices.clear(); assert.equal(choices.choices.size, 0);
});
