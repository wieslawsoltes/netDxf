import {visibleRibbonControl} from './ribbon-keyboard.mjs';

const alphabet='ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
/** Equal-length command tips avoid ambiguous prefixes while retaining mnemonics. */
export function commandKeytips(labels) {
  const used=new Set();return labels.map(label=>{
    const text=String(label||'').toUpperCase().replace(/[^A-Z0-9]/g,''),preferred=(text+'AA').slice(0,2);
    let code=preferred;
    if(used.has(code)){const candidates=[...alphabet].map(char=>(text[0]||'A')+char);code=candidates.find(value=>!used.has(value));}
    if(!code)for(const a of alphabet){code=[...alphabet].map(b=>a+b).find(value=>!used.has(value));if(code)break;}
    used.add(code);return code;
  });
}
export function matchingKeytips(items,prefix){return items.filter(item=>typeof item.code==='string'&&item.code.startsWith(prefix.toUpperCase()));}
export function tabKeytips(ids){
  const preferred={Home:'H',Insert:'N',Annotate:'A',Graph:'G',View:'V',Manage:'M',Output:'O','Block Editor':'B'},used=new Set(['F','Z']),digits=Math.max(2,ids.length.toString(36).length);let overflow=0;
  // Reserve built-in letters before assigning module/custom tabs, so a module
  // ordered before Home cannot consume its established access sequence.
  for(const id of ids)if(preferred[id])used.add(preferred[id]);
  return ids.map(id=>{
    if(preferred[id])return preferred[id];
    const code=[...'ABCDEFGHIJKLMNOPQRSTUVWXYZ'].find(value=>!used.has(value));
    if(code){used.add(code);return code;}return `Z${(overflow++).toString(36).toUpperCase().padStart(digits,'0')}`;
  });
}

