# Reusable Markdown presentation

`netDxf.Shadow.Markdown` is a Razor static web asset library with no dependency on
Studio or the agent runtime. Reference the project/package from an ASP.NET Core
or Blazor host. Assets are published at `markdown/` and work below a site base path.

```js
import { renderMarkdown, createMarkdownView } from './markdown/markdown.mjs';
container.classList.add('shadow-markdown');
container.replaceChildren(renderMarkdown(source, { onCopy: text => navigator.clipboard.writeText(text) }));

const view = createMarkdownView(container, { onCode: (text, info) => saveCode(text, info) });
view.update(partialResponse, { streaming: true }); // coalesced to 100 ms
view.update(finalResponse);                      // immediate final rendering
view.dispose();                                 // when removing the conversation row
```

Include `markdown/markdown.css` for scoped typography. Supported syntax uses
Marked 18.1.1's CommonMark/GFM lexer: headings, nested ordered/unordered/task lists,
quotes, tables/alignment, emphasis/strikethrough, references, escapes, autolinks,
inline code, indented/fenced code and incomplete streaming fences. Copy/save
callbacks receive exact code and language/file info. The host decides what saving
code means; the renderer never executes it. Syntax highlighter plugins and math
extensions are not bundled.

The library builds DOM nodes rather than parsing generated HTML. Raw HTML appears
as text, unsafe/credential-bearing links are rejected, external images appear as
links unless the host explicitly supplies `allowImage(url)`. Code is never executed.
A one-million-character input budget and 64-level render nesting bound protect the
UI. The streaming view falls back to plain text if parsing fails. All dependencies
are pinned and vendored with their MIT license; no runtime CDN is required.

Tests: `node --test shadow/tests/browser/markdown.test.mjs`.
