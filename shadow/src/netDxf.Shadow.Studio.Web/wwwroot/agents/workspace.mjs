import { mountEfficiency } from './efficiency.mjs';
import { mountEditApprovalPreview } from './edit-approval-preview.mjs';
import { createReferenceComposer, referenceChips } from './drawing-references.mjs';
import { mountExternalAgents } from './external-agents.mjs';
import { AgentClient, ChatGptCompanion } from './client.mjs';
import { StudioMcpBridge } from './mcp-bridge.mjs';
import { BrowserMcpBridge } from './browser-mcp.mjs';
import { BrowserSessionPersistence } from './persistence.mjs';
import { mountBrowserFileTransfer, renderFileDiff } from './file-workspace.mjs';
import { defaultConfiguration, providers, executionModes, restorePreferences, preferenceRecord, configuration, formatTokens, formatDuration, toolActivity, parseArguments, readable, safeFilePath, toolTemplate } from './contracts.mjs';
import { presentRun, recoveredRunSelection } from './run-state.mjs';
import { node, glyph, button, field, code, codeExecution, markdown, createMarkdownView } from './render.mjs';

const tabNames = [['chat','Conversation','chat'], ['local','Local agents','play'], ['tools','MCP','tools'], ['files','Files','file'], ['context','Context','context'], ['efficiency','Efficiency','activity'], ['settings','Settings','settings']];
const modeNames = { readOnly: 'Read only', ask: 'Approve changes', fullAccess: 'Full access' };
const storageKey = 'netdxf-agent-preferences-v1';
function savePreferences(value) { try { localStorage.setItem(storageKey, JSON.stringify(preferenceRecord(value))); } catch { /* Nonsecret preferences are optional. */ } }
function loadPreferences() {
  try { const value = restorePreferences(JSON.parse(localStorage.getItem(storageKey) || '{}')); savePreferences(value); return value; }
  catch { return {}; }
}
function download(content, name, type = 'application/json') {
  const url = URL.createObjectURL(new Blob([content], { type })), link = node('a', { href: url, download: name });
  document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function mountAgentWorkspace({ workspace, documentBar, getDocument, getDocumentName, inspectDrawing, refreshDrawing, drawingReferences, drawingPreview, openDrawingPreview, report = () => {} }) {
  const browserRuntime = globalThis.shadowBrowser?.runtime === 'blazor-webassembly';
  const existing = document.querySelector('link[data-shadow-agents]');
  if (!existing) document.head.append(node('link', { rel: 'stylesheet', href: new URL('./agents.css', import.meta.url).href, 'data-shadow-agents': '' }));
  if (!document.querySelector('link[data-shadow-markdown]')) document.head.append(node('link', { rel: 'stylesheet', href: new URL('../markdown/markdown.css?v=1', import.meta.url).href, 'data-shadow-markdown': '' }));
  const client = new AgentClient({ getDocument });
  const mcpBridge = new StudioMcpBridge({ client, getDocument });
  const browserMcp = new BrowserMcpBridge(client, getDocument);
  let opened = false, selectedTab = 'chat', connectedOnce = false, currentConfig = { ...defaultConfiguration, ...loadPreferences() };
  let companion = null, authTimer = null, authGeneration = 0, authenticated = false, disposed = false;
  let toolCatalog = [], toolLoading = false, fileList = [], file = null, fileDirty = false, pins = [], renderScheduled = false, persistence = null;
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
  mountEfficiency(panels.get('efficiency'));
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

  const externalAgents = mountExternalAgents(panels.get('local'), {
    connectDrawing: async (url, token) => { if (!client.connected) await client.connect(); await mcpBridge.connect(url, token); },
    disconnectDrawing: () => mcpBridge.disconnect(), refreshDrawing, drawingReferences, getDocument, report
  });

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
    node('p', { text: browserRuntime ? 'Inspect DXF and shadow models, edit project files, run JavaScript in a browser worker, and drive the Studio. Your workspace and completed conversations autosave on this device.' : 'Inspect DXF and shadow models, edit project files, and drive the Studio. Your workspace and completed conversations autosave on this device.' }),
    node('div', { class: 'agent-starters' }, [
      ['Understand this drawing', 'Inspect the current drawing, its native DXF entities, layers, blocks, and shadow model. Summarize its structure and any diagnostics.'],
      ['Create a dimensioned plan', 'Create a clean, dimensioned example floor plan with separate layers for walls, doors, annotations, and dimensions. Inspect the available tools first.'],
      ['Analyze the selected objects', 'Inspect the selected objects in the DXF and shadow models, analyze their geometry and connections, and explain useful edits.']
    ].map(([label, text]) => button(label, () => { prompt.value = text; resizePrompt(); prompt.focus(); }, { icon: 'arrow' }))),
    button('Configure provider', () => setTab('settings'), { icon: 'settings', class: 'agent-primary' })
  ]);
  const transcript = node('div', { class: 'agent-transcript', role: 'log', 'aria-label': 'Agent conversation and tool activity', 'aria-live': 'off', tabindex: 0 });
  const timeline = node('div', { class: 'agent-timeline' }); transcript.append(empty, timeline);
  const jumpLatest = button('Jump to latest', () => { transcript.scrollTop = transcript.scrollHeight; jumpLatest.hidden = true; }, { icon: 'arrow', class: 'agent-jump-latest', hidden: true });
  transcript.addEventListener('scroll', () => { jumpLatest.hidden = transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight < 100; }, { passive: true });
  const activityLabel = node('span'), activityCount = node('small');
  const activity = node('div', { class: 'agent-activity', role: 'status', hidden: true }, [node('span', { class: 'agent-activity-dot' }), activityLabel, activityCount]);
  const usageStrip = node('button', { type: 'button', class: 'agent-usage-strip', hidden: true, onclick: () => setTab('context'), title: 'Observed usage for this session. Open detailed token and cache metrics.' });
  const liveStatus = node('span', { class: 'agent-sr-only', role: 'status', 'aria-live': 'polite' });
  const prompt = node('textarea', { class: 'agent-prompt', placeholder: 'Ask the agent to inspect, build, or edit…', rows: 3, 'aria-label': 'Message to coding agent', spellcheck: false });
  const sendButton = button('Send task', () => run(send), { icon: 'send', compact: true, class: 'agent-send agent-primary' });
  const stopButton = button('Stop agent', () => run(async () => { await client.cancel(); statusLabel.textContent = 'Stopping…'; }), { icon: 'stop', compact: true, class: 'agent-stop', hidden: true });
  const retryButton = button('Resume task', () => run(resumeTask), { icon: 'retry', 'aria-label': 'Resume from current conversation', title: 'Continue from recorded results without replaying completed edits.', hidden: true, class: 'agent-primary' });
  const executePlanButton = button('Execute plan', () => run(async () => {
    if (!client.configured) { setTab('settings'); throw new Error('Connect a provider before executing the plan.'); }
    executionMode.value = 'normal'; updateExecutionMode(); await applyExecutionMode(); await client.retry();
  }), { icon: 'play', hidden: true, class: 'agent-primary' });
  const runTitle = node('strong'), runModeBadge = node('span', { class: 'agent-run-mode' });
  const runProgress = node('p', { class: 'agent-run-counters' }), runDetail = node('p', { class: 'agent-run-detail' });
  const runEvidence = node('details', { class: 'agent-run-evidence', hidden: true });
  const runView = node('section', { class: 'agent-run-state', role: 'region', 'aria-label': 'Agent run progress', hidden: true }, [
    node('div', { class: 'agent-run-heading' }, [runModeBadge, runTitle]), runProgress, runDetail, runEvidence,
    node('div', { class: 'agent-row' }, [retryButton, executePlanButton])
  ]); let runVersion = '';
  const executionMode = node('select', { 'aria-label': 'Agent execution mode' }, executionModes.map(mode => node('option', { value: mode.id, text: mode.name })));
  executionMode.value = currentConfig.mode;
  const executionHint = node('p', { class: 'agent-mode-hint' });
  const goalCriteria = node('textarea', { rows: 2, maxlength: 16000, 'aria-label': 'Goal success criteria', placeholder: 'Describe the observable result and checks that define success…' });
  goalCriteria.value = currentConfig.successCriteria;
  const goalField = field('Goal success criteria', goalCriteria, 'Optional: leave blank to use your task as the success criterion.');
  const applyModeButton = button('Apply mode', () => run(applyExecutionMode), { icon: 'check', class: 'agent-secondary' });
  const executionControls = node('div', { class: 'agent-mode-controls' }, [
    node('div', { class: 'agent-mode-row' }, [node('label', {}, [node('span', { text: 'Mode' }), executionMode]), applyModeButton]), executionHint, goalField
  ]);
  executionMode.addEventListener('change', updateExecutionMode); goalCriteria.addEventListener('input', updateExecutionMode);
  const permissionChip = node('button', { type: 'button', class: 'agent-permission-chip', onclick: () => setTab('settings'), text: 'Approve changes' });
  const contextLabel = node('span', { text: '0 / 128k' }), contextFill = node('span');
  const contextMeter = node('button', { class: 'agent-context-meter', type: 'button', title: 'Context usage and pinned instructions', onclick: () => setTab('context') }, [node('span', { class: 'agent-context-track' }, [contextFill]), contextLabel]);
  const referenceComposer = createReferenceComposer({ api: drawingReferences, getDocument, report: error => notice(error.message || String(error), 'error') });
  const composer = node('div', { class: 'agent-composer' }, [
    node('div', { class: 'agent-composer-context' }, [documentChip,
      button('Pin current drawing context', () => run(pinDrawing), { icon: 'pin', compact: true })]), executionControls,
    referenceComposer.element,
    node('div', { class: 'agent-prompt-box' }, [prompt,
      node('div', { class: 'agent-prompt-actions' }, [node('span', { text: 'Enter to send · Shift+Enter for a new line' }), node('span', { class: 'grow' }), stopButton, sendButton])]),
    node('div', { class: 'agent-composer-footer' }, [permissionChip, node('span', { class: 'grow' }), contextMeter])
  ]);
  panels.get('chat').append(chatActions, runView, planView, transcript, jumpLatest, activity, usageStrip, composer, liveStatus);
  function updateExecutionMode() {
    const mode = executionModes.find(item => item.id === executionMode.value) || executionModes[0];
    goalField.hidden = mode.id !== 'goal';
    const pending = !client.config || client.config.mode !== mode.id || client.config.successCriteria !== goalCriteria.value.trim();
    const turnLimit = client.config?.maxTurns ?? currentConfig.maxTurns;
    executionHint.textContent = `${mode.hint} ${turnLimit ? `Turn budget: ${turnLimit.toLocaleString()}; Resume continues after the limit.` : 'No fixed turn limit.'}` + (pending ? ' Apply mode, or send a task to apply it.' : '');
    executionControls.classList.toggle('pending', pending);
    const busy = client.transcript.running || client.busy;
    executionMode.disabled = busy; goalCriteria.disabled = busy; applyModeButton.disabled = busy || !pending;
  }
  async function applyExecutionMode() {
    client.ensureIdle();
    const selected = configuration({ ...(client.config || currentConfig), mode: executionMode.value, successCriteria: goalCriteria.value });
    if (client.config?.mode === selected.mode && client.config?.successCriteria === selected.successCriteria) return;
    await client.configure(selected); currentConfig = selected;
    savePreferences(selected);
    updateExecutionMode(); updatePermissionDescription(); scheduleRender();
  }
  async function resumeTask() {
    if (!client.configured) { setTab('settings'); throw new Error('Connect a provider before resuming the task.'); }
    await applyExecutionMode(); await client.retry();
  }
  function stageRecoveredExecution(checkpoint) {
    const selected = client.clean(recoveredRunSelection(checkpoint, client.config));
    if (!selected) return false;
    executionMode.value = selected.mode; goalCriteria.value = selected.successCriteria;
    updateExecutionMode(); return true;
  }
  updateExecutionMode();
  function resizePrompt() { prompt.style.height = 'auto'; prompt.style.height = `${Math.min(160, Math.max(60, prompt.scrollHeight))}px`; }
  prompt.addEventListener('input', resizePrompt);
  prompt.addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); if (!client.transcript.running && !client.busy) run(send); }
    if (event.key === 'Escape') { event.stopPropagation(); if (client.transcript.running && !stopButton.disabled) { event.preventDefault(); stopButton.click(); } }
  });
  async function send() {
    clearNotice();
    if (!client.configured) { setTab('settings'); throw new Error('Choose your provider and connect it before sending a task.'); }
    const text = prompt.value;
    if (!text.trim()) throw new Error('Enter a task for the agent.');
    await applyExecutionMode();
    const references = await referenceComposer.prepare();
    await client.start(text, references); referenceComposer.commit(references); if (prompt.value === text) { prompt.value = ''; resizePrompt(); }
    transcript.scrollTop = transcript.scrollHeight;
  }
  async function newSession() {
    persistence?.schedule(); await persistence?.flush();
    await client.reset(); pins = []; prompt.value = ''; referenceComposer.clear(); for (const cached of timelineNodes.values()) { cached.markdown?.dispose(); cached.preview?.dispose(); } timelineNodes.clear();
    persistence?.newSession();
    timeline.replaceChildren(); clearNotice(); renderFiles(); renderContext(); setTab('chat');
  }
  async function exportHistory() { const text = await client.exportSession(); download(JSON.stringify(JSON.parse(text), null, 2), `shadow-agent-${new Date().toISOString().replace(/[:.]/g, '-')}.json`); notice('Conversation exported. API keys and authentication tokens are excluded.'); }
  const historyImport = node('input', { type: 'file', accept: '.json,application/json', hidden: true, 'aria-label': 'Import agent conversation' });
  historyImport.addEventListener('change', () => run(async () => {
    const selected = historyImport.files[0]; historyImport.value = ''; if (!selected) return;
    assertFileClean();
    if (selected.size > 24 * 1024 * 1024) throw new Error('Conversation imports are limited to 24 MiB.');
    const checkpoint = await selected.text();
    await client.importSession(checkpoint); const recoveredMode = stageRecoveredExecution(checkpoint);
    for (const cached of timelineNodes.values()) { cached.markdown?.dispose(); cached.preview?.dispose(); } timelineNodes.clear(); timeline.replaceChildren(); pins = []; fileList = []; file = null; fileDirty = false;
    filePath.value = ''; fileEditor.value = ''; fileEditor.readOnly = false; markFileClean(); renderFiles();
    persistence?.newSession(); notice(recoveredMode ? 'Conversation imported. Review its recovered mode and criteria in Conversation; connecting a provider or applying the mode will use that selection.' : 'Conversation imported. Your current provider credentials, mode and permission setting are retained.'); setTab('chat');
  })); dock.append(historyImport);

  function scheduleRender() {
    if (renderScheduled || disposed) return;
    renderScheduled = true; requestAnimationFrame(() => { renderScheduled = false; renderStatus(); if (opened) renderTimeline(); });
  }
  function renderStatus() {
    const state = client.transcript, running = state.running || client.busy;
    const progress = presentRun(state, client.config || currentConfig);
    const nextRunVersion = JSON.stringify(progress);
    if (runVersion !== nextRunVersion) {
      runVersion = nextRunVersion; runView.hidden = !progress.visible; runView.dataset.phase = progress.phase;
      runTitle.textContent = progress.title; runModeBadge.textContent = progress.modeName;
      runProgress.textContent = progress.progress; runDetail.textContent = progress.detail; runDetail.hidden = !progress.detail;
      const evidence = [];
      if (progress.criteria) evidence.push(node('h4', { text: 'Success criteria' }), node('p', { text: progress.criteria }));
      if (progress.summary) evidence.push(node('h4', { text: 'Outcome' }), node('p', { text: progress.summary }));
      if (progress.verification.length) evidence.push(node('h4', { text: 'Agent-reported verification' }), node('ul', {}, progress.verification.map(text => node('li', { text }))));
      if (progress.stopReason) evidence.push(node('p', { class: 'agent-muted', text: `Run reason: ${progress.stopReason}` }));
      runEvidence.hidden = !evidence.length;
      runEvidence.replaceChildren(node('summary', { text: progress.verification.length ? 'Outcome & verification' : 'Task details' }), ...evidence);
    }
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
    statusLabel.textContent = approvals ? `Waiting for ${approvals} approval${approvals === 1 ? '' : 's'}` : running ? String(state.status || 'Working').replace(/^./, c => c.toUpperCase()) : progress.visible ? progress.title : client.configured ? 'Ready' : client.connected ? 'Runtime connected' : 'Ready to connect';
    dock.setAttribute('aria-busy', String(running)); launcher.classList.toggle('running', running);
    launcherBadge.hidden = !running && !approvals; launcherBadge.textContent = approvals ? String(approvals) : '●';
    tabButtons.get('chat').classList.toggle('needs-attention', approvals > 0);
    sendButton.hidden = state.running; stopButton.hidden = !state.running && client.operation?.kind !== 'work'; sendButton.disabled = client.busy;
    stopButton.disabled = client.operation?.kind === 'cancel' || client.operation?.kind === 'disconnect';
    retryButton.hidden = !progress.resumable || running; retryButton.disabled = running;
    executePlanButton.hidden = !progress.executePlan || running; executePlanButton.disabled = running;
    prompt.disabled = false; updateExecutionMode();
    const activeTools = state.items.filter(item => item.kind === 'tool' && ['running', 'approval'].includes(item.status));
    activity.hidden = !running;
    activity.classList.toggle('awaiting-approval', approvals > 0);
    activityLabel.textContent = approvals ? 'Waiting for your approval' : activeTools.length ? activeTools.slice(0, 2).map(item => toolActivity(item).label).join(' · ') : state.activeAssistant ? 'Writing response' : 'Thinking';
    activityCount.textContent = state.turn?.current ? `Turn ${state.turn.current}${state.turn.maximum ? ` / ${state.turn.maximum}` : ''}` : '';
    const usage = state.usage;
    usageStrip.hidden = usage.inputTokens == null && usage.outputTokens == null;
    const cached = usage.cachedTokens ?? usage.cachedInputTokens;
    usageStrip.textContent = [usage.inputTokens == null ? null : `${formatTokens(usage.inputTokens)} input`, usage.outputTokens == null ? null : `${formatTokens(usage.outputTokens)} output`,
      cached == null ? null : `${formatTokens(cached)} cached`, usage.inferences == null ? null : `${usage.inferences} inference${usage.inferences === 1 ? '' : 's'}`].filter(Boolean).join(' · ');
    const percent = Math.min(100, state.contextTokens / state.contextLimit * 100);
    contextFill.style.width = `${percent}%`; contextMeter.classList.toggle('near-limit', percent >= 75);
    contextLabel.textContent = `${formatTokens(state.contextTokens)} / ${formatTokens(state.contextLimit)}`;
    contextMeter.setAttribute('aria-label', `${state.contextTokens.toLocaleString()} of ${state.contextLimit.toLocaleString()} context tokens. Open context management.`);
    const planning = client.config?.mode === 'plan';
    permissionChip.textContent = planning ? 'Plan · read only' : modeNames[client.config?.permissionMode || currentConfig.permissionMode] || 'Approve changes';
    permissionChip.classList.toggle('full-access', !planning && client.config?.permissionMode === 'fullAccess');
    providerChip.querySelector('span').textContent = client.configured ? `${providers.find(p => p.id === client.config.provider)?.name || client.config.provider} · ${client.config.model}` : 'Connect a provider';
    documentChip.textContent = getDocumentName() || 'No drawing open';
    connectButton.disabled = running; turnLimitReset.disabled = running; compactButton.disabled = running; pinButton.disabled = running; saveFileButton.disabled = running || !fileDirty;
    runFileButton.disabled = running || fileDirty || !file?.path || !/\.(?:m?js|cjs)$/i.test(file.path);
    deleteFileButton.disabled = running || fileDirty || !file?.path;
    runToolButton.disabled = running || !toolName.value.trim();
    if (selectedTab === 'context') renderContext();
    liveStatus.textContent = approvals ? statusLabel.textContent : progress.visible ? `${progress.modeName}: ${progress.title}` : 'Agent is idle.';
  }
  function renderTimeline() {
    const state = client.transcript, nearBottom = transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight < 100;
    empty.hidden = state.items.length > 0;
    const liveIds = new Set(state.items.map(item => item.id));
    for (const [id, cached] of timelineNodes) if (!liveIds.has(id)) { cached.markdown?.dispose(); cached.preview?.dispose(); cached.element.remove(); timelineNodes.delete(id); }
    for (const item of state.items) {
      const version = `${item.version || ''}:${item.status || ''}:${item.streaming}:${item.text?.length || 0}`;
      let cached = timelineNodes.get(item.id);
      if (!cached) {
        const element = node('article', { class: `agent-message agent-message-${item.kind}` });
        cached = { element, version: null }; timelineNodes.set(item.id, cached); timeline.append(element);
      }
      if (cached.item !== item) { cached.markdown?.dispose(); cached.preview?.dispose(); cached.markdown = null; cached.element.replaceChildren(); cached.element.className = `agent-message agent-message-${item.kind}`; cached.body = null; cached.version = null; cached.item = item; }
      if (cached.version === version) continue;
      cached.version = version;
      if (item.kind === 'tool') { cached.preview?.dispose(); cached.preview = renderToolCard(cached.element, item); }
      else if (item.kind === 'assistant' || item.kind === 'user') {
        if (!cached.body) {
          const role = node('div', { class: 'agent-message-role' }, [item.kind === 'assistant' ? glyph('agent') : node('span', { class: 'agent-user-avatar', text: 'You' }), node('strong', { text: item.kind === 'assistant' ? 'Shadow Agent' : 'You' })]);
          if (item.time) { const date = new Date(item.time); if (!Number.isNaN(date.valueOf())) role.append(node('time', { text: date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }), datetime: date.toISOString() })); }
          role.append(button('Copy message', () => copy(item.text || ''), { icon: 'copy', compact: true }));
          cached.body = node('div', { class: 'agent-message-body' }); cached.element.append(role, cached.body);
          if (item.references?.length) cached.element.append(referenceChips(item.references, { historical: true }));
        }
        cached.body.classList.toggle('streaming', item.streaming === true);
        cached.markdown ??= createMarkdownView(cached.body, { onCopy: copy, onCode: item.kind === 'assistant' ? codeToFile : undefined, headingOffset: 2 });
        cached.markdown.update(item.text || '', { streaming: item.streaming === true });
      } else {
        cached.element.className = `agent-message agent-message-${item.kind} ${item.type || ''}`;
        cached.element.replaceChildren(node('span', { class: 'agent-event-mark', text: item.kind === 'error' ? '!' : item.type === 'compacted' ? '◇' : '·' }), node('span', { text: item.text }));
        if (item.data && Object.keys(item.data).length) cached.element.append(node('details', { class: 'agent-event-details' }, [node('summary', { text: 'Details' }), code(item.data)]));
      }
    }
    if (!state.items.length) transcript.scrollTop = 0;
    else if (nearBottom) transcript.scrollTop = transcript.scrollHeight;
    jumpLatest.hidden = transcript.scrollHeight - transcript.scrollTop - transcript.clientHeight < 100;
  }
  function renderToolCard(element, item) {
    const previous = element.querySelector('details'), wasOpen = previous?.open;
    const detail = node('details', { class: `agent-tool-card ${item.status || ''}` });
    detail.open = wasOpen ?? ['approval', 'error'].includes(item.status);
    const activity = toolActivity(item);
    detail.append(node('summary', {}, [node('span', { class: `agent-tool-indicator ${item.status || ''}`, text: item.status === 'complete' ? '✓' : item.status === 'error' ? '!' : item.status === 'approval' ? '?' : '◇' }),
      node('span', { class: 'agent-tool-label', title: item.name }, [node('strong', { text: activity.label }), activity.detail ? node('small', { text: activity.detail }) : null]),
      node('span', { class: 'grow' }), node('span', { class: 'agent-tool-status', text: item.status === 'approval' ? 'Approval needed' : item.status === 'complete' ? 'Done' : item.status || 'Queued' }),
      item.durationMs !== undefined ? node('small', { text: formatDuration(item.durationMs) }) : null]));
    const body = node('div', { class: 'agent-tool-body' });
    let populated = false, preview = null, closed = false;
    function populate() {
    if (populated || !detail.open) return; populated = true;
    body.append(node('code', { class: 'agent-tool-name', text: item.name }));
    if (item.text) body.append(node('p', { text: item.text }));
    if (item.arguments !== undefined) body.append(code(item.arguments, 'Arguments'));
    if (item.result !== undefined) body.append(item.name === 'workspace_run_javascript' ? codeExecution(item.result) : code(item.result, 'Result'));
    if (item.status === 'approval') {
      const allow = button('Approve once', () => approval(true), { icon: 'check', class: 'agent-primary' });
      const deny = button('Deny', () => approval(false), { class: 'agent-secondary' });
      let previewReady = item.name !== 'studio_apply_edit_preview';
      const approval = async approved => {
        if (approved && !previewReady) return;
        allow.disabled = true; deny.disabled = true;
        try { await client.approve(item.requestId, approved); notice(approved ? 'Tool execution approved.' : 'Tool execution denied.'); }
        catch (error) { allow.disabled = !previewReady; deny.disabled = false; notice(error.message, 'error'); }
      };
      if (!previewReady) {
        allow.disabled = true;
        preview = mountEditApprovalPreview({ container: body, previewId: item.arguments?.request?.previewId,
          load: drawingPreview, open: openDrawingPreview,
          onReady: ready => { previewReady = ready; if (!closed) allow.disabled = !ready; } });
      }
      body.append(node('div', { class: 'agent-approval-actions' }, [allow, deny]));
    }
    }
    detail.addEventListener('toggle', populate); populate();
    detail.append(body); element.replaceChildren(detail);
    return { dispose() { closed = true; preview?.dispose(); } };
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
  const browserMcpStatus = node('p', { class: 'agent-auth-status', role: 'status', text: 'Browser MCP is disabled.' });
  const enableBrowserMcp = button('Enable browser MCP', () => run(() => browserMcp.enable()), { icon: 'tools', class: 'agent-primary' });
  const disableBrowserMcp = button('Disable browser MCP', () => run(() => browserMcp.disable()), { disabled: true });
  const browserMcpSection = node('section', { class: 'agent-browser-mcp' }, [node('h3', { text: 'Browser MCP' }),
    node('p', { text: 'Expose this drawing’s official MCP server directly inside the browser. Compatible browser agents use WebMCP when available; trusted integrations can connect a MessagePort. Calls follow the same tool permissions as your coding agent.' }),
    node('p', {}, ['ChatGPT Site tools can control this page in a supported ChatGPT browser. An external browser agent does not need a Studio provider key. ', node('a', { href: 'https://learn.chatgpt.com/docs/webmcp', target: '_blank', rel: 'noopener noreferrer', text: 'Site tools support ↗' })]),
    node('div', { class: 'agent-row' }, [enableBrowserMcp, disableBrowserMcp]), browserMcpStatus,
    node('details', {}, [node('summary', { text: 'Integration details' }), code('const connection = await shadowStudioBrowserMcp.createConnection();\n// connection.port carries MCP JSON-RPC messages.\n// Initialize the MCP session before listing or calling tools.\n// Close the session when the integration disconnects.\nconnection.close();', 'Browser MessagePort API'),
      node('p', { class: 'agent-muted', text: 'Enable access explicitly for each drawing. Changing drawings revokes the previous connection. This page does not open an HTTP listener; native desktop MCP clients can use the optional companion in Settings.' })])]);
  browserMcpSection.hidden = !browserRuntime;
  panels.get('tools').append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'MCP tools' }),
    node('p', { text: 'Discover the same tool contracts the agent uses. Each invocation returns its native result and follows the active permission policy.' })]),
    browserMcpSection,
    node('div', { class: 'agent-search-row' }, [toolSearch, button('Refresh MCP tools', () => run(loadTools), { icon: 'refresh', compact: true })]),
    node('div', { class: 'agent-catalog-caption' }, [toolCount]), toolConsole, toolListNode);
  browserMcp.addEventListener('change', event => {
    const state = event.detail;
    enableBrowserMcp.disabled = state.enabled || state.activating; disableBrowserMcp.disabled = !state.enabled && !state.activating;
    browserMcpStatus.textContent = state.error || (state.activating ? 'Enabling browser MCP…' : state.enabled
      ? `Enabled for ${getDocumentName() || state.documentId} · ${state.messagePorts} MessagePort connection${state.messagePorts === 1 ? '' : 's'}${state.webMcpRegistered ? ` · ${state.webMcpRegistered} WebMCP tools` : ' · WebMCP is unavailable in this browser; MessagePort transport is ready.'}`
      : 'Browser MCP is disabled.');
  });
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
  const runFileButton = button('Run JavaScript', () => run(async () => {
    assertFileClean(); if (!file?.path || !/\.(?:m?js|cjs)$/i.test(file.path)) throw new Error('Open a saved JavaScript file to run it in the browser worker.');
    const result = await client.callTool('workspace_run_javascript', { path: file.path, arguments: [], timeoutSeconds: 30 });
    fileExecution.replaceChildren(codeExecution(result)); fileExecution.hidden = false;
    await loadFiles(); notice(result.isError ? 'The browser worker reported an error. Inspect its result below.' : 'Browser JavaScript execution completed.', result.isError ? 'error' : 'info');
  }), { icon: 'play', disabled: true, hidden: !browserRuntime });
  const deleteFileButton = button('Delete file', () => run(async () => {
    assertFileClean(); if (!file?.path) throw new Error('Open a workspace file first.');
    const current = file; fileDelete.hidden = false;
    fileDelete.replaceChildren(node('p', { text: `Delete ${current.path} from the browser workspace? Saved checkpoints and connected folder copies are retained.` }), node('div', { class: 'agent-row' }, [
      button('Delete this file', () => run(async () => { await client.applyFiles([{ path: current.path, content: null, expectedVersion: current.version }]); fileDelete.hidden = true; await newFile(); await loadFiles(); notice(`Deleted ${current.path}.`); }), { class: 'agent-danger' }),
      button('Keep file', () => { fileDelete.hidden = true; })]));
  }), { icon: 'trash', compact: true, disabled: true });
  const fileExecution = node('div', { class: 'agent-file-execution', hidden: true }), fileDelete = node('div', { class: 'agent-file-delete', hidden: true });
  const fileConflict = node('div', { class: 'agent-file-conflict', hidden: true });
  const fileDiff = node('details', { class: 'agent-editor-diff', hidden: true }, [node('summary', { text: 'Review unsaved changes' })]);
  const fileTransferHost = node('div', { class: 'agent-file-transfer-host' });
  const fileSearch = node('input', { type: 'search', placeholder: 'Filter files by path…', 'aria-label': 'Filter workspace files' });
  const fileCounter = node('span', { class: 'agent-muted', text: '0 files' });
  filesPanel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: browserRuntime ? 'Browser coding workspace' : 'Agent workspace' }), node('p', { text: browserRuntime ? 'Versioned project files with automatic local recovery, reviewed imports, folder access, and JavaScript execution. Files run as async function bodies with a virtual workspace; Node.js and operating-system commands require a native host.' : 'Versioned project files with automatic local recovery, reviewed imports, and folder access. The runtime rejects writes against a newer version.' })]),
    node('div', { class: 'agent-row agent-file-toolbar' }, [button('New file', () => run(newFile), { icon: 'plus' }), button('Import file', () => fileImport.click(), { icon: 'upload' }),
      button('Runnable example', () => codeToFile("const files = workspace.list();\nconsole.log('Workspace files:', files.length);\nconst report = { files, argument: args[0] ?? 'browser' };\nworkspace.write('reports/browser-check.json', JSON.stringify(report, null, 2));\nassert(workspace.read('reports/browser-check.json').includes('files'));\nreturn { passed: true, files: files.length };", 'javascript examples/browser-check.mjs'), { icon: 'play', hidden: !browserRuntime }),
      node('span', { class: 'grow' }), button('Refresh workspace files', () => run(loadFiles), { icon: 'refresh', compact: true })]),
    fileTransferHost, node('div', { class: 'agent-search-row' }, [fileSearch]), node('div', { class: 'agent-file-count' }, [fileCounter]),
    fileTree, node('div', { class: 'agent-file-detail' }, [field('Path', filePath), node('div', { class: 'agent-row' }, [fileReceipt]), fileEditor,
      node('div', { class: 'agent-row' }, [saveFileButton, runFileButton, button('Revert', () => run(async () => { if (file?.path) await openFile(file.path, true); else { fileEditor.value = ''; markFileClean(); } })), node('span', { class: 'grow' }),
        button('Download file', () => run(() => download(fileEditor.value, safeFilePath(filePath.value).split('/').at(-1), 'text/plain;charset=utf-8')), { icon: 'download', compact: true }), button('Pin file', () => run(pinFile), { icon: 'pin', compact: true }), deleteFileButton]), fileDelete, fileConflict, fileDiff, fileExecution ]));
  const fileTransfer = mountBrowserFileTransfer({ client, host: fileTransferHost, assertFileClean, onImported: async () => {
    if (file?.path) await openFile(file.path, true); await loadFiles(); persistence?.schedule();
  }, notice });
  function editorChanged() {
    fileDirty = true; saveFileButton.disabled = client.transcript.running || client.busy;
    runFileButton.disabled = true; deleteFileButton.disabled = true;
    fileReceipt.textContent = `${file?.version != null ? `Version ${file.version} · ` : ''}Unsaved changes`;
    fileDiff.hidden = false; if (fileDiff.open) updateEditorDiff(); persistence?.schedule();
  }
  fileEditor.addEventListener('input', editorChanged); filePath.addEventListener('input', editorChanged);
  fileSearch.addEventListener('input', renderFiles);
  function updateEditorDiff() { while (fileDiff.children.length > 1) fileDiff.lastChild.remove(); fileDiff.append(renderFileDiff(file?.content || '', fileEditor.value)); }
  fileDiff.addEventListener('toggle', () => { if (fileDiff.open) updateEditorDiff(); });
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
    file = null; filePath.value = safeFilePath(selected.name); fileEditor.value = content; fileEditor.readOnly = false; editorChanged(); fileReceipt.textContent = 'Imported · save to create workspace file';
  })); dock.append(fileImport);
  function assertFileClean() { if (fileDirty) throw new Error('Save or revert your current file before opening another.'); }
  function markFileClean() { fileDirty = false; saveFileButton.disabled = true; fileDiff.hidden = true; fileDelete.hidden = true; fileConflict.hidden = true; fileReceipt.textContent = file ? `Version ${file.version ?? '—'} · ${fileEditor.value.length.toLocaleString()} characters` : 'New file'; persistence?.schedule(); scheduleRender(); }
  async function newFile() { assertFileClean(); file = null; filePath.value = ''; fileEditor.value = ''; fileEditor.readOnly = false; fileExecution.hidden = true; markFileClean(); filePath.focus(); }
  async function loadFiles() { fileList = await client.files(); if (!Array.isArray(fileList)) throw new Error('The runtime returned an invalid workspace listing.'); renderFiles(); }
  function renderFiles() {
    const filter = fileSearch.value.trim().toLowerCase(), entries = fileList.filter(entry => String(typeof entry === 'string' ? entry : entry.path).toLowerCase().includes(filter));
    fileCounter.textContent = `${entries.length}${filter ? ` / ${fileList.length}` : ''} files · ${fileList.reduce((sum, entry) => sum + (entry.characters || 0), 0).toLocaleString()} characters`;
    fileTree.replaceChildren(...entries.map(entry => {
      const path = typeof entry === 'string' ? entry : entry.path;
      return button(path, () => run(() => openFile(path)), { icon: 'file', class: file?.path === path ? 'selected' : '', title: `${path}${entry.version != null ? ` · version ${entry.version}` : ''}` });
    }));
    if (!entries.length) fileTree.append(node('p', { class: 'agent-muted', text: fileList.length ? 'No files match this path filter.' : 'No workspace files yet. Import a project, save a code block, or ask the agent to create a runnable script.' }));
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
    if (file?.conflict && file.path === path) { await reviewFileConflict(path); return; }
    const expected = file?.path === path ? file.version ?? 0 : 0;
    let result;
    try { result = await client.writeFile(path, fileEditor.value, expected); }
    catch (error) { if (/file changed|current version|changed or no longer/i.test(error.message)) { await reviewFileConflict(path); return; } throw error; }
    file = { path, content: fileEditor.value, ...(result || {}) }; markFileClean(); await loadFiles(); notice(`Saved ${path}.`);
  }
  async function reviewFileConflict(path) {
    const current = (await client.exportFiles()).find(entry => entry.path === path) || { path, content: '', version: 0 };
    fileConflict.replaceChildren(node('h3', { text: 'The workspace file changed' }), node('p', { text: `${path} has a newer version than this editor. Review the current contents against your draft. Accepting the current version prepares the next Save file action; it does not write the file.` }),
      renderFileDiff(current.content, fileEditor.value), button('Use reviewed version for next save', () => {
        file = current; fileConflict.hidden = true; editorChanged(); notice('The current file version is now the editor baseline. Save file applies your reviewed draft with a fresh version check.');
      }, { icon: 'check', class: 'agent-secondary' }));
    fileConflict.hidden = false; fileReceipt.textContent = 'Version conflict · draft retained';
  }
  function codeToFile(content, info) {
    run(() => {
      assertFileClean(); const pieces = info.split(/\s+/), ext = { cs: 'cs', csharp: 'cs', js: 'mjs', javascript: 'mjs', ts: 'ts', json: 'json', xml: 'xml', xaml: 'axaml', py: 'py', python: 'py', md: 'md', markdown: 'md', yaml: 'yml', yml: 'yml', html: 'html', css: 'css', sh: 'sh' }[pieces[0]] || 'txt';
      const candidate = pieces.find(part => /[/.]/.test(part)) || `generated-${Date.now()}.${ext}`;
      file = null; filePath.value = safeFilePath(candidate); fileEditor.value = content; fileEditor.readOnly = false; editorChanged(); fileReceipt.textContent = 'Code artifact · save to workspace'; setTab('files');
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
    const entries = [['Input', usage.inputTokens ?? usage.promptTokens], ['Output', usage.outputTokens ?? usage.completionTokens], ['Cache read', usage.cachedInputTokens ?? usage.cachedTokens],
      ['Cache written', usage.cacheWriteTokens], ['Reasoning', usage.reasoningTokens], ['Inferences', usage.inferences ?? usage.turns ?? usage.modelTurns]];
    usageGrid.replaceChildren(...entries.map(([label, value]) => node('div', {}, [node('strong', { text: value == null ? '—' : Number(value).toLocaleString() }), node('span', { text: label })])));
    if (usage.firstTokenMilliseconds != null) usageGrid.append(node('div', {}, [node('strong', { text: formatDuration(usage.firstTokenMilliseconds) }), node('span', { text: 'Last first token' })]));
    if (state.pins?.length || state.pinsKnown) pins = state.pins;
    pinList.replaceChildren(...pins.map(pin => node('article', { class: 'agent-pin' }, [node('div', { class: 'agent-row' }, [glyph('pin'), node('strong', { text: String(pin.text || pin.content || '').split('\n')[0].slice(0, 80) || 'Pinned instruction' }), node('span', { class: 'grow' }),
      button('Remove pin', () => run(async () => { await client.unpin(pin.id); pins = pins.filter(p => p.id !== pin.id); renderContext(); }), { icon: 'close', compact: true, disabled: client.transcript.running })]),
      node('details', {}, [node('summary', { text: `${String(pin.text || pin.content || '').length.toLocaleString()} characters` }), node('pre', { text: pin.text || pin.content || '' })])] )));
    if (!pins.length) pinList.append(node('p', { class: 'agent-muted', text: 'Pin project rules, design intent, or a current drawing snapshot. Pins are retained during compaction.' }));
  }

  // Durable recovery stays in IndexedDB; provider credentials never enter it.
  const saveStatus = node('p', { class: 'agent-storage-status', role: 'status', text: 'Autosave on this device · credentials stay in memory.' });
  const savedSelect = node('select', { 'aria-label': 'Saved browser workspaces' }, [node('option', { value: '', text: 'Loading saved workspaces…' })]);
  const savedDetails = node('p', { class: 'agent-muted', text: 'Saved sessions contain conversation, plans, pinned context, files, and any editor draft.' });
  const restoreButton = button('Restore saved workspace', () => run(async () => {
    assertFileClean(); client.ensureIdle(); if (!savedSelect.value) throw new Error('Choose a saved workspace.');
    persistence.schedule(); await persistence.flush();
    const selected = savedSelect.value;
    const restored = await persistence.restore(selected);
    for (const cached of timelineNodes.values()) { cached.markdown?.dispose(); cached.preview?.dispose(); } timelineNodes.clear(); timeline.replaceChildren(); pins = []; await loadFiles();
    if (restored.draft?.basePath) {
      const existing = (await client.exportFiles()).find(entry => entry.path === restored.draft.basePath);
      if (existing && existing.content === restored.draft.baseContent) file = existing;
    }
    if (restored.draft) editorChanged();
    notice(client.config === null ? 'Saved conversation and workspace restored. Review the recovered mode and criteria in Conversation, then reconnect a provider or apply the mode.' : 'Saved conversation and workspace restored. Current provider credentials, mode, criteria and permissions are retained.');
    await refreshSaved(); setTab(restored.draft ? 'files' : 'chat');
  }), { icon: 'refresh', class: 'agent-primary' });
  const forgetSaved = button('Forget selected saved copy', () => run(async () => {
    if (!savedSelect.value) throw new Error('Choose a saved workspace to remove.');
    await persistence.remove(savedSelect.value); await refreshSaved(); notice('Saved browser copy removed. The active workspace is retained.');
  }), { icon: 'trash' });
  const persistStorage = button('Keep browser storage', () => run(async () => {
    if (!navigator.storage?.persist) throw new Error('This browser does not expose a persistent storage request. Download a checkpoint as a portable backup.');
    const granted = await navigator.storage.persist();
    notice(granted ? 'The browser granted persistent storage for this site.' : 'The browser did not grant persistent storage. Autosave remains available; download a portable checkpoint to keep another copy.');
  }), { icon: 'storage' });
  const saveNow = button('Save checkpoint now', () => run(async () => { client.ensureIdle(); persistence.schedule(); await persistence.flush(); await refreshSaved(); }), { icon: 'check' });
  const recoverySection = node('section', { class: 'agent-recovery' }, [node('h3', { text: 'Browser recovery' }), saveStatus,
    node('div', { class: 'agent-row' }, [saveNow, persistStorage]), field('Saved workspaces', savedSelect), savedDetails,
    node('div', { class: 'agent-row' }, [restoreButton, button('Refresh saved workspaces', () => run(refreshSaved), { icon: 'refresh', compact: true }), forgetSaved]),
    node('p', { class: 'agent-muted', text: 'The latest 10 saved sessions are retained within a 64 MiB budget. Restore opens a separate saved copy so tabs cannot overwrite one another. Browser data can be cleared or evicted; export a conversation checkpoint for a portable backup. Save drawing files separately in Studio; agent checkpoints do not include DXF drawings.' })]);
  contextPanel.append(recoverySection);
  let savedRecords = [], savedRefreshing = false;
  async function refreshSaved() {
    if (!persistence || savedRefreshing) return;
    savedRefreshing = true;
    try {
      const chosen = savedSelect.value; savedRecords = await persistence.store.list();
      savedSelect.replaceChildren(...(savedRecords.length ? savedRecords.map(record => node('option', { value: record.id, text: `${new Date(record.savedAt).toLocaleString()} · ${record.label}` })) : [node('option', { value: '', text: 'No saved browser workspaces yet' })]));
      if (savedRecords.some(record => record.id === chosen)) savedSelect.value = chosen;
      savedSelection();
    } catch (error) { saveStatus.textContent = error.message; saveStatus.classList.add('error'); }
    finally { savedRefreshing = false; }
  }
  function savedSelection() {
    const record = savedRecords.find(value => value.id === savedSelect.value);
    savedDetails.textContent = record ? `${record.files} files · ${record.messages} messages · ${(record.bytes / 1024).toFixed(0)} KiB${record.hasDraft ? ' · unsaved editor draft included' : ''}` : 'A completed conversation or a workspace file creates your first local recovery point.';
    restoreButton.disabled = !record || client.busy || client.transcript.running; forgetSaved.disabled = !record;
  }
  savedSelect.addEventListener('change', savedSelection);
  persistence = new BrowserSessionPersistence({ client, label: () => client.lastPrompt || getDocumentName() || 'Agent workspace',
    getDraft: () => fileDirty ? { path: filePath.value, content: fileEditor.value, basePath: file?.path, baseVersion: file?.version, baseContent: file?.content } : null,
    restoreDraft: (draft, record) => {
      if (record) stageRecoveredExecution(record.checkpoint);
      file = draft?.basePath ? { path: draft.basePath, version: draft.baseVersion, content: draft.baseContent || '', conflict: true } : null;
      filePath.value = draft?.path || ''; fileEditor.value = draft?.content || ''; fileEditor.readOnly = false;
      if (draft) editorChanged(); else markFileClean();
    } });
  persistence.addEventListener('change', event => {
    saveStatus.textContent = event.detail.message; saveStatus.classList.toggle('error', event.detail.state === 'error');
    if (event.detail.state === 'saved') void refreshSaved();
  });
  client.addEventListener('busy', savedSelection); void refreshSaved();

  // Provider credentials and subscription companion ------------------------
  const settingsPanel = panels.get('settings'), providerSelect = node('select', { 'aria-label': 'Model provider' }, providers.map(provider => node('option', { value: provider.id, text: provider.name })));
  const modelInput = node('input', { type: 'text', 'aria-label': 'Model ID', spellcheck: false, autocomplete: 'off' });
  const apiKeyInput = node('input', { type: 'password', 'aria-label': 'API key or companion access token', autocomplete: 'off', spellcheck: false, 'data-agent-private': '' });
  const endpointInput = node('input', { type: 'url', 'aria-label': 'Provider endpoint or local companion URL', placeholder: 'Provider default', spellcheck: false, autocomplete: 'off' });
  const keyLabel = node('span', { class: 'agent-field-label', text: 'API key' }), keyHint = node('small', { text: 'Held in memory for this page session. Not saved or included in conversation exports.' });
  const permissionSelect = node('select', { 'aria-label': 'Agent permission mode' }, Object.entries(modeNames).map(([value, text]) => node('option', { value, text })));
  const permissionDescription = node('p', { class: 'agent-permission-description' });
  const routingNote = node('p', { class: 'agent-routing-note', hidden: true });
  const providerRuntimeNote = node('p', { class: 'agent-provider-runtime', hidden: true, text: 'The official OpenAI, Anthropic and Google .NET clients run directly in this browser. Keys stay in this page’s memory. Custom provider endpoints must allow browser requests from this site.' });
  const providerCacheNote = node('p', { class: 'agent-muted' });
  const limits = node('div', { class: 'agent-limits' });
  for (const [key, label, min, max, help] of [
    ['maxTurns', 'Max model turns', 0, 10000, '0 continues until the task completes or needs attention. A positive budget pauses with Resume available.'],
    ['maxNoProgressTurns', 'No-progress turns before pause', 2, 100, 'Pause after repeated turns without useful progress. Completed tool work is retained.'],
    ['maxOutputTokens', 'Output tokens / turn', 256, 128000, 'Per response. The agent continues automatically after output limits.'],
    ['contextWindowTokens', 'Model context window', 4096, 4000000, 'Context compacts as needed while retaining the task, plan and workspace.']]) {
    const input = node('input', { type: 'number', min, max, step: 1, 'aria-label': label }); formFields[key] = input; input.value = currentConfig[key]; limits.append(field(label, input, help));
  }
  const turnLimitReset = button('Use no fixed turn limit', () => run(async () => {
    client.ensureIdle();
    const selected = configuration({ ...(client.config || currentConfig), maxTurns: 0 });
    await client.configure(selected); currentConfig = selected; formFields.maxTurns.value = 0;
    savePreferences(selected); updateExecutionMode(); scheduleRender();
    notice('The agent will continue until completion, a blocker, cancellation, or its no-progress budget.');
  }), { icon: 'retry', class: 'agent-secondary' });
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
    node('h3', { text: 'Sign in with ChatGPT' }), node('p', { text: 'ChatGPT plan sign-in currently requires the optional local companion because OpenAI’s supported open-source flow uses a 127.0.0.1 callback. API-key agents for OpenAI, Anthropic and Google run directly in this browser.' }),
    code('dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- --origin https://wieslawsoltes.github.io', 'Start the local companion'),
    node('p', { class: 'agent-muted', text: 'Use this page’s origin for a local or custom deployment. Copy the companion access token from its terminal into the key field above.' }),
    node('div', { class: 'agent-row' }, [signInButton, authCheckButton, signOutButton]), signInLink, signInStatus
  ]);
  const connectButton = button('Connect provider', () => run(connectProvider), { icon: 'check', class: 'agent-primary' });
  const connectionInfo = node('span', { class: 'agent-muted', text: 'No provider configured' });
  settingsPanel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'Provider & permissions' }), node('p', { text: browserRuntime ? 'Run coding agents with the official .NET clients in this browser. Model IDs, API tokens, permissions, and context limits remain fully editable.' : 'Run coding agents with the official .NET provider clients. Model IDs, API tokens, permissions, and context limits remain fully editable.' }),
    node('div', { class: 'agent-browser-features', hidden: !browserRuntime }, [node('span', { text: '3 direct providers' }), node('span', { text: 'Browser workspace' }), node('span', { text: 'JavaScript + WebAssembly' }), node('span', { text: 'Browser MCP' })])]),
    node('div', { class: 'agent-settings-form' }, [field('Provider', providerSelect), field('Model ID', modelInput, 'Use a model with tool calling. Model availability and context limits depend on your provider account.'),
      node('label', { class: 'agent-field' }, [keyLabel, node('div', { class: 'agent-key-field' }, [apiKeyInput, button('Disconnect and clear credential', () => run(async () => {
        await client.disconnect();
        connectionInfo.textContent = 'Disconnected · runtime credential cleared'; scheduleRender();
      }), { icon: 'close', compact: true })]), keyHint]),
      field('Endpoint / proxy (optional)', endpointInput, 'Blank uses the provider default. Custom endpoints receive the credential and request content.'), routingNote,
      providerRuntimeNote, providerCacheNote, companionPanel, node('hr'), field('Tool permissions', permissionSelect), permissionDescription,
      button('Apply permissions', () => run(async () => {
        await client.configure({ ...(client.config || currentConfig), permissionMode: permissionSelect.value });
        currentConfig.permissionMode = permissionSelect.value; notice(`Tool policy applied: ${modeNames[permissionSelect.value]}.`); scheduleRender();
      }), { icon: 'check', class: 'agent-secondary' }),
      node('details', { class: 'agent-advanced-settings' }, [node('summary', { text: 'Limits & context budget' }), limits, turnLimitReset]),
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
    await browserMcp.disable();
    await client.companionDisconnect(); nativeToolCount = 0; bridgeToken.value = '';
    bridgeStatus.textContent = 'Local tools and external MCP are disconnected.'; await loadTools();
  }));
  const nativeConnect = button('Connect local tools', () => run(connectNativeTools), { icon: 'file', class: 'agent-secondary' });
  let nativeToolCount = 0;
  async function connectNativeTools(endpoint = bridgeEndpoint.value, token = bridgeToken.value) {
    nativeConnect.disabled = true;
    try {
      client.ensureIdle(); await browserMcp.disable();
      const result = await client.companionConnect(endpoint, token);
      // A catalog registration belongs to one explicit connection. Re-register
      // after replacing native tools so external assistants never see stale capabilities.
      mcpBridge.disconnect();
      nativeToolCount = result.toolCount ?? result.tools?.length ?? 0;
      bridgeStatus.textContent = nativeToolCount ? `Local workspace connected · ${nativeToolCount} native tools.` : 'Companion connected. Start it with --workspace for native files; add --allow-processes for builds, tests, and Git.';
      await loadTools(); return result;
    } finally { nativeConnect.disabled = false; }
  }
  const externalSection = node('section', { class: 'agent-companion agent-external-mcp' }, [node('h3', { text: 'Local tools & external MCP' }),
    node('p', { text: 'Optional native support adds operating-system processes, dotnet builds, terminal Git, and an HTTP MCP endpoint for desktop coding assistants. Browser providers, durable project files, folder import/export, JavaScript execution, and browser MCP work without this companion.' }),
    code('dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- --origin https://wieslawsoltes.github.io --workspace /path/to/project --allow-processes', 'Start a project companion'),
    node('p', { class: 'agent-muted', text: 'Omit --allow-processes to grant native file tools without command execution. Use this page’s origin for a local or custom deployment.' }),
    field('Companion endpoint', bridgeEndpoint), field('Companion access token', bridgeToken, 'Kept in memory. Sent in authenticated local requests and the first WebSocket message, never in a URL.'),
    button('Use ChatGPT companion settings', () => run(() => {
      if (providerSelect.value !== 'chatgpt') throw new Error('Choose ChatGPT subscription to copy its companion connection.');
      bridgeEndpoint.value = endpointInput.value; bridgeToken.value = apiKeyInput.value;
    }), { icon: 'copy' }),
    node('div', { class: 'agent-row' }, [nativeConnect, bridgeConnect, bridgeDisconnect]), bridgeStatus,
    code('MCP endpoint: http://127.0.0.1:5091/mcp\nAuthorization: Bearer <companion access token>', 'External assistant connection'),
    node('p', { class: 'agent-muted', text: 'The connection is bound to this drawing. Changing drawing tabs disconnects it. Reconnect explicitly after a connection loss; tool edits are never replayed.' })]);
  settingsPanel.append(node('details', { class: 'agent-optional-native' }, [node('summary', { text: 'Optional native tools & desktop MCP' }), externalSection]));
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
    providerRuntimeNote.hidden = subscription || globalThis.shadowBrowser?.runtime !== 'blazor-webassembly';
    providerCacheNote.textContent = ({ openai: 'Prompt caching uses a stable session key and the provider’s automatic cache. Cache eligibility and retention depend on the selected model.', anthropic: 'Prompt caching uses a rolling five-minute cache breakpoint. Cache writes and reads have different provider rates.', google: 'Supported Gemini models use automatic implicit caching. Explicit paid cache resources are not created.', chatgpt: 'ChatGPT uses stable conversation prefixes. Cache usage is shown when reported by the subscription service.' })[provider.id] || '';
    companionPanel.hidden = !subscription;
    if (subscription && !endpointInput.value) endpointInput.value = 'http://127.0.0.1:5091';
    connectButton.querySelector('span').textContent = subscription ? 'Connect ChatGPT companion' : 'Connect provider'; updatePermissionDescription(); updateRouting();
  }
  function updatePermissionDescription() {
    const value = permissionSelect.value;
    permissionDescription.textContent = value === 'readOnly' ? 'The agent can inspect tools, documents, and model data. Tools that change drawing or workspace state are denied.' : value === 'fullAccess' ? 'The agent can execute all registered tools without individual approvals, including document edits, workspace file writes, and Studio control. Changes run in the current Studio workspace.' : 'Read tools run immediately. You approve each state-changing tool with its exact arguments before execution.';
    if (client.config?.mode === 'plan') permissionDescription.textContent = 'Plan mode is active: agent mutations remain blocked, including with Full access. Read-only investigation and plan publication are available. The selected permission policy applies when you switch to Normal or Goal.';
    permissionDescription.classList.toggle('full-access', value === 'fullAccess' && client.config?.mode !== 'plan');
  }
  function updateRouting() {
    routingNote.hidden = !endpointInput.value.trim();
    if (!routingNote.hidden) routingNote.textContent = providerSelect.value === 'chatgpt' ? 'Requests and the companion access token will be sent to the local companion. Subscription OAuth credentials stay in the companion.' : 'Your API key and conversation requests will be sent to this custom endpoint. Use an endpoint you control or trust.';
  }
  function readConfiguration() {
    return configuration({ provider: providerSelect.value, model: modelInput.value, apiKey: apiKeyInput.value, endpoint: endpointInput.value,
      permissionMode: permissionSelect.value, mode: executionMode.value, successCriteria: goalCriteria.value,
      ...Object.fromEntries(Object.entries(formFields).map(([key, input]) => [key, input.value])) });
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
      updateExecutionMode(); updatePermissionDescription();
      savePreferences(config);
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
  client.addEventListener('disconnected', () => {
    void browserMcp.disable().catch(report);
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
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') void persistence.flush().catch(() => {}); });
  window.addEventListener('beforeunload', event => {
    if (client.transcript.running || externalAgents.running || persistence.pending || persistence.saving) { event.preventDefault(); event.returnValue = ''; }
  });
  window.addEventListener('pagehide', event => {
    mcpBridge.disconnect(); void browserMcp.disable().catch(() => {});
    if (!event.persisted) {
      externalAgents.dispose(); referenceComposer.dispose();
      for (const cached of timelineNodes.values()) { cached.markdown?.dispose(); cached.preview?.dispose(); }
      disposed = true; authGeneration++; if (authTimer) clearTimeout(authTimer);
      // Saves are normally completed after each idle change and on visibility
      // loss. A final best-effort flush never blocks page navigation.
      void persistence.flush().catch(() => {}).finally(() => { persistence.dispose(); fileTransfer.dispose(); client.dispose(); });
      apiKeyInput.value = ''; bridgeToken.value = ''; currentConfig.apiKey = '';
    }
  });
  window.addEventListener('pageshow', event => { if (event.persisted && connectedOnce) client.schedule(0); });
  return Object.freeze({ show, hide, toggle: () => opened ? hide() : show(), documentChanged: () => { referenceComposer.changed(); externalAgents.documentChanged(); mcpBridge.documentChanged(); void browserMcp.documentChanged().catch(report); scheduleRender(); }, client,
    snapshot: () => ({ open: opened, tab: selectedTab, status: client.transcript.status, running: client.transcript.running, provider: client.config?.provider, model: client.config?.model, permissionMode: client.config?.permissionMode, mode: client.config?.mode,
      run: client.transcript.run ? { phase: client.transcript.run.phase, turnCount: client.transcript.run.turnCount, totalTurns: client.transcript.run.totalTurns,
        continuationCount: client.transcript.run.continuationCount, outputLimitCount: client.transcript.run.outputLimitCount,
        resumable: client.transcript.run.resumable, stopReason: client.transcript.run.stopReason } : null,
      externalMcp: mcpBridge.connected, browserMcp: browserMcp.status, recovery: persistence.status.state, workspace: fileTransfer.state() }) });
}
