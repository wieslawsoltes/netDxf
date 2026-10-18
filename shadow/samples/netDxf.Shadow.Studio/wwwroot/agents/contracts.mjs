/** Provider-independent presentation contracts. No credential or transcript is persisted here. */
export const providers = Object.freeze([
  { id: 'openai', name: 'OpenAI', model: 'gpt-4.1', hint: 'OpenAI API key', placeholder: 'sk-…' },
  { id: 'anthropic', name: 'Anthropic', model: 'claude-sonnet-4-5', hint: 'Anthropic API key', placeholder: 'sk-ant-…' },
  { id: 'google', name: 'Google Gemini', model: 'gemini-2.5-pro', hint: 'Google AI API key', placeholder: 'AIza…' },
  { id: 'chatgpt', name: 'ChatGPT subscription', model: 'gpt-5-codex', hint: 'Companion access token', placeholder: 'Token printed by the local companion' }
]);

export const defaultConfiguration = Object.freeze({
  provider: 'openai', model: 'gpt-4.1', apiKey: '', endpoint: '', permissionMode: 'ask',
  mode: 'normal', successCriteria: '', maxTurns: 0, maxNoProgressTurns: 8, maxOutputTokens: 8192, contextWindowTokens: 128000
});

export const executionModes = Object.freeze([
  { id: 'normal', name: 'Normal', hint: 'Complete the task and verify the result.' },
  { id: 'plan', name: 'Plan', hint: 'Investigate and publish a plan. Agent edits stay blocked.' },
  { id: 'goal', name: 'Goal', hint: 'Work toward success criteria and report completion evidence.' }
]);

export function endpointUrl(value, { companion = false } = {}) {
  const text = String(value ?? '').trim();
  if (!text) return '';
  const url = new URL(text);
  if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password)
    throw new Error('Use an HTTP(S) endpoint without credentials in the URL.');
  if (url.hash || url.search) throw new Error('Endpoint URLs cannot contain a query string or fragment.');
  if (url.protocol === 'http:' && !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname))
    throw new Error('Remote endpoints must use HTTPS. HTTP is supported for a localhost companion.');
  if (companion && !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname))
    throw new Error('The ChatGPT sign-in companion must run on localhost.');
  return url.href.replace(/\/+$/, '');
}

function integer(value, name, minimum, maximum) {
  const number = Number(value);
  if (!Number.isSafeInteger(number) || number < minimum || number > maximum)
    throw new Error(`${name} must be an integer from ${minimum.toLocaleString()} to ${maximum.toLocaleString()}.`);
  return number;
}

export function configuration(input) {
  const provider = String(input.provider || 'openai');
  if (!providers.some(p => p.id === provider)) throw new Error('Choose a supported model provider.');
  const model = String(input.model ?? '').trim();
  if (!model || model.length > 256 || /[\r\n\0]/.test(model)) throw new Error('Enter a valid model ID.');
  const apiKey = String(input.apiKey ?? '').trim();
  if (/\s/.test(apiKey)) throw new Error('The API key or companion token cannot contain whitespace.');
  const permissionMode = String(input.permissionMode || 'ask');
  if (!['readOnly', 'ask', 'fullAccess'].includes(permissionMode)) throw new Error('Choose a valid tool permission mode.');
  const mode = String(input.mode || 'normal').toLowerCase();
  if (!executionModes.some(item => item.id === mode)) throw new Error('Choose Normal, Plan, or Goal execution mode.');
  const successCriteria = String(input.successCriteria ?? '').trim();
  if (successCriteria.length > 16000) throw new Error('Goal success criteria must be at most 16,000 characters.');
  const result = {
    provider, model, apiKey, permissionMode, mode, successCriteria,
    endpoint: endpointUrl(input.endpoint, { companion: provider === 'chatgpt' }),
    maxTurns: integer(input.maxTurns ?? 0, 'Turn budget', 0, 10000),
    maxNoProgressTurns: integer(input.maxNoProgressTurns ?? 8, 'No-progress budget', 2, 100),
    maxOutputTokens: integer(input.maxOutputTokens ?? 8192, 'Output token limit', 256, 128000),
    contextWindowTokens: integer(input.contextWindowTokens ?? 128000, 'Context window', 4096, 4000000)
  };
  if (result.maxOutputTokens >= result.contextWindowTokens / 2)
    throw new Error('Output tokens must use less than half of the context window, leaving room for the task and tools.');
  if (input.documentId) result.documentId = String(input.documentId);
  return result;
}

