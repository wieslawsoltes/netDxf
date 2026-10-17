import { point, add, sub, distance, screen, world, zoom, fit, constrain, format } from './core.mjs';
import { viewKey } from '../studio-state.mjs';

/** A cached page resumes its live viewport, including its frame and resize observer. */
export function bindViewportPageLifetime(viewport, target = globalThis) {
  target.addEventListener('pagehide', event => { if (!event.persisted) viewport.dispose(); });
}

/** Presentation adapter only: native geometry and selection decisions stay in the shadow services. */
export class CadViewport {
  constructor(element, canvas, client, hooks = {}) {
    this.element = element; this.canvas = canvas; this.ctx = canvas.getContext('2d'); this.client = client; this.hooks = hooks;
    this.camera = { x: 0, y: 0, scale: 3 }; this.size = { w: 1, h: 1 }; this.dpr = 1;
    this.settings = { grid: true, snap: false, ortho: false, polar: false, osnap: true, dynamic: true, gridStep: 10, snapModes: 23 };
    this.backend = 'skia'; this.snapshot = null; this.command = null; this.cursor = point(); this.pixel = null; this.frame = null;
    this.generation = 0; this.pointers = new Map(); this.abort = null; this.snapAbort = null; this.snapGeneration = 0; this.inputVersion = 0; this.pickAbort = null; this.panMode = false;
    this.resizeObserver = new ResizeObserver(() => this.resize()); this.resizeObserver.observe(element);
    element.addEventListener('pointerdown', e => this.down(e)); element.addEventListener('pointermove', e => this.move(e));
    element.addEventListener('pointerup', e => this.up(e)); element.addEventListener('pointercancel', e => this.cancelPointer(e));
    element.addEventListener('lostpointercapture', e => { if (this.drag?.id === e.pointerId) this.cancelPointer(e); });
    element.addEventListener('pointerleave', () => { if (!this.drag) { this.pixel = null; this.invalidate(); } });
    element.addEventListener('wheel', e => { if (e.target !== canvas) return; this.client.beginInteraction?.(); e.preventDefault(); this.camera = zoom(this.camera, this.size, this.local(e), Math.exp(-Math.max(-300, Math.min(300, e.deltaY)) * .0015)); this.changed(); }, { passive: false });
    element.addEventListener('dblclick', e => { if (e.button === 1) this.fit(); });
    element.addEventListener('contextmenu', e => { if (e.target === canvas) { e.preventDefault(); this.hooks.context?.(e); } });
  }
  /** A background read must not advance its generation while a gesture owns input. */
  get inputActive() { return this.pointers.size > 0 || !!this.pickAbort && !this.pickAbort.signal.aborted; }
  local(e) { const r = this.element.getBoundingClientRect(); return point(e.clientX - r.left, e.clientY - r.top); }
  resize() {
    const r = this.element.getBoundingClientRect(), size = { w: Math.max(1, r.width), h: Math.max(1, r.height) };
    const dpr = Math.max(.1, Math.min(devicePixelRatio || 1, 2, 4096 / size.w, 4096 / size.h));
    if (size.w === this.size.w && size.h === this.size.h && dpr === this.dpr &&
        this.canvas.width === Math.max(1, Math.floor(size.w * dpr)) && this.canvas.height === Math.max(1, Math.floor(size.h * dpr))) return;
    this.size = size; this.dpr = dpr;
    this.canvas.width = Math.max(1, Math.floor(this.size.w * this.dpr)); this.canvas.height = Math.max(1, Math.floor(this.size.h * this.dpr)); this.changed();
  }
  fit() { this.camera = fit(this.client.state?.bounds, this.size); this.changed(); }
  setData() {
    const s = this.client.state, identity = s ? `${this.client.id}:${s.serverId}:${s.documentId}:${s.workspaceId}` : '';
    if (identity !== this.identity) { this.clearFrame(); this.graph = null; this.snapHit = null; this.identity = identity; }
    this.changed();
  }
  changed() { this.invalidate(); this.hooks.camera?.(this.camera); this.requestFrame(); }
  invalidate() { if (this.raf) return; this.raf = requestAnimationFrame(() => { this.raf = 0; this.draw(); }); }
  frameKey() {
    return JSON.stringify([this.client.key, this.backend, this.snapshot, this.camera.x, this.camera.y,
      this.camera.scale, this.size.w, this.size.h, this.canvas.width, this.canvas.height, this.dpr]);
  }
  clearFrame() { this.frame?.image.close?.(); this.frame = null; }
  requestFrame(force = false) {
    const key = this.client.state ? this.frameKey() : null;
    // Coalesce identical work without aborting a slow frame. A changed mutation epoch
    // still replaces an in-flight read; a completed frame has no read epoch to expire.
    if (!force && key !== null && this.pendingFrame?.key === key && this.pendingFrame.epoch === this.client.epoch) return;
    clearTimeout(this.renderTimer); this.abort?.abort(); const generation = ++this.generation;
    this.pendingFrame = null;
    if (key === null || !force && this.frame?.renderKey === key) return;
    // Camera motion previews the existing full-quality frame. Rebuild once the captured
    // pan/pinch ends instead of blocking pointer delivery with obsolete native renders.
    if (!force && this.frame && (this.drag?.pan || this.pinch)) return;
    this.pendingFrame = { key, epoch: this.client.epoch };
    this.renderTimer = setTimeout(() => { this.renderTimer = 0; void this.render(generation); }, 45);
  }
  async render(generation) {
    if (generation !== this.generation) return;
    if (!this.client.state || this.client.busy || this.size.w < 2 || this.size.h < 2) { this.pendingFrame = null; return; }
    const stateKey = this.client.key, camera = { ...this.camera }, size = { ...this.size }, dpr = this.dpr;
    const renderKey = this.frameKey(), width = this.canvas.width, height = this.canvas.height;
    const pixels = this.backend === 'skia' && globalThis.shadowBrowser?.rasterFrames === true &&
      typeof createImageBitmap === 'function' && typeof ImageData === 'function';
    const query = new URLSearchParams({ width: this.canvas.width, height: this.canvas.height, cx: camera.x, cy: camera.y,
      scale: camera.scale * dpr, backend: this.backend, revision: this.client.state.revision });
    if (this.snapshot !== null) query.set('snapshot', this.snapshot);
    if (pixels) query.set('pixels', 'true');
    const abort = this.abort = new AbortController();
    try {
      const payload = await this.client.read(`render?${query}`, { blob: !pixels, buffer: pixels, signal: abort.signal });
      if (!payload || generation !== this.generation) return;
      let image;
      if (pixels) {
        if (payload.byteLength !== width * height * 4) throw new Error('The raster frame has an invalid pixel buffer.');
        image = await createImageBitmap(new ImageData(new Uint8ClampedArray(payload), width, height), { colorSpaceConversion: 'none' });
      } else {
        const url = URL.createObjectURL(payload); image = new Image();
        try { image.src = url; await image.decode(); } finally { URL.revokeObjectURL(url); }
      }
      if (generation !== this.generation || stateKey !== this.client.key) { image.close?.(); return; }
      this.clearFrame(); this.frame = { image, camera, size, key: stateKey, renderKey }; this.invalidate(); this.hooks.frame?.();
    } catch (error) { if (generation === this.generation && error.name !== 'AbortError') this.hooks.error?.(error); }
    finally { if (generation === this.generation) this.pendingFrame = null; }
  }
  async snapped(p, anchor, force = false, excludeSelection = !!this.drag?.grip) {
    const constrained = constrain(p, anchor, this.settings);
    if (!this.settings.osnap || !this.client.state || this.client.busy || this.snapshot !== null) { this.snapHit = null; return constrained; }
    // A final acquisition owns its request. Hover must never abort it and turn
    // an exact snapped pick into an unsnapped fallback.
    const abort = new AbortController();
    if (force) { this.pickAbort?.abort(); this.pickAbort = abort; }
    else { this.snapAbort?.abort(); this.snapAbort = abort; }
    const generation = force ? this.inputVersion : ++this.snapGeneration;
    const key = this.client.key, epoch = this.client.epoch;
    try {
      const hit = await this.client.read('snap', { signal: abort.signal,
        body: { point: constrained, tolerance: 12 / this.camera.scale, modes: this.settings.snapModes, anchor, excludeSelection } });
      const current = !abort.signal.aborted && key === this.client.key && epoch === this.client.epoch &&
        !this.client.busy && generation === (force ? this.inputVersion : this.snapGeneration);
      if (!current) {
        if (force) throw new Error('The drawing changed while acquiring a point. Repeat the pick.');
        return constrained;
      }
      this.snapHit = hit; this.invalidate(); return hit?.position || constrained;
    } catch (error) { if (force) throw error; return constrained; }
    finally { if (force && this.pickAbort === abort) this.pickAbort = null; }
  }
  cancelInput() {
    const cameraActive = !!this.drag?.pan || !!this.pinch;
    this.inputVersion++; this.snapGeneration++;
    clearTimeout(this.snapTimer); this.snapAbort?.abort(); this.pickAbort?.abort();
    this.snapHit = null; this.drag = null; this.pinch = null;
    for (const id of this.pointers.keys()) if (this.element.hasPointerCapture(id)) this.element.releasePointerCapture(id);
    this.pointers.clear(); this.element.classList.remove('panning'); this.invalidate();
    if (cameraActive) this.requestFrame();
  }

