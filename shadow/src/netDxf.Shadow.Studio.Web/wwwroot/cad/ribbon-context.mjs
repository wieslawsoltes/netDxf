import {bindRovingToolbar} from './ribbon-keyboard.mjs';
import {protectedRibbonPanel} from './ribbon-customization.mjs';
import {ribbonLock} from './ribbon-host-model.mjs';

export function ribbonCommandIdentity(node) {
  const target=node?.closest?.('[data-command-id],[data-ribbon-command-id],[data-command],[data-action]');
  if(!target)return null;
  return target.getAttribute('data-command-id')||target.getAttribute('data-ribbon-command-id')||
    (target.getAttribute('data-command')?`command:${target.getAttribute('data-command')}`:target.getAttribute('data-action')?`action:${target.getAttribute('data-action')}`:null);
}

/** Context commands change the same portable preferences as Customize Ribbon. */
export class RibbonContextMenu {
  constructor(layout) {
    this.layout=layout;this.menu=null;this.drag=null;
    this.context=event=>{if(this.owns(event.target)){event.preventDefault();event.stopPropagation();this.open(event.target,event.clientX,event.clientY);}};
    this.keys=event=>{
      if(event.defaultPrevented||event.isComposing)return;
      if(this.menu&&event.key==='Escape'){event.preventDefault();event.stopPropagation();this.back();}
      else if(this.menu&&event.key==='Tab')this.close();
      else if(this.drag&&event.key==='Escape'){event.preventDefault();event.stopPropagation();this.finish(true);}
      else if((event.key==='ContextMenu'||event.shiftKey&&event.key==='F10')&&this.owns(event.target)){
        event.preventDefault();event.stopPropagation();const rect=event.target.getBoundingClientRect();this.open(event.target,rect.left,rect.bottom);
      }
    };
    this.outside=event=>{if(this.menu&&!this.menu.contains(event.target))this.close();};
    this.down=event=>this.begin(event);this.move=event=>this.update(event);this.up=event=>{if(this.drag?.pointerId===event.pointerId)this.finish(false,event);};
    this.cancel=event=>{if(this.drag?.pointerId===event.pointerId)this.finish(true);};
    this.controlUp=event=>{if(event.key==='Control'&&this.drag?.override)this.finish(false);};
    this.click=event=>{if(this.suppressUntil>Date.now()){event.preventDefault();event.stopImmediatePropagation();}};
    this.resize=()=>{this.close();this.finish(true);};
    document.addEventListener('contextmenu',this.context,true);document.addEventListener('keydown',this.keys,true);document.addEventListener('keyup',this.controlUp,true);document.addEventListener('pointerdown',this.outside);
    document.addEventListener('pointerdown',this.down);document.addEventListener('pointermove',this.move);document.addEventListener('pointerup',this.up);document.addEventListener('pointercancel',this.cancel);
    document.addEventListener('click',this.click,true);layout.host.studio.addEventListener('lostpointercapture',this.cancel);window.addEventListener('resize',this.resize);window.addEventListener('blur',this.resize);
  }
  owns(target){return !!target?.closest?.('.ribbon-host,.ribbon-floating,.ribbon-flyout,.ribbon-command-menu,.ribbon-slideout-popup');}
  panels(tab){const entries=this.layout.context.describePanels?.(tab)||this.layout.sourceGroups.map(group=>({id:group.dataset.ribbonPanel||group.dataset.panel,name:group.dataset.panel}));return this.layout.customization.panels(tab,entries,{includeHidden:true});}
  change(action,event,position=false,floating=false){
    if(position&&ribbonLock(this.layout.lockMask,floating||this.layout.host.state.placement==='float',event))return;
    try{this.layout.customization.change(action,{entries:this.layout.context.entries||[],panels:this.panels(action.tab||this.layout.context.tab)});}
    catch(error){this.layout.report(error);}
  }
  items(target){
    const layout=this.layout,frameElement=target.closest('.ribbon-floating'),group=target.closest('.ribbon-group')||layout.popup?.group,
      frame=frameElement?[...layout.floating.frames.values()].find(frame=>frame.element===frameElement):null,
      tab=target.closest('[data-tab]')?.dataset.tab||frame?.spec.tab||layout.context.tab,panel=frame?.spec.panel||group?.dataset.ribbonPanel||group?.dataset.panel,items=[],id=ribbonCommandIdentity(target);
    if(id&&layout.commandAccess){const pinned=layout.commandAccess.isPinned(id);items.push({label:pinned?'Remove from Quick Access Toolbar':'Add to Quick Access Toolbar',run:()=>pinned?layout.commandAccess.unpin(id):layout.commandAccess.pin(id)});}
    if(panel){
      const protectedPanel=protectedRibbonPanel(tab,panel);
      items.push({label:frame?'Return panel to ribbon':'Float panel',disabled:protectedPanel,run:event=>frame?layout.floating.returnPanel(frame.spec,event):layout.floatPanel(group,event)});
      if(frame)items.push({label:'Vertical panel layout',checked:frame.spec.orientation==='vertical',run:event=>layout.floating.orientPanel(frame.spec,frame.spec.orientation==='vertical'?'horizontal':'vertical',event)});
      if(!frame){const panels=this.panels(tab),index=panels.findIndex(value=>value.id===panel);
        items.push({label:'Move panel earlier',disabled:index<=0,run:event=>this.change({type:'panel-move',tab,id:panel,offset:-1},event,true)},
          {label:'Move panel later',disabled:index===panels.length-1,run:event=>this.change({type:'panel-move',tab,id:panel,offset:1},event,true)});
      }
      items.push({label:'Hide panel',disabled:protectedPanel,run:event=>this.change({type:'panel-visible',tab,id:panel,visible:false},event)});
    }
    if(target.closest('[data-tab]')){
      const tabs=layout.context.entries||[],index=tabs.findIndex(value=>value.key===tab);
      items.push({label:'Move tab earlier',disabled:index<=0,run:event=>this.change({type:'tab-move',id:tab,offset:-1},event,true)},
        {label:'Move tab later',disabled:index===tabs.length-1,run:event=>this.change({type:'tab-move',id:tab,offset:1},event,true)},
        {label:'Hide tab',disabled:['Home','Block Editor'].includes(tab),run:event=>this.change({type:'tab-visible',id:tab,visible:false},event)});
    }
    items.push({label:'Show Tabs',children:()=> (layout.context.entries||[]).map(entry=>({label:entry.name||entry.key,checked:entry.visible!==false,disabled:['Home','Block Editor'].includes(entry.key),run:event=>this.change({type:'tab-visible',id:entry.key,visible:entry.visible===false},event)}))},
      {label:'Show Panels',children:()=>this.panels(tab).map(entry=>({label:entry.name||entry.id,checked:entry.visible!==false,disabled:protectedRibbonPanel(tab,entry.id),run:event=>this.change({type:'panel-visible',tab,id:entry.id,visible:entry.visible===false},event)}))},
      {label:'Ribbon display',children:()=>[{label:'Full ribbon',value:'full'},{label:'Panel buttons',value:'buttons'},{label:'Panel titles',value:'titles'},{label:'Tabs only',value:'tabs'}].map(entry=>({label:entry.label,checked:layout.mode===entry.value,run:()=>{layout.setMode(entry.value);layout.panel.dispatchEvent(new CustomEvent('ribbonmodechange',{bubbles:true,detail:entry.value}));}}))},
      {label:'Dock ribbon',children:()=>['top','left','right','float'].map(value=>({label:value==='float'?'Floating':`Dock ${value}`,checked:layout.host.state.placement===value,run:event=>layout.host.place(value,event)}))},
      {label:'Allow Docking',checked:layout.host.state.allowDocking,run:event=>layout.host.setAllowDocking(!layout.host.state.allowDocking,event)});
    if(layout.host.state.placement==='float')items.push({label:'Vertical ribbon layout',checked:layout.host.state.orientation==='vertical',run:event=>layout.host.setOrientation(layout.host.state.orientation==='vertical'?'horizontal':'vertical',event)});
    if(layout.floating.frames.size)items.push({label:'Return all panels to ribbon',run:event=>{for(const frame of [...layout.floating.frames.values()])layout.floating.returnPanel(frame.spec,event);}});
    if(layout.commandAccess)items.push({label:'Customize ribbon…',run:()=>layout.commandAccess.execute('action:ribbon-customize')});
    return items;
  }
  open(target,x,y){
    this.close();this.layout.keytips?.close();this.origin=document.activeElement;this.anchor={x,y};this.levels=[{label:'Ribbon',items:this.items(target)}];
    this.menu=document.createElement('div');this.menu.className='ribbon-context-menu';this.menu.setAttribute('role','menu');document.body.append(this.menu);this.render();
  }
  render(){
    this.keyboard?.dispose();const level=this.levels.at(-1);this.menu.replaceChildren();this.menu.setAttribute('aria-label',level.label);
    if(this.levels.length>1)this.add({label:`‹ ${this.levels.at(-2).label}`,run:()=>this.back(),keep:true});
    for(const item of level.items)this.add(item);
    this.menu.style.left='4px';this.menu.style.top='4px';const rect=this.menu.getBoundingClientRect();
    this.menu.style.left=`${Math.max(4,Math.min(this.anchor.x,innerWidth-rect.width-4))}px`;this.menu.style.top=`${Math.max(4,Math.min(this.anchor.y,innerHeight-rect.height-4))}px`;
    this.keyboard=bindRovingToolbar(this.menu,{orientation:'vertical',onEscape:()=>this.back()});this.keyboard.focus();
  }
  add(item){
    const button=document.createElement('button');button.type='button';button.disabled=!!item.disabled;button.textContent=item.label+(item.children?' ›':'');
    button.setAttribute('aria-label',button.textContent);
    button.setAttribute('role',item.checked===undefined?'menuitem':'menuitemcheckbox');if(item.checked!==undefined)button.setAttribute('aria-checked',String(item.checked));
    if(item.children)button.setAttribute('aria-haspopup','menu');
    button.addEventListener('click',event=>{event.preventDefault();event.stopPropagation();if(item.children){this.levels.push({label:item.label,items:item.children()});this.render();}
      else{if(!item.keep)this.close(true);Promise.resolve(item.run?.(event)).catch(this.layout.report);}});this.menu.append(button);
  }
  back(){if(this.levels?.length>1){this.levels.pop();this.render();}else this.close(true);}
  close(focus=false){if(!this.menu)return;this.keyboard?.dispose();this.menu.remove();this.menu=null;if(focus&&this.origin?.isConnected)this.origin.focus({preventScroll:true});}
  begin(event){
    const target=event.target.closest?.('#ribbon-tabs [data-tab]');if(!target||event.button!==0||this.drag||ribbonLock(this.layout.lockMask,this.layout.host.state.placement==='float',event))return;
    this.drag={target,pointerId:event.pointerId,start:{x:event.clientX,y:event.clientY},moved:false,before:null,override:ribbonLock(this.layout.lockMask,this.layout.host.state.placement==='float')&&event.ctrlKey};
  }
  update(event){
    const drag=this.drag;if(!drag||drag.pointerId!==event.pointerId)return;
    if(!drag.moved&&Math.hypot(event.clientX-drag.start.x,event.clientY-drag.start.y)<6)return;
    if(!drag.moved){drag.moved=true;this.layout.host.studio.setPointerCapture(event.pointerId);}
    event.preventDefault();const vertical=this.layout.host.element.dataset.axis==='vertical',tabs=[...document.querySelectorAll('#ribbon-tabs [data-tab]')];
    tabs.forEach(tab=>tab.classList.remove('ribbon-reorder-before'));
    drag.before=tabs.find(tab=>tab!==drag.target&&event[vertical?'clientY':'clientX']<tab.getBoundingClientRect()[vertical?'top':'left']+tab.getBoundingClientRect()[vertical?'height':'width']/2);
    drag.before?.classList.add('ribbon-reorder-before');
  }
  finish(cancelled,event){
    const drag=this.drag;if(!drag)return;if(!cancelled&&event)this.update(event);this.drag=null;
    if(this.layout.host.studio.hasPointerCapture(drag.pointerId))this.layout.host.studio.releasePointerCapture(drag.pointerId);
    for(const tab of document.querySelectorAll('#ribbon-tabs [data-tab]'))tab.classList.remove('ribbon-reorder-before');
    if(!cancelled&&drag.moved){this.suppressUntil=Date.now()+100;this.change({type:'tab-reorder',id:drag.target.dataset.tab,beforeId:drag.before?.dataset.tab},drag.override?{ctrlKey:true}:event,true,this.layout.host.state.placement==='float');}
  }
  dispose(){
    this.close();this.finish(true);document.removeEventListener('contextmenu',this.context,true);document.removeEventListener('keydown',this.keys,true);document.removeEventListener('keyup',this.controlUp,true);document.removeEventListener('pointerdown',this.outside);
    document.removeEventListener('pointerdown',this.down);document.removeEventListener('pointermove',this.move);document.removeEventListener('pointerup',this.up);document.removeEventListener('pointercancel',this.cancel);
    document.removeEventListener('click',this.click,true);this.layout.host.studio.removeEventListener('lostpointercapture',this.cancel);window.removeEventListener('resize',this.resize);window.removeEventListener('blur',this.resize);
  }
}
