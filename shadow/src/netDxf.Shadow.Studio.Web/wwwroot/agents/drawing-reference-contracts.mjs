const start = '<shadow-drawing-references-v1>', end = '</shadow-drawing-references-v1>';
export const referenceKey = ref => JSON.stringify([ref.documentId, ref.workspaceId, ref.objectId, ref.occurrenceKey || null]);
export function validateReferenceShape(references) {
  if (!Array.isArray(references) || references.length > 32) throw new Error('Attach at most 32 drawing objects.');
  const seen = new Set();
  for (const ref of references) {
    if (!ref || !['documentId', 'workspaceId', 'objectId', 'type', 'layout'].every(key => typeof ref[key] === 'string' && ref[key].length > 0 && ref[key].length <= 512 && !/[\u0000-\u001f]/.test(ref[key]))
      || ![ref.revision, ref.viewVersion].every(value => Number.isSafeInteger(value) && value >= 0)
      || ref.occurrenceKey != null && (typeof ref.occurrenceKey !== 'string' || ref.occurrenceKey.length > 4096)
      || ref.insertPath != null && (!Array.isArray(ref.insertPath) || ref.insertPath.length > 64 || !ref.insertPath.every(value => typeof value === 'string' && value.length <= 512)))
      throw new Error('The drawing reference is invalid. Capture the selection again.');
    const key = referenceKey(ref); if (seen.has(key)) throw new Error('Duplicate drawing reference.'); seen.add(key);
  }
  return structuredClone(references);
}
/** Only our final bounded declaration is decoded; arbitrary earlier message text stays visible. */
export function splitReferencePrompt(value) {
  const text = String(value || ''), index = text.lastIndexOf(`\n\n${start}\n`);
  if (index < 0 || !text.endsWith(`\n${end}`)) return { text, references: [] };
  try {
    const references = validateReferenceShape(JSON.parse(text.slice(index + start.length + 3, -end.length - 1)));
    return { text: text.slice(0, index), references };
  } catch { return { text, references: [] }; }
}
