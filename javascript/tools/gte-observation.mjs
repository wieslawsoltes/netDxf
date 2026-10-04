// Envelope validation only; no algorithm output or exception normalization.
export function validateGteObservation(request, result) {
  if (request.op !== 'gte' || !Array.isArray(request.steps) || request.steps.length === 0)
    throw new Error('A nonempty GTE request is required.');
  if (!Array.isArray(result) || result.length !== request.steps.length)
    throw new Error('Incomplete GTE operation sequence.');
  for (let index = 0; index < result.length; index++) {
    const entry = result[index], step = request.steps[index];
    if (entry == null || typeof entry !== 'object' || Array.isArray(entry)) throw new Error('Invalid GTE result envelope.');
    if (entry.ok === true) {
      const outputs = step.kind === 'call' ? (step.args ?? []).filter(arg => arg && (Object.hasOwn(arg, 'out') || Object.hasOwn(arg, 'cell'))).length : 0;
      if (!Object.hasOwn(entry, 'value') || !Array.isArray(entry.outputs) || entry.outputs.length !== outputs ||
          Object.keys(entry).some(key => !['ok', 'value', 'outputs'].includes(key)))
        throw new Error('Incomplete GTE value/ref-output observation at operation ' + index);
    } else if (entry.ok !== false || typeof entry.error !== 'string' || !entry.error ||
      !(entry.param === null || typeof entry.param === 'string') || Object.keys(entry).some(key => !['ok','error','param'].includes(key)))
      throw new Error('Invalid GTE exception observation at operation ' + index);
  }
  return result;
}
