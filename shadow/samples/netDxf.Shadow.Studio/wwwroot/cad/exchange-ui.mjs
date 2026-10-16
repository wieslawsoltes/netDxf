import { el } from './ui.mjs';
import { drawingFileName, exchangeCapabilities, saveRequest } from './exchange-contracts.mjs';

/** Format selection keeps the original drawing receipt across asynchronous capability reads and modal input. */
export async function saveDrawingAs({ client, dialog, inputRow, download, documentName, refreshDocuments, log, editable }) {
  editable();
  const expectedKey = client.key;
  const response = await client.request('/api/cad/formats');
  const capabilities = exchangeCapabilities(await response.json());
  const format = el('select', { 'aria-label': 'Drawing format' }, [
    el('option', { value: 'dxf', text: 'DXF — native netDxf writer' }),
    el('option', { value: 'dwg', text: `DWG — ${capabilities.provider}` })
  ]);
  const version = el('select', { 'aria-label': 'DWG version', disabled: true },
    capabilities.dwgWriteVersions.map(v => el('option', { value: v, text: v })));
  version.value = capabilities.defaultDwgVersion;
  const filename = el('input', { type: 'text', 'aria-label': 'Drawing filename', required: true,
    maxlength: 120, value: drawingFileName(documentName, 'dxf') });
  const validateName = () => {
    try { drawingFileName(filename.value, format.value); filename.setCustomValidity(''); }
    catch (error) { filename.setCustomValidity(error.message); }
  };
  filename.addEventListener('input', validateName);
  format.addEventListener('change', () => {
    version.disabled = format.value !== 'dwg';
    validateName();
    if (filename.validity.valid) filename.value = drawingFileName(filename.value, format.value);
  });
  const accepted = await dialog('Save drawing as', [
    inputRow('Format', format), inputRow('DWG version', version), inputRow('Filename', filename),
    el('p', { class: 'hint', text: 'DWG exchange is staged and reread before download. Unsupported content or conversion diagnostics stop the export; this is not a guarantee of lossless proprietary metadata. Keep the original drawing. This download is not durable server-side storage.' })
  ], 'Download');
  if (!accepted) return;
  editable();
  const payload = saveRequest(format.value, version.value, capabilities);
  const name = drawingFileName(filename.value, payload.format);
  // The serialized queue rechecks this exact receipt before invoking the host.
  const blob = await client.mutate('save', payload, { expectedKey });
  download(blob, name);
  await refreshDocuments();
  log(`${payload.format.toUpperCase()} download created: ${name}. Keep the downloaded file to retain this session.`, 'success');
}
