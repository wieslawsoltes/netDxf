import {SelectionCycleModel, selectionCandidateLabel} from './selection-cycling.mjs';

const node = (tag, attributes = {}, text) => {
  const element = document.createElement(tag);
  for (const [key,value] of Object.entries(attributes)) element.setAttribute(key,value);
  if (text !== undefined) element.textContent = text;
  return element;
};
let nextId = 0;

/** Accessible overlap chooser; transient highlighting is independent of the source selection. */
export class SelectionCycling {
  constructor(element, {scope, load, choose, invalidate, error, eligible, cancelled}) {
    this.element = element; this.choose = choose; this.invalidate = invalidate; this.error = error; this.eligible = eligible; this.cancelled = cancelled;
    this.model = new SelectionCycleModel({scope,load,changed:()=>this.render()});
    this.host = node('section', {class:'selection-cycle',hidden:'',role:'dialog','aria-label':'Overlapping objects','aria-modal':'false'});
    this.title = node('strong'); this.close = node('button',{type:'button','aria-label':'Cancel overlapping object selection'},'×');
    this.header = node('div',{class:'selection-cycle-header'}); this.header.append(this.title,this.close);
    this.list = node('div',{class:'selection-cycle-list',role:'listbox',tabindex:'0','aria-label':'Choose overlapping object'});
    this.detail = node('p',{class:'selection-cycle-detail','aria-live':'polite'});
    this.host.append(this.header,this.list,this.detail); element.append(this.host);
    this.close.addEventListener('click',()=>this.cancel(true));
    this.onKey = event => this.key(event);
    this.onOutside = event => {
      if (this.model.state?.open && !this.host.contains(event.target) && event.target !== this.element &&
          !(event.target?.tagName === 'CANVAS' && this.element.contains(event.target))) this.cancel(false);
    };
    this.onFocus = event => {
      if (this.model.state?.open && !this.host.contains(event.target) && !this.element.contains(event.target)) this.cancel(false);
    };
    globalThis.addEventListener('keydown',this.onKey,true);
    document.addEventListener('pointerdown',this.onOutside,true);
    document.addEventListener('focusin',this.onFocus);
  }
  get active() { const state=this.model.current(); return !!state?.active || !!state?.pending && !!state.open; }
  get preview() { const state=this.model.current(); return state?.active ? state.result.candidates[state.index] : null; }
  get overlap() { const state=this.model.current(); return !state?.active && state?.result?.candidates.length > 1; }
  cancel(focus = false) {
    const active = this.active, existed=this.model.cancel();
    if(active)this.cancelled?.();
    if (focus && active) this.element.focus({preventScroll:true});
    return existed;
  }
  async hover(point,pixel) {
    if(this.active)return;
    try { await this.model.query(point,{pixel,open:false}); } catch { /* A hover is advisory; explicit picks report failures. */ }
  }
  async open(point,pixel,context) {
    try {
      const state=await this.model.query(point,{...context,pixel,open:true});
      if(!state)return;
      if(!this.eligible()){this.cancel();return;}
      if(state.result.candidates.length <= 1)await this.commit();
      else if(!state.temporary)this.list.focus({preventScroll:true});
    } catch(error) { this.error?.(error); }
  }
  async commit() {
    const state=this.model.current(); if(!state || state.pending)return;
    if(!this.eligible()){this.cancel();return;}
    const candidate=state.result.candidates[state.index] ?? null;
    this.model.cancel(); this.element.focus({preventScroll:true});
    try { await this.choose(candidate,state); } catch(error) { this.error?.(error); }
  }
  key(event) {
    if(event.defaultPrevented || event.target?.closest?.('input,textarea,select,[contenteditable="true"],dialog[open]'))return;
    const state=this.model.current();
    if(!state?.active || !this.eligible() || !this.element.contains(event.target))return;
    // Native buttons retain Enter/Space activation (especially Cancel). Only the listbox
    // and the viewport's temporary cycle own candidate navigation and acceptance.
    if(event.key!=='Escape' && !this.list.contains(event.target) && !(state.temporary && event.target===this.element))return;
    if(!['Escape','Enter','ArrowDown','ArrowUp','Home','End',' '].includes(event.key))return;
    if(event.key===' '&&!event.shiftKey)return;
    event.preventDefault(); event.stopImmediatePropagation();
    if(event.key==='Escape')this.cancel(true);
    else if(event.key==='Enter')void this.commit();
    else if(event.key==='Home')this.model.setIndex(0);
    else if(event.key==='End')this.model.setIndex(state.result.candidates.length-1);
    else this.model.advance(event.key==='ArrowUp'?-1:1);
  }
  render() {
    const state=this.model.state, active=!!state?.active;
    this.host.hidden=!active; this.invalidate?.();
    if(!active) { this.renderedResult=null;this.list.replaceChildren(); return; }
    const result=state.result, selected=result.candidates[state.index];
    this.title.textContent=`${result.candidates.length}${result.truncated?'+':''} overlapping objects`;
    this.list.hidden=!!state.temporary;
    // Keep listbox focus and stable option nodes while keyboard/hover changes the active descendant.
    if(this.renderedResult!==result) {
      this.renderedResult=result;
      const id=++nextId;
      this.list.replaceChildren(...result.candidates.map((candidate,index)=>{
        const option=node('div',{role:'option',id:`selection-cycle-${id}-${index}`,'aria-selected':'false'},selectionCandidateLabel(candidate));
        option.title=`Source ${candidate.entity}; hit ${candidate.leafType || candidate.type}; occurrence ${candidate.occurrence}`;
        option.addEventListener('pointermove',()=>{if(this.model.state?.index!==index)this.model.setIndex(index);});
        option.addEventListener('click',()=>{this.model.setIndex(index);void this.commit();});
        return option;
      }));
    }
    [...this.list.children].forEach((option,index)=>option.setAttribute('aria-selected',String(index===state.index)));
    const option=this.list.children[state.index];
    this.list.setAttribute('aria-activedescendant',option.getAttribute('id'));
    option.scrollIntoView?.({block:'nearest'});
    this.detail.textContent=`${state.index+1} of ${result.candidates.length}${result.truncated?' shown; zoom in for more':''}. ${state.temporary?'Click to cycle. ':''}↑ ↓ to cycle; Enter to select; Esc to cancel.${selected.previewTruncated?' Preview bounds may be approximate.':''}`;
    const rect=this.element.getBoundingClientRect(), width=Math.min(300,Math.max(120,rect.width-12));
    this.host.style.width=`${width}px`;this.host.style.maxHeight=`${Math.max(60,rect.height-12)}px`;
    this.host.style.left=`${Math.max(6,Math.min(state.pixel.x+14,rect.width-width-6))}px`;
    this.host.style.top=`${Math.max(6,Math.min(state.pixel.y+14,rect.height-Math.min(300,rect.height-12)-6))}px`;
  }
  dispose() {
    this.model.cancel();this.host.remove();
    globalThis.removeEventListener('keydown',this.onKey,true);
    document.removeEventListener('pointerdown',this.onOutside,true);document.removeEventListener('focusin',this.onFocus);
  }
}
