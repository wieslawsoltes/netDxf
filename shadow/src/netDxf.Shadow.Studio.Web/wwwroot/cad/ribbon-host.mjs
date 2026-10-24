import { ribbonHostPreferences, ribbonHostRect, ribbonDockTarget, ribbonLock } from './ribbon-host-model.mjs';
import { raiseWorkspaceWindow, releaseWorkspaceWindow } from './workspace-foreground.mjs';

const node = (tag, className, text = '') => { const result = document.createElement(tag); result.className = className; result.textContent = text; return result; };
const button = (label, text, callback) => { const result = node('button', '', text); result.type = 'button'; result.title = label; result.setAttribute('aria-label', label); result.addEventListener('click', callback); return result; };

/** Reparents the live ribbon; document, viewport, palette and command-editor nodes remain intact. */
export class RibbonHost {
  constructor({ panel, customization, changed = () => {}, report = () => {} }) {
    this.panel = panel; this.customization = customization; this.changed = changed; this.report = report; this.lockMask = 0; this.gesture = null;
    this.studio = panel.closest('#studio'); this.bar = this.studio?.querySelector('.ribbon-bar');
    const documents = this.studio?.querySelector('.document-bar'), workspace = this.studio?.querySelector('#workspace'), status = this.studio?.querySelector('.statusbar');
    if (!this.bar || !documents || !workspace || !status) return;
    this.element = node('section', 'ribbon-host'); this.element.setAttribute('aria-label', 'Ribbon workspace');
    this.activate=()=>{if(this.effectivePlacement==='float')raiseWorkspaceWindow(this.element);};
    this.element.addEventListener('pointerdown',this.activate);this.element.addEventListener('focusin',this.activate);
    this.stage = node('div', 'ribbon-workspace-stage'); this.body = node('div', 'ribbon-workspace-body');
    this.quickAccessSlot = document.getElementById('quick-access-below') || node('div', 'ribbon-access-slot'); this.quickAccessSlot.id = 'quick-access-below'; this.quickAccessSlot.hidden = true;
    this.header = node('header', 'ribbon-host-header');
    this.grip = button('Move ribbon', 'Ribbon', () => {}); this.grip.className = 'ribbon-host-grip';
    this.orientation = button('Toggle floating ribbon orientation', '↔', event => this.setOrientation(this.state.orientation === 'vertical' ? 'horizontal' : 'vertical',event));
    this.back = button('Dock ribbon at top', '↥', event => this.place('top', event));
    this.header.append(this.grip, this.orientation, this.back);
    this.barGrip = button('Move or dock ribbon', '⠿', () => {}); this.barGrip.className = 'ribbon-bar-grip'; this.bar.prepend(this.barGrip);
    this.resizeGrip = button('Resize ribbon', '◢', () => {}); this.resizeGrip.className = 'ribbon-host-resize';
    this.bar.before(this.element); this.element.after(this.stage);
    this.element.append(this.header, this.bar, panel, this.resizeGrip);
    this.stage.append(this.body); this.body.append(this.quickAccessSlot, documents, workspace, status);
    this.preview = node('div', 'ribbon-dock-preview'); this.preview.hidden = true; this.studio.append(this.preview);
    this.live = node('span', 'ribbon-sr-only'); this.live.setAttribute('role', 'status'); this.live.setAttribute('aria-live', 'polite'); this.studio.append(this.live);
    for (const grip of [this.grip,this.barGrip]) {
      grip.addEventListener('pointerdown', event => this.begin(event, 'move'));
      grip.addEventListener('dblclick', event => this.place(this.state.placement === 'float' ? this.state.previousDock : 'float', event));
      grip.addEventListener('keydown', event => this.keyboardMove(event));
    }
    this.resizeGrip.addEventListener('pointerdown', event => this.begin(event, 'resize'));
    this.resizeGrip.addEventListener('keydown', event => this.keyboardMove(event, true));
    this.move = event => this.update(event); this.up = event => { if (this.gesture?.pointerId === event.pointerId) this.end(false, event); };
    this.cancel = event => { if (this.gesture?.pointerId === event.pointerId) this.end(true); };
    this.escape = event => { if (event.key === 'Escape' && this.gesture) { event.preventDefault(); event.stopPropagation(); this.end(true); } };
    this.controlUp=event=>{if(event.key==='Control'&&this.gesture?.override)this.end(false);};
    this.windowResize = () => { this.end(true); this.apply(this.state); }; this.blur=()=>this.end(true);
    document.addEventListener('pointermove', this.move); document.addEventListener('pointerup', this.up); document.addEventListener('pointercancel', this.cancel);
    document.addEventListener('keydown', this.escape, true);document.addEventListener('keyup',this.controlUp,true); this.studio.addEventListener('lostpointercapture', this.cancel); window.addEventListener('resize', this.windowResize);window.addEventListener('blur',this.blur);
    this.sync();
  }
  snapshot() { return ribbonHostPreferences(this.state); }
  sync() { if (this.element && !this.gesture) this.apply(this.customization.snapshot().host); }
  setLockMask(value) { this.lockMask = Number(value) & 15; }
  locked(event) { return ribbonLock(this.lockMask, this.state.placement === 'float', event); }
  restore(value) {
    try { this.customization.change({ type: 'host', value });this.sync(); }
    catch (error) { this.report(error); this.sync(); }
  }
  place(placement, event) {
    if (!this.element || this.locked(event)) { if(this.live)this.live.textContent = 'Ribbon position is locked. Hold Ctrl while moving to override.'; return; }
    if (placement !== 'float' && !this.state.allowDocking) { this.live.textContent = 'Enable Allow Docking before docking the ribbon.'; return; }
    this.end(true);
    const value = this.snapshot(), bounds = this.element.getBoundingClientRect();
    if (placement === 'float') { value.orientation = ['left','right'].includes(value.placement) ? 'vertical' : value.orientation; value.x = bounds.left + 20; value.y = bounds.top + 20; }
    else value.previousDock = placement;
    value.placement = placement; this.restore(value);if(placement==='float')raiseWorkspaceWindow(this.element);else releaseWorkspaceWindow(this.element); this.live.textContent = placement === 'float' ? 'Ribbon floating.' : `Ribbon docked ${placement}.`;
  }
  setOrientation(orientation,event) { if(this.locked(event))return; this.restore({ ...this.snapshot(), orientation }); }
  setAllowDocking(allowDocking,event) { if(this.locked(event))return;const value = this.snapshot(); value.allowDocking = allowDocking; if (!allowDocking) value.placement = 'float'; this.restore(value); }
  apply(value) {
    this.state = ribbonHostPreferences(value); if (!this.state.allowDocking) this.state.placement = 'float';
    const focus = this.element.contains(document.activeElement) ? document.activeElement : null;
    const selection = focus && typeof focus.selectionStart === 'number' ? [focus.selectionStart,focus.selectionEnd,focus.selectionDirection] : null;
    this.effectivePlacement = ['left','right'].includes(this.state.placement) && innerWidth < 720 ? 'float' : this.state.placement;
    const placement = this.effectivePlacement, vertical = placement === 'left' || placement === 'right' || placement === 'float' && this.state.orientation === 'vertical';
    if(placement!=='float')releaseWorkspaceWindow(this.element);
    this.element.dataset.placement = placement; this.element.dataset.axis = vertical ? 'vertical' : 'horizontal';
    this.studio.dataset.ribbonPlacement = placement;
    const parent = placement === 'left' || placement === 'right' ? this.stage : this.studio;
    if (this.element.parentNode !== parent) parent.append(this.element);
    if (placement === 'top' && this.element.nextElementSibling !== this.stage) this.stage.before(this.element);
    if (placement === 'left' && this.element.nextElementSibling !== this.body) this.body.before(this.element);
    if (placement === 'right' && this.element.previousElementSibling !== this.body) this.body.after(this.element);
    for (const property of ['left','top','width','height']) this.element.style.removeProperty(property);
    if (placement === 'float') {
      const rect = ribbonHostRect(this.state,{width:innerWidth,height:innerHeight});
      Object.assign(this.element.style,{left:`${rect.x}px`,top:`${rect.y}px`,width:`${rect.width}px`,height:`${rect.height}px`});
    } else if (vertical) {const width=Math.min(this.state.dockWidth,Math.max(240,innerWidth*.38));this.element.style.width=`${width}px`;this.element.style.setProperty('--ribbon-dock-content-width',`${width-33}px`);}
    this.orientation.hidden = placement !== 'float'; this.resizeGrip.hidden = placement === 'top';
    this.orientation.setAttribute('aria-pressed',String(this.state.orientation==='vertical'));
    this.barGrip.hidden = placement !== 'top'; this.header.hidden = placement === 'top';
    this.resizeGrip.title = placement === 'float' ? 'Resize floating ribbon · arrow keys · Shift for fine adjustment' : 'Resize docked ribbon · arrow keys';
    if (focus?.isConnected) { focus.focus({preventScroll:true}); if(selection)focus.setSelectionRange(...selection); }
    this.changed();
  }
  begin(event, kind) {
    if (event.button !== 0 || this.gesture || this.locked(event)) return;
    event.preventDefault(); event.stopPropagation();
    this.activate();
    this.gesture = { kind, pointerId:event.pointerId, start:{x:event.clientX,y:event.clientY}, original:this.snapshot(), bounds:this.element.getBoundingClientRect(), moved:false, target:null, focus:document.activeElement,override:this.locked()&&event.ctrlKey };
    // Capture on the stable outer shell: the ribbon itself changes parents when
    // it leaves a dock, and moving the capture element would release capture.
    this.studio.setPointerCapture(event.pointerId);
  }
  update(event) {
    const g=this.gesture;if(!g||g.pointerId!==event.pointerId)return;
    const dx=event.clientX-g.start.x,dy=event.clientY-g.start.y;if(!g.moved&&Math.hypot(dx,dy)<4)return;const first=!g.moved;g.moved=true;
    const value=ribbonHostPreferences(g.original);
    if(g.kind==='move'){
      value.placement='float';value.orientation=['left','right'].includes(g.original.placement)?'vertical':value.orientation;value.x=g.bounds.left+dx;value.y=g.bounds.top+dy;
      this.apply(value);if(first)raiseWorkspaceWindow(this.element);g.target=ribbonDockTarget({x:event.clientX,y:event.clientY},this.stage.getBoundingClientRect(),{allowDocking:value.allowDocking,ctrlKey:event.ctrlKey});this.showPreview(g.target);
    }else if(g.original.placement==='float'){
      value[value.orientation]={width:Math.max(200,g.bounds.width+dx),height:Math.max(120,g.bounds.height+dy)};this.apply(value);
    }else{value.dockWidth=g.bounds.width+dx*(g.original.placement==='right'?-1:1);this.apply(value);}
  }
  showPreview(target) {
    this.preview.hidden=!target;if(!target)return;
    const bounds=this.stage.getBoundingClientRect(),width=target==='top'?bounds.width:this.state.dockWidth;
    Object.assign(this.preview.style,{left:`${target==='right'?bounds.right-width:bounds.left}px`,top:`${bounds.top}px`,width:`${width}px`,height:`${target==='top'?150:bounds.height}px`});
    this.preview.textContent=`Dock ribbon ${target}`;
  }
  end(cancelled,event) {
    const g=this.gesture;if(!g)return;if(!cancelled&&event)this.update(event);this.gesture=null;
    if(this.studio.hasPointerCapture(g.pointerId))this.studio.releasePointerCapture(g.pointerId);this.preview.hidden=true;
    if(cancelled){this.apply(g.original);}
    else if(g.moved){const value=this.snapshot();if(g.target){value.placement=g.target;value.previousDock=g.target;}this.restore(value);}
    if(g.focus?.isConnected)g.focus.focus({preventScroll:true});
  }
  keyboardMove(event,resize=false) {
    if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)||event.altKey||event.metaKey||this.locked(event))return;
    if(this.state.placement!=='float'&&!resize)return;
    event.preventDefault();event.stopPropagation();const step=event.shiftKey?1:10,dx=(event.key==='ArrowLeft'?-1:event.key==='ArrowRight'?1:0)*step,dy=(event.key==='ArrowUp'?-1:event.key==='ArrowDown'?1:0)*step,value=this.snapshot();
    if(resize&&value.placement!=='float')value.dockWidth+=dx*(value.placement==='right'?-1:1);
    else if(resize)value[value.orientation]={width:value[value.orientation].width+dx,height:value[value.orientation].height+dy};
    else{value.x+=dx;value.y+=dy;}this.restore(value);event.target.focus({preventScroll:true});
  }
  dispose() {
    if(!this.element)return;this.end(true);
    document.removeEventListener('pointermove',this.move);document.removeEventListener('pointerup',this.up);document.removeEventListener('pointercancel',this.cancel);document.removeEventListener('keydown',this.escape,true);document.removeEventListener('keyup',this.controlUp,true);
    this.studio.removeEventListener('lostpointercapture',this.cancel);window.removeEventListener('resize',this.windowResize);window.removeEventListener('blur',this.blur);
    releaseWorkspaceWindow(this.element);this.element.removeEventListener('pointerdown',this.activate);this.element.removeEventListener('focusin',this.activate);
    this.preview.remove();this.live.remove();this.barGrip.remove();this.stage.before(this.bar,this.panel);this.stage.replaceWith(...this.body.children);this.element.remove();delete this.studio.dataset.ribbonPlacement;
  }
}
