import { commandWindowPreferences, commandWindowRect } from './workspace-shell-model.mjs';

/** Reparents the existing command form: input, history and command handlers keep their identities. */
export class CommandLineDock {
  constructor({ element, splitter, workspace, drawingArea, initial, changed = () => {}, resized = () => {} }) {
    this.element=element; this.splitter=splitter; this.workspace=workspace; this.drawingArea=drawingArea;
    this.changed=changed; this.resized=resized; this.state=commandWindowPreferences(initial); this.gesture=null;
    this.marker=document.createComment('command-line-dock'); element.before(this.marker);
    this.grip=element.querySelector('#command-grip'); this.toggle=element.querySelector('#command-dock-toggle');
    this.resizeGrip=element.querySelector('#command-resize');
    this.preview=document.createElement('div'); this.preview.className='command-dock-preview'; this.preview.hidden=true;
    this.preview.textContent='Dock command line'; workspace.append(this.preview);
    this.move=event=>this.moveGesture(event); this.up=event=>this.endGesture(event.type==='pointercancel',event.pointerId);
    this.escape=event=>{if(event.key==='Escape'&&this.gesture){event.preventDefault();event.stopPropagation();this.endGesture(true);}};
    this.grip.addEventListener('pointerdown',event=>this.beginGesture(event,'move'));
    this.resizeGrip.addEventListener('pointerdown',event=>this.beginGesture(event,'resize'));
    this.grip.addEventListener('dblclick',()=>this.setMode(this.state.mode==='dock'?'float':'dock'));
    this.toggle.addEventListener('click',()=>this.setMode(this.state.mode==='dock'?'float':'dock'));
    this.grip.addEventListener('keydown',event=>this.keyboard(event,false));
    this.resizeGrip.addEventListener('keydown',event=>this.keyboard(event,true));
    document.addEventListener('keydown',this.escape,true);
    this.observer=new ResizeObserver(()=>this.fit()); this.observer.observe(workspace);
    this.apply();
  }
  snapshot() { return {...this.state}; }
  restore(value) { this.endGesture(true);this.state=commandWindowPreferences(value);this.apply(); }
  bounds() { return this.workspace.getBoundingClientRect(); }
  fit() { if(this.gesture)return;if(this.state.mode==='float')this.apply(); }
  apply() {
    const floating=this.state.mode==='float',target=floating?this.workspace:this.marker.parentNode;
    if(this.element.parentNode!==target){
      const active=this.element.contains(document.activeElement)?document.activeElement:null;
      const selection=active&&typeof active.selectionStart==='number'?{start:active.selectionStart,end:active.selectionEnd,direction:active.selectionDirection}:null;
      if(floating)target.append(this.element);else this.marker.after(this.element);
      if(active){active.focus({preventScroll:true});if(selection)active.setSelectionRange(selection.start,selection.end,selection.direction);}
      const gesture=this.gesture;
      if(gesture?.captureTarget&&!gesture.captureTarget.hasPointerCapture?.(gesture.pointerId)){
        try{gesture.captureTarget.setPointerCapture?.(gesture.pointerId);}catch{/* Pointer may already have ended. */}
      }
    }
    this.element.classList.toggle('command-floating',floating);this.splitter.hidden=floating;
    this.workspace.classList.toggle('has-floating-command',floating);
    this.resizeGrip.hidden=!floating;
    this.toggle.title=floating?'Dock command line':'Float command line';
    this.toggle.setAttribute('aria-label',this.toggle.title);this.toggle.setAttribute('aria-pressed',String(floating));
    this.grip.title='Move command line · double-click to dock/float · arrow keys move · Shift for fine adjustment';
    if(floating){
      const rect=commandWindowRect(this.state,this.bounds());
      this.element.style.left=`${rect.x}px`;this.element.style.top=`${rect.y}px`;
      this.element.style.width=`${rect.width}px`;this.element.style.height=`${rect.height}px`;
    } else for(const key of ['left','top','width','height'])this.element.style.removeProperty(key);
    this.resized();
  }
  setMode(mode) { this.endGesture(true);this.state.mode=mode==='float'?'float':'dock';this.apply();this.changed(this.snapshot()); }
  keyboard(event,resize) {
    if(event.defaultPrevented||event.ctrlKey||event.metaKey||event.altKey||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;
    event.preventDefault();event.stopPropagation();
    const step=event.shiftKey?1:10,dx=event.key==='ArrowRight'?step:event.key==='ArrowLeft'?-step:0,dy=event.key==='ArrowDown'?step:event.key==='ArrowUp'?-step:0;
    const rect=commandWindowRect(this.state,this.bounds());
    this.state=commandWindowPreferences({...rect,mode:'float',...(resize?{width:rect.width+dx,height:rect.height+dy}:{x:rect.x+dx,y:rect.y+dy})});
    this.state={...this.state,...commandWindowRect(this.state,this.bounds())};this.apply();this.changed(this.snapshot());
  }
  beginGesture(event,kind) {
    if(event.button!==0||event.isPrimary===false||this.gesture)return;
    event.preventDefault();
    const host=this.bounds(),rect=this.element.getBoundingClientRect();
    this.gesture={kind,pointerId:event.pointerId,captureTarget:event.currentTarget,x:event.clientX,y:event.clientY,before:this.snapshot(),moved:false,dock:false,
      rect:{x:rect.left-host.left,y:rect.top-host.top,width:rect.width,height:rect.height}};
    event.currentTarget?.setPointerCapture?.(event.pointerId);
    document.addEventListener('pointermove',this.move);document.addEventListener('pointerup',this.up);document.addEventListener('pointercancel',this.up);
  }
  moveGesture(event) {
    const g=this.gesture;if(!g||event.pointerId!==g.pointerId)return;
    const dx=event.clientX-g.x,dy=event.clientY-g.y;
    if(!g.moved&&Math.hypot(dx,dy)<4)return;g.moved=true;
    const rect=g.rect;
    this.state=commandWindowPreferences({...rect,mode:'float',...(g.kind==='resize'?{width:rect.width+dx,height:rect.height+dy}:{x:rect.x+dx,y:rect.y+dy})});
    this.state={...this.state,...commandWindowRect(this.state,this.bounds())};
    const center=this.drawingArea.getBoundingClientRect(),host=this.bounds();
    g.dock=g.kind==='move'&&!event.ctrlKey&&event.clientX>=center.left&&event.clientX<=center.right&&event.clientY>=center.bottom-45&&event.clientY<=center.bottom+5;
    this.preview.hidden=!g.dock;
    if(g.dock){this.preview.style.left=`${center.left-host.left}px`;this.preview.style.width=`${center.width}px`;this.preview.style.top=`${Math.max(0,center.bottom-host.top-110)}px`;}
    this.apply();
  }
  endGesture(cancel=false,pointerId) {
    const g=this.gesture;if(!g||(pointerId!==undefined&&pointerId!==g.pointerId))return;
    this.gesture=null;this.preview.hidden=true;
    document.removeEventListener('pointermove',this.move);document.removeEventListener('pointerup',this.up);document.removeEventListener('pointercancel',this.up);
    for(const node of [this.grip,this.resizeGrip])if(node.hasPointerCapture?.(g.pointerId))node.releasePointerCapture(g.pointerId);
    if(cancel)this.state=g.before;else if(g.dock)this.state.mode='dock';
    this.apply();if(!cancel&&g.moved)this.changed(this.snapshot());
  }
  dispose() {this.endGesture(true);this.observer.disconnect();document.removeEventListener('keydown',this.escape,true);this.preview.remove();}
}
