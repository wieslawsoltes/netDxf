/** Validate evidence without discarding negative results or treating an empty suite as a pass. */
export function evidenceProblems(report, proof, requirements = {}) {
  if (!report || typeof report !== 'object') return ['Missing evidence.'];
  const problems = [];
  for (const key of ['runtimeFingerprint', 'verificationFingerprint']) {
    if (report[key] !== proof[key]) problems.push(`Stale or missing ${key}.`);
  }
  if (report.completed !== true) problems.push('Execution did not complete.');
  if (report.fatal) problems.push(`Execution error: ${report.fatal}`);
  const count = report.stats?.failures;
  if (count !== undefined && (!Number.isInteger(count) || count < 0 || count > 0))
    problems.push(`Exact-comparison failures: ${count}.`);
  for (const key of ['failed', 'skipped', 'todo']) {
    if (report[key] !== undefined && report[key] !== 0) problems.push(`${key}: ${report[key]}.`);
  }
  for (const key of ['failures', 'missing', 'pageErrors']) {
    if (Array.isArray(report[key]) && report[key].length) problems.push(`${key}: ${report[key].length} entries.`);
  }
  for (const [field, expected] of Object.entries(requirements.equal ?? {})) {
    const actual = field.split('.').reduce((value, key) => value?.[key], report);
    if (actual !== expected) problems.push(`${field}: expected ${expected}, received ${actual}.`);
  }
  for (const [field, minimum] of Object.entries(requirements.minimum ?? {})) {
    const actual = field.split('.').reduce((value, key) => value?.[key], report);
    if (!Number.isFinite(actual) || actual < minimum) problems.push(`${field}: minimum ${minimum}, received ${actual}.`);
  }
  return problems;
}
