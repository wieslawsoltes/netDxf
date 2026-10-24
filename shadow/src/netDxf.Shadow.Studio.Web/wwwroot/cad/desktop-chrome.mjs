import { el } from './ui.mjs';
import { commandAvailability } from './command-catalog.mjs';
import { bindRovingToolbar } from './ribbon-keyboard.mjs';
import { interfaceLocks, interfaceLockMask } from './workspace-shell-model.mjs';

const menus={
  File:['action:new','action:open','action:save','action:save-as','action:save-all','action:export-png','action:export-svg'],
  Edit:['action:undo','action:redo','command:COPY','command:ERASE'],
  View:['action:fit','action:pan','action:ribbon-show','action:ribbon-hide','action:file-tabs','action:layout-tabs','action:command-line','action:command-text-window','action:palette-picker','action:clean'],
  Insert:['action:block-insert','action:block-library-import','action:attribute-create'],
  Format:['action:layer-manager','action:layer-states','action:linetypes','action:text-styles','action:annotation'],
  Tools:['action:command-search','action:ui-workspaces','action:ribbon-customize','action:quick-access-customize','action:interface-lock','action:settings'],
  Draw:['command:LINE','command:PLINE','command:CIRCLE','command:ARC','command:ELLIPSE','command:RECTANG','command:POLYGON','command:SPLINE','action:hatch-create'],
  Dimension:['command:DIMLINEAR','command:DIMALIGNED','command:DIMRADIUS','command:DIMDIAMETER','command:DIMANGULAR'],
  Modify:['command:MOVE','command:ROTATE','command:SCALE','command:MIRROR','command:OFFSET','command:TRIM','command:EXTEND','command:FILLET','command:CHAMFER','command:EXPLODE'],
  Window:['action:file-tabs','action:layout-tabs','action:close-others','action:close-all','action:ui-workspaces'],
  Help:['action:help','action:command-search']
};

