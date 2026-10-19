/** Stable ACP v1 capabilities: omitted/null/false are unsupported. No capability is inferred from provider names. */
export function acpCapabilities(initialization = {}) {
  initialization ??= {};
  const caps = initialization.agentCapabilities || {}, session = caps.sessionCapabilities || {};
  return { load: caps.loadSession === true, resume: !!session.resume, list: !!session.list,
    close: !!session.close, delete: !!session.delete, logout: !!caps.auth?.logout, httpMcp: caps.mcpCapabilities?.http === true,
    auth: (Array.isArray(initialization.authMethods) ? initialization.authMethods : []).filter(m => typeof m.id === 'string' && m.id)
      .map(m => ({ ...m, protocolLogin: m.type == null || m.type === 'agent' })) };
}

/** Full Codex turn pages contain ordered items; preserve IDs so repeated pages can be deduplicated. */
export function codexHistoryEntries(page = {}) {
  const entries = [];
  for (const turn of Array.isArray(page.data) ? page.data : []) {
    for (const item of Array.isArray(turn.items) ? turn.items : []) {
      if (!item?.id) continue;
      if (item.type === 'agentMessage') entries.push({ id: item.id, role: 'assistant', text: item.text || '' });
      else if (item.type === 'userMessage') entries.push({ id: item.id, role: 'user', text: (item.content || []).map(part => part.type === 'text' ? part.text || '' : `[${part.type || 'attachment'}]`).join('\n') });
      else if (item.type !== 'reasoning') entries.push({ id: item.id, title: item.type || 'Activity', value: item });
    }
  }
  return entries;
}

export function acpConfiguration(metadata = {}) {
  if (Array.isArray(metadata.configOptions) && metadata.configOptions.length) return metadata.configOptions.map(option => {
    const choices = [];
    for (const entry of Array.isArray(option.options) ? option.options : []) {
      if (Array.isArray(entry.options)) for (const child of entry.options) {
        if (typeof child.value === 'string') choices.push({ value: child.value, name: child.name || child.value, group: entry.name || entry.group || '' });
      } else if (typeof entry.value === 'string') choices.push({ value: entry.value, name: entry.name || entry.value, group: '' });
    }
    return { id: option.id, name: option.name || option.id, description: option.description || '', category: option.category || '',
      type: option.type, currentValue: option.currentValue, choices,
      supported: typeof option.id === 'string' && (option.type === 'boolean' || option.type === 'select') };
  });
  return (metadata.modes?.availableModes?.length ? [{ id: '_legacy_mode', name: 'Session mode', type: 'mode', supported: true,
    currentValue: metadata.modes.currentModeId, choices: metadata.modes.availableModes.map(m => ({ value: m.id, name: m.name || m.id, group: '' })) }] : []);
}

export function acpConfigurationCall(sessionId, option, value) {
  if (!sessionId || !option.supported) throw new Error('This session configuration is not available.');
  if (option.type === 'boolean') {
    if (typeof value !== 'boolean') throw new Error('This option requires a boolean value.');
    return { method: 'session/set_config_option', parameters: { sessionId, configId: option.id, type: 'boolean', value } };
  }
  if (!option.choices.some(c => c.value === value)) throw new Error('Choose an option advertised by the agent.');
  return option.type === 'mode' ? { method: 'session/set_mode', parameters: { sessionId, modeId: value } }
    : { method: 'session/set_config_option', parameters: { sessionId, configId: option.id, value } };
}

export function acpSessionIdentity(result, requestedId) {
  const id = result?.sessionId || requestedId;
  if (typeof id !== 'string' || !id) throw new Error('The agent returned no session identifier.');
  return id;
}

/** ACP tool patches leave omitted/null fields unchanged; empty arrays and false/zero remain meaningful. */
export function mergeAcpToolCall(previous = {}, patch = {}) {
  return { ...previous, ...Object.fromEntries(Object.entries(patch).filter(([key, value]) => key !== 'sessionUpdate' && value !== null && value !== undefined)) };
}

/** Each replay run preserves alternating user/agent messages; live text is keyed by the local turn. */
export function createAcpTranscriptIds() {
  let serial = 0, role, current, liveKey, liveSegment = 0, liveBoundary = false;
  return {
    next(nextRole, { sessionId, turnId, replay = false } = {}) {
      if (!replay) {
        const key = `acp-${sessionId}-${turnId}-${nextRole}`;
        if (key !== liveKey) { liveKey = key; liveSegment = 0; }
        else if (liveBoundary) liveSegment++;
        liveBoundary = false;
        return liveSegment ? `${key}-${liveSegment}` : key;
      }
      if (role !== nextRole || !current) { role = nextRole; current = `acp-history-${sessionId}-${++serial}`; }
      return current;
    },
    boundary() { role = null; current = null; liveBoundary = true; },
    reset() { serial = 0; role = null; current = null; liveKey = null; liveSegment = 0; liveBoundary = false; }
  };
}

export function agentSignInUrl(value) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password) throw new Error('The agent returned an unsupported sign-in URL.');
  return url.href;
}

export function codexQuestionAnswer(question, selected, freeText, otherSelected = false) {
  const options = Array.isArray(question.options) ? question.options : [];
  if (otherSelected && question.isOther !== true) throw new Error('This question does not allow another response.');
  const answer = String(otherSelected || !options.length ? freeText ?? selected ?? '' : selected ?? '').trim();
  if (!answer) throw new Error('Enter an answer for each agent question.');
  if (options.length && !otherSelected && !options.some(option => option.label === answer)) throw new Error('Choose an answer offered by the agent.');
  return answer;
}

/** Steering is only input for the observed active turn; it must never fall back to starting another turn. */
export function codexSteeringCall({ protocol, sessionId, turnId, running, busy = false }, text) {
  if (protocol !== 'codex' || !sessionId || !turnId || !running || busy) throw new Error('Wait for an active Codex turn before steering.');
  text = String(text ?? '').trim();
  if (!text || new TextEncoder().encode(text).length > 64 * 1024) throw new Error('Enter steering text within 64 KiB.');
  return { method: 'turn/steer', parameters: { threadId: sessionId, expectedTurnId: turnId, input: [{ type: 'text', text }] } };
}

/** A compaction acknowledgement is not completion. The UI keeps the thread busy until its terminal notification. */
export function codexCompactionCall({ protocol, sessionId, running, busy = false }) {
  if (protocol !== 'codex' || !sessionId || running || busy) throw new Error('Choose an idle Codex session before compacting context.');
  return { method: 'thread/compact/start', parameters: { threadId: sessionId } };
}
