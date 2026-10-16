import { installBrowserTransport } from './browser-transport.mjs';

const nativeFetch = globalThis.fetch.bind(globalThis);
const startup = document.getElementById('browser-startup');
const diagnostics = document.body.dataset.workspace === 'diagnostics';

try {
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
  const fontInput = document.getElementById('font-file');
  if (fontInput) {
    fontInput.accept = '.shx,.shp,.ttf,.otf';
    fontInput.title = 'Load SHX/SHP strokes or TrueType/OpenType fonts. Fonts remain private to this drawing and browser tab.';
  }
  const loading = document.getElementById('loading');
  if (loading) loading.textContent = 'Starting browser CAD engine…';
  // Blazor.start can complete before Program.cs is loaded or its async work completes.
  // Program.cs calls this fence only after the application and native fonts are ready.
  const ready = new Promise((resolve, reject) => {
    globalThis.shadowBrowserBoot = Object.freeze({ complete: resolve, fail: message => reject(new Error(message)) });
  });
  await Promise.all([Blazor.start(), ready]);
  delete globalThis.shadowBrowserBoot;
  const transport = installBrowserTransport({ nativeFetch });
  globalThis.shadowBrowser = Object.freeze({ runtime: 'blazor-webassembly', fetch: transport.fetch });
  await import(diagnostics ? './studio.js' : './cad/app.mjs');
  startup.remove();
  document.documentElement.dataset.shadowReady = 'true';
  addEventListener('pagehide', event => { if (!event.persisted) transport.dispose(); });
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
