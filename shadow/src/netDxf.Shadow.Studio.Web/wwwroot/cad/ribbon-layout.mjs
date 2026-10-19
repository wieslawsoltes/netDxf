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

/** Adaptive native-style ribbon panels. The flyout reuses controls and event handlers. */
export class RibbonLayout {
  constructor(panel) {
    this.panel = panel; this.groups = []; this.popup = null; this.frame = 0; this.mode='full';
    this.observer = new ResizeObserver(()=>this.schedule()); this.observer.observe(panel);
    this.outside = event => {
      if(this.popup && !this.popup.tools.contains(event.target) && !this.popup.button.contains(event.target))this.close();
      if(this.panel.classList.contains('ribbon-peek')&&!this.panel.contains(event.target)&&!event.target.closest('.ribbon-bar,.ribbon-flyout'))this.hide();
    };
    this.escape = event => { if(event.key==='Escape'&&(this.popup||this.panel.classList.contains('ribbon-peek'))){event.preventDefault();event.stopPropagation();if(this.popup)this.close(true);else {this.hide();document.querySelector('#ribbon-tabs [aria-selected=true]')?.focus();}} };
    document.addEventListener('pointerdown',this.outside); document.addEventListener('keydown',this.escape,true);
    this.resize=()=>{this.close();this.hide();};window.addEventListener('resize',this.resize);
    this.choose=event=>{if(event.target.closest('button[data-command],button[data-action]'))this.hide();};
    panel.addEventListener('click',this.choose);
  }
  set(groups) {
    this.close(); this.groups=groups; this.panel.replaceChildren(...groups);
    groups.forEach(group=>{
      group.querySelector('.ribbon-launcher').addEventListener('click',()=>this.open(group));
      group.querySelector('.ribbon-launcher').addEventListener('pointerenter',event=>{if(this.mode==='titles'&&event.pointerType==='mouse'&&this.popup?.group!==group)this.open(group,false);});
    });
    this.schedule();
  }
  setMode(mode) {this.close();this.hide();this.mode=ribbonModes.includes(mode)?mode:'full';this.panel.dataset.mode=this.mode;this.schedule();}
  reveal(focus=false) {
    if(this.mode==='tabs'){
      const bounds=document.querySelector('.ribbon-bar').getBoundingClientRect();
      this.panel.style.top=`${bounds.bottom}px`;this.panel.classList.add('ribbon-peek');this.schedule();
    }
    if(focus)requestAnimationFrame(()=>this.panel.querySelector('.ribbon-group:not(.collapsed) button:not(:disabled),.collapsed .ribbon-launcher')?.focus({preventScroll:true}));
  }
  hide() {this.panel.classList.remove('ribbon-peek');this.panel.style.removeProperty('top');this.close();}
  schedule() { if(!this.frame)this.frame=requestAnimationFrame(()=>{this.frame=0;this.layout();}); }
  layout() {
    if(!this.panel.clientWidth)return;
    this.close(); this.groups.forEach(group=>group.classList.remove('collapsed'));
    if(this.mode==='buttons'||this.mode==='titles'){this.groups.forEach(group=>group.classList.add('collapsed'));return;}
    const widths=this.groups.map(group=>group.getBoundingClientRect().width);
    const collapsed=collapsedPanels(widths,this.panel.clientWidth-8);
    this.groups.forEach((group,index)=>group.classList.toggle('collapsed',collapsed[index]));
  }
  open(group, focus=true) {
    if(this.popup?.group===group){this.close();return;}
    this.close();
    const tools=group.querySelector('.ribbon-tools'),button=group.querySelector('.ribbon-launcher');
    if(!tools)return;
    const marker=document.createComment('ribbon-panel');tools.before(marker);document.body.append(tools);
    tools.classList.add('ribbon-flyout');tools.setAttribute('role','group');tools.setAttribute('aria-label',group.dataset.panel);
    const bounds=button.getBoundingClientRect();
    tools.style.left='4px';tools.style.top=`${bounds.bottom+3}px`;
    const position=flyoutPosition(bounds,tools.getBoundingClientRect(),{width:innerWidth,height:innerHeight});
    tools.style.left=`${position.left}px`;tools.style.top=`${position.top}px`;
    button.setAttribute('aria-expanded','true');this.popup={group,tools,button,marker};
    if(focus)tools.querySelector('button:not(:disabled),select:not(:disabled)')?.focus({preventScroll:true});
    this.onChoose=event=>{if(event.target.closest('button[data-command],button[data-action]'))this.hide();};tools.addEventListener('click',this.onChoose);
  }
  close(focus=false) {
    const popup=this.popup;if(!popup)return;this.popup=null;
    popup.tools.removeEventListener('click',this.onChoose);popup.tools.classList.remove('ribbon-flyout');
    popup.tools.style.removeProperty('left');popup.tools.style.removeProperty('top');popup.marker.replaceWith(popup.tools);
    popup.button.setAttribute('aria-expanded','false');if(focus)popup.button.focus({preventScroll:true});
  }
  dispose() {this.hide();this.observer.disconnect();cancelAnimationFrame(this.frame);document.removeEventListener('pointerdown',this.outside);document.removeEventListener('keydown',this.escape,true);window.removeEventListener('resize',this.resize);this.panel.removeEventListener('click',this.choose);}
}
