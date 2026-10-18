import { executionModes } from './contracts.mjs';

const count = value => Number.isSafeInteger(value) && value >= 0 ? value : 0;
const terminal = new Set(['completed', 'blocked', 'paused', 'cancelled', 'error']);

/** Recover task settings for review only when the user has not configured this runtime. */
export function recoveredRunSelection(checkpoint, currentConfiguration) {
  if (currentConfiguration !== null && currentConfiguration !== undefined) return null;
  let value;
  try { value = typeof checkpoint === 'string' ? JSON.parse(checkpoint) : checkpoint; } catch { return null; }
  const mode = String(value?.run?.mode || '').toLowerCase(), successCriteria = value?.run?.successCriteria ?? '';
  if (!executionModes.some(item => item.id === mode) || typeof successCriteria !== 'string' || successCriteria.length > 16000) return null;
  return { mode, successCriteria };
}

/** Poll snapshots are authoritative; ordinary assistant text never proves completion. */
export function presentRun(transcript, configuration = {}) {
  const run = transcript.run;
  const requestedMode = String(run?.mode || configuration.mode || 'normal').toLowerCase();
  const mode = executionModes.find(item => item.id === requestedMode) || executionModes[0];
  let phase = String(run?.phase || transcript.status || 'idle').toLowerCase();
  const restarting = transcript.running && terminal.has(phase);
  if (restarting) phase = 'running';
  const labels = {
    idle: 'Ready', running: mode.id === 'plan' ? 'Investigating the task' : mode.id === 'goal' ? 'Working toward the goal' : 'Working on the task',
    continuing: 'Continuing the task', compacting: 'Managing context', approval: 'Approval required',
    completed: mode.id === 'plan' ? 'Plan ready' : mode.id === 'goal' ? 'Goal completed' : 'Task completed',
    blocked: 'Blocked', paused: 'Paused', cancelled: 'Stopped', error: 'Run interrupted', reconnecting: 'Reconnecting to the runtime'
  };
  const turns = count(run?.turnCount), totalTurns = count(run?.totalTurns), limit = count(run?.turnLimit ?? configuration.maxTurns);
  const continuations = count(run?.continuationCount), outputLimits = count(run?.outputLimitCount), noProgress = count(run?.noProgressCount);
  const progress = [limit ? `${turns} / ${limit} model turns` : `${turns} model turns · no fixed limit`];
  if (totalTurns > turns) progress.push(`${totalTurns} total`);
  if (continuations) progress.push(`${continuations} continuation${continuations === 1 ? '' : 's'}`);
  if (outputLimits) progress.push(`${outputLimits} output limit${outputLimits === 1 ? '' : 's'}`);
  if (noProgress) progress.push(`${noProgress} without progress`);
  const interrupted = ['blocked', 'paused', 'cancelled', 'error'].includes(phase);
  const hasTask = transcript.items.some(item => item.kind === 'user');
  const resumable = !transcript.running && (run ? run.resumable === true : interrupted && hasTask);
  let detail = restarting ? '' : String(run?.detail || '');
  if (!detail && interrupted) detail = run?.stopReason ? String(run.stopReason).replace(/[_-]+/g, ' ')
    : phase === 'paused' ? 'The run paused before completion. Resume to continue the existing task.'
    : phase === 'cancelled' ? 'The task was stopped. Resume when you are ready to continue.'
    : phase === 'blocked' ? 'Resolve the blocker, then resume the task.' : 'Review the error before resuming the task.';
  const verification = Array.isArray(run?.verification) ? run.verification.filter(value => typeof value === 'string').slice(0, 64) : [];
  return {
    visible: phase !== 'idle' || turns > 0 || resumable, mode: mode.id, modeName: mode.name, phase,
    title: phase === 'idle' && resumable ? 'Task ready to resume' : labels[phase] || (transcript.running ? 'Working on the task' : 'Ready'),
    detail, progress: progress.join(' · '), turns, totalTurns, limit, continuations, outputLimits,
    resumable, executePlan: !transcript.running && phase === 'completed' && mode.id === 'plan',
    criteria: mode.id === 'goal' ? String(run?.successCriteria || configuration.successCriteria || '') : '',
    summary: phase === 'completed' || phase === 'blocked' ? String(run?.completionSummary || '') : '',
    verification: restarting ? [] : verification, stopReason: restarting ? '' : String(run?.stopReason || '')
  };
}
