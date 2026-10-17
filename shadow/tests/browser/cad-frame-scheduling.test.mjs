import test from 'node:test';
import assert from 'node:assert/strict';
import { CadViewport, bindViewportPageLifetime } from '../../samples/netDxf.Shadow.Studio/wwwroot/cad/viewport.mjs';

const deferred = () => { let resolve; const promise = new Promise(complete => { resolve = complete; }); return { promise, resolve }; };
const drain = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };

function fixture(t, read = async () => new Blob(['frame'], { type: 'image/png' })) {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const savedImage = globalThis.Image, savedBrowser = globalThis.shadowBrowser;
  globalThis.Image = class { decode() { return Promise.resolve(); } };
  delete globalThis.shadowBrowser;
  t.after(() => { globalThis.Image = savedImage; globalThis.shadowBrowser = savedBrowser; });
  const requests = [], errors = [];
  const view = Object.assign(Object.create(CadViewport.prototype), {
    camera: { x: 0, y: 0, scale: 2 }, size: { w: 80, h: 60 }, dpr: 1,
    canvas: { width: 80, height: 60 }, backend: 'skia', snapshot: null, generation: 0,
    pointers: new Map(), inputVersion: 0, snapGeneration: 0, hooks: { error: error => errors.push(error) }, invalidate() {},
    client: { key: 'document:revision:selection', epoch: 0, busy: 0, state: { revision: 0 },
      read: async (...args) => { requests.push(args); return read(...args); } }
  });
  return { view, requests, errors, async tick() { t.mock.timers.tick(45); await drain(); } };
}

test('an identical refresh leaves the only pending full-quality frame running', async t => {
  const pending = deferred(), { view, requests, tick } = fixture(t, () => pending.promise);
  view.requestFrame(); await tick();
  for (let i = 0; i < 20; i++) { view.requestFrame(); await tick(); }
  assert.equal(requests.length, 1);
  assert.equal(requests[0][1].signal.aborted, false);
  pending.resolve(new Blob(['current'], { type: 'image/png' })); await drain();
  assert.ok(view.frame);
});

test('identical completed frames are reused and explicit regeneration still executes', async t => {
  const { view, requests, tick } = fixture(t);
  view.requestFrame(); await tick();
  const image = view.frame.image;
  for (let i = 0; i < 20; i++) { view.requestFrame(); await tick(); }
  assert.equal(requests.length, 1); assert.equal(view.frame.image, image);
  view.requestFrame(true); await tick();
  assert.equal(requests.length, 2); assert.notEqual(view.frame.image, image);
});

test('camera, selection, backend, retained snapshot and pixel size independently invalidate a frame', async t => {
  const { view, requests, tick } = fixture(t);
  view.requestFrame(); await tick();
  const changes = [() => view.camera.x++, () => view.camera.y++, () => view.camera.scale *= 2,
    () => { view.client.key = 'new selection'; }, () => { view.backend = 'svg'; },
    () => { view.snapshot = 0; }, () => view.canvas.width++, () => { view.size.w++; }];
  for (const change of changes) { change(); view.requestFrame(); await tick(); }
  assert.equal(requests.length, changes.length + 1);
  assert.equal(new URLSearchParams(requests.at(-1)[0].split('?')[1]).get('snapshot'), '0');
});

test('a new read epoch replaces a pending frame even if its state key is unchanged', async t => {
  const pending = deferred(); let calls = 0;
  const { view, requests, tick } = fixture(t, () => ++calls === 1 ? pending.promise : new Blob(['replacement']));
  view.requestFrame(); await tick();
  view.client.epoch++; view.requestFrame(); await tick();
  assert.equal(requests.length, 2); assert.equal(requests[0][1].signal.aborted, true);
  const frame = view.frame;
  pending.resolve(new Blob(['obsolete'])); await drain(); assert.equal(view.frame, frame);
});

for (const gesture of ['pan', 'pinch']) test(`${gesture} previews the existing frame and renders once at its final exact camera`, async t => {
  const { view, requests, tick } = fixture(t);
  view.requestFrame(); await tick();
  if (gesture === 'pan') view.drag = { pan: true }; else view.pinch = {};
  for (let i = 0; i < 30; i++) { view.camera.x += .25; view.camera.scale *= 1.01; view.requestFrame(); await tick(); }
  assert.equal(requests.length, 1);
  view.drag = null; view.pinch = null; view.requestFrame(); await tick();
  assert.equal(requests.length, 2);
  const query = new URLSearchParams(requests[1][0].split('?')[1]);
  assert.equal(Number(query.get('cx')), view.camera.x);
  assert.equal(Number(query.get('scale')), view.camera.scale * view.dpr);
  assert.deepEqual(view.frame.camera, view.camera);
});

test('unchanged resize notifications preserve the displayed canvas and pending render', async t => {
  const { view, requests, tick } = fixture(t);
  const previousRatio = globalThis.devicePixelRatio; globalThis.devicePixelRatio = 1;
  t.after(() => { globalThis.devicePixelRatio = previousRatio; });
  view.element = { getBoundingClientRect: () => ({ width: 80, height: 60 }) };
  view.requestFrame(); await tick(); const frame = view.frame;
  for (let i = 0; i < 10; i++) view.resize();
  await tick(); assert.equal(requests.length, 1); assert.equal(view.frame, frame);
});

