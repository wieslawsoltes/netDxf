import { StateGate, identityHeaders, viewKey, sameReceipt, presentationKey } from './studio-state.mjs';

const $ = id => document.getElementById(id);
const view = $('viewport'), canvas = $('overlay'), ctx = canvas.getContext('2d');
const gate = new StateGate();
let state, graph, objectId, drag, space = false, camera = { x: 0, y: 0, scale: 2 };
let frame, renderAbort, renderSequence = 0, imageUrl, displayedKey, inspectorSequence = 0, inspectorKey = '';
let busy = false, refreshFlight, refreshAgain = false, mergePlan, previewId, events, disposed = false;
const size = () => ({ w: Math.max(1, view.clientWidth), h: Math.max(1, view.clientHeight) });
const frameKey = () => presentationKey(state, camera, size(), $('backend').value, $('snapshot').value, previewId);
const readOnly = () => $('snapshot').value !== '' || !!previewId;
const branch = () => state?.branches.find(b => b.id === $('merge-source').value);

function notify(error) {
  if (error.name === 'AbortError' || disposed) return;
  $('notification').textContent = error.message || String(error);
  $('notification').hidden = false;
  clearTimeout(notify.timer);
  notify.timer = setTimeout(() => $('notification').hidden = true, 8000);
}
async function check(response) {
  if (response.ok) return response;
  let message = `HTTP ${response.status}`;
  try { message = (await response.json()).error || message; } catch { /* Non-JSON transport failure. */ }
  if (response.status === 409) queueMicrotask(() => refresh().catch(notify));
  throw new Error(message);
}
async function get(path, captured = state, signal) {
  const response = await check(await fetch(`api/${path}`, {
    headers: captured ? identityHeaders(captured) : {}, signal, cache: 'no-store'
  }));
  return response.json();
}
function usable(captured, key = viewKey(captured)) {
  return !disposed && gate.current(captured) && key === viewKey(state);
}
async function refresh() {
  if (disposed) return;
  refreshAgain = true;
  if (refreshFlight) return refreshFlight;
  refreshFlight = (async () => {
    while (refreshAgain && !disposed) {
      refreshAgain = false;
      const stamp = gate.stamp();
      const value = await get('state', null);
      if (gate.accept(value, stamp, true)) update(value);
      else refreshAgain = true; // A notice/server restart fenced this read; fetch a new receipt.
    }
  })();
  try { await refreshFlight; } finally { refreshFlight = undefined; }
}

/** No mutation is automatically retried, including after a 409 or a lost response. */
async function command(path, data = {}, { receipt = true, editable = true, captured = state, raw, binary = false } = {}) {
  if (busy) throw new Error('Another command is in progress.');
  if (!usable(captured)) throw new Error('The workspace changed. Refresh before editing.');
  if (editable && readOnly()) throw new Error('Close the preview or retained snapshot before editing.');
  busy = true; controls();
  const stamp = gate.stamp();
  try {
    const headers = identityHeaders(captured);
    if (!raw) headers['Content-Type'] = 'application/json';
    const response = await check(await fetch(`api/${path}`, {
      method: 'POST', headers, body: raw || JSON.stringify({ revision: captured.revision, ...data })
    }));
    if (binary) return await response.blob();
    const value = await response.json();
    if (receipt) {
      if (gate.accept(value, stamp)) update(value);
      else await refresh();
    } else if (!usable(captured)) throw new Error('The merge inputs changed while planning. Recompute the preview.');
    return value;
  } finally { busy = false; controls(); }
}
const action = (path, data, captured) => command(path, data, { captured: captured || state });

