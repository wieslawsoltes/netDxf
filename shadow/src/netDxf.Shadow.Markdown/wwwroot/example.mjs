import { createMarkdownView } from './markdown.mjs';
const source = '# Drawing review\n\nThe **tank and pump** drawing contains a clear process path, *editable symbols*, and ~~temporary~~ final labels.\n\n> Review the geometry and connections before publishing.\n\n| Object | Tag | Check |\n|:--|:--|--:|\n| Storage tank | `T-101` | Pass |\n| Transfer pump | `P-101` | Pass |\n| Instrument | `PI-102` | Pass |\n\n1. Inspect the drawing\n   - [x] Check equipment tags\n   - [x] Verify pipe connections\n   - [ ] Review the final sheet\n2. Save and reload the DXF\n\n```csharp drawing.cs\nvar document = ProcessDiagramFactory.Create();\ndocument.Save("tank-and-pump.dxf");\n```\n\n[Renderer documentation](https://marked.js.org/) · Raw HTML stays text: <img src=x onerror=alert(1)>\n';
const view = createMarkdownView(document.getElementById('content'), { onCopy: text => navigator.clipboard.writeText(text) });
view.update(source);
let timer;
document.getElementById('stream').addEventListener('click', () => {
  clearInterval(timer); let index = 0;
  timer = setInterval(() => { index += 36; view.update(source.slice(0,index), { streaming: index < source.length }); if (index >= source.length) clearInterval(timer); }, 45);
});
