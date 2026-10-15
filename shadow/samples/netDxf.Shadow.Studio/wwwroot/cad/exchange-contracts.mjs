/** Host-independent interchange contracts. File extensions are labels, never codec selectors. */
export const maximumDrawingBytes = 20 * 1024 * 1024;
export function drawingFormat(prefix) {
  if (!(prefix instanceof Uint8Array)) throw new TypeError('A byte prefix is required.');
  if (prefix.length < 6) throw new Error('The drawing is empty or truncated.');
  return prefix[0] === 65 && prefix[1] === 67 && prefix.subarray(2, 6).every(b => b >= 48 && b <= 57) ? 'dwg' : 'dxf';
}
export async function inspectDrawing(file) {
  if (!file || !Number.isSafeInteger(file.size) || file.size < 6 || file.size > maximumDrawingBytes)
    throw new Error('Drawing uploads require 6 bytes to 20 MiB.');
  return drawingFormat(new Uint8Array(await file.slice(0, 6).arrayBuffer()));
}
export function drawingFileName(name, format) {
  if (!['dxf', 'dwg'].includes(format)) throw new Error('Unknown drawing encoding.');
  if (typeof name !== 'string' || /[\x00-\x1f\x7f/\\]/.test(name)) throw new Error('Drawing names cannot contain controls or path separators.');
  const stem = name.trim().replace(/\.(?:dxf|dwg)$/i, '') || 'Drawing';
  return `${stem.slice(0, 116)}.${format}`;
}
export function exchangeCapabilities(value) {
  if (!value || typeof value.provider !== 'string' || value.provider.length > 120 ||
      !Array.isArray(value.dwgWriteVersions) || value.dwgWriteVersions.length < 1 || value.dwgWriteVersions.length > 16 ||
      value.dwgWriteVersions.some(v => typeof v !== 'string' || !/^AC\d{4}$/.test(v)) ||
      new Set(value.dwgWriteVersions).size !== value.dwgWriteVersions.length ||
      !value.dwgWriteVersions.includes(value.defaultDwgVersion)) throw new Error('Invalid drawing-format capabilities from the host.');
  return Object.freeze({ provider: value.provider, defaultDwgVersion: value.defaultDwgVersion,
    dwgWriteVersions: Object.freeze([...value.dwgWriteVersions]) });
}
export function saveRequest(format, version, capabilities) {
  if (format === 'dxf') return { format: 'dxf' };
  if (format !== 'dwg' || !capabilities.dwgWriteVersions.includes(version)) throw new Error('Choose a DWG version declared by this host.');
  return { format: 'dwg', version };
}
