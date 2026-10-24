import { RibbonFloatingPanels } from './ribbon-floating.mjs';
import { protectedRibbonPanel } from './ribbon-customization.mjs';
import { bindRovingToolbar, nativeRibbonInput, visibleRibbonControl } from './ribbon-keyboard.mjs';
import { RibbonSplitMenus } from './ribbon-split.mjs';
import { RibbonHost } from './ribbon-host.mjs';
import { RibbonContextMenu } from './ribbon-context.mjs';
import { RibbonKeytips } from './ribbon-keytips.mjs';
import { RibbonSlideouts } from './ribbon-slideout.mjs';

let panelSerial=0;

/** Preserve leading panels while collapsing trailing ones until they fit. */
export function collapsedPanels(widths, available, compactWidth = 56, gap = 2) {
  const result = widths.map(() => false);
  let used = widths.reduce((sum, width) => sum + width, 0) + Math.max(0, widths.length - 1) * gap;
  for (let i = widths.length - 1; i >= 0 && used > available; i--) {
    if (widths[i] <= compactWidth) continue;
    result[i] = true; used -= widths[i] - compactWidth;
  }
  return result;
}

export const ribbonModes = ['full','buttons','titles','tabs'];
export function chromePreferences(saved = {}) {
  return { ribbonMode:ribbonModes.includes(saved.ribbonMode)?saved.ribbonMode:saved.ribbonMinimized===true?'tabs':'full', commandLineVisible:saved.commandLineVisible !== false,
    commandHistoryVisible:saved.commandHistoryVisible !== false };
}

export function flyoutPosition(anchor, size, viewport) {
  return {left:Math.max(4,Math.min(anchor.left,viewport.width-size.width-4)),
    top:Math.max(4,Math.min(anchor.bottom+3,viewport.height-size.height-4))};
}

/** Contextual tabs never leave the user on a hidden or disabled module tab. */
export function reconcileRibbonContext(currentTab, context = {}, entries = []) {
  const blockEditing = entries.some(entry => entry.key === 'Block Editor');
  const enabled = key => entries.some(entry => entry.key === key && !entry.disabled);
  let tab = currentTab, previousTab = context.previousTab || null;
  if (blockEditing && !context.blockEditing) {
    previousTab = currentTab === 'Block Editor' ? 'Home' : currentTab;
    tab = 'Block Editor';
  } else if (!blockEditing && context.blockEditing) {
    tab = enabled(previousTab) ? previousTab : 'Home'; previousTab = null;
  }
  if (!enabled(tab)) tab = blockEditing ? 'Block Editor' : 'Home';
  return { tab, blockEditing, previousTab };
}

