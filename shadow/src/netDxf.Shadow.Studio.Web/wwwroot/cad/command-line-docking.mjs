import { commandWindowPreferences, commandWindowRect, resizeCommandWindow, interfaceLockMask } from './workspace-shell-model.mjs';
import { raiseWorkspaceWindow, releaseWorkspaceWindow } from './workspace-foreground.mjs';

/** Moves the original editor. Input, selection, log and command handlers retain their identities. */
export class CommandLineDock {
  constructor({ element, splitter, workspace, drawingArea, initial, changed=()=>{}, resized=()=>{} }) {
    Object.assign(this,{element,splitter,workspace,drawingArea,changed,resized});
    this.state=commandWindowPreferences(initial);this.gesture=null;this.lockMask=0;this.cleanups=[];
    this.marker=document.createComment('command-line-dock');element.before(this.marker);
    this.splitMarker=document.createComment('command-splitter-dock');splitter.before(this.splitMarker);
    this.grip=element.querySelector('#command-grip');this.toggle=element.querySelector('#command-dock-toggle');
    this.resizeGrip=element.querySelector('#command-resize');this.resizeGrip.dataset.commandResize='se';
    this.handles=[this.resizeGrip];
    for(const [edge,label] of Object.entries({n:'top',ne:'top right',e:'right',s:'bottom',sw:'bottom left',w:'left',nw:'top left'})) {
      const handle=document.createElement('div');handle.className=`command-resize-edge command-resize-${edge}`;
      handle.dataset.commandResize=edge;handle.tabIndex=0;handle.setAttribute('role','separator');
      handle.setAttribute('aria-label',`Resize command line ${label} edge`);handle.setAttribute('aria-orientation',edge==='n'||edge==='s'?'horizontal':'vertical');
      element.append(handle);this.handles.push(handle);
    }
    this.preview=document.createElement('div');this.preview.className='command-dock-preview';this.preview.hidden=true;workspace.append(this.preview);
    const listen=(node,type,handler,options)=>{node.addEventListener(type,handler,options);this.cleanups.push(()=>node.removeEventListener(type,handler,options));};
    const raise=()=>{if(this.state.mode!=='dock')raiseWorkspaceWindow(this.element);};
    listen(element,'pointerdown',raise);listen(element,'focusin',raise);
    this.move=event=>this.moveGesture(event);
    this.up=event=>{if(event.type==='pointerup'&&this.gesture?.moved)this.moveGesture(event);this.endGesture(event.type==='pointercancel',event.pointerId);};
    listen(this.grip,'pointerdown',event=>this.beginGesture(event,'move'));
    for(const handle of this.handles){
      listen(handle,'pointerdown',event=>this.beginGesture(event,'resize',handle.dataset.commandResize));
      listen(handle,'keydown',event=>this.keyboard(event,handle.dataset.commandResize));
    }
    listen(this.grip,'dblclick',event=>this.setMode(this.state.mode==='dock'?'float':'dock',this.state.dock,event));
    listen(this.toggle,'click',event=>this.setMode(this.state.mode==='dock'?'float':'dock',this.state.dock,event));
    listen(this.grip,'keydown',event=>this.keyboard(event));
    listen(document,'keydown',event=>{if(event.key==='Escape'&&this.gesture){event.preventDefault();event.stopPropagation();this.endGesture(true);}},true);
    listen(document,'lostpointercapture',event=>{
      const gesture=this.gesture;if(!gesture||event.pointerId!==gesture.pointerId)return;
      // Reparenting releases capture; apply() reacquires it in the same turn.
      queueMicrotask(()=>{if(this.gesture===gesture&&!gesture.captureTarget.hasPointerCapture?.(gesture.pointerId))this.endGesture(true);});
    },true);
    listen(window,'blur',()=>this.endGesture(true));
    listen(document,'keyup',event=>{if(event.key==='Control'&&this.gesture?.override)this.endGesture(false);},true);
    this.observer=new ResizeObserver(()=>this.fit());this.observer.observe(workspace);this.observer.observe(drawingArea);
    this.apply();
  }
  snapshot() {return {...this.state};}
  restore(value) {this.endGesture(true);this.state=commandWindowPreferences(value);if(!this.state.allowDocking)this.state.mode='float';this.apply();}
  releaseForeground() {releaseWorkspaceWindow(this.element);}
  bounds() {return this.workspace.getBoundingClientRect();}
  fit() {if(!this.gesture&&this.state.mode!=='dock')this.apply();}
  setLockMask(mask) {this.lockMask=interfaceLockMask(mask);if(this.gesture&&this.isLocked()&&!this.gesture.override)this.endGesture(true);this.applyLock();}
  isLocked(event) {return !event?.ctrlKey&&!!(this.lockMask&(this.state.mode==='float'?8:2));}
  applyLock() {
    const locked=this.isLocked();this.element.dataset.layoutLocked=String(locked);
    for(const node of [this.grip,this.toggle,...this.handles])node.setAttribute('aria-disabled',String(locked));
    this.splitter.setAttribute('aria-disabled',String(locked));
  }
  positionRect() {
    const host=this.bounds(),rect=commandWindowRect(this.state,host);
    if(this.state.mode==='anchor'){
      const area=this.drawingArea.getBoundingClientRect(),left=area.left-host.left;
      rect.width=Math.min(rect.width,Math.max(1,area.width-8));rect.height=Math.min(rect.height,Math.max(1,area.height-8));
      rect.x=Math.max(left+4,Math.min(rect.x,left+area.width-rect.width-4));
      rect.y=this.state.dock==='top'?area.top-host.top+4:area.bottom-host.top-rect.height-4;
    }
    return rect;
  }
  apply() {
    const floating=this.state.mode!=='dock',top=this.state.dock==='top';
    const wasFloating=this.element.classList.contains('command-floating');
    const active=this.element.contains(document.activeElement)?document.activeElement:null;
    const selection=active&&typeof active.selectionStart==='number'?{start:active.selectionStart,end:active.selectionEnd,direction:active.selectionDirection}:null;
    if(floating){if(this.element.parentNode!==this.workspace)this.workspace.append(this.element);if(this.splitter.previousSibling!==this.splitMarker)this.splitMarker.after(this.splitter);}
    else if(top){
      if(this.drawingArea.firstElementChild!==this.element)this.drawingArea.prepend(this.element);
      if(this.element.nextElementSibling!==this.splitter)this.element.after(this.splitter);
    } else {
      if(this.element.previousSibling!==this.marker)this.marker.after(this.element);
      if(this.splitter.previousSibling!==this.splitMarker)this.splitMarker.after(this.splitter);
    }
    if(active&&document.activeElement!==active){active.focus({preventScroll:true});if(selection)active.setSelectionRange(selection.start,selection.end,selection.direction);}
    const gesture=this.gesture;
    if(gesture?.captureTarget&&!gesture.captureTarget.hasPointerCapture?.(gesture.pointerId)){
      try{gesture.captureTarget.setPointerCapture?.(gesture.pointerId);}catch{/* The pointer may already have ended. */}
    }
    this.element.classList.toggle('command-floating',floating);this.element.dataset.commandDock=this.state.dock;
    if(!floating)releaseWorkspaceWindow(this.element);else if(!wasFloating)raiseWorkspaceWindow(this.element);
    this.element.dataset.commandMode=this.state.mode;this.splitter.hidden=floating;
    this.workspace.classList.toggle('has-floating-command',floating);
    for(const handle of this.handles)handle.hidden=!floating;
    this.toggle.title=floating?'Dock command line':'Float command line';
    this.toggle.setAttribute('aria-label',this.toggle.title);this.toggle.setAttribute('aria-pressed',String(floating));
    this.grip.title='Move command line · double-click to dock/float · arrow keys move · Ctrl overrides locks and docking';
    if(floating){
      const rect=this.positionRect();for(const key of ['left','top','width','height'])this.element.style[key]=`${rect[{left:'x',top:'y'}[key]||key]}px`;
    }else for(const key of ['left','top','width','height'])this.element.style.removeProperty(key);
    this.element.style.setProperty('--command-idle-opacity',String(this.state.inactiveOpacity));
    this.element.style.setProperty('--command-active-opacity',String(this.state.activeOpacity));
    this.applyLock();this.resized();
  }
  setMode(mode,dock=this.state.dock,event) {
    if(this.isLocked(event)||mode!=='float'&&!this.state.allowDocking)return false;
    this.endGesture(true);this.state=commandWindowPreferences({...this.state,mode,dock});this.apply();if(mode!=='dock')raiseWorkspaceWindow(this.element);this.changed(this.snapshot());return true;
  }
  setAllowDocking(value) {
    this.endGesture(true);this.state.allowDocking=!!value;if(!value)this.state.mode='float';this.apply();this.changed(this.snapshot());
  }
  setOpacity(inactive,active=this.state.activeOpacity) {
    this.state=commandWindowPreferences({...this.state,inactiveOpacity:inactive,activeOpacity:active});this.apply();this.changed(this.snapshot());
  }
  keyboard(event,edge) {
    if(event.defaultPrevented||event.metaKey||event.altKey||this.isLocked(event)||!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key))return;
    event.preventDefault();event.stopPropagation();
    const step=event.shiftKey?1:10,dx=event.key==='ArrowRight'?step:event.key==='ArrowLeft'?-step:0,dy=event.key==='ArrowDown'?step:event.key==='ArrowUp'?-step:0;
    const rect=this.positionRect(),next=edge?resizeCommandWindow(rect,dx,dy,this.bounds(),edge):{...rect,x:rect.x+dx,y:rect.y+dy};
    this.state=commandWindowPreferences({...this.state,...next,mode:edge?this.state.mode:'float'});
    this.state={...this.state,...commandWindowRect(this.state,this.bounds())};this.apply();this.changed(this.snapshot());
  }
  beginGesture(event,kind,edge='se') {
    if(event.button!==0||event.isPrimary===false||this.gesture||this.isLocked(event))return;
    event.preventDefault();const host=this.bounds(),rect=this.element.getBoundingClientRect();
    this.gesture={kind,edge,pointerId:event.pointerId,captureTarget:event.currentTarget,x:event.clientX,y:event.clientY,before:this.snapshot(),moved:false,dock:null,override:!!(event.ctrlKey&&this.isLocked()),
      rect:{x:rect.left-host.left,y:rect.top-host.top,width:rect.width,height:rect.height}};
    event.currentTarget?.setPointerCapture?.(event.pointerId);
    document.addEventListener('pointermove',this.move);document.addEventListener('pointerup',this.up);document.addEventListener('pointercancel',this.up);
  }
  moveGesture(event) {
    const g=this.gesture;if(!g||event.pointerId!==g.pointerId)return;
    const dx=event.clientX-g.x,dy=event.clientY-g.y;if(!g.moved&&Math.hypot(dx,dy)<4)return;g.moved=true;
    const next=g.kind==='resize'?resizeCommandWindow(g.rect,dx,dy,this.bounds(),g.edge):{...g.rect,x:g.rect.x+dx,y:g.rect.y+dy};
    this.state=commandWindowPreferences({...g.before,...next,mode:g.kind==='resize'&&g.before.mode==='anchor'?'anchor':'float'});
    this.state={...this.state,...commandWindowRect(this.state,this.bounds())};
    const area=this.drawingArea.getBoundingClientRect(),host=this.bounds();g.dock=null;
    if(g.kind==='move'&&this.state.allowDocking&&!event.ctrlKey&&event.clientX>=area.left&&event.clientX<=area.right){
      if(event.clientY>=area.top-5&&event.clientY<=area.top+35)g.dock='top';
      else if(event.clientY>=area.bottom-45&&event.clientY<=area.bottom+5)g.dock='bottom';
    }
    this.preview.hidden=!g.dock;
    if(g.dock){this.preview.textContent=`Dock command line ${g.dock}`;this.preview.style.left=`${area.left-host.left}px`;this.preview.style.width=`${area.width}px`;this.preview.style.top=`${Math.max(0,g.dock==='top'?area.top-host.top:area.bottom-host.top-110)}px`;}
    this.apply();
  }
  endGesture(cancel=false,pointerId) {
    const g=this.gesture;if(!g||(pointerId!==undefined&&pointerId!==g.pointerId))return;
    this.gesture=null;this.preview.hidden=true;
    document.removeEventListener('pointermove',this.move);document.removeEventListener('pointerup',this.up);document.removeEventListener('pointercancel',this.up);
    if(g.captureTarget.hasPointerCapture?.(g.pointerId))g.captureTarget.releasePointerCapture(g.pointerId);
    if(cancel)this.state=g.before;else if(g.dock)this.state={...this.state,mode:'dock',dock:g.dock};
    this.apply();if(!cancel&&g.moved)this.changed(this.snapshot());
  }
  dispose() {this.endGesture(true);releaseWorkspaceWindow(this.element);this.observer.disconnect();for(const cleanup of this.cleanups)cleanup();for(const node of this.handles)if(node!==this.resizeGrip)node.remove();this.preview.remove();}
}
