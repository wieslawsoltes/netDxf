import { installBrowserTransport } from './browser-transport.mjs';

const nativeFetch = globalThis.fetch.bind(globalThis);
const startup = document.getElementById('browser-startup');
const runtime = document.getElementById('blazor-runtime');
const diagnostics = document.body.dataset.workspace === 'diagnostics';

try {
  // Reuse the actual frontend markup. Scripts are imported only after the .NET engine
  // and the local transport are ready; no nonfunctional server frontend is published.
  const response = await nativeFetch(new URL(diagnostics ? 'diagnostics-shell.html' : 'studio-shell.html', document.baseURI));
  if (!response.ok) throw new Error(`Cannot load the Studio interface (${response.status}).`);
  const shell = new DOMParser().parseFromString(await response.text(), 'text/html');
  for (const script of shell.querySelectorAll('script')) script.remove();
  for (const link of shell.head.querySelectorAll('link[rel="stylesheet"]')) document.head.append(document.importNode(link, true));
  for (const node of [...shell.body.childNodes]) {
    if (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'NOSCRIPT') continue;
    document.body.append(document.importNode(node, true));
  }
  const badge = document.querySelector('.local-badge');
  if (badge) { badge.textContent = 'BROWSER CAD'; badge.title = 'The complete netDxf engine runs locally in this browser tab.'; }
  const loading = document.getElementById('loading');
  if (loading) loading.textContent = 'Starting browser CAD engine…';
  await Blazor.start();
  const transport = installBrowserTransport({ nativeFetch });
  // This frozen diagnostic object is useful to integration tests and browser tooling.
  globalThis.shadowBrowser = Object.freeze({ runtime: 'blazor-webassembly', fetch: transport.fetch });
  await import(diagnostics ? './studio.js' : './cad/app.mjs');
  startup.remove();
  document.documentElement.dataset.shadowReady = 'true';
  addEventListener('pagehide', event => { if (!event.persisted) transport.dispose(); }, { once: true });
} catch (error) {
  console.error('Shadow browser startup failed:', error);
  startup.dataset.error = 'true';
  startup.replaceChildren();
  const title = document.createElement('h1'); title.textContent = 'Shadow Studio could not start';
  const message = document.createElement('p'); message.textContent = error.message || String(error);
  const detail = document.createElement('p'); detail.className = 'detail';
  detail.textContent = 'Reload to retry. The deployment must contain the complete published wwwroot directory, including _framework and fonts.';
  startup.append(title, message, detail);
  document.documentElement.dataset.shadowError = message.textContent;
}