function controls() {
  const stale = !gate.current(state), blocked = busy || stale || readOnly();
  $('undo').disabled = blocked || !state?.undoCount;
  $('redo').disabled = blocked || !state?.redoCount;
  for (const button of document.querySelectorAll('[data-add], [data-transform], #delete, #retain, #layout, #linetype-alignment, #annotation-mode, #annotation-paper, #annotation-drawing, #annotation-apply')) button.disabled = blocked;
  $('source-demo').disabled = blocked || state?.workspaceId !== 'live';
  $('save').disabled = blocked || state?.workspaceId !== 'live';
  $('file').disabled = busy || stale;
  $('font-file').disabled = blocked || state?.workspaceId !== 'live';
  $('bigfont-map-file').disabled = blocked || state?.workspaceId !== 'live';
  $('workspace').disabled = busy || stale;
  $('fork').disabled = blocked || state?.workspaceId !== 'live' || state?.branches.length >= 8;
  $('discard').disabled = busy || stale || state?.workspaceId === 'live';
  $('preview-merge').disabled = busy || stale || !branch();
  $('apply-merge').disabled = busy || stale || !mergePlan?.canApply || !previewId;
  $('read-only').hidden = !readOnly();
  $('read-only').textContent = previewId ? 'MERGE PREVIEW · NOT YET APPLIED' : 'RETAINED SNAPSHOT · READ ONLY';
}
function optionList(id, values, selected) {
  $(id).replaceChildren(...values.map(([text, value]) => new Option(text, value)));
  $(id).value = values.some(v => v[1] === selected) ? selected : values[0]?.[1] || '';
}
function clearMerge(message = '') {
  mergePlan = undefined; previewId = undefined;
  $('merge-result').hidden = !message;
  $('merge-summary').textContent = message;
  $('merge-conflicts').replaceChildren();
  controls();
}
function update(value) {
  if (disposed) return;
  const previous = state;
  const switchView = !previous || previous.documentId !== value.documentId ||
    previous.workspaceId !== value.workspaceId || previous.viewVersion !== value.viewVersion;
  const changed = viewKey(previous) !== viewKey(value);
  const layoutChanged = previous?.layout !== value.layout;
  state = value;
  if (changed) { renderSequence++; renderAbort?.abort(); graph = undefined; }
  if (switchView) { drag = undefined; objectId = undefined; inspectorKey = ''; $('snapshot').value = ''; clearMerge(); }
  if (mergePlan) {
    const incoming = value.branches.find(b => b.id === mergePlan.branchId);
    if (mergePlan.documentId !== value.documentId || mergePlan.targetRevision !== value.sourceRevision ||
        mergePlan.incomingRevision !== incoming?.revision || previous?.fontRevision !== value.fontRevision)
      clearMerge('The merge inputs changed. Compute a new preview.');
  }
  $('revision').textContent = `${value.workspaceName} · r${value.revision}`;
  $('entity-count').textContent = value.occurrences.toLocaleString();
  $('view-name').textContent = `${value.layout} space`;
  $('metrics').textContent = `${value.objects.toLocaleString()} objects · ${value.entities.toLocaleString()} entities · capture ${value.captureMilliseconds.toFixed(1)} ms`;
  optionList('layout', value.layouts.map(n => [n, n]), value.layout);
  $('linetype-alignment').value = value.linetypeAlignment ?? 'Periodic';
  if ($('annotation-mode')) {
    $('annotation-mode').value = value.annotationMode ?? 'Drawing';
    $('annotation-paper').value = value.annotationPaperUnits ?? 1;
    $('annotation-drawing').value = value.annotationDrawingUnits ?? 1;
  }
  optionList('workspace', [['Live document', 'live'], ...value.branches.map(b => [`${b.name} · r${b.revision}`, b.id])], value.workspaceId);
  const mergeSource = switchView && value.workspaceId !== 'live' ? value.workspaceId : $('merge-source').value;
  optionList('merge-source', value.branches.map(b => [b.name, b.id]), mergeSource);
  optionList('snapshot', [['Current workspace', ''], ...value.snapshots.map(n => [`Snapshot ${n}`, String(n)])], $('snapshot').value);
  $('workspace-info').textContent = value.workspaceId === 'live'
    ? `${value.branches.length}/8 isolated branches · source r${value.sourceRevision}`
    : `Isolated branch · ancestor r${value.ancestorRevision} · live source r${value.sourceRevision}`;
  $('issue-count').textContent = value.diagnostics.length;
  $('issues').replaceChildren(...value.diagnostics.map(d => {
    const row = document.createElement('div'); row.className = `issue ${d.severity === 'Error' ? 'error' : ''}`;
    const code = document.createElement('code'); code.textContent = d.code;
    row.append(code, document.createTextNode(d.message)); return row;
  }));
  if (!value.diagnostics.length) $('issues').textContent = 'No projection diagnostics for this drawing.';
  controls(); tree();
  $('graph-info').hidden = !$('graph-toggle').checked || readOnly();
  if ((changed || !graph) && $('graph-toggle').checked && !readOnly()) loadGraph().catch(notify);
  const selected = value.items.find(i => value.selection.includes(i.key));
  if (selected && !readOnly()) {
    const key = viewKey(value) + selected.id;
    if (inspectorKey !== key) { inspectorKey = key; inspect(selected.id).catch(notify); }
  } else {
    inspectorSequence++; inspectorKey = ''; objectId = undefined;
    $('properties').replaceChildren(); $('property-type').textContent = '';
    $('selection-summary').textContent = readOnly() ? 'Read-only view. Close it to inspect or edit.' : 'Select geometry or an explorer item.';
  }
  if (switchView || layoutChanged) fitDrawing(); else schedule();
}
function tree() {
  if (!state) return;
  const filter = $('filter').value.toLowerCase();
  const rows = readOnly() ? [] : state.items.filter(i => `${i.type} ${i.layer} ${i.id}`.toLowerCase().includes(filter));
  $('entities').replaceChildren(...rows.map(i => {
    const row = document.createElement('button'); row.className = `entity${state.selection.includes(i.key) ? ' selected' : ''}`;
    row.setAttribute('role', 'option'); row.setAttribute('aria-selected', state.selection.includes(i.key));
    const symbol = document.createElement('span'); symbol.className = 'symbol';
    symbol.textContent = i.type === 'Text' ? 'T' : i.inserted ? '◇' : i.type === 'Circle' ? '○' : '╱';
    const title = document.createElement('strong'); title.textContent = `${i.type} ${i.id}`;
    const detail = document.createElement('small'); detail.textContent = `${i.layer}${i.inserted ? ' · instance' : ''}`;
    row.append(symbol, title, detail);
    const captured = state;
    row.onclick = e => action('select', { key: i.key, extend: e.ctrlKey || e.metaKey }, captured).catch(notify);
    return row;
  }));
}
async function inspect(id) {
  objectId = id;
  const captured = state, key = viewKey(captured), request = ++inspectorSequence;
  const info = await get(`object/${encodeURIComponent(id)}`, captured);
  if (request !== inspectorSequence || objectId !== id || readOnly() || !usable(captured, key) || !sameReceipt(info, state)) return;
  $('property-type').textContent = info.type;
  $('selection-summary').textContent = `${id} · ${state.selection.length} selected`;
  $('properties').replaceChildren(...info.properties.map(p => {
    const row = document.createElement('div'); row.className = 'property';
    const label = document.createElement('label'); label.textContent = p.name;
    let input;
    if (p.writable) {
      input = document.createElement('input'); input.type = 'text'; input.setAttribute('aria-label', p.name);
      const structure = p.kind === 'Structure'; input.value = structure ? JSON.stringify(p.value) : String(p.value ?? '');
      const original = input.value;
      input.onkeydown = async e => {
        if (e.key === 'Escape') { input.value = original; input.blur(); }
        if (e.key !== 'Enter' || input.value === original) return;
        e.preventDefault();
        try { await action('property', { id, property: p.name, value: structure ? JSON.parse(input.value) : input.value }, captured); }
        catch (error) { notify(error); }
      };
    } else if (p.value?.reference) {
      input = document.createElement('button'); input.textContent = `↗ ${p.value.reference}`;
      input.onclick = () => inspect(p.value.reference).catch(notify);
    } else {
      input = document.createElement('div'); input.className = 'value';
      input.textContent = typeof p.value === 'object' ? JSON.stringify(p.value) : String(p.value ?? '—');
    }
    row.append(label, input); return row;
  }));
}
function world(p) { const { w, h } = size(); return { x: (p.x - w / 2) / camera.scale + camera.x, y: camera.y - (p.y - h / 2) / camera.scale }; }
function screen(p) { const { w, h } = size(); return { x: (p.x - camera.x) * camera.scale + w / 2, y: (camera.y - p.y) * camera.scale + h / 2 }; }
function local(e) { const r = view.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }
function fitDrawing() {
  if (!state) return;
  const b = state.bounds, { w, h } = size();
  camera = { x: (b.minX + b.maxX) / 2, y: (b.minY + b.maxY) / 2,
    scale: Math.min(w / Math.max(1, b.maxX - b.minX), h / Math.max(1, b.maxY - b.minY)) / 1.2 };
  schedule();
}
function schedule() { if (disposed) return; renderSequence++; renderAbort?.abort(); overlay(); cancelAnimationFrame(frame); frame = requestAnimationFrame(() => render().catch(notify)); }
async function render() {
  if (!gate.current(state)) return;
  const captured = state, key = viewKey(captured), { w, h } = size(), request = ++renderSequence;
  const preview = previewId, snapshot = $('snapshot').value, paintKey = frameKey();
  renderAbort?.abort(); renderAbort = new AbortController();
  const q = new URLSearchParams({ width: w, height: h, cx: camera.x, cy: camera.y, scale: camera.scale, backend: $('backend').value });
  if (snapshot) q.set('snapshot', snapshot);
  if (preview) q.set('merge', preview);
  const start = performance.now();
  try {
    const response = await check(await fetch(`api/render?${q}`, { headers: identityHeaders(captured), signal: renderAbort.signal, cache: 'no-store' }));
    const blob = await response.blob();
    if (request !== renderSequence || !usable(captured, key) || paintKey !== frameKey()) return;
    const nextUrl = URL.createObjectURL(blob);
    // Decode before replacing the displayed frame. A slow image load cannot overwrite a newer frame.
    const image = new Image(); image.src = nextUrl;
    try { await image.decode(); } catch (error) { URL.revokeObjectURL(nextUrl); throw error; }
    if (request !== renderSequence || !usable(captured, key) || paintKey !== frameKey()) { URL.revokeObjectURL(nextUrl); return; }
    const previous = imageUrl; imageUrl = nextUrl; displayedKey = paintKey; $('drawing').src = nextUrl;
    if (previous) URL.revokeObjectURL(previous);
    $('status').textContent = `${$('backend').value} · ${(performance.now() - start).toFixed(0)} ms · ${Math.round(blob.size / 1024)} KiB · ${preview ? 'merge preview' : snapshot ? `retained r${snapshot}` : `${captured.workspaceName} r${captured.revision}`}`;
  } catch (error) { if (error.name !== 'AbortError') throw error; }
}
function overlay() {
  const { w, h } = size(), dpr = Math.min(devicePixelRatio || 1, 2);
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr); }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, w, h);
  if ($('grid-toggle').checked) {
    const step = 10 ** Math.ceil(Math.log10(16 / camera.scale)), a = world({ x: 0, y: h }), b = world({ x: w, y: 0 });
    const nx = Math.min(200, Math.ceil((b.x - a.x) / step) + 1), ny = Math.min(200, Math.ceil((b.y - a.y) / step) + 1);
    const x0 = Math.ceil(a.x / step) * step, y0 = Math.ceil(a.y / step) * step;
    ctx.fillStyle = '#657c9028';
    for (let x = 0; x < nx; x++) for (let y = 0; y < ny; y++) { const p = screen({ x: x0 + x * step, y: y0 + y * step }); ctx.fillRect(p.x, p.y, 1, 1); }
  }
  if (graph && $('graph-toggle').checked && !readOnly() && gate.current(state) && sameReceipt(graph, state, true)) {
    const nodes = new Map(graph.nodes.map(n => [n.id, n])); ctx.strokeStyle = '#81d7ae60'; ctx.lineWidth = 2;
    for (const edge of graph.edges) {
      const a = nodes.get(edge.a), b = nodes.get(edge.b); if (!a || !b) continue;
      const p = screen(a), q = screen(b); ctx.beginPath(); ctx.moveTo(p.x, p.y); ctx.lineTo(q.x, q.y); ctx.stroke();
    }
    for (const node of graph.nodes) {
      const p = screen(node); if (p.x < 0 || p.x > w || p.y < 0 || p.y > h) continue;
      ctx.fillStyle = node.degree === 1 ? '#eca371' : node.degree > 2 ? '#d4a7ef' : '#8bd9b0';
      ctx.beginPath(); ctx.arc(p.x, p.y, node.degree > 2 ? 4 : 2.5, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (drag?.mode === 'move' && drag.moved) {
    ctx.setLineDash([4, 4]); ctx.strokeStyle = '#edc786'; ctx.beginPath(); ctx.moveTo(drag.start.x, drag.start.y);
    ctx.lineTo(drag.last.x, drag.last.y); ctx.stroke(); ctx.setLineDash([]);
  }
}
async function loadGraph() {
  $('graph-info').hidden = !$('graph-toggle').checked || readOnly();
  if (!$('graph-toggle').checked || readOnly() || !gate.current(state)) { overlay(); return; }
  const captured = state, key = viewKey(captured);
  $('graph-info').textContent = 'Building topology from the immutable snapshot…';
  const result = await get('graph', captured);
  if (!usable(captured, key) || !sameReceipt(result, state, true)) return;
  graph = result;
  $('graph-info').textContent = `${graph.nodes.length} displayed nodes · ${graph.edges.length} edges · ${graph.components} components · ${graph.cycleRank} cycles · ${graph.junctions} junctions · ${graph.deadEnds} dead ends · ${graph.bridges} bridges`;
  overlay();
}

$('workspace').onchange = () => command('workspace', { id: $('workspace').value }, { editable: false }).catch(notify);
$('fork').onclick = () => command('branches', { name: $('branch-name').value }).then(() => $('branch-name').value = '').catch(notify);
$('discard').onclick = () => {
  const current = state.branches.find(b => b.id === state.workspaceId);
  if (current && confirm(`Discard branch “${current.name}” and its isolated edits?`))
    command('branches/discard', { id: current.id, branchRevision: current.revision }, { editable: false }).catch(notify);
};
for (const id of ['merge-source', 'merge-choice']) $(id).onchange = () => { clearMerge(); update(state); };
$('preview-merge').onclick = async () => {
  try {
    const incoming = branch(); if (!incoming) return;
    const value = await command('merge/preview', { documentId: state.documentId, branchId: incoming.id,
      targetRevision: state.sourceRevision, incomingRevision: incoming.revision, choice: $('merge-choice').value }, { receipt: false, editable: false });
    mergePlan = value; previewId = value.canApply ? value.planId : undefined; $('snapshot').value = '';
    $('merge-result').hidden = false;
    $('merge-summary').textContent = `${value.canApply ? 'Ready to apply' : 'Unresolved conflicts'} · ancestor r${value.ancestorRevision} → live r${value.targetRevision} + branch r${value.incomingRevision} · ${value.added} added / ${value.changed} changed / ${value.removed} removed · ${value.conflictCount} conflicts`;
    $('merge-conflicts').replaceChildren(...value.conflicts.map(conflict => {
      const row = document.createElement('div'); row.className = 'merge-conflict';
      const heading = document.createElement('strong'); heading.textContent = `${conflict.id} / ${conflict.path.join('/')} · ${conflict.kind} → ${conflict.resolution}`;
      const values = document.createElement('div'); values.textContent = `Ancestor: ${conflict.ancestor.text} | Live: ${conflict.target.text} | Branch: ${conflict.incoming.text}`;
      row.append(heading, values); return row;
    }));
    if (value.conflictCount > value.conflicts.length) $('merge-conflicts').append(document.createTextNode('Only the first 200 conflicts are displayed.'));
    update(state);
  } catch (error) { notify(error); }
};
$('apply-merge').onclick = async () => {
  const plan = mergePlan; if (!plan?.canApply) return;
  try {
    await command('merge/apply', { documentId: plan.documentId, planId: plan.planId,
      targetRevision: plan.targetRevision, incomingRevision: plan.incomingRevision }, { editable: false });
    clearMerge(); update(state);
  } catch (error) { clearMerge('The merge was not applied. Refresh and compute a new preview.'); notify(error); }
};
$('close-preview').onclick = () => { clearMerge(); update(state); };
$('save').onclick = async () => {
  try {
    const blob = await command('save', {}, { binary: true });
    const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href = url; link.download = 'shadow-edited.dxf';
    document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    await refresh();
  } catch (error) { notify(error); }
};
$('file').onchange = async e => {
  const file = e.target.files[0]; if (!file) return;
  try {
    if (file.size > 20 * 1024 * 1024) throw new Error('The sample upload limit is 20 MiB.');
    if (state.branches.length && !confirm('Opening a DXF replaces the live document and discards every isolated branch. Continue?')) return;
    await command(`load?revision=${state.revision}`, {}, { raw: file, editable: false });
  } catch (error) { notify(error); } finally { e.target.value = ''; }
};
$('font-file').onchange = async e => {
  const file = e.target.files[0]; if (!file) return;
  try {
    if (state.workspaceId !== 'live') throw new Error('Font resources can only be loaded into the live workspace.');
    if (file.size > 4 * 1024 * 1024) throw new Error('The sample font limit is 4 MiB.');
    await command(`font?${new URLSearchParams({ revision: state.revision, name: file.name })}`, {}, { raw: file });
  } catch (error) { notify(error); } finally { e.target.value = ''; }
};
$('fit').onclick = fitDrawing; $('filter').oninput = tree; $('backend').onchange = schedule;
$('grid-toggle').onchange = overlay; $('graph-toggle').onchange = () => loadGraph().catch(notify);
$('snapshot').onchange = () => { clearMerge(); update(state); };
for (const [id, path] of [['undo', 'history/undo'], ['redo', 'history/redo'], ['delete', 'delete'], ['retain', 'snapshot'], ['source-demo', 'source-demo']]) $(id).onclick = () => action(path).catch(notify);
$('linetype-alignment').onchange = () => action('linetype-view', { alignment: $('linetype-alignment').value }).catch(notify);
$('layout').onchange = () => action('layout', { name: $('layout').value }).catch(notify);
for (const button of document.querySelectorAll('[data-add]')) button.onclick = () => action('add', { kind: button.dataset.add, x: camera.x, y: camera.y }).catch(notify);
for (const button of document.querySelectorAll('[data-transform]')) button.onclick = () => action('transform', { operation: button.dataset.transform, amount: Number(button.dataset.amount || 1) }).catch(notify);

view.onpointerdown = async e => {
  if (!state) return;
  view.focus(); view.setPointerCapture(e.pointerId);
  const p = local(e), g = { start: p, last: p, world: world(p), camera: { ...camera }, moved: false, mode: e.button !== 0 || space ? 'pan' : 'pick' };
  drag = g;
  if (g.mode === 'pan' || readOnly()) return;
  if (displayedKey !== frameKey()) { drag = undefined; notify(new Error('The drawing is updating. Pick after the new frame arrives.')); return; }
  try {
    await action('pick', { x: g.world.x, y: g.world.y, tolerance: 7 / camera.scale, extend: e.ctrlKey || e.metaKey });
    if (drag === g) { g.mode = state.selection.length ? 'move' : 'empty'; g.captured = state; }
  } catch (error) { notify(error); }
};
view.onpointermove = e => {
  const p = local(e), wp = world(p); $('coordinates').textContent = `X ${wp.x.toFixed(2)} Y ${wp.y.toFixed(2)}`;
  if (!drag) return;
  drag.last = p; drag.moved ||= Math.hypot(p.x - drag.start.x, p.y - drag.start.y) > 3;
  if (drag.mode === 'pan') { camera.x = drag.camera.x - (p.x - drag.start.x) / camera.scale; camera.y = drag.camera.y + (p.y - drag.start.y) / camera.scale; schedule(); }
  else overlay();
};
view.onpointerup = e => {
  const g = drag; drag = undefined; overlay();
  if (!g?.moved || g.mode !== 'move' || readOnly()) return;
  const p = world(local(e)); let x = p.x - g.world.x, y = p.y - g.world.y;
  if (e.shiftKey) { x = Math.round(x / 10) * 10; y = Math.round(y / 10) * 10; }
  action('translate', { x, y }, g.captured).catch(notify);
};
view.onpointercancel = () => { drag = undefined; overlay(); }; view.oncontextmenu = e => e.preventDefault();
view.addEventListener('wheel', e => {
  e.preventDefault(); const p = local(e), before = world(p);
  camera.scale = Math.max(1e-8, Math.min(1e8, camera.scale * Math.exp(-e.deltaY * .001)));
  const after = world(p); camera.x += before.x - after.x; camera.y += before.y - after.y; schedule();
}, { passive: false });
document.onkeydown = e => {
  if (/INPUT|SELECT|TEXTAREA/.test(e.target.tagName)) return;
  if (e.code === 'Space') { space = true; e.preventDefault(); }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); action(`history/${e.shiftKey ? 'redo' : 'undo'}`).catch(notify); }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { e.preventDefault(); action('history/redo').catch(notify); }
  if (e.key === 'Delete') action('delete').catch(notify);
  if (e.key.toLowerCase() === 'f') fitDrawing();
  const d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] }[e.key];
  if (d && state?.selection.length) { e.preventDefault(); action('translate', { x: d[0] * (e.shiftKey ? 10 : 1), y: d[1] * (e.shiftKey ? 10 : 1) }).catch(notify); }
};
document.onkeyup = e => { if (e.code === 'Space') space = false; };
window.onblur = () => { space = false; drag = undefined; };
const resize = new ResizeObserver(schedule); resize.observe(view);
function connect() {
  events = new EventSource('api/events');
  events.onopen = () => $('connection').textContent = 'Live updates connected';
  events.onerror = () => $('connection').textContent = 'Reconnecting live updates…';
  events.addEventListener('state', event => {
    try {
      const notice = JSON.parse(event.data);
      if (gate.observe(notice) || !gate.current(state)) {
        controls(); renderSequence++; renderAbort?.abort(); overlay(); refresh().catch(notify);
      }
    } catch (error) { notify(error); }
  });
}
window.addEventListener('pagehide', () => {
  disposed = true; events?.close(); renderAbort?.abort(); resize.disconnect(); cancelAnimationFrame(frame);
  if (imageUrl) URL.revokeObjectURL(imageUrl);
});
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
connect(); refresh().catch(notify);

if ($('annotation-apply')) $('annotation-apply').onclick = () => action('annotation-view', {
  mode: $('annotation-mode').value,
  paperUnits: Number($('annotation-paper').value),
  drawingUnits: Number($('annotation-drawing').value)
}).catch(notify);

$('bigfont-map-file').onchange = async e => {
  const file = e.target.files[0]; if (!file) return;
  try {
    if (file.size > 4 * 1024 * 1024) throw new Error('The sample mapping limit is 4 MiB.');
    await command(`bigfont-map?${new URLSearchParams({ revision: state.revision })}`, {}, { raw: file });
  } catch (error) { notify(error); }
  finally { e.target.value = ''; }
};
