import test from 'node:test';
import assert from 'node:assert/strict';
import { parseMarkdown, renderMarkdown, markdownUrl, createMarkdownView } from '../../src/netDxf.Shadow.Markdown/wwwroot/markdown.mjs';

// Minimal DOM recording adapter: assertions inspect semantic elements, not HTML strings.
class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.attributes = {}; this.classList = { add: () => {} }; }
  append(...values) { for (const value of values) value?.tag === '#fragment' ? this.children.push(...value.children) : this.children.push(value); }
  replaceChildren(...values) { this.children = []; this.append(...values); }
  setAttribute(k, v) { this.attributes[k] = v; }
  addEventListener(k, v) { this[k] = v; }
  set textContent(text) { this.children = [String(text)]; }
  get textContent() { return this.children.map(c => typeof c === 'string' ? c : c.textContent).join(''); }
  set innerHTML(value) { assert.match(value, /^&(?:#[0-9]+|#x[0-9a-f]+|[a-z][a-z0-9]+);$/i); this.value = ({'&amp;':'&','&lt;':'<','&gt;':'>','&quot;':'"','&apos;':"'",'&nbsp;':'\u00a0'})[value] || value; }
}
const doc = { createElement: tag => new Element(tag), createDocumentFragment: () => new Element('#fragment'), baseURI: 'https://studio.test/app/' };
const render = (text, options = {}) => renderMarkdown(text, { document: doc, ...options });
const find = (root, tag) => [root, ...root.children.flatMap(c => typeof c === 'string' ? [] : find(c, tag))].filter(n => n.tag === tag);

test('CommonMark emphasis, references, escapes and explicit line breaks', () => {
  const root = render('# A\n\n*one* and **two** and ~~old~~  \nnext [site][ref]\n\n[ref]: https://example.org "Title"');
  assert.equal(find(root, 'h1')[0].textContent, 'A'); assert.equal(find(root, 'em')[0].textContent, 'one');
  assert.equal(find(root, 'strong')[0].textContent, 'two'); assert.equal(find(root, 'del')[0].textContent, 'old');
  assert.equal(find(root, 'br').length, 1); assert.equal(find(root, 'a')[0].title, 'Title');
});
test('GFM aligned tables and nested ordered/task lists preserve structure', () => {
  const root = render('|A|B|\n|:-|--:|\n|x|**y**|\n\n3. third\n   - [x] done\n   - [ ] next\n4. fourth');
  assert.equal(find(root,'table').length,1); assert.equal(find(root,'td')[1].className,'align-right');
  assert.equal(find(root,'ol')[0].start,3); assert.equal(find(root,'ul').length,1);
  assert.doesNotMatch(root.textContent,/\[[x ]\]/); assert.equal(find(root,'input').length,2); assert.equal(find(root,'input')[0].checked,true); assert.equal(find(root,'input')[0].disabled,true);
});
test('tilde and incomplete fences remain readable with exact code copy/save callbacks', () => {
  let copied, saved; const root = render('~~~cs file.cs\nif (a < b) { }\n~~~\n\n```js\nconst x = 1;', {onCopy:text=>copied=text,onCode:(text,info)=>saved=[text,info]});
  assert.equal(find(root,'pre').length,2); const buttons=find(root,'button');buttons[0].click();buttons[1].click();
  assert.equal(copied,'if (a < b) { }');assert.deepEqual(saved,[copied,'cs file.cs']);
  assert.equal(find(root,'pre')[1].textContent,'const x = 1;');
});
test('raw HTML, javascript links and tracking images cannot become executable DOM', () => {
  const root=render('<script>alert(1)</script>\n\n[x](javascript:alert%281%29) ![tracking](https://example.org/t.png)\n\n`<img onerror=x>`');
  assert.equal(find(root,'script').length,0);assert.equal(find(root,'img').length,0);assert.match(root.textContent, /<script>/);
  assert.equal(find(root,'a').length,1);assert.equal(find(root,'a')[0].rel,'noopener noreferrer');
  for(const url of ['javascript:alert(1)','data:text/html,x','file:///etc/passwd','https://u:p@example.org','java\nscript:x']) assert.equal(markdownUrl(url),null);
});
test('streamed content coalesces and final response renders immediately', async () => {
  const root=new Element('div'), view=createMarkdownView(root,{document:doc});
  view.update('**one',{streaming:true});view.update('**one**',{streaming:true});view.update('**final**');
  assert.equal(find(root,'strong')[0].textContent,'final');view.dispose();
  await new Promise(r=>setTimeout(r,110));assert.equal(root.textContent,'final');
});
test('size budget prevents pathological unbounded parsing', () => assert.throws(()=>parseMarkdown('a'.repeat(1_000_001)),RangeError));

test('inline code preserves literal entities', () => assert.equal(find(render('`&amp;`'),'code')[0].textContent,'&amp;'));