/** Optional desktop chrome uses the same catalog and retained editor as the ribbon. */
export class DesktopChrome {
  constructor({shell,quickAccess,lockChanged=()=>{},icons}) {
    Object.assign(this,{shell,quickAccess,lockChanged,icons});this.lastLock=null;
    const toolbar=document.getElementById('quick-access');this.toolbar=toolbar;
    this.quickMarker=document.createComment('quick-access-above');toolbar.before(this.quickMarker);
    this.quickRow=document.getElementById('quick-access-below')||el('div',{id:'quick-access-below',hidden:true});
    if(!this.quickRow.isConnected)document.querySelector('.document-bar').before(this.quickRow);
    this.quickRow.classList.add('quick-access-below');
    quickAccess?.setMenus({customize:anchor=>this.quickMenu(anchor),overflow:anchor=>this.quickMenu(anchor,true)});
    shell.listen(toolbar,'contextmenu',event=>{event.preventDefault();event.stopPropagation();this.quickMenu(event.target.closest('button')||toolbar,false,event.target.closest('[data-command-id]')?.dataset.commandId);});
    this.menuBar=el('nav',{id:'menu-bar',class:'shell-menubar',role:'menubar','aria-label':'Application menu bar'},Object.keys(menus).map(name=>{
      const button=el('button',{type:'button',role:'menuitem','aria-haspopup':'menu','data-menu-name':name,text:name});
      button.addEventListener('click',event=>{event.stopPropagation();this.showMenu(button,name);});
      button.addEventListener('keydown',event=>{if(event.key==='ArrowDown'){event.preventDefault();event.stopPropagation();this.showMenu(button,name);}});
      button.addEventListener('pointerenter',()=>{if(shell.popup?.anchor.closest?.('#menu-bar')&&shell.popup.anchor!==button)this.showMenu(button,name);});
      return button;
    }));
    document.querySelector('.titlebar').after(this.menuBar);this.menuKeys=bindRovingToolbar(this.menuBar,{orientation:'horizontal'});
    shell.listen(document,'keydown',event=>{
      if(shell.popup?.anchor.closest?.('#menu-bar')&&['ArrowLeft','ArrowRight'].includes(event.key)&&!event.ctrlKey&&!event.altKey){
        event.preventDefault();event.stopPropagation();const buttons=[...this.menuBar.children],index=buttons.indexOf(shell.popup.anchor),next=buttons[(index+(event.key==='ArrowRight'?1:-1)+buttons.length)%buttons.length];this.showMenu(next,next.dataset.menuName);
      }
    },true);
    this.lockButton=el('button',{id:'interface-lock',type:'button',title:'Lock user interface — right-click for categories','aria-label':'Lock user interface','aria-pressed':'false'},[icons('lock')]);
    document.getElementById('status-workspaces').before(this.lockButton);
    shell.listen(this.lockButton,'click',()=>this.setLock(this.shell.preferences.lockUi?0:15));
    shell.listen(this.lockButton,'contextmenu',event=>{event.preventDefault();event.stopPropagation();this.lockMenu(this.lockButton);});
    shell.listen(this.lockButton,'keydown',event=>{if(event.key==='ArrowDown'){event.preventDefault();event.stopPropagation();this.lockMenu(this.lockButton);}});
    for(const id of ['command-grip','command-dock-toggle','command-history-toggle']){
      const node=document.getElementById(id);
      shell.listen(node,'contextmenu',event=>{event.preventDefault();event.stopPropagation();this.commandMenu(node);});
      shell.listen(node,'keydown',event=>{if(event.key==='ContextMenu'||event.shiftKey&&event.key==='F10'){event.preventDefault();event.stopPropagation();this.commandMenu(node);}});
    }
    this.apply();
  }
  set(key,value){this.shell.preferenceChanged?.(key,value);this.shell.preferences[key]=value;this.apply();this.shell.persist();}
  setLock(value){this.set('lockUi',interfaceLockMask(value));}
  apply(){
    const prefs=this.shell.preferences,studio=document.getElementById('studio');
    this.menuBar.hidden=!prefs.menuBarVisible;
    studio.classList.toggle('file-tabs-hidden',!prefs.fileTabsVisible);studio.classList.toggle('layout-tabs-hidden',!prefs.layoutTabsVisible);studio.classList.toggle('ribbon-hidden',!prefs.ribbonVisible);
    if(!prefs.ribbonVisible)this.shell.suspendTransient();
    const target=prefs.quickAccessBelow?this.quickRow:this.quickMarker.parentNode;
    if(this.toolbar.parentNode!==target){
      const active=this.toolbar.contains(document.activeElement)?document.activeElement:null;
      if(prefs.quickAccessBelow)target.append(this.toolbar);else this.quickMarker.after(this.toolbar);
      active?.focus({preventScroll:true});
    }
    this.quickRow.hidden=!prefs.quickAccessBelow;this.quickAccess?.refresh();
    this.lockButton.setAttribute('aria-pressed',String(!!prefs.lockUi));
    this.lockButton.title=`${prefs.lockUi?'Interface layout locked':'Interface layout unlocked'} — right-click for categories. Hold Ctrl to move temporarily.`;
    this.shell.commandDock.setLockMask(prefs.lockUi);
    if(this.lastLock!==prefs.lockUi){this.lastLock=prefs.lockUi;this.lockChanged(prefs.lockUi);}
    this.menuKeys?.refresh();
  }
  entry(id){
    const entry=this.shell.catalog().find(value=>value.id===id);if(!entry)return null;
    return this.shell.item(entry.label,()=>this.quickAccess.execute(id),{shortcut:entry.shortcut,disabled:!!commandAvailability(entry,this.shell.state())});
  }
  showMenu(anchor,name){
    const items=menus[name].map(id=>this.entry(id)).filter(Boolean);
    if(name==='Window')items.push(el('hr'),...this.shell.documents().map(doc=>this.shell.item(doc.name,()=>this.shell.activate(doc.id),{checked:doc.id===this.shell.state().documentId,role:'menuitemradio'})));
    this.shell.openPopup(anchor,`${name} menu`,items);
  }
  quickMenu(anchor,overflow=false,currentId){
    const shell=this.shell,entries=shell.catalog(),pinned=this.quickAccess.snapshot().pinned;
    const items=[el('h3',{text:overflow?'More Quick Access commands':'Customize Quick Access'})];
    if(currentId){
      const entry=entries.find(value=>value.id===currentId);
      items.push(shell.item(`Remove ${entry?.label||'command'} from Quick Access`,()=>this.quickAccess.unpin(currentId)),el('hr'));
    }
    if(overflow)items.push(...this.quickAccess.overflow().map(id=>this.entry(id)).filter(Boolean));
    else for(const id of ['action:new','action:open','action:save','action:save-as','action:undo','action:redo','action:fit','action:properties']){
      const entry=entries.find(value=>value.id===id);if(entry)items.push(shell.item(entry.label,()=>pinned.includes(id)?this.quickAccess.unpin(id):this.quickAccess.pin(id),{checked:pinned.includes(id)}));
    }
    items.push(el('hr'),shell.item('More commands…',()=>this.quickAccess.customize()),
      shell.item(shell.preferences.quickAccessBelow?'Show Quick Access above ribbon':'Show Quick Access below ribbon',()=>this.set('quickAccessBelow',!shell.preferences.quickAccessBelow)),
      shell.item('Show menu bar',()=>this.set('menuBarVisible',!shell.preferences.menuBarVisible),{checked:shell.preferences.menuBarVisible}));
    shell.openPopup(anchor,overflow?'More Quick Access commands':'Customize Quick Access',items);
  }
  lockMenu(anchor){
    this.shell.openPopup(anchor,'Lock user interface',[el('h3',{text:'Lock location and size'}),
      ...interfaceLocks.map(({bit,label})=>this.shell.item(label,()=>this.setLock(this.shell.preferences.lockUi^bit),{checked:!!(this.shell.preferences.lockUi&bit)})),
      el('hr'),this.shell.item('Lock all',()=>this.setLock(15)),this.shell.item('Unlock all',()=>this.setLock(0)),
      el('p',{class:'hint',text:'Hold Ctrl to move or resize a locked element temporarily. Opening and closing remain available.'})]);
  }
  commandMenu(anchor){
    const dock=this.shell.commandDock,state=dock.snapshot(),locked=dock.isLocked();
    this.shell.openPopup(anchor,'Command window settings',[el('h3',{text:'Command window'}),
      ...[['dock','top','Dock at top'],['dock','bottom','Dock at bottom'],['anchor','top','Anchor at top'],['anchor','bottom','Anchor at bottom'],['float',state.dock,'Float command window']].map(([mode,side,label])=>this.shell.item(label,()=>dock.setMode(mode,side),{checked:state.mode===mode&&(mode==='float'||state.dock===side),role:'menuitemradio',disabled:locked||(mode!=='float'&&!state.allowDocking)})),
      el('hr'),this.shell.item('Allow docking',()=>dock.setAllowDocking(!state.allowDocking),{checked:state.allowDocking,disabled:locked}),
      this.shell.item('Transparency…',()=>this.opacityMenu(anchor)),this.shell.item('Command history window',()=>this.historyWindow(),{shortcut:'Ctrl+F2'}),
      this.shell.actionItem('command-line-hide','Hide command line',{shortcut:'Ctrl+9'})]);
  }
  opacityMenu(anchor){
    const dock=this.shell.commandDock,state=dock.snapshot();
    const rows=[['Inactive opacity','inactiveOpacity'],['Active opacity','activeOpacity']].map(([label,key])=>{
      const output=el('output',{text:`${Math.round(state[key]*100)}%`});
      const input=el('input',{type:'range',min:20,max:100,step:5,value:Math.round(state[key]*100),'aria-label':label});
      input.addEventListener('input',()=>{output.textContent=`${input.value}%`;const now=dock.snapshot();dock.setOpacity(key==='inactiveOpacity'?Number(input.value)/100:now.inactiveOpacity,key==='activeOpacity'?Number(input.value)/100:now.activeOpacity);});
      return el('label',{class:'shell-opacity-row'},[el('span',{text:label}),input,output]);
    });
    const popup=this.shell.openPopup(anchor,'Command window transparency',[el('h3',{text:'Command window transparency'}),...rows,el('p',{class:'hint',text:'Opacity applies when floating or anchored. Focus and pointer hover use the active value.'}),this.shell.item('Restore opacity',()=>dock.setOpacity(1,1))]);
    popup?.querySelector('input')?.focus();
  }
  historyWindow(){
    if(document.querySelector('dialog[open]'))return;
    this.shell.closePopup(true);this.shell.suspendTransient();
    const origin=document.activeElement,history=document.getElementById('history');
    const text=el('pre',{tabindex:0,'aria-label':'Command history',text:history.innerText});
    const close=el('button',{type:'button','aria-label':'Close command history window',text:'Close'});
    const dialog=el('dialog',{id:'command-text-window',class:'shell-text-window','aria-label':'Command history window'},[
      el('header',{},[el('strong',{text:'Command history'}),close]),text,
      el('footer',{},[el('span',{text:'Select text to copy · Escape closes'}),this.shell.item('Focus command line',()=>{finish();return this.shell.execute('command-line-show');})])]);
    const observer=new MutationObserver(()=>{const atEnd=text.scrollTop+text.clientHeight>=text.scrollHeight-8;text.textContent=history.innerText;if(atEnd)text.scrollTop=text.scrollHeight;});
    const finish=()=>{observer.disconnect();dialog.close();dialog.remove();if(origin?.isConnected)origin.focus({preventScroll:true});this.textWindow=null;};
    close.addEventListener('click',finish);dialog.addEventListener('cancel',event=>{event.preventDefault();finish();});
    document.body.append(dialog);dialog.showModal();observer.observe(history,{childList:true,subtree:true,characterData:true});text.focus();text.scrollTop=text.scrollHeight;
    this.textWindow={close:finish};
  }
  dispose(){this.textWindow?.close();this.menuKeys.dispose();this.menuBar.remove();this.lockButton.remove();this.quickAccess?.setMenus(null);}
}
