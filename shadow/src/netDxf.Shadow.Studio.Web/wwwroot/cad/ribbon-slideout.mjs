import {bindRovingToolbar} from './ribbon-keyboard.mjs';
import {splitMenuPosition} from './ribbon-split.mjs';

let serial=0;
export function addPanelSlideout(group,controls){
  if(!controls?.length)return group;
  const content=document.createElement('div');content.className='ribbon-slideout-content';content.hidden=true;
  content.id=`ribbon-slideout-${++serial}`;content.setAttribute('role','toolbar');content.setAttribute('aria-label',`${group.dataset.panel} additional commands`);content.append(...controls);
  const toggle=document.createElement('button');toggle.type='button';toggle.className='ribbon-slideout-toggle';toggle.textContent='▾';toggle.setAttribute('data-ribbon-slideout-toggle','');
  toggle.setAttribute('aria-label',`Expand ${group.dataset.panel} panel`);toggle.title=`Expand ${group.dataset.panel} panel`;toggle.setAttribute('aria-controls',content.id);toggle.setAttribute('aria-expanded','false');toggle.setAttribute('aria-keyshortcuts','ArrowDown');
  group.querySelector('.ribbon-group-title').append(toggle);group.append(content);return group;
}

/** Expanded controls keep their identity when opened, pinned, collapsed or floated. */
export class RibbonSlideouts {
  constructor(layout){
    this.layout=layout;this.groups=new Map();this.popups=new Map();this.inline=new Map();
    this.click=event=>{
      const toggle=event.target.closest?.('[data-ribbon-slideout-toggle]'),group=toggle?.closest('.ribbon-group');
      if(toggle&&this.groups.has(group)){event.preventDefault();event.stopPropagation();this.popups.has(group)?this.close(group,true):this.open(group,true);}
      else for(const [group,popup]of this.popups)if(popup.content.contains(event.target)&&event.target.closest('button[data-command],button[data-action],button[data-ribbon-command-id]')&&!popup.pinned)this.close(group);
    };
    this.outside=event=>{for(const [group,popup]of this.popups)if(!popup.pinned&&!popup.element.contains(event.target)&&!popup.toggle.contains(event.target))this.close(group);};
    this.keys=event=>{
      if(event.defaultPrevented||this.layout.keytips?.active||event.isComposing||event.ctrlKey||event.metaKey||event.altKey)return;
      const toggle=event.target.closest?.('[data-ribbon-slideout-toggle]');
      if(toggle&&this.groups.has(toggle.closest('.ribbon-group'))&&event.key==='ArrowDown'){event.preventDefault();event.stopPropagation();this.open(toggle.closest('.ribbon-group'),true);}
      else if(event.key==='Escape'){const entry=[...this.popups].find(([,popup])=>popup.element.contains(event.target));if(entry){event.preventDefault();event.stopPropagation();this.close(entry[0],true);}}
    };
    this.resize=()=>{for(const [group,popup]of this.popups)if(popup.pinned)this.position(popup);else this.close(group);};
    document.addEventListener('click',this.click);document.addEventListener('pointerdown',this.outside);document.addEventListener('keydown',this.keys,true);window.addEventListener('resize',this.resize);
  }
  set(groups,context){
    this.closeAll();for(const group of [...this.inline.keys()])this.expose(group,null,false);
    this.groups=new Map(groups.map(group=>[group,{tab:group.closest('.ribbon-floating')?JSON.parse(group.closest('.ribbon-floating').dataset.ribbonSource)[0]:context.tab,panel:group.dataset.ribbonPanel||group.dataset.panel}]));
    for(const [group,spec]of this.groups){
      const content=group.querySelector('.ribbon-slideout-content');if(!content)continue;
      content.hidden=true;
      if(group.closest('.ribbon-floating')){content.hidden=false;content.classList.add('ribbon-slideout-inline');continue;}
      content.classList.remove('ribbon-slideout-inline');
      if(this.layout.customization?.snapshot().expanded.some(value=>value.tab===spec.tab&&value.panel===spec.panel))this.open(group,false,true);
    }
  }
  refresh(){
    for(const [group,popup]of this.popups)if(group.classList.contains('collapsed')||this.layout.mode!=='full'&&this.layout.mode!=='tabs')this.close(group);else this.position(popup);
    if(this.layout.mode!=='full'&&!(this.layout.mode==='tabs'&&this.layout.panel.classList.contains('ribbon-peek')))return;
    for(const [group,spec]of this.groups)if(!group.classList.contains('collapsed')&&!group.closest('.ribbon-floating')&&this.layout.customization?.snapshot().expanded.some(value=>value.tab===spec.tab&&value.panel===spec.panel))this.open(group,false,true);
  }
  popupFor(toggle){return this.popups.get(toggle.closest('.ribbon-group'));}
  contains(node){return [...this.popups.values()].some(popup=>popup.element.contains(node));}
  open(group,focus=false,pinned=false){
    if(this.popups.has(group)){if(focus)this.popups.get(group).keyboard.focus();return;}
    for(const [other,popup]of this.popups)if(!popup.pinned)this.close(other);
    const content=group.querySelector('.ribbon-slideout-content'),toggle=group.querySelector('[data-ribbon-slideout-toggle]');if(!content||!toggle||group.classList.contains('collapsed'))return;
    const marker=document.createComment('ribbon-slideout');content.before(marker);
    const element=document.createElement('section');element.className='ribbon-slideout-popup';element.setAttribute('aria-label',`${group.dataset.panel} expanded panel`);
    const footer=document.createElement('footer'),pin=document.createElement('button'),close=document.createElement('button');
    pin.type=close.type='button';pin.textContent='⌖';pin.setAttribute('aria-label',`Keep ${group.dataset.panel} panel expanded`);pin.setAttribute('aria-pressed',String(pinned));pin.title='Keep panel expanded';
    close.textContent='×';close.setAttribute('aria-label',`Close ${group.dataset.panel} expanded panel`);footer.append(pin,close);element.append(content,footer);document.body.append(element);
    content.hidden=false;toggle.setAttribute('aria-expanded','true');
    const popup={group,content,toggle,marker,element,pin,pinned};this.popups.set(group,popup);
    popup.keyboard=bindRovingToolbar(element,{onEscape:()=>this.close(group,true)});
    pin.addEventListener('click',event=>{event.preventDefault();event.stopPropagation();const spec=this.groups.get(group);try{this.layout.customization.change({type:'panel-expanded',...spec,expanded:!popup.pinned});}catch(error){this.layout.report(error);return;}
      if(this.popups.get(group)===popup){popup.pinned=!popup.pinned;pin.setAttribute('aria-pressed',String(popup.pinned));}else this.popups.get(this.layout.groups.find(node=>(node.dataset.ribbonPanel||node.dataset.panel)===spec.panel))?.pin.focus();});
    close.addEventListener('click',event=>{event.preventDefault();event.stopPropagation();const spec=this.groups.get(group);if(popup.pinned){try{this.layout.customization.change({type:'panel-expanded',...spec,expanded:false});}catch(error){this.layout.report(error);return;}}this.close(group,true);
      if(!toggle.isConnected)this.layout.groups.find(node=>(node.dataset.ribbonPanel||node.dataset.panel)===spec.panel)?.querySelector('[data-ribbon-slideout-toggle]')?.focus({preventScroll:true});});
    this.position(popup);if(focus)popup.keyboard.focus();this.layout.keyboard?.refresh();
  }
  position(popup){const position=splitMenuPosition(popup.group.getBoundingClientRect(),popup.element.getBoundingClientRect(),{width:innerWidth,height:innerHeight});popup.element.style.left=`${position.left}px`;popup.element.style.top=`${position.top}px`;}
  close(group,focus=false){
    const popup=this.popups.get(group);if(!popup)return;this.popups.delete(group);popup.keyboard.dispose();popup.content.hidden=true;popup.marker.replaceWith(popup.content);popup.element.remove();popup.toggle.setAttribute('aria-expanded','false');
    if(focus&&popup.toggle.isConnected)popup.toggle.focus({preventScroll:true});this.layout.keyboard?.refresh();
  }
  closeAll(){for(const group of [...this.popups.keys()])this.close(group);}
  closeUnpinned(){for(const [group,popup]of this.popups)if(!popup.pinned)this.close(group);}
  expose(group,tools,value){
    this.close(group);const existing=this.inline.get(group);
    if(value&&!existing){const content=group.querySelector('.ribbon-slideout-content');if(!content)return;const marker=document.createComment('ribbon-slideout-inline');content.before(marker);tools.append(content);content.hidden=false;content.classList.add('ribbon-slideout-inline');this.inline.set(group,{content,marker});}
    else if(!value&&existing){existing.content.hidden=true;existing.content.classList.remove('ribbon-slideout-inline');existing.marker.replaceWith(existing.content);this.inline.delete(group);}
  }
  dispose(){this.closeAll();for(const group of [...this.inline.keys()])this.expose(group,null,false);document.removeEventListener('click',this.click);document.removeEventListener('pointerdown',this.outside);document.removeEventListener('keydown',this.keys,true);window.removeEventListener('resize',this.resize);}
}
