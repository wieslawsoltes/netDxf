import { el, icon } from './ui.mjs';
import { commandAvailability, resolveCommand, searchCommands, changeQuickAccess, quickAccessPreferences,
  quickAccessCapacity, quickAccessKey, quickAccessLimit } from './command-catalog.mjs';
import { bindRovingToolbar } from './ribbon-keyboard.mjs';

let nextInstance = 0;
const button = (label, callback, children = []) => el('button', {type:'button','aria-label':label,title:label,
  onclick:event=>{event.preventDefault();event.stopPropagation();callback(event.currentTarget,event);}}, children.length?children:[label]);

/** Reusable local command discovery. The host alone owns command execution and native transactions. */
export function createCommandAccess({ toolbar, catalog, state, execute, report = () => {}, storage, host = document.body, loadStyles = true }) {
  if (!toolbar || typeof catalog!=='function' || typeof state!=='function' || typeof execute!=='function')
    throw new Error('Command Access requires a toolbar, catalog, state, and execution callback.');
  const prefix=`command-access-${++nextInstance}`;
  let preferences, available=[], signature='', toolbarSignature='', resultSignature='', mode='search', origin=null, disposed=false;
  let controls=[], rows=[], executing=false, focusAfterRender=null, menus=null;
  try { storage = storage===undefined?globalThis.localStorage:storage; } catch { storage=null; }
  try { const value=storage?.getItem(quickAccessKey);preferences=quickAccessPreferences(value && value.length<=16384?JSON.parse(value):null); }
  catch { preferences=quickAccessPreferences(null); }
  if (loadStyles && !document.querySelector('link[data-command-access]'))
    document.head.append(el('link',{rel:'stylesheet',href:new URL('./command-access.css',import.meta.url).href,'data-command-access':''}));
  toolbar.classList.add('command-quick-access');
  toolbar.setAttribute('role','toolbar');toolbar.setAttribute('aria-label','Quick Access');
  toolbar.setAttribute('aria-orientation','horizontal');
  const toolbarKeyboard=bindRovingToolbar(toolbar,{orientation:'horizontal'});
  const title=el('h2',{id:`${prefix}-title`,text:'Search commands'});
  const input=el('input',{id:`${prefix}-query`,type:'search','aria-label':'Search commands',placeholder:'Search names, aliases, tools or disciplines',maxlength:256,autocomplete:'off',spellcheck:'false'});
  const status=el('p',{class:'command-access-status',role:'status','aria-live':'polite'});
  const results=el('div',{id:`${prefix}-results`,class:'command-access-results',role:'region','aria-label':'Command search results'});
  input.setAttribute('aria-controls',results.id || `${prefix}-results`);
  const pinned=el('div',{class:'command-access-pins','aria-label':'Pinned Quick Access commands'});
  const closeButton=button('Close command search',()=>close(),['×']);
  const searchTab=button('Search commands',()=>showMode('search'),['Search']);
  const customizeTab=button('Customize Quick Access',()=>showMode('customize'),['Quick Access']);
  const tabs=el('div',{class:'command-access-tabs'},[searchTab,customizeTab]);
  const dialog=el('dialog',{id:prefix,class:'command-access-dialog','aria-labelledby':`${prefix}-title`},[
    el('header',{},[title,closeButton]),tabs,input,status,pinned,results,
    el('footer',{},[el('span',{text:'↑ ↓ Choose · Enter Run · Escape Close'}),button('Reset Quick Access',()=>change({type:'reset'}),['Reset Quick Access'])])
  ]);
  host.append(dialog);

  function notify(error) { status.textContent=error.message || String(error);report(error); }
  function save(value) {
    try { if(!storage)throw new Error();storage.setItem(quickAccessKey,JSON.stringify(value)); }
    catch { throw new Error('Quick Access could not be saved. Browser storage is unavailable or full.'); }
  }
  function change(action, focusId) {
    try {
      const next=changeQuickAccess(preferences,action,available);save(next);preferences=next;
      focusAfterRender=focusId || null;refresh(true);
      status.textContent=`${preferences.pinned.length} of ${quickAccessLimit} Quick Access commands pinned.`;
    } catch(error) { notify(error); }
  }
  function entryFor(id) { return available.find(entry=>entry.id===id); }
  function labelFor(id) { return entryFor(id)?.label || `Unavailable command (${id})`; }
  function close(restoreFocus = true) {
    if (!dialog.open) return;
    dialog.close();
    if (restoreFocus && origin?.isConnected) origin.focus({preventScroll:true});
  }
  function showMode(next) {
    mode=next;title.textContent=next==='customize'?'Customize Quick Access':'Search commands';
    searchTab.setAttribute('aria-pressed',String(next==='search'));
    customizeTab.setAttribute('aria-pressed',String(next==='customize'));
    pinned.hidden=next!=='customize';
    resultSignature='';renderResults(true);input.focus({preventScroll:true});
  }
  function open(next, query='') {
    if (disposed) return;
    const other=document.querySelector('dialog[open]');
    if (other && other!==dialog) return;
    if (!dialog.open) origin=document.activeElement;
    input.value=typeof query==='string'?query.slice(0,256):'';
    refresh();showMode(next);
    if (!dialog.open) dialog.showModal();
    input.focus({preventScroll:true});
  }
  async function activate(id) {
    if (executing || disposed) return;
    try {
      // Resolve again after pointer/key activation: a disabled module or stale
      // toolbar node must never dispatch metadata captured by an earlier render.
      const entry=resolveCommand(id,catalog(),state());
      executing=true;close();
      await execute(entry);
    } catch(error) { notify(error); }
    finally { executing=false;if(!disposed)refresh(); }
  }
  function updateControl(control, id) {
    const entry=entryFor(id),reason=commandAvailability(entry,state()),label=labelFor(id);
    control.disabled=!!reason;
    control.setAttribute('title',`${label}${entry?.shortcut?` (${entry.shortcut})`:''}${reason?` — ${reason}`:''}`);
    control.setAttribute('aria-label',label);
  }
  function renderToolbar(force = false) {
    const width=toolbar.clientWidth || 244, capacity=quickAccessCapacity(width);
    const next=JSON.stringify([signature,preferences.pinned,capacity]);
    if (force || next!==toolbarSignature) {
      const focused=document.activeElement;
      const focusedId=controls.find(value=>value.button===focused)?.id;
      const focusedKind=focused?.getAttribute?.('data-access-control');
      const replaceOrigin=dialog.open && toolbar.contains(origin);
      const originId=replaceOrigin?origin.getAttribute('data-command-id'):null;
      const originKind=replaceOrigin?origin.getAttribute('data-access-control'):null;
      toolbarSignature=next;controls=[];
      const nodes=preferences.pinned.slice(0,capacity).map(id=>{
        const entry=entryFor(id),control=button(labelFor(id),()=>void activate(id),[icon(entry?.target || 'settings')]);
        if(entry?.kind==='action')control.setAttribute('data-action',entry.target);
        else if(entry?.kind==='command')control.setAttribute('data-command',entry.target);
        control.setAttribute('data-command-id',id);controls.push({id,button:control});return control;
      });
      if (preferences.pinned.length>capacity) {
        const overflow=button('More Quick Access commands',anchor=>menus?.overflow?menus.overflow(anchor):open('customize'),['»']);
        overflow.setAttribute('data-access-control','overflow');nodes.push(overflow);
      }
      const searchButton=button('Search commands (Ctrl+K)',()=>open('search'),['⌕']);
      searchButton.setAttribute('data-access-control','search');searchButton.setAttribute('aria-keyshortcuts','Control+K Meta+K');
      const customizeButton=button('Customize Quick Access',anchor=>menus?.customize?menus.customize(anchor):open('customize'),['▾']);
      customizeButton.setAttribute('data-access-control','customize');nodes.push(searchButton,customizeButton);
      for(const node of nodes.filter(node=>['overflow','customize'].includes(node.dataset.accessControl))){
        node.setAttribute('aria-haspopup','menu');
        node.addEventListener('keydown',event=>{if(event.key==='ArrowDown'&&!event.ctrlKey&&!event.metaKey&&!event.altKey){event.preventDefault();event.stopPropagation();node.click();}});
      }
      toolbar.replaceChildren(...nodes);
      const replacement=(id,kind)=>controls.find(value=>value.id===id)?.button ||
        (kind?nodes.find(node=>node.getAttribute('data-access-control')===kind):null) || searchButton;
      if (replaceOrigin) origin=replacement(originId,originKind);
      if (focusedId || focusedKind) {
        const target=replacement(focusedId,focusedKind);
        target.focus({preventScroll:true});
      }
    }
    for (const control of controls) updateControl(control.button,control.id);
    toolbarKeyboard.refresh();
  }
  function pinButton(entry) {
    const isPinned=preferences.pinned.includes(entry.id);
    const control=button(`${isPinned?'Unpin':'Pin'} ${entry.label}`,()=>{
      const type=preferences.pinned.includes(entry.id)?'remove':'pin';change({type,id:entry.id},`pin:${entry.id}`);
    },[isPinned?'★':'☆']);
    control.setAttribute('aria-pressed',String(isPinned));control.setAttribute('data-focus-id',`pin:${entry.id}`);
    return control;
  }
  function renderPinned() {
    const items=preferences.pinned.map((id,index)=>{
      const label=labelFor(id),run=button(label,()=>void activate(id),[label]);
      run.classList.add('command-access-run');run.setAttribute('data-focus-id',`run:${id}`);updateControl(run,id);
      const move=(offset,direction)=>button(`Move ${label} ${direction}`,()=>change({type:'move',id,offset},`${direction}:${id}`),[offset<0?'↑':'↓']);
      const up=move(-1,'earlier'),down=move(1,'later');up.disabled=index===0;down.disabled=index===preferences.pinned.length-1;
      up.setAttribute('data-focus-id',`earlier:${id}`);down.setAttribute('data-focus-id',`later:${id}`);
      const remove=button(`Remove ${label} from Quick Access`,()=>change({type:'remove',id},'query'),['×']);
      const reason=el('small',{text:commandAvailability(entryFor(id),state())});
      rows.push({id,run,reason,section:'pinned'});
      return el('div',{class:'command-access-pin-row','data-command-id':id},[run,up,down,remove,reason]);
    });
    pinned.replaceChildren(el('h3',{text:`Pinned commands (${preferences.pinned.length}/${quickAccessLimit})`}),
      ...(items.length?items:[el('p',{text:'No pinned commands. Pin a search result below.'})]));
  }
  function renderResults(force = false) {
    if (!dialog.open && !force) return;
    const next=JSON.stringify([signature,preferences.pinned,input.value,mode]);
    if (force || resultSignature!==next) {
      const focusedId=document.activeElement?.getAttribute?.('data-focus-id');
      resultSignature=next;rows=[];
      const matches=searchCommands(available,input.value,60);
      results.replaceChildren(...matches.map(entry=>{
        const run=button(entry.label,()=>void activate(entry.id),[
          el('strong',{text:entry.label}),el('small',{text:[entry.kind==='command'?entry.target:entry.location,entry.shortcut].filter(Boolean).join(' · ')})
        ]);
        run.classList.add('command-access-run');run.setAttribute('data-focus-id',`result:${entry.id}`);
        const reason=el('small',{class:'command-access-reason'});rows.push({id:entry.id,run,reason,section:'search'});
        return el('div',{class:'command-access-result','data-command-id':entry.id},[run,pinButton(entry),reason]);
      }));
      if (!matches.length) results.append(el('p',{class:'command-access-empty',text:'No matching commands. Try a name, alias or tool category.'}));
      status.textContent=matches.length===60?'Showing the first 60 matches. Refine your search.':`${matches.length} matching commands.`;
      renderPinned();
      const restore=focusAfterRender || focusedId;focusAfterRender=null;
      if (restore==='query') input.focus({preventScroll:true});
      else if (restore) {
        const candidates=[...results.querySelectorAll('button'),...pinned.querySelectorAll('button')];
        const match=candidates.find(node=>node.getAttribute('data-focus-id')===restore);
        if (match && !match.disabled) match.focus({preventScroll:true});
        else input.focus({preventScroll:true});
      }
    }
    for (const row of rows) { updateControl(row.run,row.id);row.reason.textContent=commandAvailability(entryFor(row.id),state()); }
  }
  function refresh(force = false) {
    if(disposed)return;
    available=catalog();
    const next=JSON.stringify(available.map(entry=>[entry.id,entry.label,entry.aliases,entry.description,entry.category,entry.location,entry.shortcut]));
    if (next!==signature) { signature=next;resultSignature=''; }
    renderToolbar(force);renderResults(force);
  }
  input.addEventListener('input',()=>renderResults());
  dialog.addEventListener('cancel',event=>{event.preventDefault();event.stopPropagation();close();});
  dialog.addEventListener('pointerdown',event=>{
    if(event.target!==dialog)return;
    const bounds=dialog.getBoundingClientRect();
    if(event.clientX<bounds.left || event.clientX>bounds.right || event.clientY<bounds.top || event.clientY>bounds.bottom)close();
  });
  dialog.addEventListener('keydown',event=>{
    if(event.key==='Escape'){event.preventDefault();event.stopPropagation();close();return;}
    if(event.isComposing || event.ctrlKey || event.metaKey || event.altKey)return;
    if(event.key==='ArrowDown' || event.key==='ArrowUp' || (event.target!==input && ['Home','End'].includes(event.key))) {
      const current=mode==='customize'?[...rows.filter(row=>row.section==='pinned'),...rows.filter(row=>row.section==='search')]:
        rows.filter(row=>row.section==='search');
      const choices=current.filter(row=>!row.run.disabled).map(row=>row.run);
      const index=choices.indexOf(document.activeElement),offset=event.key==='ArrowDown'?1:-1;
      if(choices.length) {
        const choice=choices[event.key==='Home'?0:event.key==='End'?choices.length-1:index<0?(offset>0?0:choices.length-1):(index+offset+choices.length)%choices.length];
        choice.focus({preventScroll:true});
        choice.scrollIntoView({block:'nearest',inline:'nearest'});
      }
      event.preventDefault();event.stopPropagation();
    } else if(event.key==='Enter' && event.target===input) {
      const first=rows.find(row=>row.section==='search');
      if(first && !first.run.disabled)void activate(first.id);event.preventDefault();event.stopPropagation();
    }
  });
  const observer=typeof ResizeObserver==='function'?new ResizeObserver(()=>renderToolbar()):null;
  observer?.observe(toolbar);
  refresh();
  return {
    search:query=>open('search',query),customize:()=>open('customize'),refresh,
    setMenus:value=>{menus=value;},
    pin:id=>change({type:'pin',id}),unpin:id=>change({type:'remove',id}),isPinned:id=>preferences.pinned.includes(id),
    execute:id=>activate(id),overflow:()=>preferences.pinned.slice(quickAccessCapacity(toolbar.clientWidth||244)),
    snapshot:()=>quickAccessPreferences(preferences),
    restore:value=>{
      preferences=quickAccessPreferences(value);refresh(true);
      // Applying a whole UI workspace must stay coherent even in private/full
      // storage. Persistence failures are reported separately from restoration.
      try { save(preferences); } catch(error) { notify(error); }
    },
    handleShortcut(event) {
      if(event.defaultPrevented || event.isComposing || !(event.ctrlKey||event.metaKey) || event.altKey || event.shiftKey || event.key?.toLowerCase()!=='k')return false;
      const other=document.querySelector('dialog[open]');if(other && other!==dialog)return false;
      event.preventDefault();event.stopPropagation();open('search');return true;
    },
    destroy() { disposed=true;observer?.disconnect();toolbarKeyboard.dispose();close(false);dialog.remove();toolbar.replaceChildren(); },
    get dialog() { return dialog; }
  };
}
