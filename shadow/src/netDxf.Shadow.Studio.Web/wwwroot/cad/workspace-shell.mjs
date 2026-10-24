import { el, icon } from './ui.mjs';
import { bindRovingToolbar } from './ribbon-keyboard.mjs';
import { commandAvailability } from './command-catalog.mjs';
import { CommandLineDock } from './command-line-docking.mjs';
import { DesktopChrome } from './desktop-chrome.mjs';
import { adjacentTab, draftingIndicators, shellPreferences, shellStorageKey } from './workspace-shell-model.mjs';

const paths={
  menu:'M4 6h16M4 12h16M4 18h16',chevron:'M8 10l4 4l4-4',close:'M6 6l12 12M6 18L18 6',lock:'M6 10h12v11H6zM8 10V6a4 4 0 0 1 8 0v4M12 14v3',
  plus:'M12 5v14M5 12h14',minus:'M5 12h14',search:'M16 16l5 5M18 10a8 8 0 1 1-16 0a8 8 0 1 1 16 0',
  window:'M3 4h18v16H3zM3 9h18M8 9v11',workspace:'M3 4h18v16H3zM3 8h18M8 8v12M16 8v12',
  clean:'M3 9V3h6M15 3h6v6M21 15v6h-6M9 21H3v-6',
  float:'M4 9v11h12M9 4h11v11H9zM13 7h4v4M17 7l-5 5',grip:'M8 4h1M15 4h1M8 9h1M15 9h1M8 14h1M15 14h1M8 19h1M15 19h1',
  resize:'M8 20l12-12M14 20l6-6M20 20h.01',command:'M4 7l5 5l-5 5M12 18h8',
  grid:'M4 4h16v16H4zM4 9h16M4 15h16M9 4v16M15 4v16',
  snap:'M4 4h1M12 4h1M20 4h1M4 12h1M12 12h1M20 12h1M4 20h1M12 20h1M20 20h1',
  ortho:'M5 3v17h16M5 14h6v6',polar:'M4 20L19 5M4 20h17M13 20a9 9 0 0 0-3-7M15 3h6v6',
  osnap:'M4 4h16v16H4zM8 12h8M12 8v8',tracking:'M3 18h15V3M4 9h5M9 4v5M18 18l3-3M18 18l-3 3',
  dynamic:'M3 6h18v12H3zM7 9v6M10 9h7M10 13h4',selectionCycling:'M3 3h12v12H3zM9 9h12v12H9z',
  layout:'M3 3h18v18H3zM6 6h12v9H6zM6 18h6M15 18h3',
  pan:'M12 2v20M2 12h20M8 6l4-4l4 4M8 18l4 4l4-4M6 8l-4 4l4 4M18 8l4 4l-4 4',
  settings:'M12 4a8 8 0 1 1 0 16a8 8 0 1 1 0-16M12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6M12 1v3M12 20v3M1 12h3M20 12h3',
  help:'M9 8a3 3 0 1 1 5 2c-2 1-2 2-2 4M12 18h.01M22 12a10 10 0 1 1-20 0a10 10 0 1 1 20 0'
};

export function shellIcon(name) {
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
  svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('class','icon shell-icon');svg.setAttribute('aria-hidden','true');
  const path=document.createElementNS(svg.namespaceURI,'path');path.setAttribute('d',paths[name]||paths.window);svg.append(path);return svg;
}
const workspaceLabel=name=>name==='Drafting'?'Drafting & Annotation':name;