/** Remember mode, model choices and numeric limits. Task criteria and full access are not preferences. */
export function preferences(input) {
  const result = configuration({ ...defaultConfiguration, ...input, apiKey: '', endpoint: '', permissionMode: 'ask' });
  const { provider, model, mode, maxTurns, maxNoProgressTurns, maxOutputTokens, contextWindowTokens } = result;
  return { provider, model, mode, maxTurns, maxNoProgressTurns, maxOutputTokens, contextWindowTokens };
}

/** Version 1 did not distinguish its default 40-turn cap from a user-selected 40. */
export function restorePreferences(value) {
  if (!value || Array.isArray(value) || typeof value !== 'object') throw new Error('Invalid saved agent preferences.');
  if (value.schemaVersion !== undefined && ![1, 2].includes(value.schemaVersion)) throw new Error('Unknown agent preference version.');
  const input = { ...value };
  if (value.schemaVersion !== 2 && Number(input.maxTurns) === 40) input.maxTurns = 0;
  return preferences(input);
}

export function preferenceRecord(input) { return { schemaVersion: 2, ...preferences(input) }; }

const credentialKey = /^(?:api[-_]?key|access[-_]?token|refresh[-_]?token|id[-_]?token|bridge[-_]?token|password|authorization|client[-_]?secret|credentials?|secrets?)$/i;

export function redact(value, secrets = [], depth = 0) {
  if (depth > 64) return '[Maximum nesting]';
  if (typeof value === 'string') {
    let result = value;
    for (const secret of secrets) if (typeof secret === 'string' && secret.length >= 4) result = result.split(secret).join('[redacted]');
    return result;
  }
  if (Array.isArray(value)) return value.map(item => redact(item, secrets, depth + 1));
  if (value && typeof value === 'object') {
    const result = {};
    for (const [key, item] of Object.entries(value))
      if (!credentialKey.test(key) && !['__proto__', 'prototype', 'constructor'].includes(key))
        result[key] = redact(item, secrets, depth + 1);
    return result;
  }
  return value;
}

export function parseSession(text, secrets = []) {
  const maximumBytes = 24 * 1024 * 1024;
  if (typeof text !== 'string' || text.length > maximumBytes || new TextEncoder().encode(text).byteLength > maximumBytes)
    throw new Error('Session files must be JSON no larger than 24 MiB in UTF-8.');
  let value;
  try { value = JSON.parse(text); } catch { throw new Error('The session file is not valid JSON.'); }
  if (!value || Array.isArray(value) || typeof value !== 'object') throw new Error('The session must be a JSON object.');
  return JSON.stringify(redact(value, secrets));
}

export function parseArguments(text) {
  if (String(text).length > 1024 * 1024) throw new Error('Tool arguments must be smaller than 1 MiB.');
  let value;
  try { value = JSON.parse(text || '{}'); } catch (error) { throw new Error(`Invalid tool JSON: ${error.message}`); }
  if (!value || Array.isArray(value) || typeof value !== 'object') throw new Error('Tool arguments must be a JSON object.');
  return value;
}

export function toolTemplate(schema = {}) {
  const visit = (item, depth = 0) => {
    if (depth > 5) return null;
    if (item.default !== undefined) return item.default;
    if (item.enum?.length) return item.enum[0];
    if (item.const !== undefined) return item.const;
    if (item.type === 'object' || item.properties) {
      const result = {};
      for (const key of item.required || []) if (item.properties?.[key]) result[key] = visit(item.properties[key], depth + 1);
      return result;
    }
    if (item.type === 'array') return [];
    if (item.type === 'boolean') return false;
    if (['number', 'integer'].includes(item.type)) return item.minimum ?? 0;
    return '';
  };
  return JSON.stringify(visit(typeof schema === 'string' ? JSON.parse(schema) : schema), null, 2);
}

export const eventType = value => String(value || '').replace(/[-_\s]/g, '').toLowerCase();
export function readable(value) {
  if (value === undefined || value === null) return '';
  if (typeof value === 'string') return value;
  return JSON.stringify(value, null, 2);
}
function textOf(event) {
  const data = event.data;
  return typeof data === 'string' ? data : String(data?.text ?? data?.content ?? data?.delta ?? event.message ?? '');
}

