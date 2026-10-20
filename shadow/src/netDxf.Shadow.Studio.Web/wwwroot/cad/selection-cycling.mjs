const finitePoint = p => p && Number.isFinite(p.x) && Number.isFinite(p.y) && Number.isFinite(p.z ?? 0);
const boundedText = (value, maximum = 256) => typeof value === 'string' && value.length > 0 && value.length <= maximum;

/** Validate the bounded read contract before drawing previews or offering editable source IDs. */
export function selectionCandidates(value) {
  if (!value || !Array.isArray(value.candidates) || value.candidates.length > 64)
    throw new Error('Invalid overlap candidate response.');
  const roots = new Set(); let points = 0;
  for (const candidate of value.candidates) {
    if (!boundedText(candidate.entity) || !boundedText(candidate.occurrence, 2048) || roots.has(candidate.entity) ||
        !boundedText(candidate.leafEntity) || !Array.isArray(candidate.preview) || candidate.preview.length > 256 ||
        !candidate.bounds || !['minX','minY','maxX','maxY'].every(k => Number.isFinite(candidate.bounds[k])))
      throw new Error('Invalid overlap candidate identity or preview.');
    roots.add(candidate.entity);
    for (const path of candidate.preview) {
      if (!Array.isArray(path.points) || !path.points.every(finitePoint) || (points += path.points.length) > 1024)
        throw new Error('Overlap preview exceeds its geometry budget.');
    }
  }
  return value;
}

/** One cancellable query owner. A late response can never replace a newer scope or selection gesture. */
export class SelectionCycleModel {
  constructor({scope, load, changed = () => {}}) {
    this.scope = scope; this.load = load; this.changed = changed; this.generation = 0; this.state = null;
  }
  cancel() {
    const existed = !!this.state; this.generation++; this.abort?.abort(); this.abort = null; this.state = null;
    if (existed) this.changed(); return existed;
  }
  current() {
    if (this.state && this.state.scope !== this.scope()) this.cancel();
    return this.state;
  }
  async query(point, context = {}) {
    this.cancel();
    const generation = ++this.generation, scope = this.scope(), abort = this.abort = new AbortController();
    this.state = {scope, point, ...context, pending:true, active:false, index:0}; this.changed();
    try {
      const value = await this.load(point, abort.signal);
      if (generation !== this.generation || scope !== this.scope() || abort.signal.aborted) {
        if (generation === this.generation) this.cancel();
        return null;
      }
      if (!value) { this.cancel(); return null; }
      const result = selectionCandidates(value);
      this.state = {...this.state, pending:false, active:!!context.open && result.candidates.length > 1, result};
      this.changed(); return this.state;
    } catch (error) {
      if (generation !== this.generation || abort.signal.aborted || scope !== this.scope()) return null;
      this.cancel(); throw error;
    } finally { if (this.abort === abort) this.abort = null; }
  }
  setIndex(index) {
    const state = this.current(), count = state?.result?.candidates.length;
    if (!count) return;
    state.index = ((index % count) + count) % count; this.changed();
  }
  advance(delta = 1) { this.setIndex((this.current()?.index ?? 0) + delta); }
}

export function selectionCandidateLabel(candidate) {
  const type = String(candidate.type || 'Object').split('.').at(-1);
  return `${type}${candidate.handle ? ` ${candidate.handle}` : ''} — ${candidate.layer || '0'}`;
}