  down(e) {
    if (e.target !== this.canvas || e.button === 2) return;
    this.client.beginInteraction?.();
    e.preventDefault(); this.element.focus({ preventScroll: true }); const pixel = this.local(e);
    this.inputVersion++; this.pickAbort?.abort();
    this.pointers.set(e.pointerId, pixel); this.element.setPointerCapture(e.pointerId);
    if (this.pointers.size === 2) {
      const [a, b] = [...this.pointers.values()]; this.pinch = { distance: distance(a, b), midpoint: point((a.x + b.x) / 2, (a.y + b.y) / 2) }; this.drag = null; return;
    }
    const pan = e.button === 1 || this.space || this.panMode, p = world(pixel, this.camera, this.size);
    let grip;
    if (!pan && !this.command && this.snapshot === null) grip = this.client.data?.grips.find(g => distance(screen(g.position, this.camera, this.size), pixel) < 8);
    this.drag = { id: e.pointerId, inputVersion: this.inputVersion, key: this.client.key, epoch: this.client.epoch,
      command: this.command, commandVersion: this.command?.version, snapshot: this.snapshot, pixel, last: pixel, first: p, current: p, pan, grip, moved: false, extend: e.ctrlKey || e.metaKey, remove: e.shiftKey };
    this.element.classList.toggle('panning', !!pan); this.invalidate();
  }
  move(e) {
    const pixel = this.local(e); this.pixel = pixel;
    if (this.pointers.has(e.pointerId)) this.pointers.set(e.pointerId, pixel);
    if (this.pinch && this.pointers.size >= 2) {
      const [a, b] = [...this.pointers.values()], length = distance(a, b), mid = point((a.x + b.x) / 2, (a.y + b.y) / 2);
      this.camera = zoom(this.camera, this.size, this.pinch.midpoint, length / Math.max(1, this.pinch.distance));
      this.camera.x -= (mid.x - this.pinch.midpoint.x) / this.camera.scale; this.camera.y += (mid.y - this.pinch.midpoint.y) / this.camera.scale;
      this.pinch = { distance: length, midpoint: mid }; this.changed(); return;
    }
    this.cursor = world(pixel, this.camera, this.size);
    const drag = this.drag;
    if (drag?.id === e.pointerId) {
      drag.moved ||= distance(pixel, drag.pixel) > 4;
      if (drag.pan) { this.camera.x -= (pixel.x - drag.last.x) / this.camera.scale; this.camera.y += (pixel.y - drag.last.y) / this.camera.scale; this.changed(); }
      drag.last = pixel; drag.current = this.cursor;
    }
    const anchor = this.command?.anchor || this.drag?.grip?.position;
    this.resolved = constrain(this.cursor, anchor, this.settings); this.hooks.move?.(this.resolved, pixel);
    clearTimeout(this.snapTimer);
    if ((this.command && !this.command.selecting) || drag?.grip) this.snapTimer = setTimeout(async () => {
      const version = this.inputVersion, generation = this.snapGeneration + 1, command = this.command, commandVersion = command?.version;
      const position = await this.snapped(this.cursor, anchor);
      if (version !== this.inputVersion || generation !== this.snapGeneration || command !== this.command || commandVersion !== this.command?.version) return;
      this.resolved = position; this.hooks.move?.(position, this.pixel); this.invalidate();
    }, 70);
    else this.snapHit = null;
    this.invalidate();
  }
  async up(e) {
    this.pointers.delete(e.pointerId);
    if (this.pinch) { if (this.pointers.size < 2) { this.pinch = null; this.changed(); } this.drag = null; return; }
    const drag = this.drag; if (!drag || drag.id !== e.pointerId) return;
    this.drag = null; this.element.classList.remove('panning');
    if (this.element.hasPointerCapture(e.pointerId)) this.element.releasePointerCapture(e.pointerId);
    if (drag.pan) { this.changed(); return; }
    if (this.client.busy || this.snapshot !== null) { this.invalidate(); return; }
    try {
      clearTimeout(this.snapTimer);
      const current = () => {
        if (drag.inputVersion !== this.inputVersion || drag.key !== this.client.key || drag.epoch !== this.client.epoch ||
            drag.command !== this.command || drag.commandVersion !== this.command?.version || drag.snapshot !== this.snapshot || this.client.busy)
          throw new Error('The drawing or command changed during the gesture. Repeat the pick.');
      };
      current();
      const p = world(this.local(e), this.camera, this.size);
      if (drag.grip && drag.moved) {
        const pos = await this.snapped(p, drag.grip.position, true, true); current(); await this.hooks.grip?.(drag.grip, pos);
      } else if (this.command && !this.command.selecting) {
        const pos = await this.snapped(p, this.command.anchor, true); current(); await this.hooks.pick?.(pos);
      } else if (drag.moved) await this.hooks.box?.(drag.first, p, e.clientX < this.element.getBoundingClientRect().left + drag.pixel.x, drag.extend, drag.remove);
      else await this.hooks.select?.(p, drag.extend || !!this.command?.selecting, drag.remove);
    } catch (error) { this.hooks.error?.(error); }
    this.invalidate();
  }
  cancelPointer(e) { this.pointers.delete(e.pointerId); this.cancelInput(); }
  draw() {
    const c = this.ctx, { w, h } = this.size; c.setTransform(this.dpr, 0, 0, this.dpr, 0, 0); c.clearRect(0, 0, w, h);
    c.fillStyle = '#212830'; c.fillRect(0, 0, w, h);
    if (this.settings.grid) this.grid(c);
    if (this.frame) {
      const f = this.frame, scale = this.camera.scale / f.camera.scale;
      const center = screen(point(f.camera.x, f.camera.y), this.camera, this.size);
      c.drawImage(f.image, center.x - f.size.w * scale / 2, center.y - f.size.h * scale / 2, f.size.w * scale, f.size.h * scale);
    }
    if (this.graph && this.graphKey === viewKey(this.client.state)) {
      c.strokeStyle = '#e2b65b'; c.lineWidth = 1; c.globalAlpha = .7;
      for (const edge of this.graph.edges) { const a = this.graph.nodes[edge.a], b = this.graph.nodes[edge.b]; if (a && b) this.path(c, [a, b]); }
      c.globalAlpha = 1;
    }
    if (this.snapshot === null) {
      for (const grip of this.client.data?.grips || []) {
        const p = screen(grip.position, this.camera, this.size); c.fillStyle = '#429ced'; c.strokeStyle = '#bddefc'; c.lineWidth = 1;
        c.fillRect(p.x - 3, p.y - 3, 6, 6); c.strokeRect(p.x - 3, p.y - 3, 6, 6);
      }
      this.preview(c);
    }
    if (this.drag && !this.drag.pan && !this.drag.grip && (!this.command || this.command.selecting) && this.drag.moved) {
      const a = screen(this.drag.first, this.camera, this.size), b = this.drag.last, crossing = b.x < a.x;
      c.fillStyle = crossing ? '#5cbd7840' : '#4b95dc40'; c.strokeStyle = crossing ? '#90e6a5' : '#8cbbf4';
      c.setLineDash(crossing ? [5, 3] : []); c.lineWidth = 1; c.fillRect(a.x, a.y, b.x - a.x, b.y - a.y); c.strokeRect(a.x, a.y, b.x - a.x, b.y - a.y); c.setLineDash([]);
    }
    if (this.snapHit && this.pixel) {
      const p = screen(this.snapHit.position, this.camera, this.size); c.strokeStyle = '#a8ef68'; c.lineWidth = 1.5; c.strokeRect(p.x - 5, p.y - 5, 10, 10);
      c.fillStyle = '#a8ef68'; c.font = '11px Segoe UI'; c.fillText(`${this.snapHit.kind}${this.snapHit.approximate ? ' ≈' : ''}`, p.x + 12, p.y - 9);
    }
    if (this.pixel && !this.drag?.pan) {
      const p = this.pixel; c.strokeStyle = '#dce5ef'; c.lineWidth = 1; c.beginPath(); c.moveTo(0, p.y + .5); c.lineTo(p.x - 5, p.y + .5); c.moveTo(p.x + 5, p.y + .5); c.lineTo(w, p.y + .5);
      c.moveTo(p.x + .5, 0); c.lineTo(p.x + .5, p.y - 5); c.moveTo(p.x + .5, p.y + 5); c.lineTo(p.x + .5, h); c.stroke(); c.strokeRect(p.x - 4.5, p.y - 4.5, 9, 9);
    }
  }
  grid(c) {
    const { w, h } = this.size; let spacing = this.settings.gridStep * this.camera.scale;
    while (spacing < 12) spacing *= 5; while (spacing > 120) spacing /= 5;
    const origin = screen(point(), this.camera, this.size);
    const lines = (step, color) => {
      c.strokeStyle = color; c.lineWidth = 1; c.beginPath();
      for (let x = ((origin.x % step) + step) % step; x < w; x += step) { c.moveTo(Math.floor(x) + .5, 0); c.lineTo(Math.floor(x) + .5, h); }
      for (let y = ((origin.y % step) + step) % step; y < h; y += step) { c.moveTo(0, Math.floor(y) + .5); c.lineTo(w, Math.floor(y) + .5); } c.stroke();
    };
    lines(spacing, '#29313b'); lines(spacing * 5, '#333e49');
    c.strokeStyle = '#72404b'; c.beginPath(); c.moveTo(0, origin.y + .5); c.lineTo(w, origin.y + .5); c.stroke();
    c.strokeStyle = '#386356'; c.beginPath(); c.moveTo(origin.x + .5, 0); c.lineTo(origin.x + .5, h); c.stroke();
  }
  path(c, points, close = false) {
    c.beginPath(); points.forEach((p, i) => { const q = screen(p, this.camera, this.size); if (!i) c.moveTo(q.x, q.y); else c.lineTo(q.x, q.y); }); if (close) c.closePath(); c.stroke();
  }
  preview(c) {
    const session = this.command, p = session?.command.points || [], cursor = this.resolved || this.cursor;
    c.strokeStyle = '#e4ecf3'; c.lineWidth = 1; c.setLineDash([6, 3]);
    if (this.drag?.grip) this.path(c, [this.drag.grip.position, cursor]);
    if (!session || session.selecting || !p.length || !this.pixel) { c.setLineDash([]); return; }
    const name = session.name;
    if (name === 'CIRCLE' || name === 'POLYGON') {
      const center = screen(p[0], this.camera, this.size), r = distance(p[0], cursor) * this.camera.scale;
      if (name === 'CIRCLE') { c.beginPath(); c.arc(center.x, center.y, r, 0, Math.PI * 2); c.stroke(); }
      else { const angle = Math.atan2(cursor.y - p[0].y, cursor.x - p[0].x), count = session.command.count || 6;
        this.path(c, Array.from({ length: count }, (_, i) => add(p[0], point(Math.cos(angle + i * Math.PI * 2 / count) * r / this.camera.scale, Math.sin(angle + i * Math.PI * 2 / count) * r / this.camera.scale))), true); }
      this.path(c, [p[0], cursor]);
    } else if (name === 'RECTANG') this.path(c, [p[0], point(cursor.x, p[0].y), cursor, point(p[0].x, cursor.y)], true);
    else if (name === 'ARC' && p.length === 2) {
      const [a, b, d] = [...p, cursor], determinant = 2 * (a.x * (b.y - d.y) + b.x * (d.y - a.y) + d.x * (a.y - b.y));
      if (Math.abs(determinant) > 1e-10) {
        const aa = a.x*a.x+a.y*a.y, bb = b.x*b.x+b.y*b.y, dd = d.x*d.x+d.y*d.y;
        const center = point((aa*(b.y-d.y)+bb*(d.y-a.y)+dd*(a.y-b.y))/determinant,(aa*(d.x-b.x)+bb*(a.x-d.x)+dd*(b.x-a.x))/determinant);
        const q = screen(center, this.camera, this.size); c.beginPath(); c.arc(q.x,q.y,distance(center,a)*this.camera.scale,-Math.atan2(a.y-center.y,a.x-center.x),-Math.atan2(d.y-center.y,d.x-center.x),determinant>0); c.stroke();
      } else this.path(c, [...p, cursor]);
    } else if (name === 'ELLIPSE' && p.length === 2) {
      const major = distance(p[0],p[1]), d = sub(p[1],p[0]), minor = Math.abs(d.x*(cursor.y-p[0].y)-d.y*(cursor.x-p[0].x))/major, center = screen(p[0],this.camera,this.size);
      c.beginPath(); c.ellipse(center.x,center.y,major*this.camera.scale,minor*this.camera.scale,-Math.atan2(d.y,d.x),0,Math.PI*2); c.stroke();
    } else this.path(c, [...p, cursor], name === 'AREA');
    c.setLineDash([]);
    const q = screen(cursor, this.camera, this.size); c.fillStyle = '#a8c4dd'; c.font = '11px Consolas'; c.fillText(format(distance(p.at(-1), cursor)), q.x + 12, q.y + 19);
  }
  dispose() { this.cancelInput(); this.generation++; this.pendingFrame = null; this.clearFrame(); this.resizeObserver.disconnect(); this.abort?.abort(); this.snapAbort?.abort(); clearTimeout(this.renderTimer); clearTimeout(this.snapTimer); cancelAnimationFrame(this.raf); }
}
