/** Keyboard editing belongs to the native control, including its arrow/Home/End keys. */
export function nativeRibbonInput(target) {
  for (let node=target;node;node=node.parentElement || node.parentNode)
    if (['INPUT','SELECT','TEXTAREA'].includes(node.tagName) || node.isContentEditable) return true;
  return false;
}

/** Includes CSS visibility, so collapsed panels and suspended contextual controls cannot receive focus. */
export function visibleRibbonControl(node) {
  if (!node || node.disabled || node.getAttribute?.('aria-disabled')==='true' || node.isConnected===false) return false;
  for (let parent=node;parent;parent=parent.parentElement || parent.parentNode)
    if (parent.hidden || parent.inert || parent.getAttribute?.('aria-hidden')==='true') return false;
  // The fallback supports non-layout hosts; browsers always use the actual rendered boxes.
  return typeof node.getClientRects!=='function' || node.getClientRects().length>0;
}

/** Spatial navigation follows the rendered three-row ribbon, not its column-major DOM order. */
export function nextRibbonIndex(boxes, current, key) {
  if (!boxes.length) return -1;
  if (key==='End') return boxes.length-1;
  if (key==='Home' || current<0) return 0;
  const horizontal=key==='ArrowLeft' || key==='ArrowRight', forward=key==='ArrowRight' || key==='ArrowDown';
  const center=box=>horizontal?[(box.left+box.right)/2,(box.top+box.bottom)/2]:[(box.top+box.bottom)/2,(box.left+box.right)/2];
  const origin=center(boxes[current]);
  const candidates=boxes.map((box,index)=>({index,point:center(box)})).filter(value=>value.index!==current && (value.point[0]-origin[0])*(forward?1:-1)>1);
  // Prefer the same row/column, then the nearest control in the requested direction.
  candidates.sort((a,b)=>Math.abs(a.point[1]-origin[1])-Math.abs(b.point[1]-origin[1]) || Math.abs(a.point[0]-origin[0])-Math.abs(b.point[0]-origin[0]) || a.index-b.index);
  return candidates[0]?.index ?? (current+(forward?1:-1)+boxes.length)%boxes.length;
}

/** One Tab stop for command buttons; native form controls keep their normal Tab stops and editing keys.
 * Tab always leaves the command composite. No command is executed by navigation.
 */
export function bindRovingToolbar(element, {items=()=>[...element.querySelectorAll('button')], orientation='both', onEscape, onTab}={}) {
  let current=null, disposed=false;
  const managed=new Map();
  function refresh() {
    if (disposed) return [];
    const all=items(), choices=all.filter(visibleRibbonControl);
    const focused=choices.includes(document.activeElement)?document.activeElement:null;
    current=focused || (choices.includes(current)?current:choices[0]) || null;
    for (const item of all) {
      if (!managed.has(item)) managed.set(item,item.getAttribute('tabindex'));
      item.setAttribute('tabindex',item===current?'0':'-1');
    }
    // Avoid retaining detached controls across periodic host redraws.
    for (const item of managed.keys()) if (!all.includes(item)) managed.delete(item);
    return choices;
  }
  function restore(item,original) { if(original===null)item.removeAttribute?.('tabindex');else item.setAttribute('tabindex',original); }
  function focus(last=false) {
    const choices=refresh(),target=last===true?choices.at(-1):current || choices[0];
    if(target){current=target;target.focus({preventScroll:true});refresh();target.scrollIntoView?.({block:'nearest',inline:'nearest'});}return target || null;
  }
  const focusin=()=>refresh();
  const keydown=event=>{
    if(disposed || event.defaultPrevented || event.isComposing || event.ctrlKey || event.metaKey || event.altKey || nativeRibbonInput(event.target))return;
    if(event.key==='Escape' && onEscape){event.preventDefault();event.stopPropagation();onEscape();return;}
    if(event.key==='Tab'){onTab?.();return;}
    if(event.shiftKey)return;
    const keys=orientation==='horizontal'?['ArrowLeft','ArrowRight','Home','End']:orientation==='vertical'?['ArrowUp','ArrowDown','Home','End']:['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'];
    if(!keys.includes(event.key))return;
    const choices=refresh();if(!choices.length)return;
    const index=choices.indexOf(document.activeElement);
    const next=orientation==='both'?nextRibbonIndex(choices.map(node=>node.getBoundingClientRect()),index,event.key):
      event.key==='Home'?0:event.key==='End'?choices.length-1:index<0?0:(index+(['ArrowLeft','ArrowUp'].includes(event.key)?-1:1)+choices.length)%choices.length;
    current=choices[next];event.preventDefault();event.stopPropagation();current.focus({preventScroll:true});refresh();current.scrollIntoView?.({block:'nearest',inline:'nearest'});
  };
  element.addEventListener('keydown',keydown);element.addEventListener('focusin',focusin);
  const observer=typeof MutationObserver==='function'?new MutationObserver(()=>refresh()):null;
  observer?.observe(element,{subtree:true,childList:true,attributes:true,attributeFilter:['disabled','hidden','aria-disabled','aria-hidden','class']});
  refresh();
  return {refresh,focus,dispose(){if(disposed)return;disposed=true;observer?.disconnect();element.removeEventListener?.('keydown',keydown);element.removeEventListener?.('focusin',focusin);for(const [item,original]of managed)restore(item,original);managed.clear();}};
}
