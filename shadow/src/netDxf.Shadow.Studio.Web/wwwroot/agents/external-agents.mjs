import { createReferenceComposer, referenceChips, splitReferencePrompt } from './drawing-references.mjs';
import { node, button, field, code, createMarkdownView } from './render.mjs';
import { endpointUrl } from './contracts.mjs';
import { acpCapabilities, acpConfiguration, acpConfigurationCall, acpSessionIdentity, mergeAcpToolCall, createAcpTranscriptIds, agentSignInUrl, codexHistoryEntries, codexQuestionAnswer, codexSteeringCall, codexCompactionCall } from './external-agent-contracts.mjs';

/** Local process protocols own their conversations; no credentials or transcripts are persisted here. */
export function mountExternalAgents(panel, { connectDrawing, disconnectDrawing, refreshDrawing, drawingReferences, getDocument, report }) {
  let connection, protocol, sessionId, turnId, cursor = 0, timer, running = false, generation = 0, initialization, sessionMetadata = {}, sessionCursor, replaying = false;
  const transcriptIds = createAcpTranscriptIds(), toolRows = new Map(), controls = {};
  const messages = new Map(), historyIds = new Set();
  let historyCursor, historyBusy = false, connecting = false, preparing = false, sessionBusy = false, compacting = false, boundDocument = null;
  const obsolete = () => Object.assign(new Error('The agent connection changed; this response was discarded.'), { name: 'ObsoleteAgentResponse' });
  const current = stamp => { if (stamp !== generation) throw obsolete(); };
  const historyAnchor = node('span', { hidden: true });
  const endpoint = node('input', { type: 'url', value: 'http://127.0.0.1:5091', 'aria-label': 'Coding agent companion URL' });
  const token = node('input', { type: 'password', autocomplete: 'off', 'aria-label': 'Coding agent companion access token' });
  const profile = node('select', { 'aria-label': 'Coding agent protocol' }, [node('option', { value: 'codex', text: 'Codex app-server' }), node('option', { value: 'acp', text: 'ACP coding agent' })]);
  const model = node('input', { placeholder: 'Select a model or enter its ID', 'aria-label': 'Coding agent model', list: 'external-agent-models' });
  const models = node('datalist', { id: 'external-agent-models' });
  const auth = node('select', { 'aria-label': 'Agent authentication method' });
  const configuration = node('div', { class: 'agent-section', 'aria-label': 'Agent session configuration', hidden: true });
  const authHelp = node('p', { class: 'agent-muted' });
  const modelField = field('Model', model);
  const status = node('p', { class: 'agent-muted', role: 'status', text: 'Connect an enabled local agent profile. Its own client manages sign-in and credentials.' });
  const loginLink = node('a', { target: '_blank', rel: 'noopener noreferrer', hidden: true, text: 'Continue sign-in in browser' });
  const deviceCode = node('code', { hidden: true });
  const history = node('div', { class: 'agent-transcript external-transcript', role: 'log', 'aria-label': 'External coding agent conversation', 'aria-live': 'off' });
  const approvals = node('div', { class: 'external-approvals', 'aria-live': 'polite' });
  const prompt = node('textarea', { class: 'agent-prompt', rows: 3, 'aria-label': 'Message to external coding agent', placeholder: 'Describe a drawing or coding task…' });
  const guard = callback => Promise.resolve().then(callback).catch(error => { if(error.name==='ObsoleteAgentResponse')return;status.textContent = clean(error.message || String(error)); report?.(error); });
  const clean = value => connection?.token ? String(value).split(connection.token).join('[companion credential]') : String(value);
  async function request(path, body) {
    if (!connection) throw new Error('Connect the local companion first.');
    const target=connection,stamp=generation;
    try {
      const result = await fetch(`${target.url}/agents/${path}`, { method: body === undefined ? 'GET' : 'POST', cache: 'no-store',
        headers: { Authorization: `Bearer ${target.token}`, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
      current(stamp);
      let value; try { value = await result.json(); } catch { throw new Error(`The companion returned HTTP ${result.status}.`); }
      current(stamp);
      if (!result.ok) throw new Error(value.error || `The companion returned HTTP ${result.status}.`); return value;
    } catch(error) { current(stamp); throw error; }
  }
  const call = (method, parameters = {}, references = []) => request('call', { method, parameters, ...(references.length ? { references } : {}) });
  const referenceComposer = createReferenceComposer({ api: drawingReferences, getDocument, report: error => { status.textContent = error.message; report?.(error); } });
  function message(id, role, text, append = false, streaming = false, references = null) {
    let value = messages.get(id);
    if (!value) {
      const body = node('div', { class: 'agent-message-body' });
      const row = node('article', { class: `agent-message agent-message-${role}` }, [node('strong', { class: 'agent-message-role', text: role === 'user' ? 'You' : protocol === 'codex' ? 'Codex' : 'ACP agent' }), body]);
      value = { text: '', view: createMarkdownView(body, { onCopy: value => navigator.clipboard.writeText(value), headingOffset: 2 }), row }; messages.set(id, value); history.append(row);
    }
    const atBottom = history.scrollHeight - history.scrollTop - history.clientHeight < 120;
    value.text = append ? value.text + text : text;
    const parsed = role === 'user' ? splitReferencePrompt(value.text) : { text: value.text, references: [] };
    value.view.update(clean(parsed.text), { streaming });
    const attached = references || parsed.references;
    if (attached.length && JSON.stringify(attached) !== value.referenceSignature) { value.chips?.remove(); value.chips = referenceChips(attached, { historical: true }); value.row.append(value.chips); value.referenceSignature = JSON.stringify(attached); }
    trimHistory();
    if (atBottom) history.scrollTop = history.scrollHeight;
    return value.row;
  }
  function detail(title, value) {
    const row = node('details', { class: 'agent-event-details' }, [node('summary', { text: title }), code(value)]); history.append(row);
    trimHistory();
    return row;
  }
  function trimHistory() {
    while (history.children.length > 600) {
      const row = [...history.children].find(child => child !== historyAnchor); if (!row) break;
      row.remove();
      for (const [id, value] of messages) if (value.row === row) { value.view.dispose(); messages.delete(id); }
      for (const [id, value] of toolRows) if (value.row === row) toolRows.delete(id);
    }
  }
  function finish(text) { running = false; compacting = false; turnId = null; updateControls(); status.textContent = text; for (const m of messages.values()) m.view.flush(); void Promise.resolve(refreshDrawing?.()).catch(report); }
  function capabilities() { return acpCapabilities(initialization); }
  function updateControls() {
    const ready = !!protocol && !connecting && !preparing && !sessionBusy, acp = protocol === 'acp', caps = capabilities();
    const enable = (name, enabled) => { if (controls[name]) controls[name].disabled = !enabled; };
    enable('login', ready && (!acp || caps.auth.some(m => m.protocolLogin)));
    enable('device', ready && protocol === 'codex'); if (controls.device) controls.device.hidden = acp;
    enable('account', ready); enable('logout', ready && !running && (!acp || caps.logout));
    enable('new', ready && !running); enable('list', ready && !running && (!acp || caps.list));
    enable('load', ready && !running && !!sessions.value && (!acp || caps.load || caps.resume));
    enable('resume', ready && acp && !running && !!sessions.value && caps.load && caps.resume);
    if (controls.resume) controls.resume.hidden = !acp || !(caps.load && caps.resume);
    enable('close', ready && acp && !!sessionId && caps.close); if (controls.close) controls.close.hidden = !acp || !caps.close;
    enable('delete', ready && acp && !running && !!sessions.value && caps.delete); if (controls.delete) controls.delete.hidden = !acp || !caps.delete;
    enable('more', ready && !running && !!sessionCursor); if (controls.more) controls.more.hidden = !sessionCursor;
    enable('history', ready && !historyBusy && !!historyCursor); if (controls.history) controls.history.hidden = protocol !== 'codex' || !historyCursor;
    enable('send', ready && !running); enable('stop', !!protocol && running && (acp || !!turnId));
    enable('steer', ready && protocol === 'codex' && running && !!turnId && !compacting); if (controls.steer) controls.steer.hidden = protocol !== 'codex';
    enable('compact', ready && protocol === 'codex' && !!sessionId && !running); if (controls.compact) controls.compact.hidden = protocol !== 'codex';
    modelField.hidden = acp; models.hidden = acp; auth.hidden = !acp || !caps.auth.length;
    authHelp.hidden = !acp;
  }
  function renderConfiguration(metadata = sessionMetadata) {
    sessionMetadata = metadata; configuration.replaceChildren();
    const options = protocol === 'acp' ? acpConfiguration(metadata) : [];
    configuration.hidden = options.length === 0;
    for (const option of options) {
      let input;
      if (option.type === 'boolean') input = node('input', { type: 'checkbox', checked: option.currentValue === true });
      else {
        input = node('select');
        for (const choice of option.choices) input.append(node('option', { value: choice.value, text: `${choice.group ? `${choice.group} · ` : ''}${choice.name}` }));
        input.value = String(option.currentValue ?? '');
      }
      input.setAttribute('aria-label', option.name); input.disabled = !option.supported || !sessionId;
      input.addEventListener('change', () => guard(async () => {
        const requestedSession=sessionId;
        const request = acpConfigurationCall(requestedSession, option, option.type === 'boolean' ? input.checked : input.value);
        input.disabled = true;
        try {
          const result = await call(request.method, request.parameters);
          if(requestedSession!==sessionId)return;
          renderConfiguration(result.configOptions ? { ...sessionMetadata, configOptions: result.configOptions }
            : { ...sessionMetadata, modes: { ...sessionMetadata.modes, currentModeId: input.value } });
        } catch (error) { if(requestedSession===sessionId)renderConfiguration(); throw error; }
      }));
      configuration.append(field(option.name, input, option.supported ? option.description : `Unsupported option type: ${option.type}`));
    }
  }
  function clearConversation() {
    for (const m of messages.values()) m.view.dispose(); messages.clear(); history.replaceChildren(historyAnchor); historyCursor = null; historyIds.clear();
    approvals.replaceChildren(); approvals.dataset.signature = ''; toolRows.clear(); transcriptIds.reset();
  }
  function notification(event) {
    const p = event.data || {};
    if (protocol === 'codex') {
      if (sessionId && p.threadId && p.threadId !== sessionId) return;
      if (event.method === 'item/agentMessage/delta') message(p.itemId, 'assistant', p.delta || '', true, true);
      else if (event.method === 'turn/started') { turnId = p.turn?.id; running = true; updateControls(); status.textContent = compacting ? 'Compacting Codex context…' : 'Codex is working…'; }
      else if (event.method === 'turn/completed') {
        if (turnId && p.turn?.id && turnId !== p.turn.id) return;
        finish(compacting ? `Context compaction ${p.turn?.status || 'ended'}.` : `Turn ${p.turn?.status || 'ended'}.`);
      }
      else if (event.method === 'item/completed' && p.item?.type === 'agentMessage') message(p.item.id, 'assistant', p.item.text || '');
      else if (event.method === 'account/login/completed') { loginLink.hidden = true; deviceCode.hidden = true; status.textContent = p.success ? 'Codex sign-in complete.' : `Sign-in failed: ${p.error || 'Try again.'}`; }
      else if (event.method === 'account/updated') status.textContent = `Codex account: ${p.authMode || 'signed out'}`;
      else if (event.method === 'item/started' || event.method === 'item/completed') detail(`${p.item?.type || 'Activity'} · ${event.method.endsWith('completed') ? 'completed' : 'started'}`, p.item || p);
      else if (event.method.includes('error')) detail(event.method, p);
    } else if (event.method === 'session/update') {
      if (sessionId && p.sessionId && p.sessionId !== sessionId) return;
      const u = p.update || {}, type = u.sessionUpdate;
      if ((type === 'agent_message_chunk' || type === 'user_message_chunk') && u.content?.type === 'text') {
        const role = type === 'user_message_chunk' ? 'user' : 'assistant';
        if (role !== 'user' || replaying) message(transcriptIds.next(role, { sessionId: p.sessionId || sessionId, turnId, replay: replaying }), role, u.content.text || '', true, running);
      } else if (type === 'config_option_update') renderConfiguration({ ...sessionMetadata, configOptions: u.configOptions || [] });
      else if (type === 'current_mode_update') renderConfiguration({ ...sessionMetadata, modes: { ...sessionMetadata.modes, currentModeId: u.currentModeId } });
      else if (type === 'tool_call' || type === 'tool_call_update') {
        transcriptIds.boundary();
        const key = `${p.sessionId}/${u.toolCallId}`, previous = toolRows.get(key), value = mergeAcpToolCall(previous?.value, u);
        const row = previous?.row || node('details', { class: 'agent-event-details' });
        row.replaceChildren(node('summary', { text: `${value.title || value.name || 'Tool'} · ${value.status || 'pending'}` }), code(value));
        if (!previous) history.append(row); toolRows.set(key, { value, row }); trimHistory();
      }
      else if (type !== 'agent_thought_chunk') detail(type || 'Agent update', u);
    }
  }
  function renderApprovals(values) {
    const signature = JSON.stringify(values); if (approvals.dataset.signature === signature) return;
    approvals.dataset.signature = signature; approvals.replaceChildren();
    for (const value of values) {
      const card = node('div', { class: 'agent-approval' }, [node('strong', { text: value.method === 'item/tool/requestUserInput' ? 'Agent question' : 'Approval required' }), code(value.parameters)]);
      const approvalGeneration=generation;
      const respond = result => guard(async () => {current(approvalGeneration);await request('reply', { requestId: value.requestId, result }); card.remove(); });
      if (value.method === 'session/request_permission') {
        for (const option of value.parameters.options || []) card.append(button(option.name || option.optionId, () => respond({ optionId: option.optionId })));
        card.append(button('Cancel', () => respond({ optionId: null })));
      } else if (value.method === 'item/tool/requestUserInput') {
        const answers = new Map();
        for (const q of value.parameters.questions || []) {
          const input = q.options?.length ? node('select', {}, q.options.map(o => node('option', { value: o.label, text: `${o.label}${o.description ? ` — ${o.description}` : ''}` }))) : node('input', { type: q.isSecret ? 'password' : 'text' });
          const otherId = `other-${crypto.randomUUID()}`;
          const other = q.options?.length && q.isOther === true ? node('input', { type: q.isSecret ? 'password' : 'text' }) : null;
          answers.set(q.id, () => codexQuestionAnswer(q, input.value, other?.value, !!other && input.value === otherId));
          card.append(field(q.question || q.header || q.id, input));
          if (other) {
            input.append(node('option', { value: otherId, text: 'Other response…' }));
            const otherField = field('Other response', other); otherField.hidden = true;
            input.addEventListener('change', () => { otherField.hidden = input.value !== otherId; });
            card.append(otherField);
          }
        }
        card.append(button('Send answers', () => guard(() => respond({ answers: Object.fromEntries([...answers].map(([id, answer]) => [id, { answers: [answer()] }])) }))));
      } else {
        const choices = value.parameters.availableDecisions || ['accept', 'decline', 'cancel'];
        for (const decision of choices.filter(c => ['accept', 'decline', 'cancel'].includes(c))) card.append(button({ accept: 'Approve once', decline: 'Decline', cancel: 'Cancel turn' }[decision], () => respond({ decision })));
      }
      approvals.append(card);
    }
  }
  async function poll(stamp) {
    if (stamp !== generation || !connection || !protocol) return;
    try {
      const state = await request(`events?after=${cursor}`); if (stamp !== generation) return;
      if (state.truncated) detail('Earlier events expired', protocol==='codex'?'The local event buffer is bounded. Load saved history to inspect earlier turns.':'The local event buffer is bounded. Reload the session if the agent supports history loading, or start a new session.');
      for (const event of state.events || []) if (event.sequence > cursor) { if (event.kind === 'notification') notification(event); cursor = event.sequence; }
      renderApprovals(state.pending || []);
      if (state.connected === false && state.status !== 'starting') {
        generation++;clearTimeout(timer);connecting=false;preparing=false;sessionBusy=false;
        protocol = null; running = false; compacting = false; sessionId = null; turnId = null;
        approvals.replaceChildren(); approvals.dataset.signature = ''; loginLink.hidden = true; deviceCode.hidden = true;
        renderConfiguration({}); updateControls(); for (const m of messages.values()) m.view.flush();
        status.textContent = 'The local agent disconnected. Connect again to start its configured process. Requests are not replayed.';
        return;
      }
    } catch (error) { if(stamp!==generation)return;status.textContent = `Connection interrupted: ${clean(error.message)}. Requests are not replayed.`; }
    if (stamp === generation) timer = setTimeout(() => poll(stamp), 500);
  }
  async function connect() {
    if (running || preparing || sessionBusy) throw new Error('Stop the current turn or wait for session setup before reconnecting.');
    if(connecting)throw new Error('The local agent connection is already starting.');
    const url = endpointUrl(endpoint.value, { companion: true }), credential = token.value.trim();
    if (!credential || /\s/.test(credential)) throw new Error('Enter the companion access token.');
    const stamp=++generation;connecting=true;boundDocument=getDocument?.()??null;clearTimeout(timer);protocol=null;updateControls();
    try {
    connection = { url, token: credential };
    const available = await request('profiles');
    if (!available.profiles?.some(p => p.id === profile.value && p.enabled)) throw new Error('Enable this agent executable when starting the local companion. See the local agent guide.');
    await connectDrawing(url, credential);current(stamp);
    const result = await request('start', { profile: profile.value }); protocol = result.protocol; initialization = result.initialization;
    sessionId = null; turnId = null; compacting = false; cursor = 0; sessionCursor = null; sessionMetadata = {}; replaying = false;
    clearConversation(); sessions.replaceChildren(); renderConfiguration();
    auth.replaceChildren();
    for (const method of capabilities().auth) auth.append(node('option', { value: method.id,
      text: `${method.name || method.id}${method.protocolLogin ? '' : ' · external interactive login'}`, disabled: !method.protocolLogin }));
    auth.value = capabilities().auth.find(m => m.protocolLogin)?.id || '';
    authHelp.textContent = capabilities().auth.some(m => !m.protocolLogin)
      ? 'Interactive terminal login must complete in the configured agent’s own client. Reconnect afterward. Studio offers only agent-managed protocol login here.'
      : capabilities().auth.length ? 'Sign-in is handled by the selected agent. Studio does not read its credentials.' : 'This agent advertises no protocol login. Use its own documented sign-in workflow before connecting.';
    if (!capabilities().httpMcp) authHelp.textContent += ' This agent does not advertise HTTP MCP, so drawing tools are unavailable through this connection.';
    updateControls();
    status.textContent = `Connected to ${protocol === 'codex' ? 'Codex' : initialization.agentInfo?.name || 'ACP agent'}. Workspace: ${available.workspace}`;
    void poll(generation);
    if (protocol === 'codex') await checkAccount();
    } finally {if(stamp===generation){connecting=false;updateControls();}}
  }
  async function checkAccount() {
    if (protocol !== 'codex') { status.textContent = `Agent capabilities: ${capabilities().auth.filter(m => m.protocolLogin).length} protocol login methods. Session settings appear after creating or loading a session.`; return; }
    const result = await call('account/read', { refreshToken: false }); status.textContent = result.account ? `Signed in: ${result.account.email || result.account.type || 'Codex account'}` : 'Codex is not signed in.';
    const catalog = await call('model/list', { limit: 100 }); models.replaceChildren();
    for (const item of catalog.data || []) models.append(node('option', { value: item.model || item.id, label: item.displayName || item.model || item.id }));
    if (!model.value) model.value = catalog.data?.find(m => m.isDefault)?.model || catalog.data?.[0]?.model || '';
  }
  async function signIn(device = false) {
    if (protocol === 'codex') {
      const result = await call('account/login/start', { type: device ? 'chatgptDeviceCode' : 'chatgpt' });
      const href = result.authUrl || result.verificationUrl;
      if (href) { loginLink.href = agentSignInUrl(href); loginLink.hidden = false; }
      deviceCode.textContent = result.userCode || ''; deviceCode.hidden = !result.userCode;
      status.textContent = 'Continue in the provider’s browser flow, then check sign-in here.';
    } else {
      if (!capabilities().auth.some(m => m.id === auth.value && m.protocolLogin)) throw new Error('Choose an advertised agent-managed authentication method.');
      status.textContent = 'Complete authentication in the agent’s own client or browser window.';
      await call('authenticate', { methodId: auth.value }); status.textContent = 'Agent authentication completed.';
    }
  }
  function applySession(result, requestedId) {
    sessionId = protocol === 'acp' ? acpSessionIdentity(result, requestedId) : result.thread?.id || requestedId;
    if (!sessionId) throw new Error('The agent returned no session identifier.');
    renderConfiguration(result); updateControls(); status.textContent = `Session ready · ${sessionId}`;
  }
  async function createSession() {
    const result = await call(protocol === 'codex' ? 'thread/start' : 'session/new', protocol === 'codex' ? { model: model.value || undefined } : {});
    clearConversation(); replaying = false; applySession(result);
  }
  async function newSession() {
    if (!protocol || connecting) throw new Error('Connect the local agent first.');
    if (running || preparing || sessionBusy) throw new Error('Wait for the active turn or session setup.');
    const stamp=generation;sessionBusy=true;updateControls();
    try {await createSession();} finally {if(stamp===generation){sessionBusy=false;updateControls();}}
  }
  async function send() {
    if (!protocol || connecting) throw new Error('Connect a local agent first.');
    if (running || preparing || sessionBusy) throw new Error('Wait for the current turn or session setup.');
    const text = prompt.value.trim(); if (!text) return; if (new TextEncoder().encode(text).length > 64 * 1024) throw new Error('The prompt exceeds 64 KiB of UTF-8 text.');
    const stamp=generation;preparing=true;updateControls();
    try {
      const references = await referenceComposer.prepare();current(stamp);
      if (!sessionId) await createSession();current(stamp);
      replaying = false; const messageId = crypto.randomUUID(); turnId = protocol === 'codex' ? null : messageId; transcriptIds.boundary(); message(`user-${messageId}`, 'user', text, false, false, references); preparing=false;running=true;updateControls();status.textContent='Agent is working…';
      if (protocol === 'codex') { const result = await call('turn/start', { threadId: sessionId, input: [{ type: 'text', text }] }, references); if (running && !turnId) turnId = result.turn?.id || null; }
      else { const result = await call('session/prompt', { sessionId, prompt: [{ type: 'text', text }] }, references); finish(`Turn finished: ${result.stopReason || 'complete'}`); }
      if (prompt.value.trim() === text) prompt.value = ''; referenceComposer.commit(references);
    } catch (error) {if(stamp!==generation)return;finish(`Turn failed: ${clean(error.message)}`);throw error;}
    finally {if(stamp===generation){preparing=false;updateControls();}}
  }
  async function steer() {
    const text = prompt.value.trim(), requestedSession = sessionId, requestedTurn = turnId, stamp = generation;
    codexSteeringCall({ protocol, sessionId, turnId, running, busy: connecting || preparing || sessionBusy || compacting }, text);
    preparing = true; updateControls();
    try {
      const references = await referenceComposer.prepare(); current(stamp);
      if (requestedSession !== sessionId || requestedTurn !== turnId) throw new Error('The active turn changed before steering. Your message was kept.');
      const request = codexSteeringCall({ protocol, sessionId, turnId, running, busy: compacting }, text);
      const result = await call(request.method, request.parameters, references);
      if (result.turnId !== requestedTurn) throw new Error('The agent returned an unexpected steering receipt. Inspect the thread before retrying.');
      message(`user-${crypto.randomUUID()}`, 'user', text, false, false, references);
      if (prompt.value.trim() === text) prompt.value = ''; referenceComposer.commit(references);
      if (running && turnId === requestedTurn) status.textContent = 'Steering sent to the active Codex turn.';
    } finally { if (stamp === generation) { preparing = false; updateControls(); } }
  }
  async function compact() {
    const request = codexCompactionCall({ protocol, sessionId, running, busy: connecting || preparing || sessionBusy });
    const stamp = generation; running = true; compacting = true; turnId = null; updateControls(); status.textContent = 'Requesting Codex context compaction…';
    try { await call(request.method, request.parameters); if (compacting) status.textContent = 'Compacting Codex context…'; }
    catch (error) { if (stamp === generation) finish(`Context compaction failed: ${clean(error.message)}`); throw error; }
  }
  async function stop() {
    if (!sessionId || !running || protocol === 'codex' && !turnId) return;
    await call(protocol === 'codex' ? 'turn/interrupt' : 'session/cancel', protocol === 'codex' ? { threadId: sessionId, turnId } : { sessionId }); status.textContent = 'Stopping…';
  }
  const sessions = node('select', { 'aria-label': 'Saved agent session' });
  async function listSessions(more = false) {
    if (protocol === 'acp' && !capabilities().list) throw new Error('This ACP agent does not advertise session listing.');
    const result = await call(protocol === 'codex' ? 'thread/list' : 'session/list', more && sessionCursor ? { cursor: sessionCursor } : {});
    if (!more) sessions.replaceChildren();
    for (const item of result.data || result.sessions || []) sessions.append(node('option', { value: item.id || item.sessionId, text: item.title || item.preview || item.id || item.sessionId }));
    sessionCursor = result.nextCursor || null; updateControls();
  }
  async function loadSession(withoutHistory = false) {
    if (running || preparing || sessionBusy) throw new Error('Wait for the active turn or session setup.'); if (!sessions.value) throw new Error('Choose a saved session.');
    const requestedId = sessions.value, oldId = sessionId;
    const method = protocol === 'codex' ? 'thread/resume' : !withoutHistory && capabilities().load ? 'session/load' : 'session/resume';
    if (protocol === 'acp' && !(method === 'session/load' ? capabilities().load : capabilities().resume)) throw new Error('This agent does not advertise session loading or resuming.');
    const stamp=generation;sessionBusy=true;updateControls();
    clearConversation(); sessionId = requestedId; replaying = protocol === 'acp'; transcriptIds.reset();
    try { applySession(await call(method, protocol === 'codex' ? { threadId: requestedId, model: model.value || undefined } : { sessionId: requestedId }), requestedId); }
    catch (error) {if(stamp===generation)sessionId=oldId;throw error;}
    finally {if(stamp===generation){sessionBusy=false;updateControls();}}
    if (protocol === 'codex') await loadCodexHistory();
  }
  async function loadCodexHistory() {
    if (protocol !== 'codex' || !sessionId || historyBusy) return;
    const requestedId = sessionId, stamp = generation; historyBusy = true; updateControls();
    try {
      const page = await call('thread/turns/list', { threadId: requestedId, limit: 20, sortDirection: 'asc', itemsView: 'full', ...(historyCursor ? { cursor: historyCursor } : {}) });
      if (stamp !== generation || requestedId !== sessionId) return;
      for (const entry of codexHistoryEntries(page)) {
        if (historyIds.has(entry.id)) continue; historyIds.add(entry.id);
        const row = entry.role ? message(entry.id, entry.role, entry.text) : detail(entry.title, entry.value);
        // History pages stay before any new live turn while later pages arrive.
        if (historyAnchor.parentNode === history) history.insertBefore(row, historyAnchor);
      }
      historyCursor = page.nextCursor || null;
      status.textContent = historyCursor ? 'Session ready. More saved history is available.' : `Session ready · ${sessionId}`;
    } finally { historyBusy = false; updateControls(); }
  }
  const action = (id, label, callback, attributes) => controls[id] = button(label, () => guard(callback), attributes);
  panel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'Local coding agents' }), node('p', { text: 'Run Codex through its app-server or an installed ACP agent such as Gemini CLI. The companion launches only executables configured by its owner. Drawing tools connect through MCP.' })]),
    node('details', { open: '', class: 'agent-section' }, [node('summary', { text: 'Connection and sign-in' }), field('Local companion', endpoint), field('Companion token', token), field('Agent', profile),
      button('Connect local agent', () => guard(connect), { class: 'agent-primary' }), modelField, models, auth, authHelp,
      node('div', { class: 'agent-row' }, [action('login', 'Sign in', () => signIn()), action('device', 'Device sign-in', () => signIn(true)), action('account', 'Check account / capabilities', checkAccount)]),
      loginLink, deviceCode, action('logout', 'Sign out', async () => {
        if (running) throw new Error('Stop the active turn first.');
        await call(protocol === 'codex' ? 'account/logout' : 'logout'); sessionId = null; renderConfiguration({}); updateControls(); status.textContent = 'Signed out.';
      }),
      node('p', { class: 'agent-muted', text: 'Claude account sign-in must complete in the unmodified Claude Code client. Studio never imports Claude or Gemini session tokens. Configure an ACP adapter you trust; its supported auth methods appear after connection.' })]),
    status, node('div', { class: 'agent-row' }, [action('new', 'New session', newSession), action('list', 'List sessions', () => listSessions()), sessions,
      action('more', 'More sessions', () => listSessions(true)), action('load', 'Load / resume', () => loadSession()), action('resume', 'Resume without history', () => loadSession(true)), action('compact', 'Compact context', compact, { title: 'Ask Codex to summarize this session. Completion appears in the activity stream.' }),
      action('close', 'Close session', async () => {
        if (!sessionId) return; await call('session/close', { sessionId }); sessionId = null; finish('Session closed.'); renderConfiguration({}); updateControls();
      }),
      action('delete', 'Delete saved session', async () => {
        if (running || !sessions.value) throw new Error('Choose a saved session while the agent is idle.');
        const id = sessions.value;
        if (!confirm('Delete this saved agent session from its history?')) return;
        await call('session/delete', { sessionId: id }); if (sessionId === id) { sessionId = null; renderConfiguration({}); }
        sessions.selectedOptions[0]?.remove(); updateControls(); status.textContent = 'Saved session deleted.';
      })]),
    configuration, action('history', 'Load more saved history', loadCodexHistory), history, approvals, node('div', { class: 'agent-composer' }, [referenceComposer.element, prompt, node('div', { class: 'agent-row' }, [action('send', 'Send', send, { class: 'agent-primary' }), action('steer', 'Steer active turn', steer, { title: 'Send this message to the active turn without interrupting it.' }), action('stop', 'Stop', stop), button('Disconnect', () => guard(async () => { await request('stop', {}); dispose(); disconnectDrawing(); status.textContent = 'Local agent disconnected.'; }))]) ]));
  sessions.addEventListener('change', updateControls);
  prompt.addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void guard(send); } if (event.key === 'Escape' && running) { event.preventDefault(); void guard(stop); } });
  function dispose() { generation++; clearTimeout(timer); running = false; compacting=false;turnId=null;connecting=false;preparing=false;sessionBusy=false;boundDocument=null; protocol = null; sessionId = null; connection = null; token.value = ''; loginLink.hidden = true; deviceCode.hidden = true; renderConfiguration({}); updateControls(); for (const m of messages.values()) m.view.dispose(); }
  updateControls();
  return { dispose() { referenceComposer.dispose(); dispose(); }, get running() { return running||preparing||sessionBusy; }, documentChanged() {
    referenceComposer.changed();
    if(!connection||(!protocol&&!connecting&&!preparing&&!sessionBusy)||boundDocument===(getDocument?.()??null))return;
    if(running){const cancelling=stop();void guard(()=>cancelling);}
    generation++;clearTimeout(timer);running=false;compacting=false;connecting=false;preparing=false;sessionBusy=false;protocol=null;sessionId=null;turnId=null;
    approvals.replaceChildren();approvals.dataset.signature='';loginLink.hidden=true;deviceCode.hidden=true;renderConfiguration({});updateControls();
    for(const value of messages.values())value.view.flush();
    status.textContent='Drawing changed. Reconnect the local agent to bind its MCP tools to this drawing.';
  } };
}