test('raw browser frames retain every RGBA byte and close replaced ImageBitmaps', async t => {
  const pixels = Uint8Array.from({ length: 80 * 60 * 4 }, (_, i) => i % 256).buffer;
  const { view, requests, tick } = fixture(t, async () => pixels);
  const previousData = globalThis.ImageData, previousBitmap = globalThis.createImageBitmap;
  let decoded, closed = 0;
  globalThis.shadowBrowser = { rasterFrames: true };
  globalThis.ImageData = class { constructor(data, width, height) { Object.assign(this, { data, width, height }); } };
  globalThis.createImageBitmap = async data => { decoded = data; return { close() { closed++; } }; };
  t.after(() => { globalThis.ImageData = previousData; globalThis.createImageBitmap = previousBitmap; });
  view.requestFrame(); await tick();
  assert.match(requests[0][0], /pixels=true/); assert.equal(requests[0][1].buffer, true);
  assert.equal(decoded.width, 80); assert.equal(decoded.height, 60);
  assert.deepEqual(new Uint8Array(decoded.data.buffer), new Uint8Array(pixels));
  view.camera.x++; view.requestFrame(); await tick(); assert.equal(closed, 1);
  view.clearFrame(); assert.equal(closed, 2); assert.equal(view.frame, null);
});

test('invalid raw frame dimensions retain the previous valid presentation', async t => {
  const { view, errors, tick } = fixture(t, async () => new ArrayBuffer(4));
  const previousData = globalThis.ImageData, previousBitmap = globalThis.createImageBitmap;
  globalThis.shadowBrowser = { rasterFrames: true };
  globalThis.ImageData = class {};
  globalThis.createImageBitmap = () => assert.fail('Invalid bytes must not reach a canvas.');
  t.after(() => { globalThis.ImageData = previousData; globalThis.createImageBitmap = previousBitmap; });
  const old = view.frame = { image: {}, camera: { ...view.camera }, size: { ...view.size }, renderKey: 'older' };
  view.requestFrame(); await tick(); assert.equal(view.frame, old); assert.match(errors[0].message, /invalid pixel buffer/);
});

test('a decoded obsolete bitmap is released and cannot replace the current frame', async t => {
  const decoded = deferred(), { view, tick } = fixture(t, async () => new ArrayBuffer(80 * 60 * 4));
  const previousData = globalThis.ImageData, previousBitmap = globalThis.createImageBitmap;
  globalThis.shadowBrowser = { rasterFrames: true };
  globalThis.ImageData = class {};
  globalThis.createImageBitmap = () => decoded.promise;
  t.after(() => { globalThis.ImageData = previousData; globalThis.createImageBitmap = previousBitmap; });
  view.requestFrame(); await tick(); view.camera.x++; view.requestFrame();
  let closed = 0; decoded.resolve({ close() { closed++; } }); await drain();
  assert.equal(closed, 1); assert.equal(view.frame, undefined);
});

test('back/forward cache preserves a resumable viewport while a real page exit releases it', async t => {
  const pending = deferred(), { view, requests, tick } = fixture(t, () => pending.promise);
  const previousCancel = globalThis.cancelAnimationFrame;
  let closed = 0, disconnected = 0, cancelled = 0;
  globalThis.cancelAnimationFrame = () => { cancelled++; };
  t.after(() => { globalThis.cancelAnimationFrame = previousCancel; });
  view.element = { hasPointerCapture: () => false, classList: { remove() {} } };
  view.resizeObserver = { disconnect() { disconnected++; } };
  view.abort = new AbortController(); const initialAbort = view.abort;
  const frame = view.frame = { image: { close() { closed++; } }, camera: { ...view.camera },
    size: { ...view.size }, renderKey: view.frameKey() };
  const events = new EventTarget(); bindViewportPageLifetime(view, events);

  events.dispatchEvent(Object.assign(new Event('pagehide'), { persisted: true }));
  assert.equal(view.frame, frame); assert.equal(closed, 0); assert.equal(disconnected, 0);
  assert.equal(initialAbort.signal.aborted, false); assert.equal(cancelled, 0);
  events.dispatchEvent(Object.assign(new Event('pageshow'), { persisted: true }));
  view.camera.x++; view.requestFrame(); await tick();
  assert.equal(requests.length, 1); assert.equal(requests[0][1].signal.aborted, false);

  events.dispatchEvent(Object.assign(new Event('pagehide'), { persisted: false }));
  assert.equal(view.frame, null); assert.equal(closed, 1); assert.equal(disconnected, 1);
  assert.equal(requests[0][1].signal.aborted, true); assert.equal(cancelled, 1);
  pending.resolve(new Blob(['obsolete frame'])); await tick();
  assert.equal(view.frame, null); assert.equal(requests.length, 1);
});
