import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, cp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { Element, all, installDom } from './ui-fixture.mjs';

const assets = await mkdtemp(join(tmpdir(), 'netdxf-reference-ui-'));
await cp(new URL('../../src/netDxf.Shadow.Studio.Web/wwwroot/agents', import.meta.url), join(assets, 'agents'), { recursive: true });
await cp(new URL('../../src/netDxf.Shadow.Markdown/wwwroot', import.meta.url), join(assets, 'markdown'), { recursive: true });
const { createReferenceComposer } = await import(pathToFileURL(join(assets, 'agents/drawing-references.mjs')));
test.after(() => rm(assets, { recursive: true, force: true }));

test('removing the last focused attachment reports focus ownership before detaching its button', async t => {
  const root = installDom(t), oldContains = Element.prototype.contains, oldReplace = Element.prototype.replaceChildren;
  Element.prototype.contains = function (target) { return all(this).includes(target); };
  Element.prototype.replaceChildren = function (...children) {
    if (this.contains(document.activeElement) && this !== document.activeElement) document.activeElement = root;
    return oldReplace.call(this, ...children);
  };
  t.after(() => { Element.prototype.replaceChildren = oldReplace; if (oldContains) Element.prototype.contains = oldContains; else delete Element.prototype.contains; });
  const reference = { documentId: 'drawing', workspaceId: 'live', revision: 1, viewVersion: 1, objectId: 'line', handle: 'A1', type: 'netDxf.Entities.Line', layout: 'Model', insertPath: [] };
  const changes = [];
  const composer = createReferenceComposer({ compact: true, getDocument: () => 'drawing', onChange: (count, info) => changes.push({ count, ...info }), api: {
    capture: async () => ({ references: [reference] }),
    validate: async () => ({ canSend: true, items: [{ reference, current: reference, status: 'current' }] })
  } });
  root.append(composer.element); t.after(() => composer.dispose());
  await composer.capture();
  const remove = all(composer.chips).find(element => element.getAttribute('aria-label')?.startsWith('Remove '));
  assert(remove); document.activeElement = remove;
  await remove.click();
  assert.equal(document.activeElement, root, 'the DOM replacement detached the focused remove control');
  assert.deepEqual(changes.at(-1), { count: 0, focusWasInside: true });
  assert.equal(composer.chips.hidden, true); assert.deepEqual(composer.references, []);
});
