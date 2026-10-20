import { point, add, sub, distance, screen, world, zoom, fit, constrain, trackPoint, format } from './core.mjs';
import { viewKey } from '../studio-state.mjs';
import { InsertionPreview } from './insertion-preview.mjs';
import { drawGraph, graphBounds, graphIdentity, hitGraph } from './graph-visualization.mjs';
import { SelectionCycling } from './selection-cycling-ui.mjs';

/** A cached page resumes its live viewport, including its frame and resize observer. */
export function bindViewportPageLifetime(viewport, target = globalThis) {
  target.addEventListener('pagehide', event => { if (!event.persisted) viewport.dispose(); });
}

/** Presentation adapter only: native geometry and selection decisions stay in the shadow services. */
export class CadViewport {
  constructor(element, canvas, client, hooks = {}) {
    this.element = element; this.canvas = canvas; this.ctx = canvas.getContext('2d'); this.client = client; this.hooks = hooks;
    this.camera = { x: 0, y: 0, scale: 3 }; this.size = { w: 1, h: 1 }; this.dpr = 1;
    this.insertionPreview = new InsertionPreview(client, {invalidate:()=>this.invalidate(),error:error=>this.hooks.error?.(error)});
    this.settings = { grid: true, snap: false, ortho: false, polar: false, osnap: true, dynamic: true, tracking: false, selectionCycling:false, polarAngle: 15, polarTolerance: 5, gridStep: 10, snapModes: 23 };
    this.backend = 'skia'; this.snapshot = null; this.command = null; this.cursor = point(); this.pixel = null; this.frame = null;
    this.generation = 0; this.pointers = new Map(); this.abort = null; this.snapAbort = null; this.snapGeneration = 0; this.inputVersion = 0; this.pickAbort = null; this.panMode = false;
    this.trackingOrigins = []; this.trackingGuides = [];
    this.selectionCycle = new SelectionCycling(element, {
      scope:()=>this.selectionScope(),
      eligible:()=>this.canCycleSelection(),
      load:async(p,signal)=>{
        const s=client.state;
        const result=await client.read('selection-candidates',{method:'POST',body:{revision:s.revision,layout:s.layout,point:p,tolerance:8/this.camera.scale,limit:32},signal});
        if(result && ['documentId','workspaceId','viewVersion','revision','changeVersion','layout'].some(k=>result[k]!==s[k]))
          throw new Error('The drawing changed during the overlap query. Repeat the pick.');
        return result;
      },
      choose:async(candidate,context)=>{
        this.temporaryCycle=false;
        if(!this.canCycleSelection() || client.key!==context.expectedKey)throw new Error('The drawing or command changed. Repeat the overlap pick.');
        if(candidate)await this.hooks.selectCandidate?.(candidate,context.point,context.extend,context.remove,context.expectedKey);
        else await this.hooks.select?.(context.point,context.extend,context.remove);
      },
      cancelled:()=>{this.temporaryCycle=false;},
      invalidate:()=>this.invalidate(),error:error=>this.hooks.error?.(error)
    });
    this.cycleModifier = e => {
      if(e.defaultPrevented || e.key!==' ' || !e.shiftKey || e.repeat || !this.element.contains(e.target) ||
          e.target?.closest?.('input,textarea,select,[contenteditable="true"],dialog[open]') || !this.canCycleSelection())return;
      e.preventDefault();e.stopImmediatePropagation();this.space=false;this.temporaryCycle=true;this.setTemporaryOrtho(false);
      this.hooks.message?.('Selection cycling: click overlapping objects, click again to cycle, then press Enter.');
    };
    globalThis.addEventListener('keydown',this.cycleModifier,true);
    this.modifierDown = e => { if (e.key === 'Shift' && !e.target?.closest?.('input,textarea,select')) this.setTemporaryOrtho(true); };
    this.modifierUp = e => { if (e.key === 'Shift') this.setTemporaryOrtho(false); };
    this.modifierBlur = () => this.setTemporaryOrtho(false);
    globalThis.addEventListener?.('keydown', this.modifierDown); globalThis.addEventListener?.('keyup', this.modifierUp); globalThis.addEventListener?.('blur', this.modifierBlur);
    this.resizeObserver = new ResizeObserver(() => this.resize()); this.resizeObserver.observe(element);
    element.addEventListener('pointerdown', e => this.down(e)); element.addEventListener('pointermove', e => this.move(e));
    element.addEventListener('pointerup', e => this.up(e)); element.addEventListener('pointercancel', e => this.cancelPointer(e));
    element.addEventListener('lostpointercapture', e => { if (this.drag?.id === e.pointerId) this.cancelPointer(e); });
    element.addEventListener('pointerleave', () => { if (!this.drag) { this.pixel = null; if(!this.selectionCycle.active)this.selectionCycle.cancel();clearTimeout(this.cycleHoverTimer);this.invalidate(); } });
    element.addEventListener('wheel', e => { if (e.target !== canvas) return; this.client.beginInteraction?.(); e.preventDefault(); this.camera = zoom(this.camera, this.size, this.local(e), Math.exp(-Math.max(-300, Math.min(300, e.deltaY)) * .0015)); this.changed(); }, { passive: false });
    element.addEventListener('dblclick', e => { if (e.button === 1) this.fit(); });
    element.addEventListener('keydown', e => {
      if(e.defaultPrevented||e.isComposing||e.ctrlKey||e.metaKey||e.altKey||!e.shiftKey||e.key!=='F10'||
        (e.target!==element&&e.target!==canvas))return;
      e.preventDefault();e.stopPropagation();
      const bounds=element.getBoundingClientRect();
      this.hooks.context?.({clientX:bounds.left+bounds.width/2,clientY:bounds.top+bounds.height/2});
    });
    element.addEventListener('contextmenu', e => {
      if (e.target !== canvas && e.target !== element) return;
      e.preventDefault();
      const bounds=element.getBoundingClientRect();
      this.hooks.context?.(e.clientX||e.clientY?e:{clientX:bounds.left+bounds.width/2,clientY:bounds.top+bounds.height/2});
    });
  }
  /** A background read must not advance its generation while a gesture owns input. */
  get inputActive() { return this.pointers.size > 0 || !!this.pickAbort && !this.pickAbort.signal.aborted || !!this.selectionCycle?.active; }
  get selectionCycleActive() { return !!this.selectionCycle?.active; }
  selectionScope() {
    if(this.cycleCommand!==this.command){this.cycleCommand=this.command;this.cycleCommandSerial=(this.cycleCommandSerial||0)+1;}
    return JSON.stringify([this.client.key,this.client.epoch,this.inputVersion,this.cycleCommandSerial,this.command?.version,
      this.snapshot,this.panMode,this.space,this.camera.x,this.camera.y,this.camera.scale,this.size.w,this.size.h]);
  }
  canCycleSelection() { return !!this.client.state && !this.client.busy && this.snapshot===null && !this.graphPick && !this.panMode && !this.space &&
    !this.element.ownerDocument?.querySelector('dialog[open]') &&
    (!this.command || this.command.selecting && !this.command.spec?.crossingSelection) && !!this.hooks.selectCandidate; }
  cancelSelectionCycle() {
    const active=!!this.temporaryCycle||!!this.selectionCycle?.active;this.temporaryCycle=false;clearTimeout(this.cycleHoverTimer);
    this.selectionCycle?.cancel(active);return active;
  }
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
  setGraph(scene) { this.graph=scene;this.graphKey=graphIdentity(this.client.state);this.invalidate(); }
  graphCurrent() { return !!this.graph && this.snapshot===null && (this.graphKey===graphIdentity(this.client.state)||this.graphKey===viewKey(this.client.state)); }
  fitGraph(scene=this.graph) { const bounds=graphBounds(scene);if(bounds){this.camera=fit(bounds,this.size);this.changed();} }
  beginGraphPick({kind='any',onPick,onCancel}={}) {
    if(!this.graphCurrent())throw new Error('Load current topology before picking graph items.');
    if(this.command)throw new Error('Finish the drawing command before picking graph items.');
    this.cancelInput();this.graphPick={kind,onPick,onCancel,key:graphIdentity(this.client.state)};
    this.element.focus({preventScroll:true});this.invalidate();
  }
  cancelGraphPick() { const pick=this.graphPick;if(!pick)return false;this.graphPick=null;pick.onCancel?.();this.invalidate();return true; }
  setData() {
    const s = this.client.state, identity = s ? `${this.client.id}:${s.serverId}:${s.documentId}:${s.workspaceId}` : '';
    if (identity !== this.identity) { this.clearFrame(); this.graph = null; this.snapHit = null; this.identity = identity; }
    if(this.graphPick&&this.graphPick.key!==graphIdentity(s))this.cancelGraphPick();
    this.selectionCycle?.model.current();
    this.changed();
  }
  changed() { this.selectionCycle?.model.current();this.invalidate(); this.hooks.camera?.(this.camera); this.requestFrame(); }
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
  setTemporaryOrtho(enabled) {
    if (!!this.settings.temporaryOrtho === enabled) return;
    this.settings.temporaryOrtho = enabled; this.snapGeneration++; this.snapAbort?.abort(); this.snapHit = null;
    this.resolved = constrain(this.cursor, this.command?.anchor || this.drag?.grip?.position, this.settings); this.invalidate();
  }
  async snapped(p, anchor, force = false, excludeSelection = !!this.drag?.grip) {
    const modes = this.snapOverride ?? this.settings.snapModes;
    let constrained = constrain(p, anchor, this.settings);
    this.trackingGuides = [];
    if (this.settings.tracking && !this.settings.ortho && !this.settings.temporaryOrtho) {
      const tracked = trackPoint(constrained, this.trackingOrigins, 8 / this.camera.scale);
      constrained = tracked.position; this.trackingGuides = tracked.origins;
    }
    if (!(this.snapOverride !== undefined ? modes > 0 : this.settings.osnap) || !this.client.state || this.client.busy || this.snapshot !== null) { this.snapHit = null; return constrained; }
    // A final acquisition owns its request. Hover must never abort it and turn
    // an exact snapped pick into an unsnapped fallback.
    const abort = new AbortController();
    if (force) { this.pickAbort?.abort(); this.pickAbort = abort; }
    else { this.snapAbort?.abort(); this.snapAbort = abort; }
    const generation = force ? this.inputVersion : ++this.snapGeneration;
    const key = this.client.key, epoch = this.client.epoch;
    try {
      const hit = await this.client.read('snap', { signal: abort.signal,
        body: { point: p, tolerance: 12 / this.camera.scale, modes, anchor, excludeSelection } });
      const current = !abort.signal.aborted && key === this.client.key && epoch === this.client.epoch &&
        !this.client.busy && generation === (force ? this.inputVersion : this.snapGeneration);
      if (!current) {
        if (force) throw new Error('The drawing changed while acquiring a point. Repeat the pick.');
        return constrained;
      }
      this.snapHit = hit;
      if (hit && this.settings.tracking) this.trackingOrigins = [...(this.trackingOrigins || []).filter(p => distance(p, hit.position) > 1e-9), hit.position].slice(-4);
      this.invalidate(); return hit?.position || constrained;
    } catch (error) { if (force) throw error; return constrained; }
    finally { if (force && this.pickAbort === abort) this.pickAbort = null; }
  }
  cancelInput() {
    this.cancelGraphPick();
    this.cancelSelectionCycle();
    const cameraActive = !!this.drag?.pan || !!this.pinch;
    this.inputVersion++; this.snapGeneration++;
    clearTimeout(this.snapTimer); this.snapAbort?.abort(); this.pickAbort?.abort();
    this.snapHit = null; this.drag = null; this.pinch = null; this.snapOverride = undefined; this.trackingOrigins = []; this.trackingGuides = [];
    for (const id of this.pointers.keys()) if (this.element.hasPointerCapture(id)) this.element.releasePointerCapture(id);
    this.pointers.clear(); this.element.classList.remove('panning'); this.invalidate();
    if (cameraActive) this.requestFrame();
  }