/** Desktop shell binds to existing host commands; it never owns a drawing or editor transaction. */
export class StudioShell {
  constructor({ storage, report=()=>{}, execute, catalog, state, documents, activate, closeDocument,
    profiles, settings, toggle, persistDrafting, currentLayout, changeLayout, quickAccess, lockChanged, preferenceChanged, suspendTransient=()=>{}, resize=()=>{} }) {
    Object.assign(this,{storage,report,execute,catalog,state,documents,activate,closeDocument,profiles,settings,toggle,persistDrafting,currentLayout,changeLayout,preferenceChanged,suspendTransient});
    let saved;try{const text=storage?.getItem(shellStorageKey);if(text&&text.length<16384)saved=JSON.parse(text);}catch{/* Optional local preferences. */}
    this.preferences=shellPreferences(saved);this.popup=null;this.cleanups=[];
    this.commandDock=new CommandLineDock({element:document.getElementById('command-line'),splitter:document.getElementById('command-splitter'),
      workspace:document.getElementById('workspace'),drawingArea:document.getElementById('drawing-area'),initial:this.preferences.commandWindow,
      changed:value=>{this.preferences.commandWindow=value;this.persist();},resized:resize});
    const listen=(node,type,handler,options)=>{node.addEventListener(type,handler,options);this.cleanups.push(()=>node.removeEventListener(type,handler,options));};
    this.listen=listen;
    const bind=(id,callback)=>listen(document.getElementById(id),'click',event=>{event.stopPropagation();callback(event.currentTarget);});
    bind('file-menu',anchor=>this.applicationMenu(anchor));
    bind('workspace-switch',anchor=>this.workspaceMenu(anchor));
    bind('status-workspaces',anchor=>this.workspaceMenu(anchor));
    bind('drawing-list',anchor=>this.documentsMenu(anchor));
    bind('layout-list',anchor=>this.layoutsMenu(anchor));
    bind('status-customize',anchor=>this.statusMenu(anchor));
    bind('space-mode',anchor=>this.layoutsMenu(anchor));
    for(const node of document.querySelectorAll('[data-shell-icon]'))node.prepend(shellIcon(node.dataset.shellIcon));
    listen(document,'pointerdown',event=>{if(this.popup&&!this.popup.node.contains(event.target)&&!this.popup.anchor.contains(event.target))this.closePopup();});
    listen(document,'keydown',event=>{
      if(event.key==='Escape'&&this.popup){event.preventDefault();event.stopPropagation();this.closePopup(true);}
    },true);
    listen(window,'resize',()=>this.closePopup());
    listen(document.getElementById('documents'),'keydown',event=>this.tabKey(event,'document'));
    listen(document.getElementById('layouts'),'keydown',event=>this.tabKey(event,'layout'));
    listen(document.getElementById('documents'),'contextmenu',event=>{
      const node=event.target.closest('[data-document-tab]');if(!node)return;event.preventDefault();
      const doc=this.documents().find(value=>value.id===node.dataset.documentTab);if(doc)this.documentMenu(node.querySelector('.name'),doc);
    });
    this.chrome=new DesktopChrome({shell:this,quickAccess,lockChanged,icons:shellIcon});
    this.renderModes();this.refreshWorkspace();this.refreshLayout();
  }
  run(callback) {return Promise.resolve().then(callback).catch(this.report);}
  snapshot() {return shellPreferences({...this.preferences,commandWindow:this.commandDock.snapshot()});}
  restore(value,{preservePolicy=true}={}) {this.closePopup();const policy=this.preferences.workspaceAutoSave;this.preferences=shellPreferences(value);if(preservePolicy)this.preferences.workspaceAutoSave=policy;this.commandDock.restore(this.preferences.commandWindow);this.chrome?.apply();this.renderModes();this.refreshWorkspace();this.persist();}
  persist() {try{this.storage?.setItem(shellStorageKey,JSON.stringify(this.snapshot()));}catch{/* Preferences can still work without storage. */}}
  reset() {this.restore(null,{preservePolicy:false});}
  containsFocus() {return !!this.popup?.node.contains(document.activeElement);}
  setWorkspace(name,preset=true) {this.preferences.workspaceName=name;this.preferences.workspacePreset=preset;this.preferences.workspaceDetached=false;this.refreshWorkspace();this.persist();}
  workspaceChanged(change) {
    if(!this.preferences.workspaceDetached&&!this.preferences.workspacePreset&&this.preferences.workspaceName===change.name){
      if(change.type==='rename')this.setWorkspace(change.next,false);
      else{this.preferences.workspaceDetached=true;this.refreshWorkspace();this.persist();}
    }
  }
  refreshWorkspace() {const name=this.preferences.workspaceDetached?'Custom layout':this.preferences.workspacePreset?workspaceLabel(this.preferences.workspaceName):this.preferences.workspaceName;document.getElementById('workspace-name').textContent=name;
    document.getElementById('workspace-switch').title=`Workspace: ${name}`;document.getElementById('status-workspaces').title=`Switch workspace — ${name}`;}
  refreshLayout() {
    const name=this.currentLayout()?.name||'Model',button=document.getElementById('space-mode');button.textContent=name==='Model'?'MODEL':'PAPER';
    button.title=`Current space: ${name}. Choose a drawing layout.`;
    for(const root of [document.getElementById('layouts'),document.getElementById('documents')]){
      for(const node of root.querySelectorAll('[role=tab]')){
        node.tabIndex=node.getAttribute('aria-selected')==='true'?0:-1;node.setAttribute('aria-controls','viewport');
      }
    }
  }
  tabKey(event,kind) {
    if(event.defaultPrevented||event.ctrlKey||event.metaKey||event.altKey||event.shiftKey||!event.target.matches('[role=tab]'))return;
    const nodes=[...event.currentTarget.querySelectorAll('[role=tab]')].filter(node=>!node.disabled),index=nodes.indexOf(event.target);
    const next=adjacentTab(nodes,index<0?null:nodes[index],event.key);if(!next)return;
    event.preventDefault();event.stopPropagation();next.focus({preventScroll:true});next.scrollIntoView({block:'nearest',inline:'nearest'});
    // Automatic activation uses exactly the existing document/layout button handler.
    next.click();
  }
  closePopup(focus=false) {
    const value=this.popup;if(!value)return;this.popup=null;value.keyboard?.dispose();value.node.remove();value.anchor.setAttribute('aria-expanded','false');
    if(focus&&value.anchor.isConnected)value.anchor.focus({preventScroll:true});
  }
  openPopup(anchor,label,body,{application=false}={}) {
    if(this.popup?.anchor===anchor){this.closePopup(true);return null;}
    this.closePopup();
    const node=el('section',{class:`shell-popup${application?' shell-application':''}`,role:application?'dialog':'menu','aria-label':label,id:'shell-popup'},body);
    node.addEventListener('click',event=>event.stopPropagation());
    anchor.setAttribute('aria-controls',node.id);anchor.setAttribute('aria-expanded','true');anchor.setAttribute('aria-haspopup',application?'dialog':'menu');
    document.body.append(node);
    const box=anchor.getBoundingClientRect();
    const below=Math.max(0,innerHeight-box.bottom-7),above=Math.max(0,box.top-7);
    const openBelow=node.offsetHeight<=below||below>=above;
    node.style.maxHeight=`${Math.max(40,openBelow?below:above)}px`;
    node.style.left=`${Math.max(4,Math.min(box.left,innerWidth-node.offsetWidth-4))}px`;
    node.style.top=`${Math.max(4,Math.min(openBelow?box.bottom+3:box.top-node.offsetHeight-3,innerHeight-node.offsetHeight-4))}px`;
    const keyboard=bindRovingToolbar(node,{orientation:'vertical',onEscape:()=>this.closePopup(true),onTab:()=>this.closePopup(true)});
    this.popup={node,anchor,keyboard};keyboard.focus();return node;
  }
  item(label,callback,{glyph,shortcut,checked,disabled=false,detail,keepOpen=false,role}={}) {
    const button=el('button',{type:'button',class:'shell-menu-item',role:role||(checked===undefined?'menuitem':'menuitemcheckbox'),
      'aria-label':label,'aria-checked':checked===undefined?undefined:String(checked),disabled},[
      glyph?(paths[glyph]?shellIcon(glyph):icon(glyph)):el('span',{class:'shell-menu-check','aria-hidden':'true',text:checked?'✓':''}),
      el('span',{class:'shell-menu-text'},[el('span',{text:label}),detail?el('small',{text:detail}):null]),shortcut?el('kbd',{text:shortcut}):null
    ]);
    button.addEventListener('click',()=>{if(!keepOpen)this.closePopup(true);this.run(callback);});return button;
  }
  actionItem(name,label,options={}) {
    const entry=this.catalog().find(value=>value.id===`action:${name}`),reason=commandAvailability(entry,this.state());
    const button=this.item(label,()=>{const current=this.catalog().find(value=>value.id===`action:${name}`),error=commandAvailability(current,this.state());if(error)throw new Error(error);return this.execute(name);},{...options,disabled:!!reason});
    if(reason)button.title=reason;return button;
  }
  applicationMenu(anchor) {
    const docs=this.documents(),active=this.state().documentId;
    const actions=[['new','New drawing','Ctrl+N'],['open','Open DXF / DWG…','Ctrl+O'],['save','Save DXF','Ctrl+S'],['save-as','Save drawing as…',''],
      ['export-png','Export PNG',''],['export-svg','Export SVG','']].map(([name,label,shortcut])=>this.actionItem(name,label,{glyph:name.startsWith('export')?'output':name==='save-as'?'save':name,shortcut}));
    const documents=docs.map(doc=>this.item(doc.name,()=>this.activate(doc.id),{glyph:'layout',detail:doc.dirty?'Unsaved changes':'Open drawing',checked:doc.id===active}));
    this.openPopup(anchor,'Drawing menu',[
      el('header',{class:'shell-menu-heading'},[el('strong',{text:'Drawing'}),this.item('Search commands',()=>this.execute('command-search'),{glyph:'search',shortcut:'Ctrl+K'})]),
      el('div',{class:'shell-application-body'},[
        el('nav',{'aria-label':'Drawing file commands'},actions),
        el('section',{class:'shell-open-drawings'},[el('h3',{text:'Open drawings'}),...documents,...(!docs.length?[el('p',{text:'Open a drawing or create a new one.'})]:[])])
      ]),
      el('footer',{},[this.actionItem('demo','New sample drawing',{glyph:'new'}),this.actionItem('settings','Drafting settings',{glyph:'settings'}),this.actionItem('help','Help',{glyph:'help'})])
    ],{application:true});
  }
  documentsMenu(anchor) {
    const active=this.state().documentId;
    this.openPopup(anchor,'Open drawings',[el('h3',{text:'Open drawings'}),...this.documents().map(doc=>this.item(doc.name,()=>this.activate(doc.id),{
      checked:doc.id===active,detail:doc.dirty?'Unsaved changes':undefined,role:'menuitemradio'})),el('hr'),this.actionItem('new','New drawing',{glyph:'new',shortcut:'Ctrl+N'}),this.actionItem('open','Open drawing…',{glyph:'open',shortcut:'Ctrl+O'})]);
  }
  documentMenu(anchor,doc) {
    this.openPopup(anchor,`${doc.name} drawing actions`,[el('h3',{text:doc.name}),
      this.item('Activate drawing',()=>this.activate(doc.id),{glyph:'layout'}),
      this.item('Save drawing',async()=>{await this.activate(doc.id);await this.execute('save');},{glyph:'save'}),
      this.actionItem('save-all','Save all drawings',{glyph:'save'}),el('hr'),
      this.item('Close drawing',()=>this.closeDocument(doc),{glyph:'close'}),
      this.item('Close other drawings',async()=>{await this.activate(doc.id);return this.execute('close-others');}),
      this.actionItem('close-all','Close all drawings')]);
  }
  layoutsMenu(anchor) {
    const current=this.currentLayout();
    this.openPopup(anchor,'Drawing layouts',[el('h3',{text:'Model and paper layouts'}),...(current?.names||[]).map(name=>this.item(name,()=>this.changeLayout(name),{
      checked:current.name===name,disabled:!!current.disabled,role:'menuitemradio'}))]);
  }
  workspaceMenu(anchor) {
    const choices=this.profiles.list();
    const current=!this.preferences.workspaceDetached&&choices.find(entry=>entry.name===this.preferences.workspaceName&&!!entry.preset===this.preferences.workspacePreset);
    this.openPopup(anchor,'Switch workspace',[el('h3',{text:'Workspaces'}),...choices.map(entry=>this.item(entry.preset?workspaceLabel(entry.name):entry.name,()=>this.profiles.apply(entry),{
      checked:entry===current,role:'menuitemradio',detail:entry.preset?'Preset':'Saved workspace'})),el('hr'),
      this.item('Save current as…',()=>this.execute('ui-workspaces'),{glyph:'save'}),
      this.item('Manage UI workspaces…',()=>this.execute('ui-workspaces'),{glyph:'workspace'}),
      this.item('Automatically save workspace changes',()=>{this.preferences.workspaceAutoSave=!this.preferences.workspaceAutoSave;this.persist();},{checked:this.preferences.workspaceAutoSave,detail:'Save the current named workspace when switching'}),
      this.item(current?'Reset current workspace':'Reset to Drafting & Annotation',()=>this.profiles.apply(current||{name:'Drafting',preset:true},{reset:true}),{glyph:'undo'})]);
  }
  renderModes() {
    const root=document.getElementById('drafting-modes'),focused=document.activeElement?.closest('[data-mode-key]')?.dataset.modeKey;
    root.replaceChildren(...draftingIndicators.filter(entry=>this.preferences.indicators.includes(entry.key)).map(entry=>{
      const button=el('button',{type:'button','data-mode-key':entry.key,'aria-label':entry.label,'aria-pressed':String(!!this.settings()[entry.key]),
        title:`${entry.name} (${entry.shortcut}) — ${this.settings()[entry.key]?'On':'Off'}. Right-click for settings.`,class:'drafting-indicator'},[
        shellIcon(entry.key),el('span',{class:'drafting-label',text:entry.label})]);
      button.addEventListener('click',()=>this.toggle(entry.key));
      button.addEventListener('contextmenu',event=>{event.preventDefault();event.stopPropagation();this.draftingMenu(button,entry);});
      button.addEventListener('keydown',event=>{if(event.key==='ArrowDown'&&!event.ctrlKey&&!event.metaKey){event.preventDefault();event.stopPropagation();this.draftingMenu(button,entry);}});
      return button;
    }));
    root.classList.toggle('status-labels',this.preferences.statusLabels);
    document.getElementById('coordinates').hidden=!this.preferences.coordinates;
    if(focused)root.querySelector(`[data-mode-key="${focused}"]`)?.focus({preventScroll:true});
  }
  draftingMenu(anchor,entry) {
    const items=[el('h3',{text:entry.name}),this.item(`Enable ${entry.name.toLowerCase()}`,()=>this.toggle(entry.key),{checked:!!this.settings()[entry.key]})];
    if(entry.key==='osnap')for(const [bit,name]of [[1,'Endpoint'],[2,'Midpoint'],[4,'Center'],[8,'Quadrant'],[16,'Intersection'],[32,'Perpendicular'],[64,'Nearest'],[128,'Insertion'],[256,'Tangent']]){
      const item=this.item(name,()=>{this.settings().snapModes^=bit;this.persistDrafting();item.setAttribute('aria-checked',String(!!(this.settings().snapModes&bit)));item.querySelector('.shell-menu-check').textContent=(this.settings().snapModes&bit)?'✓':'';},{checked:!!(this.settings().snapModes&bit),keepOpen:true});items.push(item);
    }
    items.push(el('hr'),this.actionItem('settings','Drafting settings…',{glyph:'settings'}));this.openPopup(anchor,`${entry.name} settings`,items);
  }
  statusMenu(anchor) {
    const update=(key,value)=>{this.preferences[key]=value;this.renderModes();this.persist();};
    const checkbox=(label,checked,callback)=>{
      const item=this.item(label,()=>{const next=item.getAttribute('aria-checked')!=='true';item.setAttribute('aria-checked',String(next));item.querySelector('.shell-menu-check').textContent=next?'✓':'';callback(next);},{checked,keepOpen:true});return item;
    };
    this.openPopup(anchor,'Customize status bar',[el('h3',{text:'Customize status bar'}),
      checkbox('Coordinates',this.preferences.coordinates,value=>update('coordinates',value)),
      ...draftingIndicators.map(entry=>checkbox(entry.name,this.preferences.indicators.includes(entry.key),value=>update('indicators',value?[...this.preferences.indicators,entry.key]:this.preferences.indicators.filter(key=>key!==entry.key)))),
      el('hr'),checkbox('Show text labels',this.preferences.statusLabels,value=>update('statusLabels',value)),
      this.item('Restore status defaults',()=>{const defaults=shellPreferences();this.preferences={...this.preferences,indicators:defaults.indicators,coordinates:true,statusLabels:false};this.renderModes();this.persist();})]);
  }
  dispose() {this.closePopup();this.chrome.dispose();this.commandDock.dispose();for(const cleanup of this.cleanups)cleanup();this.cleanups=[];}
}
