import { node, button, code } from './render.mjs';

import { referenceKey, validateReferenceShape } from './drawing-reference-contracts.mjs';
export { splitReferencePrompt } from './drawing-reference-contracts.mjs';

export function referenceChips(references, { historical = false, statuses = new Map(), remove, inspect } = {}) {
  const row = node('div', { class: 'agent-reference-chips', 'aria-label': historical ? 'Drawing objects attached to this message' : 'Attached drawing objects' });
  for (const ref of references || []) {
    const status = statuses.get(referenceKey(ref)), label = `${ref.type.split('.').at(-1)} ${ref.handle || ref.objectId}`;
    const details = node('details', { class: `agent-reference${status && !['current', 'refreshed'].includes(status.status) ? ' stale' : ''}`, 'data-reference-key': referenceKey(ref) });
    details.append(node('summary', { title: `${ref.objectId} · ${ref.workspaceId} · revision ${ref.revision}`, text: label }),
      node('small', { text: historical ? `Captured at revision ${ref.revision}` : status?.message || `Revision ${ref.revision} · ${ref.workspaceId}` }), code(ref));
    if (inspect) details.append(button('Check current object', () => inspect(ref)));
    if (remove) details.append(button(`Remove ${label}`, () => remove(ref), { icon: 'trash', compact: true }));
    row.append(details);
  }
  return row;
}

export function createReferenceComposer({ api, getDocument = () => null, report = () => {}, compact = false, onChange = () => {} }) {
  let references = [], routeId = null, statuses = new Map(), generation = 0, disposed = false, checking = false, rendered = '';
  const chips = node('div', { hidden: true }), status = node('span', { class: 'agent-muted', role: 'status' });
  const action = callback => Promise.resolve().then(callback).catch(error => report(error));
  const attach = button('Attach selected objects', () => action(capture), { icon: 'link', compact, disabled: !api });
  const refresh = button('Refresh attachments', () => action(() => check(true)), { icon: compact ? 'refresh' : undefined, compact, hidden: true });
  const controls = node('div', { class: 'agent-row agent-reference-controls' }, [attach, refresh, status]);
  const element = node('div', { class: 'agent-reference-composer' }, [controls, chips]);
  function render() {
    const next = JSON.stringify([references, [...statuses], routeId, getDocument()]);
    if (rendered === next) return;
    rendered = next;
    const focusWasInside = chips.contains(document.activeElement);
    const expanded = new Set([...chips.querySelectorAll('details[open]')].map(item => item.getAttribute('data-reference-key')));
    refresh.hidden = references.length === 0;
    chips.hidden = references.length === 0;
    chips.replaceChildren(referenceChips(references, { statuses, remove: ref => { generation++; references = references.filter(item => referenceKey(item) !== referenceKey(ref)); render(); }, inspect: ref => action(async () => {
      const value = await api.validate([ref], false); if (!value) throw new Error('The drawing changed while checking this object.');
      for (const item of value.items || []) statuses.set(referenceKey(item.reference), item); render();
    }) }));
    for (const item of chips.querySelectorAll('details')) item.open = expanded.has(item.getAttribute('data-reference-key'));
    status.textContent = references.length ? `${references.length} attached${routeId !== getDocument() ? ' · different drawing' : ''}` : '';
    onChange(references.length, { focusWasInside });
  }
  async function capture() {
    const route = getDocument(), stamp = ++generation;
    const value = await api.capture(); if (!value || route !== getDocument() || stamp !== generation) throw new Error('The drawing changed while capturing selection. Attach again.');
    const incoming = validateReferenceShape(value.references);
    if (!incoming.length) throw new Error('Select one or more drawing objects first.');
    if (references.length && routeId !== route) throw new Error('Remove references from the previous drawing before attaching this selection.');
    const merged = new Map(references.map(ref => [referenceKey(ref), ref])); for (const ref of incoming) merged.set(referenceKey(ref), ref);
    references = validateReferenceShape([...merged.values()]); routeId = route; statuses.clear(); render(); await check(false);
  }
  async function check(update = false) {
    if (!references.length) return [];
    if (routeId !== getDocument()) { render(); throw new Error('Attached objects belong to another drawing. Return to it or remove the attachments.'); }
    const stamp = generation, original = references;
    const result = await api.validate(original, update);
    if (!result || disposed || stamp !== generation || routeId !== getDocument()) throw new Error('The drawing changed while validating attachments. Try again.');
    if (!Array.isArray(result.items) || result.items.length !== original.length) throw new Error('Invalid drawing reference validation response.');
    statuses = new Map(result.items.map(item => [referenceKey(item.reference), item]));
    if (update) { generation++; references = original.map(ref => { const item = statuses.get(referenceKey(ref)); return item?.status === 'refreshed' || item?.status === 'current' ? item.current : ref; }); }
    render();
    if (!result.canSend || result.items.some(item => !['current', 'refreshed'].includes(item.status) || !item.current))
      throw new Error('Some attached objects changed or were deleted. Refresh or remove the marked attachments before sending.');
    return validateReferenceShape(result.items.map(item => item.current));
  }
  const timer = setInterval(() => {
    if (disposed || checking || !references.length || routeId !== getDocument()) return;
    checking = true; void check(false).catch(() => {}).finally(() => { checking = false; });
  }, 2000);
  return Object.freeze({ element, controls, chips, prepare: () => check(false), capture, clear() { generation++; references = []; statuses.clear(); routeId = null; render(); },
    commit(expected) { if (JSON.stringify(references) === JSON.stringify(expected)) { generation++; references = []; statuses.clear(); routeId = null; render(); } },
    changed() { generation++; render(); }, get references() { return structuredClone(references); }, dispose() { disposed = true; clearInterval(timer); } });
}
