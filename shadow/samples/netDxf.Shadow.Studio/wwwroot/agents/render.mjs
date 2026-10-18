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
    arrow: 'M4 12h16M14 6l6 6-6 6', retry: 'M3 9a9 9 0 1 1 1 8M3 3v7h7'
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

function inline(text) {
  const fragment = document.createDocumentFragment();
  // Link labels, inline code, and emphasis remain text nodes. No HTML renderer or model-generated attributes.
  const pattern = /(`[^`\n]+`|\*\*[^*\n]+\*\*|\[[^\]\n]+\]\(https?:\/\/[^\s)]+\))/g;
  let last = 0;
  for (const match of text.matchAll(pattern)) {
    fragment.append(text.slice(last, match.index)); const value = match[0];
    if (value.startsWith('`')) fragment.append(node('code', { text: value.slice(1, -1) }));
    else if (value.startsWith('**')) fragment.append(node('strong', { text: value.slice(2, -2) }));
    else {
      const parts = /^\[([^\]]+)\]\(([^)]+)\)$/.exec(value);
      try {
        const url = new URL(parts[2]);
        if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error();
        fragment.append(node('a', { text: parts[1], href: url.href, target: '_blank', rel: 'noopener noreferrer' }));
      } catch { fragment.append(value); }
    }
    last = match.index + value.length;
  }
  fragment.append(text.slice(last)); return fragment;
}

/** Minimal, safe rich text with first-class code artifacts. Streaming calls remain plain text for speed. */
export function markdown(text, { onCode, onCopy } = {}) {
  const fragment = document.createDocumentFragment(), lines = String(text).split('\n');
  let paragraph = [], list = null;
  const flush = () => { if (paragraph.length) { fragment.append(node('p', {}, [inline(paragraph.join('\n'))])); paragraph = []; } list = null; };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i], fence = /^\s*```([^`]*)$/.exec(line);
    if (fence) {
      flush(); const collected = [];
      while (++i < lines.length && !/^\s*```\s*$/.test(lines[i])) collected.push(lines[i]);
      const content = collected.join('\n'), info = fence[1].trim(), language = info.split(/\s+/)[0] || 'text';
      const toolbar = node('div', { class: 'agent-code-label' }, [node('span', { text: info || 'text' }), node('span', { class: 'grow' })]);
      if (onCopy) toolbar.append(button('Copy code', () => onCopy(content), { icon: 'copy', compact: true }));
      if (onCode) toolbar.append(button('Save to workspace', () => onCode(content, info), { icon: 'file', compact: true }));
      fragment.append(node('div', { class: 'agent-code' }, [toolbar, node('pre', {}, [node('code', { text: content, 'data-language': language })]) ]));
      continue;
    }
    if (!line.trim()) { flush(); continue; }
    const heading = /^(#{1,4})\s+(.+)$/.exec(line);
    if (heading) { flush(); fragment.append(node(`h${Math.min(heading[1].length + 2, 6)}`, {}, [inline(heading[2])])); continue; }
    const bullet = /^\s*(?:[-*]\s+|\d+\.\s+)(.+)$/.exec(line);
    if (bullet) {
      if (paragraph.length) flush();
      if (!list) { list = node(/^\s*\d/.test(line) ? 'ol' : 'ul'); fragment.append(list); }
      list.append(node('li', {}, [inline(bullet[1])])); continue;
    }
    if (/^>\s?/.test(line)) { flush(); fragment.append(node('blockquote', {}, [inline(line.replace(/^>\s?/, ''))])); continue; }
    if (/^\s*(?:---+|\*\*\*+)\s*$/.test(line)) { flush(); fragment.append(node('hr')); continue; }
    if (list) flush(); paragraph.push(line);
  }
  flush(); return fragment;
}
