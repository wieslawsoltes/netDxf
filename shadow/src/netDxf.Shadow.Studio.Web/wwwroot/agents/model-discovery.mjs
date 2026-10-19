import { modelDiscoveryRequest } from './contracts.mjs';

const positiveInteger = value => Number.isSafeInteger(value) && value > 0 ? value : null;
const count = value => value.toLocaleString();
const sameIdentity = (left, right) => ['provider', 'endpoint', 'apiKey'].every(key => left[key] === right[key]);

/** Provider metadata is optional. Missing limits never acquire a guessed value. */
export function modelCatalog(value, provider) {
  if (!value || value.provider !== provider || !['available', 'unavailable'].includes(value.status) || !Array.isArray(value.models))
    throw new Error('The provider returned an invalid model catalog.');
  const seen = new Set(), models = [];
  for (const item of value.models.slice(0, 2048)) {
    if (!item || typeof item.id !== 'string' || !item.id.trim() || item.id.length > 256 || /[\r\n\0]/.test(item.id) || seen.has(item.id)) continue;
    seen.add(item.id);
    models.push({ id: item.id, displayName: String(item.displayName || item.id).slice(0, 512),
      contextWindowTokens: positiveInteger(item.contextWindowTokens), inputTokenLimit: positiveInteger(item.inputTokenLimit),
      outputTokenLimit: positiveInteger(item.outputTokenLimit), metadataSource: String(item.metadataSource || 'unavailable').slice(0, 100),
      metadataUrl: typeof item.metadataUrl === 'string' && /^https:\/\//.test(item.metadataUrl) ? item.metadataUrl : null });
  }
  return { provider, status: value.status, models, notice: String(value.notice || '').slice(0, 4000), truncated: value.truncated === true || value.models.length > 2048 };
}

export function modelOptionLabel(model) {
  const name = model.displayName === model.id ? model.id : `${model.displayName} (${model.id})`;
  const limits = [model.contextWindowTokens ? `context ${count(model.contextWindowTokens)}` : 'context unknown'];
  if (model.inputTokenLimit) limits.push(`input ${count(model.inputTokenLimit)}`);
  if (model.outputTokenLimit) limits.push(`output ${count(model.outputTokenLimit)}`);
  return `${name} — ${limits.join(' · ')}`;
}

/** Return editable local budgets; a provider input cap is a conservative fallback, not a shared context claim. */
export function selectModelBudgets(model, contextValue, outputValue) {
  const limit = model.contextWindowTokens ?? model.inputTokenLimit;
  const kind = model.contextWindowTokens ? 'context window' : 'input limit';
  let context = String(contextValue), output = String(outputValue), note;
  if (limit === null || limit === undefined) note = 'The provider did not report a context window or input limit. Your manual context budget is retained.';
  else if (limit < 4096 || limit > 4000000) note = `Reported ${kind}: ${count(limit)} tokens, outside the supported local budget range of 4,096–4,000,000. Your manual budget is retained.`;
  else {
    context = String(limit);
    note = model.contextWindowTokens ? `Context window: ${count(limit)} tokens.`
      : `Input limit: ${count(limit)} tokens. Used as a conservative local context budget; the output reserve is also deducted.`;
  }
  const localBudget = Number(context), validBudget = Number.isSafeInteger(localBudget) && localBudget >= 4096 && localBudget <= 4000000;
  const maximum = Math.min(128000, validBudget ? Math.floor(localBudget / 2) - 1 : Infinity, model.outputTokenLimit ?? Infinity);
  if (!validBudget) note += ' Enter a local context budget from 4,096 to 4,000,000 before connecting.';
  if (maximum >= 256 && Number.isSafeInteger(Number(outputValue)) && Number(outputValue) > maximum) {
    output = String(maximum); note += ` Output budget reduced to ${count(maximum)} to fit the selected limits.`;
  } else if (maximum < 256) note += ' The reported output cap is below the supported 256-token minimum; enter compatible limits before connecting.';
  if (model.outputTokenLimit) note += ` Reported output limit: ${count(model.outputTokenLimit)} tokens.`;
  return { context, output, note };
}

/** Latest request wins, even after switching away and back to the same provider. */
export class ModelDiscovery {
  constructor({ load, getIdentity, onChange = () => {} }) {
    this.load = load; this.getIdentity = getIdentity; this.onChange = onChange; this.generation = 0; this.disposed = false;
    this.state = { loading: false, models: [], status: 'idle', notice: '', truncated: false };
  }
  publish(value) { this.state = value; this.onChange(value); }
  invalidate() {
    this.generation++;
    if (!this.disposed) this.publish({ loading: false, models: [], status: 'idle', notice: '', truncated: false });
  }
  async discover() {
    if (this.disposed) return null;
    const request = modelDiscoveryRequest(this.getIdentity()), generation = ++this.generation;
    const current = () => {
      if (this.disposed || generation !== this.generation) return false;
      try { return sameIdentity(request, modelDiscoveryRequest(this.getIdentity())); } catch { return false; }
    };
    this.publish({ loading: true, models: [], status: 'loading', notice: 'Loading the provider model catalog…', truncated: false });
    try {
      const reply = await this.load(request);
      if (!current()) { if (!this.disposed && generation === this.generation) this.invalidate(); return null; }
      const catalog = modelCatalog(reply, request.provider);
      this.publish({ ...catalog, loading: false }); return catalog;
    } catch (error) {
      if (!current()) { if (!this.disposed && generation === this.generation) this.invalidate(); return null; }
      this.publish({ loading: false, models: [], status: 'error', notice: error.message || String(error), truncated: false }); return null;
    }
  }
  dispose() { this.disposed = true; this.generation++; }
}