/** Alt is separate from drafting function keys: F10 remains Polar tracking. */
export class RibbonKeytips {
  constructor(layout){
    this.layout=layout;this.active=false;this.altPending=false;this.levels=[];this.items=[];
    this.down=event=>this.keydown(event);this.up=event=>{
      if(event.key!=='Alt')return;const pending=this.altPending;this.altPending=false;
      if(pending){event.preventDefault();if(this.active)this.close(true);else this.top();}
    };
    this.outside=()=>{this.altPending=false;this.close();};this.resize=()=>this.close();this.scroll=()=>{if(this.active)this.render();};
    document.addEventListener('keydown',this.down,true);document.addEventListener('keyup',this.up,true);document.addEventListener('pointerdown',this.outside,true);
    window.addEventListener('blur',this.outside);window.addEventListener('resize',this.resize);document.addEventListener('scroll',this.scroll,true);
  }
  keydown(event){
    if(event.defaultPrevented||event.isComposing||event.getModifierState?.('AltGraph'))return;
    if(event.key==='Alt'&&!event.ctrlKey&&!event.metaKey&&!event.shiftKey){if(!event.repeat)this.altPending=true;event.preventDefault();return;}
    if(event.ctrlKey||event.metaKey){this.altPending=false;this.close();return;}
    const character=/^[A-Za-z0-9]$/.test(event.key);
    if(this.altPending&&event.altKey&&character){
      this.altPending=false;
      // Existing panel launchers expose Alt+F for tear-off. Bare Alt, then F
      // always addresses the application menu, including from that launcher.
      if(!this.active&&event.key.toLowerCase()==='f'&&event.target.closest?.('.ribbon-launcher'))return;
      if(!this.active)this.top();
    }else if(event.key!=='Alt')this.altPending=false;
    if(!this.active)return;
    if(event.key==='Escape'){event.preventDefault();event.stopImmediatePropagation();if(this.prefix){this.prefix='';this.render();}else if(this.levels.length>1){this.levels.pop();this.render();}else this.close(true);return;}
    if(event.key==='Backspace'){event.preventDefault();event.stopImmediatePropagation();this.prefix=this.prefix.slice(0,-1);this.render();return;}
    if(!character){if(/^F\d+$/.test(event.key)||['Tab','ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Enter'].includes(event.key))this.close();return;}
    event.preventDefault();event.stopImmediatePropagation();
    const prefix=this.prefix+event.key.toUpperCase(),choices=matchingKeytips(this.items,prefix);
    if(!choices.length){this.prefix='';this.render();return;}
    this.prefix=prefix;const match=choices.find(item=>item.code===prefix);
    if(match){this.prefix='';this.activate(match);}else this.paint(choices);
  }
  top(){
    if(document.querySelector('dialog[open]'))return;
    this.origin=document.activeElement;this.active=true;this.layout.contextMenu?.close();this.prefix='';
    this.levels=[()=>{
      const result=[],app=document.getElementById('file-menu');if(visibleRibbonControl(app))result.push({node:app,code:'F',kind:'menu'});
      const tabs=[...document.querySelectorAll('#ribbon-tabs [data-tab]')].filter(visibleRibbonControl),codes=tabKeytips(tabs.map(tab=>tab.dataset.tab));
      tabs.forEach((tab,index)=>result.push({node:tab,code:codes[index],kind:'tab'}));
      [...document.querySelectorAll('#quick-access button')].filter(visibleRibbonControl).slice(0,9).forEach((node,index)=>result.push({node,code:String(index+1),kind:'command'}));
      return result;
    }];this.render();
  }
  controls(root){
    return [...root.querySelectorAll('button,select,input')].filter(node=>visibleRibbonControl(node)&&!node.classList.contains('ribbon-tear-button'));
  }
  push(root){
    this.prefix='';this.levels.push(()=>{
      const nodes=this.controls(typeof root==='function'?root():root),codes=commandKeytips(nodes.map(node=>node.getAttribute('aria-label')||node.getAttribute('title')||node.textContent));
      return nodes.map((node,index)=>({node,code:codes[index],kind:'control'}));
    });this.render();
  }
  activate(item){
    const node=item.node;
    if(!visibleRibbonControl(node)){this.render();return;}
    if(item.kind==='tab'){
      node.click();this.layout.reveal();requestAnimationFrame(()=>{if(this.active)this.push(()=>this.layout.panel);});return;
    }
    if(item.kind==='menu'){
      node.click();requestAnimationFrame(()=>{const popup=document.getElementById(node.getAttribute('aria-controls'));if(this.active&&popup)this.push(popup);else this.close();});return;
    }
    if(['INPUT','SELECT','TEXTAREA'].includes(node.tagName)){this.close();node.focus({preventScroll:true});return;}
    const popup=node.matches?.('.ribbon-launcher,[data-ribbon-split-toggle],[data-ribbon-slideout-toggle]');
    if(popup){
      node.click();requestAnimationFrame(()=>{
        const current=this.layout.splitMenus.popup?.menu||this.layout.slideouts?.popupFor?.(node)?.content||this.layout.popup?.tools;
        if(this.active&&current)this.push(current);else this.close();
      });return;
    }
    this.close();node.click();
  }
  render(){
    if(!this.active)return;this.items=this.levels.at(-1)().filter(item=>visibleRibbonControl(item.node));this.paint(matchingKeytips(this.items,this.prefix));
  }
  paint(items){
    this.clearLabels();if(!this.overlay){this.overlay=document.createElement('div');this.overlay.className='ribbon-keytips';this.overlay.setAttribute('aria-label','Ribbon KeyTips');document.body.append(this.overlay);}
    for(const item of items){
      const rect=item.node.getBoundingClientRect(),clip=item.node.closest('#ribbon-tabs,.ribbon-tools')?.getBoundingClientRect();
      if(rect.right<=0||rect.left>=innerWidth||rect.bottom<=0||rect.top>=innerHeight||clip&&(rect.right<=clip.left||rect.left>=clip.right||rect.bottom<=clip.top||rect.top>=clip.bottom))continue;
      const label=document.createElement('kbd');label.className='ribbon-keytip';label.textContent=item.code;label.setAttribute('aria-hidden','true');
      item.node.dataset.ribbonKeytip=item.code;this.labeled.push(item.node);this.overlay.append(label);
      label.style.left=`${Math.max(4,Math.min(rect.left+rect.width/2-10,innerWidth-30))}px`;label.style.top=`${Math.max(4,Math.min(rect.bottom-5,innerHeight-24))}px`;
    }
  }
  clearLabels(){for(const node of this.labeled||[])delete node.dataset.ribbonKeytip;this.labeled=[];this.overlay?.replaceChildren();}
  close(focus=false){this.active=false;this.altPending=false;this.prefix='';this.clearLabels();this.overlay?.remove();this.overlay=null;this.levels=[];this.items=[];if(focus&&this.origin?.isConnected)this.origin.focus({preventScroll:true});}
  dispose(){this.close();document.removeEventListener('keydown',this.down,true);document.removeEventListener('keyup',this.up,true);document.removeEventListener('pointerdown',this.outside,true);window.removeEventListener('blur',this.outside);window.removeEventListener('resize',this.resize);document.removeEventListener('scroll',this.scroll,true);}
}
