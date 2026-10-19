import { node, button, code } from './render.mjs';
import { evaluationRows, evaluationGroups } from './evaluation-contracts.mjs';
import { formatDuration, formatTokens } from './contracts.mjs';

/** Local report comparison only: this view does not start evaluations, read keys or call a provider. */
export function mountEfficiency(panel) {
  let rows = [];
  const status = node('p', { class: 'agent-muted', role: 'status', text: 'Import a report.json or summary.json produced by the reusable evaluator.' });
  const input = node('input', { type: 'file', accept: '.json,application/json', multiple: true, hidden: true });
  const content = node('div', { class: 'agent-evaluation-results' });
  panel.append(node('div', { class: 'agent-section-intro' }, [node('h2', { text: 'Measured efficiency' }),
    node('p', { text: 'Compare identical drawing tasks using recorded success, latency, tokens, cache usage and cost estimates. Reports with different seeds, prompts, graders or tool catalogs stay in separate groups.' })]),
    node('div', { class: 'agent-row' }, [button('Import evaluation reports', () => input.click(), { icon: 'file' }), button('Clear reports', () => { rows = []; render(); })]), input, status, content,
    node('small', { class: 'agent-muted', text: 'A dash means the provider or protocol did not report a value. Requested caching does not prove a cache hit. Estimates use caller-supplied rates and are not billing invoices; subscription agents have no inferred API price. Imported reports stay in this tab and are discarded when the page closes.' }));
  input.addEventListener('change', async () => {
    try {
      const files = [...input.files]; if (files.length > 20) throw new Error('Import at most 20 files at once.');
      const added = [];
      for (const file of files) { if (file.size > 8 * 1024 * 1024) throw new Error('Each report must be smaller than 8 MiB.'); added.push(...evaluationRows(JSON.parse(await file.text()))); }
      if (rows.length + added.length > 1000) throw new Error('This view supports at most 1,000 results. Clear existing reports first.');
      rows.push(...added); render();
    } catch (error) { status.textContent = error.message; }
    finally { input.value = ''; }
  });
  const numeric = value => value == null ? '—' : formatTokens(value);
  const duration = value => value == null ? '—' : formatDuration(value);
  const usd = value => value == null ? '—' : `$${Number(value).toFixed(6)}`;
  function render() {
    content.replaceChildren(); status.textContent = rows.length ? `${rows.length} observations · all successful and failed attempts included` : 'Import a report.json or summary.json produced by the reusable evaluator.';
    for (const group of evaluationGroups(rows)) {
      const table = node('table', { class: 'agent-evaluation-table' });
      table.append(node('thead', {}, [node('tr', {}, ['Target / cache request', 'Run', 'Result', 'Elapsed', 'First token', 'Usage', 'Input', 'Output', 'Cache read', 'Cache write', 'Tools / errors', 'Undo', 'Estimated USD', 'Reserved USD'].map(text => node('th', { scope: 'col', text })))]));
      const body = node('tbody'); table.append(body);
      for (const row of group.rows) {
        const label = node('details', {}, [node('summary', { text: `${row.target.model} · ${row.target.cacheMode}` }), code({ ...row.target, costBasis: row.cost?.basis ?? 'Not reported' })]);
        const values = [row.repeat, row.passed ? 'Pass' : `Fail · ${row.status}`, duration(row.elapsedMilliseconds), duration(row.telemetry.firstTokenMilliseconds), row.telemetry.usageComplete === true ? 'Complete' : row.telemetry.usageComplete === false ? 'Partial' : 'Unknown', numeric(row.telemetry.inputTokens), numeric(row.telemetry.outputTokens), numeric(row.telemetry.cacheReadTokens), numeric(row.telemetry.cacheWriteTokens), `${row.toolCalls} / ${row.toolErrors}`, row.undoTransactions, usd(row.cost?.estimatedUsd), usd(row.cost?.reservedUsd)];
        body.append(node('tr', {}, [node('td', {}, [label]), ...values.map(text => node('td', { text: String(text ?? '—') }))]));
      }
      content.append(node('section', {}, [node('h3', { text: `${group.caseId} · ${group.successes}/${group.attempts} successful` }),
        node('small', { class: 'agent-muted', text: `Task ${group.fingerprint.slice(0, 12)} · tools ${group.toolCatalogHash.slice(0, 12)}` }), node('div', { class: 'agent-evaluation-scroll' }, [table])]));
    }
  }
  return Object.freeze({ clear() { rows = []; render(); } });
}
