import { readable } from './contracts.mjs';

export function node(tag, attributes = {}, children = []) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(attributes)) {
    if (key === 'text') element.textContent = value;
    else if (key === 'class') element.className = value;
    else if (key.startsWith('on')) element.addEventListener(key.slice(2), value);
    else if (['hidden', 'disabled', 'checked', 'readOnly'].includes(key)) element[key] = !!value;
    else if (value !== undefined && value !== null) element.setAttribute(key, String(value));
  }
  for (const child of children) if (child !== null && child !== undefined) element.append(child);
  return element;
}

export function glyph(name = 'agent') {
  const paths = {
    agent: 'M8 4h8l4 4v8l-4 4H8l-4-4V8zM8 10h2M14 10h2M8 15h8M12 1v3M12 20v3M1 12h3M20 12h3',
    close: 'm6 6 12 12M18 6 6 18', expand: 'M8 3H3v5M16 3h5v5M3 16v5h5M21 16v5h-5',
    send: 'm3 3 18 9-18 9 4-9-4-9M7 12h14', stop: 'M5 5h14v14H5z',
    settings: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M5 19l2-2M17 7l2-2',
    chat: 'M3 4h18v13H9l-5 4v-4H3zM7 8h10M7 12h7',
    tools: 'm4 4 5 5M3 3l4 1-3 3zM9 9l-6 9 3 3 9-6M15 3a6 6 0 0 0-6 6M15 3v5h6M21 8a6 6 0 0 1-6 6M15 14l6 7',
    file: 'M5 2h10l5 5v15H5zM15 2v6h5M8 12h9M8 16h7',
    context: 'M3 5h18v14H3zM7 9h10M7 13h7M7 17h4M7 2v3M17 2v3M7 19v3M17 19v3',
    refresh: 'M20 7a9 9 0 1 0 1 9M20 2v6h-6', plus: 'M12 4v16M4 12h16',
    copy: 'M8 8h13v13H8zM16 5V2H2v14h3', download: 'M12 2v13M7 10l5 5 5-5M3 16v5h18v-5',
    upload: 'M12 16V3M7 8l5-5 5 5M3 16v5h18v-5', pin: 'm8 2 8 2-1 5 3 4-6 1-5 8 2-9-3-4 3-2z',
    check: 'm4 12 5 5L20 6', search: 'M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0m-2 6 6 6',
    key: 'M14 9a5 5 0 1 1-10 0 5 5 0 0 1 10 0m-2 4 8 8m-3-3 3-3m-6 0 3-3',
    arrow: 'M4 12h16M14 6l6 6-6 6', retry: 'M3 9a9 9 0 1 1 1 8M3 3v7h7',
    folder: 'M3 5h7l2 3h9v12H3zM3 8h9', archive: 'M3 3h18v5H3zM5 8v13h14V8M9 12h6',
    play: 'm7 4 14 8-14 8z', trash: 'M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7',
    storage: 'M4 5c0-4 16-4 16 0s-16 4-16 0v14c0 4 16 4 16 0V5M4 12c0 4 16 4 16 0'
  };
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('class', 'agent-icon'); svg.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', paths[name] || paths.agent); svg.append(path);
  return svg;
}

export function button(label, callback, { icon, compact = false, ...attributes } = {}) {
  return node('button', { type: 'button', title: compact ? label : undefined, 'aria-label': compact ? label : undefined,
    ...attributes, onclick: callback }, [icon ? glyph(icon) : null, compact ? null : node('span', { text: label })]);
}
export function field(label, control, help) {
  return node('label', { class: 'agent-field' }, [node('span', { class: 'agent-field-label', text: label }), control,
    help ? node('small', { text: help }) : null]);
}
export function code(value, label = 'JSON') {
  return node('div', { class: 'agent-code' }, [node('div', { class: 'agent-code-label', text: label }), node('pre', {}, [node('code', { text: readable(value) })])]);
}

/** Execution receipts keep output, failures, return values and committed files individually readable. */
export function codeExecution(value) {
  let result = value;
  try {
    if (typeof result === 'string') result = JSON.parse(result);
    if (typeof result?.text === 'string') result = JSON.parse(result.text);
  } catch { return code(value, 'Browser worker result'); }
  if (result?.runtime !== 'isolated-javascript-worker') return code(value, 'Browser worker result');
  const success = result.exitCode === 0 && !result.timedOut;
  const element = node('div', { class: `agent-execution-result${success ? ' success' : ' failed'}` });
  element.append(node('div', { class: 'agent-execution-summary' }, [glyph(success ? 'check' : 'stop'),
    node('strong', { text: result.timedOut ? 'Execution timed out' : success ? 'Execution completed' : `Execution failed · exit ${result.exitCode}` }),
    node('span', { class: 'grow' }), node('small', { text: Number.isFinite(result.elapsedMilliseconds) ? `${Math.round(result.elapsedMilliseconds).toLocaleString()} ms` : '' })]));
  if (result.standardOutput) element.append(code(result.standardOutput, 'Console output'));
  if (result.standardError) element.append(code(result.standardError, 'Console errors'));
  if (result.result !== undefined && result.result !== null) element.append(code(result.result, 'Return value'));
  if (result.truncated) element.append(node('p', { class: 'agent-muted', text: 'Console output reached its display budget.' }));
  const changed = Array.isArray(result.changedFiles) ? result.changedFiles : [], deleted = Array.isArray(result.deletedFiles) ? result.deletedFiles : [];
  element.append(node('p', { class: 'agent-execution-commit', text: result.committed ? `${changed.length} files saved · ${deleted.length} files deleted` : 'Staged file changes were discarded.' }));
  if (changed.length || deleted.length) element.append(node('details', {}, [node('summary', { text: 'Workspace changes' }),
    node('ul', {}, [...changed.slice(0, 512).map(file => node('li', { text: `${file.path} · version ${file.version}` })), ...deleted.slice(0, 512).map(path => node('li', { text: `${path} · deleted` }))])]));
  element.append(node('details', { class: 'agent-execution-receipt' }, [node('summary', { text: 'Full execution receipt' }), code(result, 'Receipt')]));
  return element;
}

// The independent Markdown library also serves non-agent consumers.
export { renderMarkdown as markdown, createMarkdownView } from '../markdown/markdown.mjs';
