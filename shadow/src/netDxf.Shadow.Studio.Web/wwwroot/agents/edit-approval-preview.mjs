import { node, button } from './render.mjs';

/** Keep approval disabled unless both immutable preview frames have loaded. */
export function mountEditApprovalPreview({ container, previewId, load, open, onReady }) {
  let disposed = false;
  const urls = [], status = node('p', { role: 'status', text: 'Loading before and after views…' });
  const frames = node('div', { class: 'agent-edit-preview-frames' });
  const view = node('section', { class: 'agent-edit-preview', 'aria-label': 'Drawing edit preview' }, [status, frames]);
  container.append(view); onReady(false);
  void (async () => {
    if (typeof load !== 'function' || typeof previewId !== 'string' || !previewId.length || previewId.length > 128)
      throw new Error('A current drawing preview is required before approving this edit.');
    const blobs = await Promise.all([load(previewId, false), load(previewId, true)]);
    if (disposed) return;
    if (blobs.some(blob => !(blob instanceof Blob) || !blob.type.startsWith('image/') || blob.size === 0))
      throw new Error('Drawing preview is unavailable or expired. Ask the agent to prepare it again.');
    await Promise.all(blobs.map((blob, index) => new Promise((resolve, reject) => {
      const url = URL.createObjectURL(blob); urls.push(url);
      const image = node('img', { alt: index ? 'Drawing after the proposed edit' : 'Drawing before the proposed edit' });
      image.onload = resolve; image.onerror = () => reject(new Error('The drawing preview could not be displayed. Prepare a new preview.'));
      frames.append(node('figure', {}, [image, node('figcaption', { text: index ? 'After' : 'Before' })])); image.src = url;
    })));
    if (disposed) return;
    status.textContent = 'Review both views. Applying this preview creates one undo transaction.';
    if (open) view.append(button('Open drawing preview', () => open(previewId)));
    onReady(true);
  })().catch(error => { if (!disposed) { status.textContent = `${error.message} Deny this request and prepare a new preview.`; onReady(false); } });
  return Object.freeze({ dispose() { disposed = true; for (const url of urls) URL.revokeObjectURL(url); urls.length = 0; } });
}
