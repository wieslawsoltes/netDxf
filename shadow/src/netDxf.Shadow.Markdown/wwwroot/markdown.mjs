import { Marked } from './vendor/marked.mjs';

const parser = new Marked({ gfm: true, breaks: false, pedantic: false });
const maxCharacters = 1_000_000;
/** CommonMark/GFM tokenization without HTML generation or global parser mutation. */
export function parseMarkdown(source) {
  source = String(source ?? '');
  if (source.length > maxCharacters) throw new RangeError('Markdown exceeds the one-million-character display limit.');
  return parser.lexer(source);
}
/** Links never execute script or import credential-bearing URLs. Images require a separate policy. */
export function markdownUrl(value, baseUrl = 'https://markdown.invalid/') {
  if (typeof value !== 'string' || /[\u0000-\u0020\u007f]/.test(value)) return null;
  try {
    const url = new URL(value, baseUrl);
    return ['https:', 'http:', 'mailto:'].includes(url.protocol) && !url.username && !url.password ? url.href : null;
  } catch { return null; }
}

/** Renders tokens using DOM creation only; raw HTML remains visible text. No model HTML is inserted. */
export function renderMarkdown(source, { document: doc = globalThis.document, onCopy, onCode,
  headingOffset = 0, baseUrl = doc?.baseURI, allowImage = () => false } = {}) {
  if (!doc?.createElement) throw new TypeError('A DOM document is required.');
  const element = (name, text) => { const e = doc.createElement(name); if (text !== undefined) e.textContent = text; return e; };
  const fragment = () => doc.createDocumentFragment();
  const entities = text => String(text ?? '').replace(/&(?:#[0-9]+|#x[0-9a-f]+|[a-z][a-z0-9]+);/gi, entity => {
    // Only a single validated entity enters this inert textarea, never source markup.
    const decoder = doc.createElement('textarea'); decoder.innerHTML = entity; return decoder.value;
  });
  const add = (parent, tokens, depth = 0) => {
    if (depth > 64) { parent.append('[Markdown nesting limit]'); return; }
    for (const token of tokens || []) {
      const child = (tag, items = token.tokens) => { const e = element(tag); add(e, items, depth + 1); parent.append(e); return e; };
      switch (token.type) {
        case 'space': case 'checkbox': break;
        case 'heading': child(`h${Math.max(1, Math.min(6, token.depth + headingOffset))}`); break;
        case 'paragraph': child('p'); break;
        case 'blockquote': child('blockquote'); break;
        case 'strong': child('strong'); break;
        case 'em': child('em'); break;
        case 'del': child('del'); break;
        case 'br': parent.append(element('br')); break;
        case 'hr': parent.append(element('hr')); break;
        case 'codespan': parent.append(element('code', token.text)); break;
        case 'escape': parent.append(entities(token.text)); break;
        case 'text': if (token.tokens) add(parent, token.tokens, depth + 1); else parent.append(entities(token.text)); break;
        case 'html': parent.append(token.text); break;
        case 'link': {
          const href = markdownUrl(entities(token.href), baseUrl);
          if (!href) { add(parent, token.tokens, depth + 1); break; }
          const a = element('a'); a.href = href; a.target = '_blank'; a.rel = 'noopener noreferrer';
          if (token.title) a.title = entities(token.title); add(a, token.tokens, depth + 1); parent.append(a); break;
        }
        case 'image': {
          const href = markdownUrl(entities(token.href), baseUrl), alt = entities(token.text);
          if (href && allowImage(href)) {
            const img = element('img'); img.src = href; img.alt = alt; img.loading = 'lazy'; img.referrerPolicy = 'no-referrer'; parent.append(img);
          } else if (href) {
            const a = element('a', alt || 'Image'); a.href = href; a.target = '_blank'; a.rel = 'noopener noreferrer'; a.className = 'markdown-image-link'; parent.append(a);
          } else parent.append(alt);
          break;
        }
        case 'list': {
          const list = element(token.ordered ? 'ol' : 'ul');
          if (token.ordered && token.start !== 1) list.start = token.start;
          for (const item of token.items) {
            const li = element('li');
            if (item.task) { const box = element('input'); box.type = 'checkbox'; box.checked = item.checked; box.disabled = true; box.setAttribute('aria-label', item.checked ? 'Completed task' : 'Incomplete task'); li.append(box); li.className = 'markdown-task'; }
            add(li, item.tokens, depth + 1); list.append(li);
          }
          parent.append(list); break;
        }
        case 'table': {
          const wrap = element('div'); wrap.className = 'markdown-table'; wrap.tabIndex = 0; wrap.setAttribute('aria-label', 'Scrollable table');
          const table = element('table'), head = element('thead'), row = element('tr'), body = element('tbody');
          const cell = (tag, value, i) => { const c = element(tag); if (tag === 'th') c.scope = 'col'; if (token.align[i]) c.className = `align-${token.align[i]}`; add(c, value.tokens, depth + 1); return c; };
          token.header.forEach((value, i) => row.append(cell('th', value, i))); head.append(row);
          token.rows.forEach(values => { const r = element('tr'); values.forEach((value, i) => r.append(cell('td', value, i))); body.append(r); });
          table.append(head, body); wrap.append(table); parent.append(wrap); break;
        }
        case 'code': {
          const info = token.lang || '', language = info.split(/\s+/)[0] || 'text', wrap = element('div'); wrap.className = 'markdown-code agent-code';
          const toolbar = element('div'); toolbar.className = 'markdown-code-toolbar agent-code-label'; toolbar.append(element('span', info || 'text'));
          const action = (label, callback) => { const b = element('button', label); b.type = 'button'; b.addEventListener('click', () => callback(token.text, info)); toolbar.append(b); };
          if (onCopy) action('Copy code', onCopy);
          if (onCode) action('Save to workspace', onCode);
          const pre = element('pre'), code = element('code', token.text); code.setAttribute('data-language', language); pre.append(code); wrap.append(toolbar, pre); parent.append(wrap); break;
        }
        default: parent.append(token.raw || token.text || '');
      }
    }
  };
  const root = fragment(); add(root, parseMarkdown(source)); return root;
}

/** Throttles streamed updates; completed messages flush immediately. Dispose when their row is removed. */
export function createMarkdownView(container, options = {}) {
  let timer, pending = '', previous = null, disposed = false;
  container.classList.add('shadow-markdown');
  const flush = () => {
    clearTimeout(timer); timer = undefined;
    if (disposed || previous === pending) return;
    try { container.replaceChildren(renderMarkdown(pending, options)); }
    catch (error) { container.textContent = pending; container.setAttribute('data-markdown-fallback', error.name); }
    previous = pending;
  };
  return {
    update(text, { streaming = false } = {}) { if (disposed) return; pending = String(text ?? ''); if (!streaming) flush(); else if (!timer) timer = setTimeout(flush, 100); },
    flush,
    dispose() { disposed = true; clearTimeout(timer); }
  };
}