/** Stable items let streamed text update in place while expanded tool cards retain their state. */
export class AgentTranscript {
  constructor() { this.reset(); }
  reset(sessionId = null) {
    this.sessionId = sessionId; this.sequence = 0; this.items = []; this.tools = new Map();
    this.activeAssistant = null; this.running = false; this.status = 'idle'; this.contextTokens = 0;
    this.contextLimit = 128000; this.usage = {}; this.pins = []; this.pinsKnown = false; this.plan = []; this.dropped = 0; this.version = 0;
    this.run = null;
  }
  add(item) {
    this.items.push(item);
    // This bounds DOM history only; export remains backed by the complete .NET session.
    while (this.items.length > 1200) {
      const old = this.items.shift(); this.dropped++;
      if (old.callId) this.tools.delete(old.callId);
    }
    this.version++;
    return item;
  }
  hydrate(messages, sessionId, sequence) {
    this.reset(sessionId);
    let index = 0;
    for (const message of messages) {
      const role = String(message.role).toLowerCase(), contents = message.contents || [];
      const text = contents.filter(content => content.kind === 'text').map(content => content.text || '').join('\n');
      if (text && ['assistant', 'user'].includes(role)) this.add({ id: `history-${index++}`, kind: role, text, streaming: false });
      for (const content of contents) {
        if (content.kind === 'functionCall' && content.callId) {
          const item = this.add({ id: `history-${index++}`, kind: 'tool', callId: content.callId, name: content.name,
            arguments: content.arguments, status: 'interrupted' });
          this.tools.set(content.callId, item);
        } else if (content.kind === 'functionResult' && content.callId) {
          const item = this.tools.get(content.callId);
          if (item) { item.result = content.text; item.status = content.isError ? 'error' : 'complete'; }
        }
        // Provider reasoning and protected state exist only to preserve protocol continuity; they are not UI text.
      }
    }
    this.sequence = Number.isSafeInteger(sequence) ? sequence : 0;
  }
  apply(reply) {
    if (!reply || typeof reply !== 'object') throw new Error('The agent runtime returned an invalid status.');
    if (reply.reset || reply.sessionId && this.sessionId && reply.sessionId !== this.sessionId) this.reset(reply.sessionId);
    if (reply.sessionId) this.sessionId = reply.sessionId;
    if (Array.isArray(reply.messages) && !reply.running) {
      this.hydrate(reply.messages, reply.sessionId || this.sessionId, reply.latestSequence ?? Math.max(0, ...(reply.events || []).map(event => event.sequence)));
      const last = reply.events?.at(-1);
      if (last?.sequence === this.sequence && ['error', 'cancelled', 'paused', 'compacted'].includes(eventType(last.type)))
        this.add({ id: `event-${last.sequence}`, kind: eventType(last.type) === 'error' ? 'error' : 'status', type: eventType(last.type), text: last.message, data: last.data, time: last.time });
    }
    for (const event of reply.events || []) this.accept(event);
    if (typeof reply.running === 'boolean') this.running = reply.running;
    if (reply.status) this.status = String(reply.status);
    if (Number.isFinite(reply.contextTokens)) this.contextTokens = Math.max(0, reply.contextTokens);
    if (Number.isFinite(reply.contextLimit)) this.contextLimit = Math.max(1, reply.contextLimit);
    if (Array.isArray(reply.pins)) { this.pins = reply.pins; this.pinsKnown = true; }
    if (Array.isArray(reply.plan)) this.plan = reply.plan;
    if (reply.run && typeof reply.run === 'object' && !Array.isArray(reply.run))
      this.run = { ...reply.run, mode: String(reply.run.mode || 'normal').toLowerCase(), phase: String(reply.run.phase || reply.status || 'idle').toLowerCase() };
    if (reply.usage && typeof reply.usage === 'object') this.usage = { ...this.usage, ...reply.usage };
    if (reply.running === false && (!Number.isSafeInteger(reply.latestSequence) || this.sequence >= reply.latestSequence)) {
      if (this.activeAssistant) { this.activeAssistant.streaming = false; this.activeAssistant.version = this.sequence; this.activeAssistant = null; }
      for (const tool of this.tools.values()) if (['running', 'approval'].includes(tool.status)) {
        tool.status = 'interrupted'; tool.version = this.sequence + 1;
      }
    }
    this.version++;
    return this;
  }
  accept(event) {
    const sequence = Number(event.sequence);
    if (!Number.isSafeInteger(sequence) || sequence <= this.sequence) return false;
    this.sequence = sequence;
    const type = eventType(event.type), data = event.data ?? {}, time = event.time ?? event.timestamp;
    const id = `event-${sequence}`;
    if (type === 'user') {
      this.activeAssistant = null;
      this.add({ id, kind: 'user', text: textOf(event), time });
    } else if (type === 'assistantdelta' || type === 'textdelta') {
      if (!this.activeAssistant) this.activeAssistant = this.add({ id, kind: 'assistant', text: '', streaming: true, time });
      this.activeAssistant.text += textOf(event); this.activeAssistant.version = sequence;
    } else if (type === 'assistant') {
      const text = textOf(event);
      if (this.activeAssistant) {
        if (text) this.activeAssistant.text = text;
        this.activeAssistant.streaming = false; this.activeAssistant.version = sequence; this.activeAssistant = null;
      } else if (text) this.add({ id, kind: 'assistant', text, time });
    } else if (type === 'toolstarted' || type === 'toolcompleted' || type === 'approvalrequested') {
      if (this.activeAssistant) { this.activeAssistant.streaming = false; this.activeAssistant = null; }
      const callId = String(data.callId ?? data.toolCallId ?? data.id ?? data.requestId ?? id);
      let item = this.tools.get(callId);
      if (!item && type !== 'toolstarted') {
        // Runtimes may give an approval its own request ID while keeping the tool name.
        item = [...this.tools.values()].reverse().find(tool => tool.name === (data.name ?? data.toolName ?? data.tool) && ['running', 'approval'].includes(tool.status));
      }
      if (!item) {
        item = this.add({ id, callId, kind: 'tool', name: String(data.name ?? data.toolName ?? data.tool ?? event.message ?? 'Tool'), time });
        this.tools.set(callId, item);
      }
      if (type === 'toolstarted') { item.status = 'running'; item.arguments = data.arguments ?? data.args ?? data.input; }
      if (type === 'toolcompleted') {
        item.status = data.isError || data.error ? 'error' : 'complete';
        item.result = data.result ?? data.output ?? data; item.durationMs = data.durationMs ?? data.milliseconds;
      }
      if (type === 'approvalrequested') {
        item.status = 'approval'; item.requestId = String(data.requestId ?? data.id ?? callId);
        item.arguments = data.arguments ?? data.args ?? item.arguments; item.text = event.message || data.reason || '';
      }
      item.version = sequence;
    } else if (type === 'usage') {
      if (typeof data === 'object') this.usage = { ...this.usage, ...data };
    } else if (type === 'turn') {
      this.usage.turns = data.turn ?? this.usage.turns;
      this.add({ id, kind: 'status', type, text: event.message, data, time });
    } else if (type === 'plan') {
      if (Array.isArray(data.steps)) this.plan = data.steps;
    } else if (type === 'run') {
      this.run = { ...data, mode: String(data.mode || 'normal').toLowerCase(), phase: String(data.phase || 'idle').toLowerCase() };
    } else if (type === 'retrying') {
      if (data.discardPartial && this.activeAssistant) {
        const index = this.items.indexOf(this.activeAssistant); if (index >= 0) this.items.splice(index, 1);
        this.activeAssistant = null;
      }
      this.add({ id, kind: 'status', type, text: event.message, data, time });
    } else if (type === 'completed' || type === 'cancelled') {
      this.running = false; this.status = type;
      if (this.activeAssistant) { this.activeAssistant.streaming = false; this.activeAssistant.version = sequence; this.activeAssistant = null; }
      for (const tool of this.tools.values()) if (['running', 'approval'].includes(tool.status)) { tool.status = type === 'cancelled' ? 'cancelled' : 'complete'; tool.version = sequence; }
      this.add({ id, kind: 'status', type, text: event.message || (type === 'completed' ? 'Turn completed' : 'Stopped'), data, time });
    } else {
      this.add({ id, kind: type === 'error' ? 'error' : 'status', type, text: event.message || textOf(event) || type, data, time });
    }
    this.version++;
    return true;
  }
}

export function formatTokens(number) {
  const n = Number(number) || 0;
  return n >= 1000000 ? `${(n / 1000000).toFixed(1)}m` : n >= 1000 ? `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k` : Math.round(n).toString();
}

export function safeFilePath(value) {
  const path = String(value ?? '').trim().replaceAll('\\', '/');
  if (!path || path.startsWith('/') || path.includes(':') || path.split('/').some(p => !p || p === '.' || p === '..') || /[\0-\x1f]/.test(path))
    throw new Error('Use a relative workspace path without empty, . or .. segments.');
  if (path.length > 512) throw new Error('Workspace paths must be at most 512 characters.');
  return path;
}