/** Adaptive native-style ribbon panels. The flyout reuses controls and event handlers. */
export class RibbonLayout {
  constructor(panel, { customization, report = () => {} } = {}) {
    this.panel = panel; this.groups = []; this.popup = null; this.frame = 0; this.mode='full';
    this.customization=customization;this.report=report;this.lockMask=0;
    this.keyboard=bindRovingToolbar(panel,{items:()=>this.panelButtons(),onEscape:()=>{this.hide();document.querySelector('#ribbon-tabs [aria-selected=true]')?.focus({preventScroll:true});}});
    this.splitMenus=new RibbonSplitMenus({onChoose:()=>this.hide(),changed:()=>this.keyboard?.refresh(),keytipsActive:()=>this.keytips?.active});
    this.sourceGroups = []; this.context = {};
    this.floating = customization ? new RibbonFloatingPanels({ panel, customization, report,
      changed: () => this.set(this.sourceGroups, this.context) }) : null;
    this.host=customization?new RibbonHost({panel,customization,report,changed:()=>this.schedule()}):null;
    this.contextMenu=this.host?.element?new RibbonContextMenu(this):null;
    this.keytips=this.host?.element?new RibbonKeytips(this):null;
    this.slideouts=this.host?.element?new RibbonSlideouts(this):null;
    this.observer = new ResizeObserver(()=>this.schedule()); this.observer.observe(panel);
    this.outside = event => {
      if(this.splitMenus.contains(event.target)||this.slideouts?.contains(event.target))return;
      if(this.popup && !this.popup.tools.contains(event.target) && !this.popup.button.contains(event.target))this.close();
      if(this.panel.classList.contains('ribbon-peek')&&!this.panel.contains(event.target)&&!event.target.closest('.ribbon-bar,.ribbon-flyout'))this.hide();
    };
    this.escape = event => { if(event.key==='Escape'&&!this.keytips?.active&&!event.defaultPrevented&&!event.isComposing&&!nativeRibbonInput(event.target)&&(this.popup||this.panel.classList.contains('ribbon-peek'))){event.preventDefault();event.stopPropagation();if(this.popup)this.close(true);else {this.hide();document.querySelector('#ribbon-tabs [aria-selected=true]')?.focus();}} };
    document.addEventListener('pointerdown',this.outside); document.addEventListener('keydown',this.escape,true);
    this.focusOutside=event=>{if(this.popup&&!this.popup.tools.contains(event.target)&&event.target!==this.popup.button&&!this.splitMenus.contains(event.target))this.close();};
    document.addEventListener('focusin',this.focusOutside);
    this.resize=()=>{this.close();this.hide();};window.addEventListener('resize',this.resize);
    this.choose=event=>{if(event.target.closest('button[data-command],button[data-action]'))this.hide();};
    panel.addEventListener('click',this.choose);
  }
  set(groups, context = {}) {
    const splitFocused=this.splitMenus?.contains(document.activeElement);
    this.splitMenus?.close();this.slideouts?.closeAll();this.close(); this.sourceGroups=groups;this.context=context;
    this.groups=this.floating?this.floating.reconcile(groups,context):groups;
    this.host?.sync();
    this.panel.replaceChildren(...this.groups);
    this.slideouts?.set([...groups,...[...this.floating?.frames.values()||[]].map(frame=>frame.group)],context);
    this.splitMenus?.set([...groups,...[...this.floating?.frames.values()||[]].map(frame=>frame.group)]);
    if(this.floating?.focusLost||splitFocused)document.querySelector('#ribbon-tabs [aria-selected=true]')?.focus({preventScroll:true});
    groups.forEach(group=>{
      this.boundGroups ||= new WeakSet(); if(this.boundGroups.has(group))return;this.boundGroups.add(group);
      const tools=group.querySelector('.ribbon-tools'),launcher=group.querySelector('.ribbon-launcher');
      const canFloat=this.floating&&!protectedRibbonPanel(context.tab,group.dataset.ribbonPanel||group.dataset.panel);
      if(tools){if(!tools.getAttribute('id'))tools.setAttribute('id',`ribbon-tools-${++panelSerial}`);tools.setAttribute('role','toolbar');tools.setAttribute('aria-label',`${group.dataset.panel} commands`);launcher.setAttribute('aria-controls',tools.getAttribute('id'));}
      launcher.setAttribute('aria-haspopup','false');launcher.setAttribute('aria-keyshortcuts',canFloat?'ArrowDown Alt+F':'ArrowDown');
      launcher.title=`${group.dataset.panel} tools (Down opens${canFloat?', Alt+F floats panel':''})`;
      group.querySelector('.ribbon-launcher').addEventListener('click',()=>this.activate(group));
      group.querySelector('.ribbon-launcher').addEventListener('pointerenter',event=>{if(this.mode==='titles'&&event.pointerType==='mouse'&&this.popup?.group!==group)this.open(group,false);});
      group.querySelector('.ribbon-launcher').addEventListener('keydown',event=>{
        if(event.defaultPrevented||event.isComposing||event.ctrlKey||event.metaKey)return;
        if(event.altKey&&!event.shiftKey&&event.key.toLowerCase()==='f'&&canFloat){event.preventDefault();event.stopPropagation();this.floatPanel(group,event);return;}
        if(event.altKey||event.shiftKey)return;
        if(event.key!=='ArrowDown')return;event.preventDefault();event.stopPropagation();this.open(group,true);
      });
      if(canFloat){
        const title=group.querySelector('.ribbon-group-title');
        const float=document.createElement('button');float.type='button';float.className='ribbon-tear-button';float.textContent='↗';
        float.setAttribute('aria-label',`Float ${group.dataset.panel} panel`);float.title=`Float ${group.dataset.panel} panel`;
        float.addEventListener('click',event=>{event.preventDefault();event.stopPropagation();this.floatPanel(group,event);});
        title?.append(float);
        title?.addEventListener('pointerdown',event=>{
          if(event.target.closest('button'))return;this.close();
          this.floating.beginGesture(event,group,{tab:context.tab,panel:group.dataset.ribbonPanel||group.dataset.panel});
        });
      }
    });
    this.keyboard?.refresh();this.keytips?.render();
    this.schedule();
  }
  panelButtons(){return [...this.panel.querySelectorAll('button')].filter(button=>{
    const group=button.closest('.ribbon-group');if(!group)return false;
    return group.classList.contains('collapsed')?button.classList.contains('ribbon-launcher'):!button.classList.contains('ribbon-launcher');
  });}
  containsFocus(){return this.panel.contains(document.activeElement)||!!this.popup?.tools.contains(document.activeElement)||!!this.splitMenus?.contains(document.activeElement)||!!this.slideouts?.contains(document.activeElement)||!!this.floating?.containsFocus();}
  setLockMask(value){this.lockMask=Number(value)&15;this.host?.setLockMask(this.lockMask);this.floating?.setLockMask?.(this.lockMask);}
  setCommandAccess(api){this.commandAccess=api;}
  floatPanel(group,event){this.close();this.floating?.floatPanel(group,this.context.tab,event);}
  setMode(mode) {this.slideouts?.closeAll();this.close();this.hide();this.mode=ribbonModes.includes(mode)?mode:'full';this.panel.dataset.mode=this.mode;this.schedule();}
  reveal(focus=false) {
    if(this.mode==='tabs'){
      const bounds=document.querySelector('.ribbon-bar').getBoundingClientRect();
      this.panel.style.top=`${bounds.bottom}px`;this.panel.classList.add('ribbon-peek');this.schedule();
    }
    if(focus)requestAnimationFrame(()=>{this.layout();this.keyboard?.focus();});
  }
  hide() {this.splitMenus?.close();if(this.mode==='tabs')this.slideouts?.closeAll();else this.slideouts?.closeUnpinned();this.panel.classList.remove('ribbon-peek');this.panel.style.removeProperty('top');this.close();}
  suspend(){this.keytips?.close();this.contextMenu?.close();this.slideouts?.closeAll();this.hide();}
  schedule() { if(!this.frame)this.frame=requestAnimationFrame(()=>{this.frame=0;this.layout();}); }
  layout() {
    if(!this.panel.clientWidth)return;
    const focused=document.activeElement,focusGroup=this.panel.contains(focused)?focused.closest('.ribbon-group'):null;
    this.close(); this.groups.forEach(group=>group.classList.remove('collapsed','ribbon-wrap'));
    if(this.mode==='buttons'||this.mode==='titles')this.groups.forEach(group=>group.classList.add('collapsed'));
    else {
      const widths=this.groups.map(group=>group.getBoundingClientRect().width);
      const collapsed=this.host?.element?.dataset.axis==='vertical'?widths.map(()=>false):collapsedPanels(widths,this.panel.clientWidth-8);
      this.groups.forEach((group,index)=>group.classList.toggle('collapsed',collapsed[index]));
      if(this.host?.element?.dataset.axis==='vertical')this.groups.forEach(group=>{const tools=group.querySelector('.ribbon-tools');if(tools&&tools.scrollWidth>tools.clientWidth+2)group.classList.add('ribbon-wrap');});
    }
    this.keyboard?.refresh();
    this.slideouts?.refresh();
    if(focusGroup&&!visibleRibbonControl(focused)){
      const target=focusGroup.classList.contains('collapsed')?focusGroup.querySelector('.ribbon-launcher'):
        [...focusGroup.querySelectorAll('.ribbon-tools button,.ribbon-tools select,.ribbon-tools input')].find(visibleRibbonControl);
      if(visibleRibbonControl(target))target.focus({preventScroll:true});
    }
  }
  activate(group) {
    // Mouse hover precedes click. Promote that preview instead of closing it.
    if(this.popup?.group===group&&!this.popup.preview){this.close(true);return;}
    this.open(group,true);
  }
  focusTools() {if(!this.popup?.keyboard?.focus())[...this.popup?.tools.querySelectorAll('select,input')||[]].find(visibleRibbonControl)?.focus({preventScroll:true});}
  open(group, focus=true) {
    if(this.popup?.group===group){if(focus){this.popup.preview=false;this.focusTools();}return;}
    this.close();
    const tools=group.querySelector('.ribbon-tools'),button=group.querySelector('.ribbon-launcher');
    if(!tools)return;
    const marker=document.createComment('ribbon-panel');tools.before(marker);document.body.append(tools);
    this.slideouts?.expose(group,tools,true);
    tools.classList.add('ribbon-flyout');tools.setAttribute('role','toolbar');tools.setAttribute('aria-label',`${group.dataset.panel} commands`);
    this.splitMenus?.expose(tools,true);
    const bounds=button.getBoundingClientRect();
    tools.style.left='4px';tools.style.top=`${bounds.bottom+3}px`;
    const position=flyoutPosition(bounds,tools.getBoundingClientRect(),{width:innerWidth,height:innerHeight});
    tools.style.left=`${position.left}px`;tools.style.top=`${position.top}px`;
    button.setAttribute('aria-expanded','true');this.popup={group,tools,button,marker,preview:!focus};
    this.onChoose=event=>{if(event.target.closest('button[data-command],button[data-action]'))this.hide();};tools.addEventListener('click',this.onChoose);
    if(this.floating&&!protectedRibbonPanel(this.context.tab,group.dataset.ribbonPanel||group.dataset.panel)){
      const float=document.createElement('button');float.type='button';float.textContent=`Float ${group.dataset.panel} panel`;float.className='ribbon-flyout-float';
      float.addEventListener('click',event=>{event.preventDefault();event.stopPropagation();this.floatPanel(group,event);});
      tools.append(float);this.popup.floatButton=float;
    }
    this.keyboard?.refresh();
    this.popup.keyboard=bindRovingToolbar(tools,{onEscape:()=>this.close(true)});
    if(focus)this.focusTools();
  }
  close(focus=false) {
    const popup=this.popup;if(!popup)return;this.popup=null;
    popup.keyboard?.dispose();
    this.slideouts?.expose(popup.group,null,false);
    this.splitMenus?.expose(popup.tools,false);
    popup.floatButton?.remove();
    popup.tools.removeEventListener('click',this.onChoose);popup.tools.classList.remove('ribbon-flyout');
    popup.tools.style.removeProperty('left');popup.tools.style.removeProperty('top');popup.marker.replaceWith(popup.tools);
    popup.button.setAttribute('aria-expanded','false');this.keyboard?.refresh();if(focus)popup.button.focus({preventScroll:true});
  }
  dispose() {this.hide();this.slideouts?.dispose();this.keytips?.dispose();this.contextMenu?.dispose();this.splitMenus?.dispose();this.keyboard?.dispose();this.floating?.dispose();this.host?.dispose();this.observer.disconnect();cancelAnimationFrame(this.frame);document.removeEventListener('pointerdown',this.outside);document.removeEventListener('keydown',this.escape,true);document.removeEventListener('focusin',this.focusOutside);window.removeEventListener('resize',this.resize);this.panel.removeEventListener('click',this.choose);}
}
