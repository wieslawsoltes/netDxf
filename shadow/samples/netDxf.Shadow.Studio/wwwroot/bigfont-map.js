import { identityHeaders } from './studio-state.mjs';

// Shares the existing font control's read-only/workspace state without a second source model.
const font = document.getElementById('font-file');
const input = document.getElementById('bigfont-map-file');
if (font && input) {
  let uploading = false;
  const sync = () => { input.disabled = uploading || font.disabled; };
  const observer = new MutationObserver(sync);
  observer.observe(font, { attributes: true, attributeFilter: ['disabled'] });
  sync();
  async function checked(response) {
    if (response.ok) return response;
    let message = `HTTP ${response.status}`;
    try { message = (await response.json()).error || message; } catch { /* Keep transport status. */ }
    throw new Error(message);
  }
  input.addEventListener('change', async () => {
    const file = input.files?.[0]; input.value = '';
    if (!file || uploading || font.disabled) return;
    uploading = true; sync();
    try {
      if (file.size > 4 * 1024 * 1024) throw new Error('BIGFONT map limit is 4 MiB.');
      const captured = await (await checked(await fetch('api/state', { cache: 'no-store' }))).json();
      if (captured.workspaceId !== 'live') throw new Error('BIGFONT maps can only be changed in the live workspace.');
      const bytes = await file.arrayBuffer();
      if (font.disabled) throw new Error('The view changed before the mapping upload.');
      await checked(await fetch(`api/bigfont-map?revision=${captured.revision}`, {
        method: 'POST', headers: { ...identityHeaders(captured), 'Content-Type': 'application/json' }, body: bytes
      }));
      // The existing coalescing SSE feed publishes the new resource/view receipt. Never retry a mutation.
    } catch (error) {
      const notice = document.getElementById('notification');
      if (notice) { notice.textContent = error.message || String(error); notice.hidden = false; }
    } finally { uploading = false; sync(); }
  });
  window.addEventListener('pagehide', () => observer.disconnect(), { once: true });
}
