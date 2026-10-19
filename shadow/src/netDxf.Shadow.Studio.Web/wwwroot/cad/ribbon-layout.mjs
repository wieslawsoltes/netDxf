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

export function chromePreferences(saved = {}) {
  return { ribbonMinimized:saved.ribbonMinimized === true, commandLineVisible:saved.commandLineVisible !== false,
    commandHistoryVisible:saved.commandHistoryVisible !== false };
}

/** Adaptive native-style ribbon panels. The flyout reuses controls and event handlers. */
export class RibbonLayout {
  constructor(panel) {
    this.panel = panel; this.groups = []; this.popup = null; this.frame = 0;
    this.observer = new ResizeObserver(()=>this.schedule()); this.observer.observe(panel);
    this.outside = event => { if(this.popup && !this.popup.tools.contains(event.target) && !this.popup.button.contains(event.target))this.close(); };
    this.escape = event => { if(event.key==='Escape'&&this.popup){event.preventDefault();event.stopPropagation();this.close(true);} };
    document.addEventListener('pointerdown',this.outside); document.addEventListener('keydown',this.escape,true);
    window.addEventListener('resize',()=>this.close());
  }
  set(groups) {
    this.close(); this.groups=groups; this.panel.replaceChildren(...groups);
    groups.forEach(group=>group.querySelector('.ribbon-launcher').addEventListener('click',()=>this.open(group)));
    this.schedule();
  }
  schedule() { if(!this.frame)this.frame=requestAnimationFrame(()=>{this.frame=0;this.layout();}); }
  layout() {
    if(!this.panel.clientWidth)return;
    this.close(); this.groups.forEach(group=>group.classList.remove('collapsed'));
    const widths=this.groups.map(group=>group.getBoundingClientRect().width);
    const collapsed=collapsedPanels(widths,this.panel.clientWidth-8);
    this.groups.forEach((group,index)=>group.classList.toggle('collapsed',collapsed[index]));
  }
  open(group) {
    if(this.popup?.group===group){this.close();return;}
    this.close();
    const tools=group.querySelector('.ribbon-tools'),button=group.querySelector('.ribbon-launcher');
    if(!tools)return;
    const marker=document.createComment('ribbon-panel');tools.before(marker);document.body.append(tools);
    tools.classList.add('ribbon-flyout');tools.setAttribute('role','group');tools.setAttribute('aria-label',group.dataset.panel);
    const bounds=button.getBoundingClientRect();
    tools.style.left='4px';tools.style.top=`${bounds.bottom+3}px`;
    tools.style.left=`${Math.max(4,Math.min(innerWidth-tools.getBoundingClientRect().width-4,bounds.left))}px`;
    button.setAttribute('aria-expanded','true');this.popup={group,tools,button,marker};
    tools.querySelector('button:not(:disabled),select:not(:disabled)')?.focus({preventScroll:true});
    this.onChoose=event=>{if(event.target.closest('button[data-command],button[data-action]'))this.close();};tools.addEventListener('click',this.onChoose);
  }
  close(focus=false) {
    const popup=this.popup;if(!popup)return;this.popup=null;
    popup.tools.removeEventListener('click',this.onChoose);popup.tools.classList.remove('ribbon-flyout');
    popup.tools.style.removeProperty('left');popup.tools.style.removeProperty('top');popup.marker.replaceWith(popup.tools);
    popup.button.setAttribute('aria-expanded','false');if(focus)popup.button.focus({preventScroll:true});
  }
}