  down(e) {
    if (e.target !== this.canvas || e.button === 2) return;
    clearTimeout(this.cycleHoverTimer);
    if(e.button===0 && this.selectionCycle?.active && this.selectionCycle.model.state?.temporary && this.canCycleSelection()){
      e.preventDefault();this.element.focus({preventScroll:true});this.selectionCycle.model.advance();return;
    }
    this.selectionCycle?.cancel();
    this.client.beginInteraction?.();
    e.preventDefault(); this.element.focus({ preventScroll: true }); const pixel = this.local(e);
    this.inputVersion++; this.pickAbort?.abort();
    this.pointers.set(e.pointerId, pixel); this.element.setPointerCapture(e.pointerId);
    if (this.pointers.size === 2) {
      const [a, b] = [...this.pointers.values()]; this.pinch = { distance: distance(a, b), midpoint: point((a.x + b.x) / 2, (a.y + b.y) / 2) }; this.drag = null; return;
    }
    const pan = e.button === 1 || this.space || this.panMode, p = world(pixel, this.camera, this.size);
    let grip;
    if (!pan && !this.command && !this.graphPick && this.snapshot === null) grip = this.client.data?.grips.find(g => distance(screen(g.position, this.camera, this.size), pixel) < 8);
    this.drag = { id: e.pointerId, inputVersion: this.inputVersion, key: this.client.key, epoch: this.client.epoch,
      command: this.command, commandVersion: this.command?.version, graphPick:this.graphPick, snapshot: this.snapshot, pixel, last: pixel, first: p, current: p, pan, grip, moved: false, extend: e.ctrlKey || e.metaKey || this.settings.pickAdd !== false, remove: e.shiftKey && !this.temporaryCycle };
    this.element.classList.toggle('panning', !!pan); this.invalidate();
  }
  move(e) {
    if(e.target!==this.canvas&&!this.pointers.has(e.pointerId))return;
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
    this.resolved = this.command?.step.kind==='window'?this.cursor:constrain(this.cursor, anchor, this.settings); this.hooks.move?.(this.resolved, pixel);
    clearTimeout(this.snapTimer);
    if ((this.command && !this.command.selecting && this.command.step.kind!=='window') || drag?.grip) this.snapTimer = setTimeout(async () => {
      const version = this.inputVersion, generation = this.snapGeneration + 1, command = this.command, commandVersion = command?.version;
      const position = await this.snapped(this.cursor, anchor);
      if (version !== this.inputVersion || generation !== this.snapGeneration || command !== this.command || commandVersion !== this.command?.version) return;
      this.resolved = position; this.hooks.move?.(position, this.pixel); this.invalidate();
    }, 70);
    else this.snapHit = null;
    clearTimeout(this.cycleHoverTimer);
    if(!drag && !this.selectionCycle?.active) {
      this.selectionCycle?.cancel();
      if(this.settings.selectionCycling && this.canCycleSelection())
        this.cycleHoverTimer=setTimeout(()=>{if(this.canCycleSelection())void this.selectionCycle?.hover(this.cursor,pixel);},140);
    }
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
      if(drag.graphPick) {
        if(drag.graphPick!==this.graphPick||drag.graphPick.key!==graphIdentity(this.client.state)||!this.graphCurrent())throw new Error('The graph changed during the pick. Load topology again.');
        if(!drag.moved){const hit=hitGraph(this.graph,this.local(e),p=>screen(p,this.camera,this.size),{kind:drag.graphPick.kind});if(hit){const pick=this.graphPick;this.graphPick=null;await pick.onPick?.(hit);}else this.hooks.error?.(new Error('No visible graph item at this location. Pick closer or zoom in.'));}
      } else if (drag.grip && drag.moved) {
        const pos = await this.snapped(p, drag.grip.position, true, true); current(); await this.hooks.grip?.(drag.grip, pos);
      } else if(this.command?.step.kind==='window') {
        if(drag.moved && this.command.index===0)await this.hooks.window?.(drag.first,p);
        else await this.hooks.pick?.(p);
      } else if (this.command && !this.command.selecting) {
        const pos = await this.snapped(p, this.command.anchor, true, ['MOVE','STRETCH'].includes(this.command.name) && !!this.command.anchor); current(); this.snapOverride = undefined; await this.hooks.pick?.(pos);
      } else if (drag.moved) await this.hooks.box?.(drag.first, p, e.clientX < this.element.getBoundingClientRect().left + drag.pixel.x, drag.extend || !!this.command?.selecting, drag.remove);
      else if(!drag.grip && (this.settings.selectionCycling || this.temporaryCycle) && this.canCycleSelection())
        await this.selectionCycle.open(p,this.local(e),{extend:drag.extend||!!this.command?.selecting,remove:drag.remove,temporary:!!this.temporaryCycle,expectedKey:drag.key});
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
    if (this.graphCurrent()) drawGraph(c,this.graph,p=>screen(p,this.camera,this.size),this.size);
    const candidate=this.selectionCycle?.preview;
    if(candidate){
      c.save();c.strokeStyle='#ffe58c';c.lineWidth=3;
      for(const path of candidate.preview)this.path(c,path.points,path.closed);
      if(candidate.previewTruncated || !candidate.preview.length){
        const b=candidate.bounds,a=screen(point(b.minX,b.minY),this.camera,this.size),z=screen(point(b.maxX,b.maxY),this.camera,this.size);
        c.setLineDash([4,3]);c.strokeRect(a.x-2,z.y-2,Math.max(4,z.x-a.x+4),Math.max(4,a.y-z.y+4));
      }
      c.restore();
    }
    if (this.snapshot === null) {
      for (const grip of this.client.data?.grips || []) {
        const p = screen(grip.position, this.camera, this.size); c.fillStyle = '#429ced'; c.strokeStyle = '#bddefc'; c.lineWidth = 1;
        c.fillRect(p.x - 3, p.y - 3, 6, 6); c.strokeRect(p.x - 3, p.y - 3, 6, 6);
      }
      this.preview(c);
    }
    if (this.drag && !this.drag.pan && !this.drag.grip && !this.graphPick && (!this.command || this.command.selecting || this.command.step.kind==='window') && this.drag.moved) {
      const a = screen(this.drag.first, this.camera, this.size), b = this.drag.last, crossing = this.command?.spec.crossingSelection || b.x < a.x;
      c.fillStyle = crossing ? '#5cbd7840' : '#4b95dc40'; c.strokeStyle = crossing ? '#90e6a5' : '#8cbbf4';
      c.setLineDash(crossing ? [5, 3] : []); c.lineWidth = 1; c.fillRect(a.x, a.y, b.x - a.x, b.y - a.y); c.strokeRect(a.x, a.y, b.x - a.x, b.y - a.y); c.setLineDash([]);
    }
    if (this.pixel && this.resolved && this.trackingGuides?.length) {
      c.strokeStyle = '#a8ef68'; c.lineWidth = 1; c.setLineDash([3, 4]);
      for (const origin of this.trackingGuides) this.path(c, [origin, this.resolved]);
      c.setLineDash([]);
    }
    if (this.snapHit && this.pixel) {
      const p = screen(this.snapHit.position, this.camera, this.size); c.strokeStyle = '#a8ef68'; c.lineWidth = 1.5; c.strokeRect(p.x - 5, p.y - 5, 10, 10);
      c.fillStyle = '#a8ef68'; c.font = '11px Segoe UI'; c.fillText(`${this.snapHit.kind}${this.snapHit.approximate ? ' ≈' : ''}`, p.x + 12, p.y - 9);
    }
    if (this.pixel && !this.drag?.pan) {
      const p = this.pixel; c.strokeStyle = '#dce5ef'; c.lineWidth = 1; c.beginPath(); c.moveTo(0, p.y + .5); c.lineTo(p.x - 5, p.y + .5); c.moveTo(p.x + 5, p.y + .5); c.lineTo(w, p.y + .5);
      c.moveTo(p.x + .5, 0); c.lineTo(p.x + .5, p.y - 5); c.moveTo(p.x + .5, p.y + 5); c.lineTo(p.x + .5, h); c.stroke(); c.strokeRect(p.x - 4.5, p.y - 4.5, 9, 9);
      if(this.settings.selectionCycling && this.selectionCycle?.overlap){
        c.strokeStyle='#ffe58c';c.strokeRect(p.x+12,p.y+12,10,10);c.strokeRect(p.x+16,p.y+16,10,10);
      }
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
    const session = this.command, all = session?.previewPoints || session?.command.points || [], p = session?.spec.crossingSelection?all.slice(2):all, cursor = this.resolved || this.cursor;
    this.insertionPreview.sync(session);
    if(session?.name==='INSERT'){this.insertionPreview.draw(c,this.camera,this.size,cursor,this.pixel);return;}
    if(session?.spec.crossingSelection&&all.length) {
      const a=screen(all[0],this.camera,this.size),b=screen(all[1]||cursor,this.camera,this.size);
      c.fillStyle='#5cbd7820';c.strokeStyle='#90e6a5';c.lineWidth=1;c.setLineDash([5,3]);
      c.fillRect(a.x,a.y,b.x-a.x,b.y-a.y);c.strokeRect(a.x,a.y,b.x-a.x,b.y-a.y);c.setLineDash([]);
    }
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
  dispose() {
    globalThis.removeEventListener?.('keydown',this.cycleModifier,true);this.selectionCycle?.dispose();clearTimeout(this.cycleHoverTimer);
    globalThis.removeEventListener?.('keydown', this.modifierDown); globalThis.removeEventListener?.('keyup', this.modifierUp); globalThis.removeEventListener?.('blur', this.modifierBlur); this.insertionPreview.clear(); this.cancelInput(); this.generation++; this.pendingFrame = null; this.clearFrame(); this.resizeObserver.disconnect(); this.abort?.abort(); this.snapAbort?.abort(); clearTimeout(this.renderTimer); clearTimeout(this.snapTimer); cancelAnimationFrame(this.raf); }
}
