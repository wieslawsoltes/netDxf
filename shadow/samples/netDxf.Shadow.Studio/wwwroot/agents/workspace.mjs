import { AgentClient, ChatGptCompanion } from './client.mjs';
import { StudioMcpBridge } from './mcp-bridge.mjs';
import { defaultConfiguration, providers, preferences, configuration, formatTokens, parseArguments, readable, safeFilePath, toolTemplate } from './contracts.mjs';
import { node, glyph, button, field, code, markdown } from './render.mjs';

const tabNames = [['chat','Conversation','chat'], ['tools','MCP','tools'], ['files','Files','file'], ['context','Context','context'], ['settings','Settings','settings']];
const modeNames = { readOnly: 'Read only', ask: 'Approve changes', fullAccess: 'Full access' };
const storageKey = 'netdxf-agent-preferences-v1';
function loadPreferences() { try { return preferences(JSON.parse(localStorage.getItem(storageKey) || '{}')); } catch { return {}; } }
function download(content, name, type = 'application/json') {
  const url = URL.createObjectURL(new Blob([content], { type })), link = node('a', { href: url, download: name });
  document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function mountAgentWorkspace({ workspace, documentBar, getDocument, getDocumentName, inspectDrawing, refreshDrawing, report = () => {} }) {
  const existing = document.querySelector('link[data-shadow-agents]');
  if (!existing) document.head.append(node('link', { rel: 'stylesheet', href: new URL('./agents.css', import.meta.url).href, 'data-shadow-agents': '' }));
  const client = new AgentClient({ getDocument });
  const mcpBridge = new StudioMcpBridge({ client, getDocument });
  let opened = false, selectedTab = 'chat', connectedOnce = false, currentConfig = { ...defaultConfiguration, ...loadPreferences() };
  let companion = null, authTimer = null, authGeneration = 0, authenticated = false, disposed = false;
  let toolCatalog = [], toolLoading = false, fileList = [], file = null, fileDirty = false, pins = [], renderScheduled = false;
  const timelineNodes = new Map(), tabButtons = new Map(), panels = new Map(), formFields = {};
  const run = callback => Promise.resolve().then(callback).catch(error => notice(error.message || String(error), 'error'));
  const dock = node('aside', { class: 'agent-workspace', hidden: true, 'aria-label': 'Coding agent workspace', 'data-agent-private': '' });
  const resize = node('div', { class: 'agent-resize', role: 'separator', tabindex: '0', 'aria-label': 'Resize agent workspace', 'aria-orientation': 'vertical', hidden: true });
  const launcher = button('Agents', () => show(), { icon: 'agent', class: 'agent-launcher', title: 'Open coding agent workspace (Ctrl+Shift+A)', 'aria-expanded': 'false' });
  const launcherBadge = node('span', { class: 'agent-launcher-badge', hidden: true }); launcher.append(launcherBadge);
  documentBar.insertBefore(launcher, documentBar.querySelector('.grow'));
  workspace.append(resize, dock);

  const statusDot = node('span', { class: 'agent-status-dot' }), statusLabel = node('span', { text: 'Ready to connect' });
  const titleStatus = node('div', { class: 'agent-title-status', role: 'status' }, [statusDot, statusLabel]);
  const expandButton = button('Expand workspace', () => {
    const expanded = dock.classList.toggle('agent-expanded');
    expandButton.setAttribute('aria-pressed', String(expanded)); expandButton.setAttribute('aria-label', expanded ? 'Restore docked workspace' : 'Expand workspace');
    expandButton.title = expanded ? 'Restore docked workspace' : 'Expand workspace';
  }, { icon: 'expand', compact: true, class: 'agent-header-button', 'aria-pressed': 'false' });
  const header = node('header', { class: 'agent-header' }, [
    node('div', { class: 'agent-brand' }, [glyph('agent'), node('div', {}, [node('strong', { text: 'Shadow Agent' }), titleStatus])]),
    node('div', { class: 'grow' }),
    button('New conversation', () => run(newSession), { icon: 'plus', compact: true, class: 'agent-header-button' }),
    expandButton,
    button('Close agent workspace', () => hide(), { icon: 'close', compact: true, class: 'agent-header-button' })
  ]);
  const tabs = node('nav', { class: 'agent-tabs', role: 'tablist', 'aria-label': 'Agent workspace' });
  for (const [id, label, icon] of tabNames) {
    const control = button(label, () => setTab(id), { icon, id: `agent-tab-${id}`, role: 'tab', 'aria-controls': `agent-panel-${id}`, 'aria-selected': id === selectedTab, tabindex: id === selectedTab ? 0 : -1 });
    control.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault(); const i = tabNames.findIndex(tab => tab[0] === selectedTab);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabNames.length - 1 : (i + (event.key === 'ArrowRight' ? 1 : -1) + tabNames.length) % tabNames.length;
      setTab(tabNames[next][0]); tabButtons.get(tabNames[next][0]).focus();
    });
    tabButtons.set(id, control); tabs.append(control);
    panels.set(id, node('section', { id: `agent-panel-${id}`, class: `agent-panel agent-panel-${id}`, role: 'tabpanel', 'aria-labelledby': `agent-tab-${id}`, hidden: id !== selectedTab }));
  }
  const alertText = node('span'), alert = node('div', { class: 'agent-alert', role: 'status', hidden: true }, [alertText,
    button('Dismiss message', () => { alert.hidden = true; }, { icon: 'close', compact: true })]);
  dock.append(header, tabs, alert, ...panels.values());
  function notice(message, kind = 'info') {
    alertText.textContent = client.clean(String(message)); alert.className = `agent-alert ${kind}`; alert.hidden = false;
    if (kind === 'error') statusLabel.textContent = client.transcript.running ? 'Run active · action needs attention' : 'Needs attention';
  }
  function clearNotice() { alert.hidden = true; }
  function show(tab = selectedTab) {
    opened = true; dock.hidden = false; resize.hidden = false; launcher.setAttribute('aria-expanded', 'true');
    workspace.classList.add('has-agent'); setTab(tab); scheduleRender();
    if (!connectedOnce) { connectedOnce = true; run(async () => { await client.connect(); }); }
    else if (!client.connected) client.schedule(0);
  }
  function hide() { opened = false; dock.hidden = true; resize.hidden = true; launcher.setAttribute('aria-expanded', 'false'); workspace.classList.remove('has-agent'); }
  function setTab(id) {
    selectedTab = id;
    for (const [name, panel] of panels) {
      panel.hidden = id !== name; tabButtons.get(name).setAttribute('aria-selected', String(id === name)); tabButtons.get(name).tabIndex = id === name ? 0 : -1;
    }
    if (id === 'tools' && !toolCatalog.length && !toolLoading) run(loadTools);
    if (id === 'files' && !fileList.length) run(loadFiles);
    if (id === 'context') renderContext();
  }
  async function copy(value) {
    try { await navigator.clipboard.writeText(value); notice('Copied to clipboard.'); }
    catch { notice('Clipboard access is unavailable. Select and copy the text directly.', 'error'); }
  }

  // Conversation -----------------------------------------------------------
  const providerChip = node('button', { type: 'button', class: 'agent-provider-chip', onclick: () => setTab('settings') }, [glyph('key'), node('span', { text: 'Connect a provider' })]);
  const documentChip = node('span', { class: 'agent-document-chip', text: 'Current drawing', title: 'Each turn is bound to the active drawing at its start.' });
  const chatActions = node('div', { class: 'agent-chat-toolbar' }, [providerChip, node('span', { class: 'grow' }),
    button('Import conversation', () => historyImport.click(), { icon: 'upload', compact: true }),
    button('Export conversation', () => run(exportHistory), { icon: 'download', compact: true })]);
  const planSummary = node('summary'), planSteps = node('ol');
  const planView = node('details', { class: 'agent-plan', hidden: true }, [planSummary, planSteps]); let planVersion = '';
  const empty = node('div', { class: 'agent-empty' }, [node('div', { class: 'agent-empty-mark' }, [glyph('agent')]),
    node('h2', { text: 'Build with your drawing.' }),
    node('p', { text: 'Inspect native DXF objects, work with immutable shadow models, author workspace files, and drive the Studio from one conversation.' }),
    node('div', { class: 'agent-starters' }, [
      ['Understand this drawing', 'Inspect the current drawing, its native DXF entities, layers, blocks, and shadow model. Summarize its structure and any diagnostics.'],
      ['Create a dimensioned plan', 'Create a clean, dimensioned example floor plan with separate layers for walls, doors, annotations, and dimensions. Inspect the available tools first.'],
      ['Analyze the selected objects', 'Inspect the selected objects in the DXF and shadow models, analyze their geometry and connections, and explain useful edits.']
    ].map(([label, text]) => button(label, () => { prompt.value = text; resizePrompt(); prompt.focus(); }, { icon: 'arrow' }))),
    button('Configure provider', () => setTab('settings'), { icon: 'settings', class: 'agent-primary' })
  ]);
  const transcript = node('div', { class: 'agent-transcript', role: 'log', 'aria-label': 'Agent conversation and tool activity', 'aria-live': 'off', tabindex: 0 });
  const timeline = node('div', { class: 'agent-timeline' }); transcript.append(empty, timeline);
  const liveStatus = node('span', { class: 'agent-sr-only', role: 'status', 'aria-live': 'polite' });
  const prompt = node('textarea', { class: 'agent-prompt', placeholder: 'Ask the agent to inspect, build, or edit…', rows: 3, 'aria-label': 'Message to coding agent', spellcheck: false });
  const sendButton = button('Send task', () => run(send), { icon: 'send', compact: true, class: 'agent-send agent-primary' });
  const stopButton = button('Stop agent', () => run(async () => { await client.cancel(); statusLabel.textContent = 'Stopping…'; }), { icon: 'stop', compact: true, class: 'agent-stop', hidden: true });
  const retryButton = button('Resume from current conversation', () => run(() => client.retry()), { icon: 'retry', compact: true, hidden: true });
  const permissionChip = node('button', { type: 'button', class: 'agent-permission-chip', onclick: () => setTab('settings'), text: 'Approve changes' });
  const contextLabel = node('span', { text: '0 / 128k' }), contextFill = node('span');
  const contextMeter = node('button', { class: 'agent-context-meter', type: 'button', title: 'Context usage and pinned instructions', onclick: () => setTab('context') }, [node('span', { class: 'agent-context-track' }, [contextFill]), contextLabel]);
  const composer = node('div', { class: 'agent-composer' }, [
    node('div', { class: 'agent-composer-context' }, [documentChip,
      button('Pin current drawing context', () => run(pinDrawing), { icon: 'pin', compact: true })]),
    node('div', { class: 'agent-prompt-box' }, [prompt,
      node('div', { class: 'agent-prompt-actions' }, [node('span', { text: 'Enter to send · Shift+Enter for a new line' }), node('span', { class: 'grow' }), retryButton, stopButton, sendButton])]),
    node('div', { class: 'agent-composer-footer' }, [permissionChip, node('span', { class: 'grow' }), contextMeter])
  ]);
  panels.get('chat').append(chatActions, planView, transcript, composer, liveStatus);
  function resizePrompt() { prompt.style.height = 'auto'; prompt.style.height = `${Math.min(160, Math.max(60, prompt.scrollHeight))}px`; }
  prompt.addEventListener('input', resizePrompt);
  prompt.addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); if (!client.transcript.running && !client.busy) run(send); }
    if (event.key === 'Escape') { event.stopPropagation(); }
  });
  async function send() {
    clearNotice();
    if (!client.configured) { setTab('settings'); throw new Error('Choose your provider and connect it before sending a task.'); }
    const text = prompt.value;
    await client.start(text); if (prompt.value === text) { prompt.value = ''; resizePrompt(); }
    transcript.scrollTop = transcript.scrollHeight;
  }
  async function newSession() {
    await client.reset(); pins = []; prompt.value = ''; timelineNodes.clear();
    timeline.replaceChildren(); clearNotice(); renderFiles(); renderContext(); setTab('chat');
  }
  async function exportHistory() { const text = await client.exportSession(); download(JSON.stringify(JSON.parse(text), null, 2), `shadow-agent-${new Date().toISOString().replace(/[:.]/g, '-')}.json`); notice('Conversation exported. API keys and authentication tokens are excluded.'); }
  const historyImport = node('input', { type: 'file', accept: '.json,application/json', hidden: true, 'aria-label': 'Import agent conversation' });
  historyImport.addEventListener('change', () => run(async () => {
    const selected = historyImport.files[0]; historyImport.value = ''; if (!selected) return;
    assertFileClean();
    if (selected.size > 16 * 1024 * 1024) throw new Error('Conversation imports are limited to 16 MiB.');
    await client.importSession(await selected.text()); timelineNodes.clear(); timeline.replaceChildren(); pins = []; fileList = []; file = null; fileDirty = false;
    filePath.value = ''; fileEditor.value = ''; fileEditor.readOnly = false; markFileClean(); renderFiles();
    notice('Conversation imported. Your current provider credentials and permission setting are retained.'); setTab('chat');
  })); dock.append(historyImport);

  function scheduleRender() {
    if (renderScheduled || disposed) return;
    renderScheduled = true; requestAnimationFrame(() => { renderScheduled = false; renderStatus(); if (opened) renderTimeline(); });
  }
  function renderStatus() {
    const state = client.transcript, running = state.running || client.busy;
    const nextPlanVersion = JSON.stringify(state.plan);
    if (planVersion !== nextPlanVersion) {
      planVersion = nextPlanVersion; planView.hidden = !state.plan.length;
      const completed = state.plan.filter(step => step.status === 'completed').length;
      const active = state.plan.find(step => step.status === 'in_progress');
      planSummary.replaceChildren(glyph('context'), node('span', { text: active?.step || 'Task plan' }), node('small', { text: `${completed}/${state.plan.length}` }));
      planSteps.replaceChildren(...state.plan.map(step => node('li', { class: step.status }, [node('span', { text: step.status === 'completed' ? '✓' : step.status === 'in_progress' ? '●' : '○' }), node('span', { text: step.step })])));
    }
    const approvals = state.items.filter(item => item.kind === 'tool' && item.status === 'approval').length;
    statusDot.className = `agent-status-dot${approvals ? ' approval' : running ? ' running' : client.connected ? ' connected' : ''}`;
    statusLabel.textContent = approvals ? `Waiting for ${approvals} approval${approvals === 1 ? '' : 's'}` : running ? String(state.status || 'Working').replace(/^./, c => c.toUpperCase()) : state.status === 'error' ? 'Error · resume available' : state.status === 'paused' ? 'Paused · turn budget reached' : state.status === 'cancelled' ? 'Stopped' : client.configured ? 'Ready' : client.connected ? 'Runtime connected' : 'Ready to connect';
    dock.setAttribute('aria-busy', String(running)); launcher.classList.toggle('running', running);
    launcherBadge.hidden = !running && !approvals; launcherBadge.textContent = approvals ? String(approvals) : '●';
    tabButtons.get('chat').classList.toggle('needs-attention', approvals > 0);
    sendButton.hidden = state.running; stopButton.hidden = !state.running && client.operation?.kind !== 'work'; sendButton.disabled = client.busy;
    stopButton.disabled = client.operation?.kind === 'cancel' || client.operation?.kind === 'disconnect';
    retryButton.hidden = !client.lastPrompt && !state.items.some(item => item.kind === 'user') || running; prompt.disabled = false;
    const percent = Math.min(100, state.contextTokens / state.contextLimit * 100);
    contextFill.style.width = `${percent}%`; contextMeter.classList.toggle('near-limit', percent >= 75);
    contextLabel.textContent = `${formatTokens(state.contextTokens)} / ${formatTokens(state.contextLimit)}`;
    contextMeter.setAttribute('aria-label', `${state.contextTokens.toLocaleString()} of ${state.contextLimit.toLocaleString()} context tokens. Open context management.`);
    permissionChip.textContent = modeNames[client.config?.permissionMode || currentConfig.permissionMode] || 'Approve changes';
    permissionChip.classList.toggle('full-access', client.config?.permissionMode === 'fullAccess');
    providerChip.querySelector('span').textContent = client.configured ? `${providers.find(p => p.id === client.config.provider)?.name || client.config.provider} · ${client.config.model}` : 'Connect a provider';
    documentChip.textContent = getDocumentName() || 'No drawing open';
    connectButton.disabled = running; compactButton.disabled = running; pinButton.disabled = running; saveFileButton.disabled = running || !fileDirty;
    runToolButton.disabled = running || !toolName.value.trim();
    if (selectedTab === 'context') renderContext();
    liveStatus.textContent = approvals ? statusLabel.textContent : running ? 'Agent is working.' : 'Agent is idle.';
  }
  function renderTimeline() {
    const state = client.transcript, nearBottom = transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight < 100;
    empty.hidden = state.items.length > 0;
    const liveIds = new Set(state.items.map(item => item.id));
    for (const [id, cached] of timelineNodes) if (!liveIds.has(id)) { cached.element.remove(); timelineNodes.delete(id); }
    for (const item of state.items) {
      const version = `${item.version || ''}:${item.status || ''}:${item.streaming}:${item.text?.length || 0}`;
      let cached = timelineNodes.get(item.id);
      if (!cached) {
        const element = node('article', { class: `agent-message agent-message-${item.kind}` });
        cached = { element, version: null }; timelineNodes.set(item.id, cached); timeline.append(element);
      }
      if (cached.item !== item) { cached.element.replaceChildren(); cached.element.className = `agent-message agent-message-${item.kind}`; cached.body = null; cached.version = null; cached.item = item; }
      if (cached.version === version) continue;
      cached.version = version;
      if (item.kind === 'tool') renderToolCard(cached.element, item);
      else if (item.kind === 'assistant' || item.kind === 'user') {
        if (!cached.body) {
          const role = node('div', { class: 'agent-message-role' }, [item.kind === 'assistant' ? glyph('agent') : node('span', { class: 'agent-user-avatar', text: 'You' }), node('strong', { text: item.kind === 'assistant' ? 'Shadow Agent' : 'You' })]);
          if (item.time) { const date = new Date(item.time); if (!Number.isNaN(date.valueOf())) role.append(node('time', { text: date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }), datetime: date.toISOString() })); }
          role.append(button('Copy message', () => copy(item.text || ''), { icon: 'copy', compact: true }));
          cached.body = node('div', { class: 'agent-message-body' }); cached.element.append(role, cached.body);
        }
        cached.body.classList.toggle('streaming', item.streaming === true);
        if (item.streaming || item.kind === 'user') { cached.body.classList.add('plain'); cached.body.textContent = item.text || ''; }
        else { cached.body.classList.remove('plain'); cached.body.replaceChildren(markdown(item.text || '', { onCopy: copy, onCode: codeToFile })); }
      } else {
        cached.element.className = `agent-message agent-message-${item.kind} ${item.type || ''}`;
        cached.element.replaceChildren(node('span', { class: 'agent-event-mark', text: item.kind === 'error' ? '!' : item.type === 'compacted' ? '◇' : '·' }), node('span', { text: item.text }));
        if (item.data && Object.keys(item.data).length) cached.element.append(node('details', { class: 'agent-event-details' }, [node('summary', { text: 'Details' }), code(item.data)]));
      }
    }
    if (!state.items.length) transcript.scrollTop = 0;
    else if (nearBottom) transcript.scrollTop = transcript.scrollHeight;
  }
  function renderToolCard(element, item) {
    const previous = element.querySelector('details'), wasOpen = previous?.open;
    const detail = node('details', { class: `agent-tool-card ${item.status || ''}` });
    detail.open = wasOpen ?? item.status === 'approval';
    detail.append(node('summary', {}, [node('span', { class: `agent-tool-indicator ${item.status || ''}`, text: item.status === 'complete' ? '✓' : item.status === 'error' ? '!' : item.status === 'approval' ? '?' : '◇' }),
      node('code', { text: item.name }), node('span', { class: 'grow' }), node('span', { class: 'agent-tool-status', text: item.status === 'approval' ? 'Approval needed' : item.status || 'Queued' }),
      item.durationMs !== undefined ? node('small', { text: `${Number(item.durationMs).toLocaleString()} ms` }) : null]));
    const body = node('div', { class: 'agent-tool-body' });
    if (item.text) body.append(node('p', { text: item.text }));
    if (item.arguments !== undefined) body.append(code(item.arguments, 'Arguments'));
    if (item.result !== undefined) body.append(code(item.result, 'Result'));
    if (item.status === 'approval') {
      const allow = button('Approve once', () => approval(true), { icon: 'check', class: 'agent-primary' });
      const deny = button('Deny', () => approval(false), { class: 'agent-secondary' });
      const approval = async approved => {
        allow.disabled = true; deny.disabled = true;
        try { await client.approve(item.requestId, approved); notice(approved ? 'Tool execution approved.' : 'Tool execution denied.'); }
        catch (error) { allow.disabled = false; deny.disabled = false; notice(error.message, 'error'); }
      };
      body.append(node('div', { class: 'agent-approval-actions' }, [allow, deny]));
    }
    detail.append(body); element.replaceChildren(detail);
  }

  // MCP tool catalog and structured console --------------------------------
  const toolSearch = node('input', { type: 'search', placeholder: 'Search DXF, shadow, geometry, editor, UI…', 'aria-label': 'Search MCP tools' });
  const toolCount = node('span', { class: 'agent-count', text: 'Discover tools' });
  const toolListNode = node('div', { class: 'agent-tool-catalog' });
  const toolName = node('input', { type: 'text', list: 'agent-tool-names', placeholder: 'Select a tool from the catalog', 'aria-label': 'MCP tool name', spellcheck: false });
  const toolNames = node('datalist', { id: 'agent-tool-names' });
  const toolArgs = node('textarea', { class: 'agent-json-editor', rows: 7, spellcheck: false, 'aria-label': 'MCP arguments JSON', value: '{}' }); toolArgs.value = '{}';
  const toolResult = node('div', { class: 'agent-console-result', role: 'status' });
  const runToolButton = button('Run tool', () => run(callTool), { icon: 'arrow', class: 'agent-primary', disabled: true });
  const toolConsole = node('details', { class: 'agent-console' }, [node('summary', { text: 'JSON tool console' }),
    node('div', { class: 'agent-console-body' }, [field('Tool', toolName), toolNames, field('Arguments', toolArgs),
      node('div', { class: 'agent-row' }, [runToolButton, button('Format JSON', () => run(() => { toolArgs.value = JSON.stringify(parseArguments(toolArgs.value), null, 2); })), node('span', { class: 'grow' }), button('Copy result', () => copy(toolResult.textContent || ''), { icon: 'copy', compact: true })]), toolResult])]);
  panels.get('tools').append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'MCP tools' }),
    node('p', { text: 'Discover the same tool contracts the agent uses. Each invocation returns its native result and follows the active permission policy.' })]),
    node('div', { class: 'agent-search-row' }, [toolSearch, button('Refresh MCP tools', () => run(loadTools), { icon: 'refresh', compact: true })]),
    node('div', { class: 'agent-catalog-caption' }, [toolCount]), toolConsole, toolListNode);
  toolSearch.addEventListener('input', renderTools);
  toolName.addEventListener('input', () => { runToolButton.disabled = !toolName.value.trim() || client.transcript.running || client.busy; });
  async function loadTools() {
    toolLoading = true; toolCount.textContent = 'Loading tool contracts…';
    try {
      toolCatalog = await client.tools();
      if (!Array.isArray(toolCatalog)) throw new Error('The MCP runtime returned an invalid tool catalog.');
      toolCatalog.sort((a, b) => a.name.localeCompare(b.name));
      toolNames.replaceChildren(...toolCatalog.map(tool => node('option', { value: tool.name })));
      renderTools();
    } finally { toolLoading = false; }
  }
  function useTool(tool) {
    toolName.value = tool.name;
    try { toolArgs.value = toolTemplate(tool.inputSchema); } catch { toolArgs.value = '{}'; }
    toolConsole.open = true; runToolButton.disabled = client.transcript.running || client.busy;
    toolConsole.scrollIntoView({ block: 'nearest' }); toolArgs.focus();
  }
  function renderTools() {
    const query = toolSearch.value.trim().toLowerCase();
    const matches = toolCatalog.filter(tool => `${tool.name} ${tool.description}`.toLowerCase().includes(query));
    toolCount.textContent = `${matches.length} of ${toolCatalog.length} tools${matches.length > 150 ? ' · showing 150, refine search' : ''}`;
    toolListNode.replaceChildren(...matches.slice(0, 150).map(tool => {
      const readOnly = tool.readOnly === true || tool.annotations?.readOnlyHint === true;
      return node('details', { class: 'agent-catalog-tool' }, [node('summary', {}, [glyph('tools'), node('code', { text: tool.name }), node('span', { class: `agent-scope-badge ${readOnly ? 'read-only' : 'write'}`, text: readOnly ? 'Read' : 'Action' })]),
        node('div', { class: 'agent-catalog-tool-body' }, [node('p', { text: tool.description || 'No description supplied.' }), button('Use in console', () => useTool(tool), { icon: 'arrow' }),
          node('details', {}, [node('summary', { text: 'Input schema' }), code(tool.inputSchema || {})])])]);
    }));
    if (!matches.length) toolListNode.append(node('p', { class: 'agent-muted', text: toolCatalog.length ? 'No matching tools. Try a model or subsystem name.' : 'No MCP tools loaded. Refresh to reconnect to the runtime.' }));
  }
  async function callTool() {
    clearNotice(); const name = toolName.value.trim();
    if (!toolCatalog.some(tool => tool.name === name)) throw new Error('Choose a tool from the current MCP catalog.');
    const args = parseArguments(toolArgs.value); toolResult.replaceChildren(node('p', { text: 'Running… approvals appear in Conversation.' }));
    runToolButton.disabled = true;
    try { const result = await client.callTool(name, args); toolResult.replaceChildren(code(result, 'MCP result')); await refreshDrawing(); }
    finally { runToolButton.disabled = false; }
  }

  // Versioned agent workspace files ---------------------------------------
  const filesPanel = panels.get('files'), fileTree = node('div', { class: 'agent-file-tree' });
  const filePath = node('input', { type: 'text', placeholder: 'src/drawing-analysis.cs', 'aria-label': 'Workspace file path', spellcheck: false });
  const fileEditor = node('textarea', { class: 'agent-file-editor', spellcheck: false, rows: 18, 'aria-label': 'Workspace file content', placeholder: 'Open a workspace file, or enter a path and start authoring.' });
  const fileReceipt = node('span', { class: 'agent-muted', text: 'No file selected' });
  const saveFileButton = button('Save file', () => run(saveFile), { icon: 'check', class: 'agent-primary', disabled: true });
  filesPanel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'Agent workspace' }), node('p', { text: 'Versioned text files for generated code, scripts, plans, and reports. Save edits before changing files; the runtime rejects writes against a newer version.' })]),
    node('div', { class: 'agent-row agent-file-toolbar' }, [button('New file', () => run(newFile), { icon: 'plus' }), button('Import file', () => fileImport.click(), { icon: 'upload' }), node('span', { class: 'grow' }), button('Refresh workspace files', () => run(loadFiles), { icon: 'refresh', compact: true })]),
    fileTree, node('div', { class: 'agent-file-detail' }, [field('Path', filePath), node('div', { class: 'agent-row' }, [fileReceipt]), fileEditor,
      node('div', { class: 'agent-row' }, [saveFileButton, button('Revert', () => run(async () => { if (file?.path) await openFile(file.path, true); else { fileEditor.value = ''; markFileClean(); } })), node('span', { class: 'grow' }),
        button('Download file', () => run(() => download(fileEditor.value, safeFilePath(filePath.value).split('/').at(-1), 'text/plain;charset=utf-8')), { icon: 'download', compact: true }), button('Pin file', () => run(pinFile), { icon: 'pin', compact: true })]) ]));
  fileEditor.addEventListener('input', () => { fileDirty = true; saveFileButton.disabled = client.transcript.running || client.busy; fileReceipt.textContent = `${file?.version != null ? `Version ${file.version} · ` : ''}Unsaved changes`; });
  filePath.addEventListener('input', () => { fileDirty = true; saveFileButton.disabled = client.transcript.running || client.busy; });
  fileEditor.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') { event.preventDefault(); run(saveFile); }
    if (event.key === 'Tab') { event.preventDefault(); const start = fileEditor.selectionStart, end = fileEditor.selectionEnd; fileEditor.setRangeText('    ', start, end, 'end'); fileEditor.dispatchEvent(new Event('input')); }
  });
  const fileImport = node('input', { type: 'file', hidden: true, 'aria-label': 'Import text file to agent workspace' });
  fileImport.addEventListener('change', () => run(async () => {
    const selected = fileImport.files[0]; fileImport.value = ''; if (!selected) return;
    if (fileDirty) throw new Error('Save or revert the current file before importing another.');
    if (selected.size > 2 * 1024 * 1024) throw new Error('Workspace text files are limited to 2 MiB in this editor.');
    const content = await selected.text(); if (content.includes('\0')) throw new Error('The workspace editor imports text files.');
    file = null; filePath.value = safeFilePath(selected.name); fileEditor.value = content; fileEditor.readOnly = false; fileDirty = true; saveFileButton.disabled = client.transcript.running || client.busy; fileReceipt.textContent = 'Imported · save to create workspace file';
  })); dock.append(fileImport);
  function assertFileClean() { if (fileDirty) throw new Error('Save or revert your current file before opening another.'); }
  function markFileClean() { fileDirty = false; saveFileButton.disabled = true; fileReceipt.textContent = file ? `Version ${file.version ?? '—'} · ${fileEditor.value.length.toLocaleString()} characters` : 'New file'; }
  async function newFile() { assertFileClean(); file = null; filePath.value = ''; fileEditor.value = ''; fileEditor.readOnly = false; markFileClean(); filePath.focus(); }
  async function loadFiles() { fileList = await client.files(); if (!Array.isArray(fileList)) throw new Error('The runtime returned an invalid workspace listing.'); renderFiles(); }
  function renderFiles() {
    fileTree.replaceChildren(...fileList.map(entry => {
      const path = typeof entry === 'string' ? entry : entry.path;
      return button(path, () => run(() => openFile(path)), { icon: 'file', class: file?.path === path ? 'selected' : '', title: `${path}${entry.version != null ? ` · version ${entry.version}` : ''}` });
    }));
    if (!fileList.length) fileTree.append(node('p', { class: 'agent-muted', text: 'No workspace files yet. Save a code block from the conversation, import a text file, or ask the agent to create one.' }));
  }
  async function openFile(path, discard = false) {
    if (!discard) assertFileClean();
    const result = await client.readFile(safeFilePath(path));
    file = typeof result === 'string' ? { path, content: result, version: null } : { ...result, path: result.path || path };
    filePath.value = file.path; fileEditor.value = file.content ?? file.text ?? ''; fileEditor.readOnly = file.hasMore === true; markFileClean(); renderFiles();
    if (file.hasMore) notice(`Previewing ${fileEditor.value.length.toLocaleString()} of ${Number(file.totalCharacters).toLocaleString()} characters. Use ranged workspace tools to edit this large file.`, 'info');
  }
  async function saveFile() {
    const path = safeFilePath(filePath.value);
    if (fileEditor.value.length > 2 * 1024 * 1024) throw new Error('Workspace text files are limited to 2 MiB in this editor.');
    if (file?.hasMore) throw new Error('This file exceeds the editor read limit. Use workspace_read with ranges and workspace_patch to edit it without truncation.');
    const expected = file?.path === path ? file.version ?? 0 : 0;
    const result = await client.writeFile(path, fileEditor.value, expected);
    file = { path, content: fileEditor.value, ...(result || {}) }; markFileClean(); await loadFiles(); notice(`Saved ${path}.`);
  }
  function codeToFile(content, info) {
    run(() => {
      assertFileClean(); const pieces = info.split(/\s+/), ext = { cs: 'cs', csharp: 'cs', js: 'mjs', javascript: 'mjs', ts: 'ts', json: 'json', xml: 'xml', xaml: 'axaml', py: 'py', python: 'py', md: 'md', markdown: 'md', yaml: 'yml', yml: 'yml', html: 'html', css: 'css', sh: 'sh' }[pieces[0]] || 'txt';
      const candidate = pieces.find(part => /[/.]/.test(part)) || `generated-${Date.now()}.${ext}`;
      file = null; filePath.value = safeFilePath(candidate); fileEditor.value = content; fileEditor.readOnly = false; fileDirty = true; saveFileButton.disabled = client.transcript.running || client.busy; fileReceipt.textContent = 'Code artifact · save to workspace'; setTab('files');
    });
  }
  async function pinFile() {
    const path = safeFilePath(filePath.value);
    await addPin(`Workspace file: ${path}\n${fileEditor.value}`); notice(`Pinned ${path} to agent context.`);
  }

  // Context accounting, instructions and compaction -------------------------
  const contextPanel = panels.get('context'), tokenValue = node('strong', { text: '0' }), tokenLimit = node('span', { text: '/ 128,000 tokens' });
  const largeFill = node('span'), usageGrid = node('div', { class: 'agent-usage-grid' }), pinList = node('div', { class: 'agent-pin-list' });
  const pinText = node('textarea', { rows: 4, placeholder: 'Add a stable requirement, architectural constraint, or drawing context to keep across compaction…', 'aria-label': 'Pinned context text' });
  const pinButton = button('Pin context', () => run(async () => { await addPin(pinText.value); pinText.value = ''; }), { icon: 'pin', class: 'agent-primary' });
  const compactButton = button('Compact context now', () => run(async () => { await client.compact(); notice('Context compaction completed. Pinned instructions and the active task remain available.'); }), { icon: 'context' });
  contextPanel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'Context management' }), node('p', { text: 'Track prompt usage, retain essential instructions, and compact conversation history while preserving the working context.' })]),
    node('section', { class: 'agent-context-summary' }, [node('div', { class: 'agent-token-total' }, [tokenValue, tokenLimit]), node('div', { class: 'agent-large-track' }, [largeFill]), usageGrid,
      node('div', { class: 'agent-row' }, [compactButton]), node('small', { class: 'agent-muted', text: 'Token estimates guide compaction. Provider usage is shown when it is available; set the context window to match your selected model.' })]),
    node('section', { class: 'agent-pins' }, [node('div', { class: 'agent-row' }, [node('h3', { text: 'Pinned instructions' }), node('span', { class: 'grow' }), button('Pin drawing snapshot', () => run(pinDrawing), { icon: 'pin', compact: true })]), pinList, pinText, node('div', { class: 'agent-row' }, [pinButton]) ]));
  async function addPin(text) {
    const value = String(text).trim(); if (!value) throw new Error('Enter text to pin.');
    if (value.length > 32000) throw new Error('A context pin is limited to 32,000 characters. Pin a focused excerpt or refer to a workspace file.');
    const result = await client.pin(value);
    if (typeof result === 'string') pins.push({ id: result, text: value });
    else if (result?.id) pins.push({ ...result, text: result.text || value });
    renderContext();
  }
  async function pinDrawing() {
    const info = await inspectDrawing();
    // Pin only document facts, never the provider form, credential inputs, or complete DOM.
    await addPin(`Drawing context captured ${new Date().toISOString()}\n${JSON.stringify({ documentId: info.documentId, document: info.document, state: info.state, selection: info.selection, layer: info.layer, camera: info.camera }, null, 2)}`);
    notice('Current drawing facts pinned. Re-pin after significant drawing changes.');
  }
  function renderContext() {
    const state = client.transcript;
    tokenValue.textContent = state.contextTokens.toLocaleString(); tokenLimit.textContent = `/ ${state.contextLimit.toLocaleString()} tokens`;
    largeFill.style.width = `${Math.min(100, state.contextTokens / state.contextLimit * 100)}%`;
    const usage = state.usage;
    const entries = [['Input', usage.inputTokens ?? usage.promptTokens], ['Output', usage.outputTokens ?? usage.completionTokens], ['Cached', usage.cachedInputTokens ?? usage.cachedTokens], ['Model turns', usage.turns ?? usage.modelTurns]];
    usageGrid.replaceChildren(...entries.map(([label, value]) => node('div', {}, [node('strong', { text: value == null ? '—' : Number(value).toLocaleString() }), node('span', { text: label })])));
    if (state.pins?.length || state.pinsKnown) pins = state.pins;
    pinList.replaceChildren(...pins.map(pin => node('article', { class: 'agent-pin' }, [node('div', { class: 'agent-row' }, [glyph('pin'), node('strong', { text: String(pin.text || pin.content || '').split('\n')[0].slice(0, 80) || 'Pinned instruction' }), node('span', { class: 'grow' }),
      button('Remove pin', () => run(async () => { await client.unpin(pin.id); pins = pins.filter(p => p.id !== pin.id); renderContext(); }), { icon: 'close', compact: true, disabled: client.transcript.running })]),
      node('details', {}, [node('summary', { text: `${String(pin.text || pin.content || '').length.toLocaleString()} characters` }), node('pre', { text: pin.text || pin.content || '' })])] )));
    if (!pins.length) pinList.append(node('p', { class: 'agent-muted', text: 'Pin project rules, design intent, or a current drawing snapshot. Pins are retained during compaction.' }));
  }

  // Provider credentials and subscription companion ------------------------
  const settingsPanel = panels.get('settings'), providerSelect = node('select', { 'aria-label': 'Model provider' }, providers.map(provider => node('option', { value: provider.id, text: provider.name })));
  const modelInput = node('input', { type: 'text', 'aria-label': 'Model ID', spellcheck: false, autocomplete: 'off' });
  const apiKeyInput = node('input', { type: 'password', 'aria-label': 'API key or companion access token', autocomplete: 'off', spellcheck: false, 'data-agent-private': '' });
  const endpointInput = node('input', { type: 'url', 'aria-label': 'Provider endpoint or local companion URL', placeholder: 'Provider default', spellcheck: false, autocomplete: 'off' });
  const keyLabel = node('span', { class: 'agent-field-label', text: 'API key' }), keyHint = node('small', { text: 'Held in memory for this page session. Not saved or included in conversation exports.' });
  const permissionSelect = node('select', { 'aria-label': 'Agent permission mode' }, Object.entries(modeNames).map(([value, text]) => node('option', { value, text })));
  const permissionDescription = node('p', { class: 'agent-permission-description' });
  const routingNote = node('p', { class: 'agent-routing-note', hidden: true });
  const providerRuntimeNote = node('p', { class: 'agent-routing-note', hidden: true, text: 'Anthropic’s .NET SDK runs through the local companion. Connect Local tools & external MCP first; a native workspace grant is optional. The Anthropic API key and requests are sent to that companion.' });
  const limits = node('div', { class: 'agent-limits' });
  for (const [key, label, min, max] of [['maxTurns', 'Max model turns', 1, 1000], ['maxOutputTokens', 'Output tokens / turn', 256, 128000], ['contextWindowTokens', 'Model context window', 4096, 4000000]]) {
    const input = node('input', { type: 'number', min, max, step: 1, 'aria-label': label }); formFields[key] = input; input.value = currentConfig[key]; limits.append(field(label, input));
  }
  const signInStatus = node('p', { class: 'agent-auth-status', text: 'Start the local companion to use your ChatGPT subscription.' });
  const signInLink = node('a', { target: '_blank', rel: 'noopener noreferrer', hidden: true, text: 'Continue sign-in in your browser ↗' });
  const signInButton = button('Sign in with ChatGPT', () => run(signIn), { icon: 'key', class: 'agent-primary' });
  const authCheckButton = button('Check sign-in', () => run(checkSignIn), { icon: 'refresh' });
  const signOutButton = button('Sign out', () => run(async () => {
    const current = makeCompanion(); invalidateSignIn('Signing out…');
    if (client.config?.provider === 'chatgpt') await client.disconnect();
    await current.logout(); signInStatus.textContent = 'Signed out of the companion.'; scheduleRender();
  }));
  const companionPanel = node('section', { class: 'agent-companion', hidden: true }, [
    node('h3', { text: 'Sign in with ChatGPT' }), node('p', { text: 'A localhost .NET companion completes the app sign-in callback and keeps subscription credentials on your computer. The Studio connects using the separate companion access token.' }),
    code('dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- --origin https://wieslawsoltes.github.io', 'Start the local companion'),
    node('p', { class: 'agent-muted', text: 'Use this page’s origin for a local or custom deployment. Copy the companion access token from its terminal into the key field above.' }),
    node('div', { class: 'agent-row' }, [signInButton, authCheckButton, signOutButton]), signInLink, signInStatus
  ]);
  const connectButton = button('Connect provider', () => run(connectProvider), { icon: 'check', class: 'agent-primary' });
  const connectionInfo = node('span', { class: 'agent-muted', text: 'No provider configured' });
  settingsPanel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'Provider & permissions' }), node('p', { text: 'Use the official .NET provider clients with your own API credentials, or sign in through the local ChatGPT companion. Model IDs and limits remain fully editable.' })]),
    node('div', { class: 'agent-settings-form' }, [field('Provider', providerSelect), field('Model ID', modelInput, 'Use a model with tool calling. Model availability and context limits depend on your provider account.'),
      node('label', { class: 'agent-field' }, [keyLabel, node('div', { class: 'agent-key-field' }, [apiKeyInput, button('Disconnect and clear credential', () => run(async () => {
        await client.disconnect();
        connectionInfo.textContent = 'Disconnected · runtime credential cleared'; scheduleRender();
      }), { icon: 'close', compact: true })]), keyHint]),
      field('Endpoint / proxy (optional)', endpointInput, 'Blank uses the provider default. Custom endpoints receive the credential and request content.'), routingNote,
      providerRuntimeNote, companionPanel, node('hr'), field('Tool permissions', permissionSelect), permissionDescription,
      button('Apply permissions', () => run(async () => {
        await client.configure({ ...(client.config || currentConfig), permissionMode: permissionSelect.value });
        currentConfig.permissionMode = permissionSelect.value; notice(`Tool policy applied: ${modeNames[permissionSelect.value]}.`); scheduleRender();
      }), { icon: 'check', class: 'agent-secondary' }),
      node('details', { class: 'agent-advanced-settings' }, [node('summary', { text: 'Limits & context budget' }), limits]),
      node('div', { class: 'agent-row agent-connect-row' }, [connectButton, connectionInfo]) ]));
  const bridgeEndpoint = node('input', { type: 'url', value: 'http://127.0.0.1:5091', 'aria-label': 'External MCP companion endpoint', spellcheck: false });
  const bridgeToken = node('input', { type: 'password', autocomplete: 'off', 'aria-label': 'External MCP companion access token', 'data-agent-private': '' });
  const bridgeStatus = node('p', { class: 'agent-auth-status', text: 'External MCP is disconnected.' });
  const bridgeConnect = button('Connect MCP', () => run(async () => {
    const endpoint = bridgeEndpoint.value, token = bridgeToken.value;
    bridgeConnect.disabled = true; bridgeStatus.textContent = 'Connecting this drawing to MCP…';
    try { await connectNativeTools(endpoint, token); await mcpBridge.connect(endpoint, token); }
    finally { bridgeConnect.disabled = false; }
  }), { icon: 'tools', class: 'agent-primary' });
  const bridgeDisconnect = button('Disconnect', () => run(async () => {
    client.ensureIdle(); mcpBridge.disconnect(); bridgeStatus.textContent = 'External MCP disconnected. Disconnecting local tools…';
    await client.companionDisconnect(); nativeToolCount = 0; bridgeToken.value = '';
    bridgeStatus.textContent = 'Local tools and external MCP are disconnected.'; await loadTools();
  }));
  const nativeConnect = button('Connect local tools', () => run(connectNativeTools), { icon: 'file', class: 'agent-secondary' });
  let nativeToolCount = 0;
  async function connectNativeTools(endpoint = bridgeEndpoint.value, token = bridgeToken.value) {
    nativeConnect.disabled = true;
    try {
      const result = await client.companionConnect(endpoint, token);
      // A catalog registration belongs to one explicit connection. Re-register
      // after replacing native tools so external assistants never see stale capabilities.
      mcpBridge.disconnect();
      nativeToolCount = result.toolCount ?? result.tools?.length ?? 0;
      bridgeStatus.textContent = nativeToolCount ? `Local workspace connected · ${nativeToolCount} native tools.` : 'Companion connected. Start it with --workspace to enable native files, builds, tests, and Git.';
      await loadTools(); return result;
    } finally { nativeConnect.disabled = false; }
  }
  const externalSection = node('section', { class: 'agent-companion agent-external-mcp' }, [node('h3', { text: 'Local tools & external MCP' }),
    node('p', { text: 'Attach a real project directory to the browser harness for native file edits, builds, tests, and Git. You can also publish this tab’s live DXF, shadow, and Studio tools to external coding assistants.' }),
    code('dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- --origin https://wieslawsoltes.github.io --workspace /path/to/project', 'Start a project companion'),
    field('Companion endpoint', bridgeEndpoint), field('Companion access token', bridgeToken, 'Kept in memory. Sent in authenticated local requests and the first WebSocket message, never in a URL.'),
    button('Use ChatGPT companion settings', () => run(() => {
      if (providerSelect.value !== 'chatgpt') throw new Error('Choose ChatGPT subscription to copy its companion connection.');
      bridgeEndpoint.value = endpointInput.value; bridgeToken.value = apiKeyInput.value;
    }), { icon: 'copy' }),
    node('div', { class: 'agent-row' }, [nativeConnect, bridgeConnect, bridgeDisconnect]), bridgeStatus,
    code('MCP endpoint: http://127.0.0.1:5091/mcp\nAuthorization: Bearer <companion access token>', 'External assistant connection'),
    node('p', { class: 'agent-muted', text: 'The connection is bound to this drawing. Changing drawing tabs disconnects it. Reconnect explicitly after a connection loss; tool edits are never replayed.' })]);
  settingsPanel.append(externalSection);
  mcpBridge.addEventListener('connected', () => { bridgeStatus.textContent = `MCP connected to ${getDocumentName() || mcpBridge.documentId}${nativeToolCount ? ` · ${nativeToolCount} native workspace tools` : ''}.`; notice('External MCP is connected to this drawing.'); });
  mcpBridge.addEventListener('disconnected', event => { bridgeStatus.textContent = event.detail.reason; });
  mcpBridge.addEventListener('error', event => { bridgeStatus.textContent = event.detail.message; });
  providerSelect.value = currentConfig.provider; modelInput.value = currentConfig.model; permissionSelect.value = currentConfig.permissionMode;
  providerSelect.addEventListener('change', () => {
    const provider = providers.find(p => p.id === providerSelect.value);
    modelInput.value = provider.model; apiKeyInput.value = ''; endpointInput.value = provider.id === 'chatgpt' ? 'http://127.0.0.1:5091' : '';
    invalidateSignIn(); updateProviderFields();
  });
  permissionSelect.addEventListener('change', updatePermissionDescription);
  endpointInput.addEventListener('input', () => { invalidateSignIn(); updateRouting(); });
  apiKeyInput.addEventListener('input', () => invalidateSignIn());
  function invalidateSignIn(message = 'Start the local companion to use your ChatGPT subscription.') {
    authenticated = false; companion = null; authGeneration++;
    if (authTimer) clearTimeout(authTimer); authTimer = null;
    signInLink.hidden = true; signInLink.removeAttribute('href'); signInButton.disabled = false; signInStatus.textContent = message;
  }
  function updateProviderFields() {
    const provider = providers.find(p => p.id === providerSelect.value), subscription = provider.id === 'chatgpt';
    keyLabel.textContent = provider.hint; apiKeyInput.placeholder = provider.placeholder;
    providerRuntimeNote.hidden = provider.id !== 'anthropic' || globalThis.shadowBrowser?.runtime !== 'blazor-webassembly';
    companionPanel.hidden = !subscription;
    if (subscription && !endpointInput.value) endpointInput.value = 'http://127.0.0.1:5091';
    connectButton.querySelector('span').textContent = subscription ? 'Connect ChatGPT companion' : 'Connect provider'; updatePermissionDescription(); updateRouting();
  }
  function updatePermissionDescription() {
    const value = permissionSelect.value;
    permissionDescription.textContent = value === 'readOnly' ? 'The agent can inspect tools, documents, and model data. Tools that change drawing or workspace state are denied.' : value === 'fullAccess' ? 'The agent can execute all registered tools without individual approvals, including document edits, workspace file writes, and Studio control. Changes run in the current Studio workspace.' : 'Read tools run immediately. You approve each state-changing tool with its exact arguments before execution.';
    permissionDescription.classList.toggle('full-access', value === 'fullAccess');
  }
  function updateRouting() {
    routingNote.hidden = !endpointInput.value.trim();
    if (!routingNote.hidden) routingNote.textContent = providerSelect.value === 'chatgpt' ? 'Requests and the companion access token will be sent to the local companion. Subscription OAuth credentials stay in the companion.' : 'Your API key and conversation requests will be sent to this custom endpoint. Use an endpoint you control or trust.';
  }
  function readConfiguration() {
    return configuration({ provider: providerSelect.value, model: modelInput.value, apiKey: apiKeyInput.value, endpoint: endpointInput.value,
      permissionMode: permissionSelect.value, ...Object.fromEntries(Object.entries(formFields).map(([key, input]) => [key, input.value])) });
  }
  async function connectProvider() {
    clearNotice(); const config = readConfiguration(), generation = authGeneration;
    connectButton.disabled = true; connectionInfo.textContent = 'Configuring…';
    try {
      if (config.provider === 'chatgpt') {
        const status = await makeCompanion().status();
        if (generation !== authGeneration || disposed) throw new Error('Companion credentials changed while checking sign-in. Connect again with the current settings.');
        authenticated = status.authenticated === true;
        if (!authenticated) throw new Error('Complete Sign in with ChatGPT before connecting the companion.');
      } else if (!config.apiKey) throw new Error('Enter your provider API key.');
      await client.configure(config); currentConfig = config;
      try { localStorage.setItem(storageKey, JSON.stringify(preferences(config))); } catch { /* Preferences are optional in private browsing. */ }
      connectionInfo.textContent = 'Configured · credentials in memory'; clearNotice(); setTab('chat'); prompt.focus();
    } catch (error) { connectionInfo.textContent = client.configured ? 'Previous provider remains configured' : 'No provider configured'; throw error; }
    finally { connectButton.disabled = false; }
  }
  function makeCompanion() {
    const config = readConfiguration(); if (config.provider !== 'chatgpt') throw new Error('Choose ChatGPT subscription first.');
    companion = new ChatGptCompanion(config.endpoint, config.apiKey); return companion;
  }
  async function signIn() {
    const current = makeCompanion(), generation = ++authGeneration;
    if (authTimer) clearTimeout(authTimer); signInButton.disabled = true; signInStatus.textContent = 'Starting app sign-in…';
    try {
      const result = await current.login(); if (generation !== authGeneration) return;
      signInLink.href = result.authorizationUrl; signInLink.hidden = false;
      const popup = window.open(result.authorizationUrl, '_blank', 'noopener,noreferrer');
      if (popup) popup.opener = null;
      signInStatus.textContent = 'Complete sign-in in the opened tab. If it was blocked, use the link above.';
      const started = Date.now();
      const poll = async () => {
        if (generation !== authGeneration || disposed) return;
        try {
          const status = await current.status(); if (generation !== authGeneration) return;
          if (status.authenticated) { authenticated = true; signInStatus.textContent = 'Signed in. Connect the companion to start a conversation.'; signInLink.hidden = true; return; }
        } catch (error) { if (generation !== authGeneration || disposed) return; signInStatus.textContent = client.clean(error.message); }
        if (Date.now() - started < 10 * 60 * 1000) authTimer = setTimeout(poll, 2000);
        else signInStatus.textContent = 'Sign-in polling paused. Use Check sign-in when you finish.';
      };
      authTimer = setTimeout(poll, 1000);
    } finally { if (generation === authGeneration) signInButton.disabled = false; }
  }
  async function checkSignIn() {
    const generation = authGeneration, status = await makeCompanion().status();
    if (generation !== authGeneration || disposed) return;
    authenticated = status.authenticated === true;
    signInStatus.textContent = authenticated ? `Signed in${status.expiresAt ? ` · expires ${new Date(status.expiresAt).toLocaleString()}` : ''}. Connect the companion to start.` : 'Not signed in. Start app sign-in to continue.';
    if (authenticated) signInLink.hidden = true;
  }
  updateProviderFields();

  // Runtime events and dock behavior ---------------------------------------
  let lastRevision = null;
  client.addEventListener('change', event => {
    const state = event.detail;
    if (!state.running && lastRevision !== state.sequence) {
      lastRevision = state.sequence; Promise.resolve().then(refreshDrawing).catch(report);
      if (selectedTab === 'files') run(loadFiles);
    }
    scheduleRender();
  });
  client.addEventListener('configured', scheduleRender); client.addEventListener('connected', scheduleRender); client.addEventListener('busy', scheduleRender);
  client.addEventListener('providerdisconnected', event => {
    if (providerSelect.value === 'anthropic') apiKeyInput.value = '';
    currentConfig.apiKey = ''; connectionInfo.textContent = 'Anthropic disconnected · local companion required';
    notice(event.detail.reason); scheduleRender();
  });
  client.addEventListener('disconnected', () => {
    mcpBridge.disconnect(); nativeToolCount = 0; bridgeToken.value = ''; apiKeyInput.value = ''; currentConfig.apiKey = '';
    bridgeStatus.textContent = 'Local tools and external MCP are disconnected.';
    invalidateSignIn('Runtime disconnected. Use Sign in with ChatGPT or check the companion when reconnecting.');
    run(loadTools); scheduleRender();
  });
  client.addEventListener('connectionerror', event => {
    const { error, attempt, retryMs } = event.detail;
    if (attempt === 1 || attempt === 4) notice(`Agent connection interrupted. ${client.transcript.running || attempt < 4 ? `Reconnecting in ${Math.round(retryMs / 1000)}s. ` : 'Reopen the workspace or connect again to retry. '}${error.message}`, 'error');
    scheduleRender();
  });
  resize.addEventListener('pointerdown', event => {
    event.preventDefault(); resize.setPointerCapture(event.pointerId); const start = event.clientX, width = dock.getBoundingClientRect().width;
    const move = e => document.documentElement.style.setProperty('--agent-width', `${Math.max(340, Math.min(innerWidth * .8, width + start - e.clientX))}px`);
    const end = () => { resize.removeEventListener('pointermove', move); resize.removeEventListener('pointerup', end); resize.removeEventListener('pointercancel', end); };
    resize.addEventListener('pointermove', move); resize.addEventListener('pointerup', end); resize.addEventListener('pointercancel', end);
  });
  resize.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    event.preventDefault(); const width = dock.getBoundingClientRect().width;
    document.documentElement.style.setProperty('--agent-width', `${Math.max(340, Math.min(innerWidth * .8, width + (event.key === 'ArrowLeft' ? 24 : -24)))}px`);
  });
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === 'a') { event.preventDefault(); opened ? hide() : show(); }
  });
  dock.addEventListener('keydown', event => {
    if (event.key === 'Escape' && event.target === dock) hide();
    // Studio command shortcuts cannot consume input from an agent code editor or settings form.
    if (!(event.shiftKey && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a')) event.stopPropagation();
  });
  window.addEventListener('pagehide', event => { mcpBridge.disconnect(); if (!event.persisted) { disposed = true; authGeneration++; if (authTimer) clearTimeout(authTimer); client.dispose(); apiKeyInput.value = ''; bridgeToken.value = ''; currentConfig.apiKey = ''; } });
  window.addEventListener('pageshow', event => { if (event.persisted && connectedOnce) client.schedule(0); });
  return Object.freeze({ show, hide, toggle: () => opened ? hide() : show(), documentChanged: () => { mcpBridge.documentChanged(); scheduleRender(); }, client, snapshot: () => ({ open: opened, tab: selectedTab, status: client.transcript.status, running: client.transcript.running, provider: client.config?.provider, model: client.config?.model, permissionMode: client.config?.permissionMode, externalMcp: mcpBridge.connected }) });
}
