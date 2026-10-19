/** Parse exported evaluation results without rendering raw prompts, events or checkpoints. */
export function evaluationRows(value) {
  const results = value?.results ?? [value];
  if (!Array.isArray(results) || results.length > 1000) throw new Error('Import at most 1,000 evaluation results.');
  return results.map(result => {
    const text = (v, max = 512) => typeof v === 'string' && v.length > 0 && v.length <= max && !/[\u0000-\u001f]/u.test(v);
    const hash = v => typeof v === 'string' && /^[a-f0-9]{64}$/u.test(v);
    const number = v => v == null || typeof v === 'number' && Number.isFinite(v) && v >= 0;
    if (result?.schemaVersion !== 1 || !text(result.caseId) || !hash(result.fingerprint) || !hash(result.toolCatalogHash)
      || !text(result.target?.id) || !text(result.target.model) || !text(result.target.protocol) || !text(result.target.provider) || !text(result.target.cacheMode)
      || !Number.isInteger(result.repeat) || result.repeat < 1 || typeof result.passed !== 'boolean' || !text(result.status)
      || !number(result.elapsedMilliseconds) || result.elapsedMilliseconds == null || !number(result.toolCalls) || !number(result.toolErrors)
      || !number(result.toolMilliseconds) || !number(result.undoTransactions) || !result.telemetry)
      throw new Error('This file does not contain supported measured evaluation results.');
    for (const field of ['inputTokens', 'outputTokens', 'cacheReadTokens', 'cacheWriteTokens', 'reasoningTokens', 'inferences', 'retries', 'inferenceMilliseconds', 'firstTokenMilliseconds'])
      if (!number(result.telemetry[field])) throw new Error(`Invalid evaluation measurement: ${field}`);
    if (result.telemetry.usageComplete != null && typeof result.telemetry.usageComplete !== 'boolean') throw new Error('Invalid usage completeness.');
    if (result.cost && (!text(result.cost.basis, 2048) || !number(result.cost.estimatedUsd) || !number(result.cost.reservedUsd))) throw new Error('Invalid evaluation cost.');
    return structuredClone({ caseId: result.caseId, fingerprint: result.fingerprint, toolCatalogHash: result.toolCatalogHash,
      target: { id: result.target.id, model: result.target.model, provider: result.target.provider, protocol: result.target.protocol, cacheMode: result.target.cacheMode,
        settings: result.target.settings && typeof result.target.settings === 'object' ? result.target.settings : {} }, repeat: result.repeat,
      passed: result.passed, status: result.status, elapsedMilliseconds: result.elapsedMilliseconds, toolCalls: result.toolCalls,
      toolErrors: result.toolErrors, toolMilliseconds: result.toolMilliseconds, undoTransactions: result.undoTransactions,
      telemetry: result.telemetry, cost: result.cost ?? null });
  });
}

/** Failures remain in the denominator. Compare only the exact task seed/prompt/grader and catalog. */
export function evaluationGroups(rows) {
  const groups = new Map();
  for (const row of rows) {
    const key = `${row.fingerprint}:${row.toolCatalogHash}`;
    if (!groups.has(key)) groups.set(key, { key, caseId: row.caseId, fingerprint: row.fingerprint, toolCatalogHash: row.toolCatalogHash, rows: [] });
    groups.get(key).rows.push(row);
  }
  return [...groups.values()].map(group => ({ ...group, successes: group.rows.filter(r => r.passed).length, attempts: group.rows.length }));
}
