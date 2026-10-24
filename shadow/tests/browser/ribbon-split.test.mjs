import test from 'node:test';
import assert from 'node:assert/strict';
import { RibbonLayout } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-layout.mjs';
import { RibbonCustomization } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-customization.mjs';
import { RibbonSplitMenus, splitMenuPosition, splitTool } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-split.mjs';
import { el, group, ribbon, tool } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ui.mjs';
import { ribbonDom } from './ribbon-dom-fixture.mjs';

function fixture(t) {
  const dom = ribbonDom(t), primary = tool('ARC','Arc'), ellipse = tool('ELLIPSE','Ellipse'), spline = tool('SPLINE','Spline');
  const split = splitTool(primary,[ellipse,spline],'Curves'), draw = group('Draw',[split]); dom.panel.append(draw);
  let chosen = 0;
  const menus = new RibbonSplitMenus({onChoose:()=>chosen++}); menus.set([draw]); dom.disposeWith(()=>menus.dispose());
  const toggle = split.querySelector('[data-ribbon-split-toggle]'), menu = split.querySelector('.ribbon-split-menu');
  return {...dom,primary,ellipse,spline,split,draw,menus,toggle,menu,chosen:()=>chosen};
}

test('split command families retain their original actions and expose alternatives with keyboard navigation',t=>{
  const f=fixture(t); let invoked=0;f.ellipse.addEventListener('click',()=>invoked++);
  assert.equal(f.menu.hidden,true);assert.equal(f.primary.getAttribute('data-command'),'ARC');
  f.events.emit('keydown',{target:f.toggle,key:'ArrowDown'});
  assert.equal(f.menus.popup.menu,f.menu);assert.equal(f.menu.parentNode,document.body);
  assert.equal(f.toggle.getAttribute('aria-expanded'),'true');assert.equal(document.activeElement,f.ellipse);
  f.menu.emit('keydown',{target:f.ellipse,key:'ArrowDown'});assert.equal(document.activeElement,f.spline);
  f.ellipse.emit('click');f.events.emit('click',{target:f.ellipse});
  assert.equal(invoked,1);assert.equal(f.chosen(),1);assert.equal(f.menus.popup,null);
  assert.equal(f.menu.parentNode,f.split);assert.equal(f.menu.hidden,true);assert.equal(f.toggle.getAttribute('aria-expanded'),'false');
});
test('Escape restores the split arrow and outside focus closes the menu without moving that focus',t=>{
  const f=fixture(t),outside=el('button',{text:'Outside'});f.host.append(outside);
  f.menus.open(f.toggle);let prevented=0;
  f.events.emit('keydown',{target:f.ellipse,key:'Escape',preventDefault:()=>prevented++});
  assert.equal(prevented,1);assert.equal(document.activeElement,f.toggle);assert.equal(f.menu.hidden,true);
  f.menus.open(f.toggle);outside.focus();f.events.emit('focusin',{target:outside});
  assert.equal(f.menus.popup,null);assert.equal(document.activeElement,outside);
  f.menus.open(f.toggle);f.events.emit('pointerdown',{target:outside});assert.equal(f.menus.popup,null);
});
test('split menu skips disabled alternatives, supports upward entry, and does not capture native editing or modified shortcuts',t=>{
  const f=fixture(t);f.ellipse.disabled=true;
  f.events.emit('keydown',{target:f.toggle,key:'ArrowUp'});assert.equal(document.activeElement,f.spline);
  f.menus.close();let prevented=0;
  for(const extra of [{ctrlKey:true},{metaKey:true},{altKey:true},{isComposing:true},{defaultPrevented:true}])
    f.events.emit('keydown',{target:f.toggle,key:'ArrowDown',preventDefault:()=>prevented++,...extra});
  assert.equal(prevented,0);assert.equal(f.menus.popup,null);
  const input=el('input');f.host.append(input);f.menus.open(f.toggle);
  f.events.emit('keydown',{target:input,key:'Escape',preventDefault:()=>prevented++});assert.equal(prevented,0);
  f.menu.emit('keydown',{target:f.spline,key:'Tab',preventDefault:()=>prevented++});assert.equal(prevented,0);assert.equal(f.menus.popup,null);
});
test('responsive panel flyouts expose the same alternatives inline and restore their menu roles when closed',t=>{
  const dom=ribbonDom(t),layout=new RibbonLayout(dom.panel);dom.disposeWith(()=>layout.dispose());
  const alternative=tool('POLYGON','Polygon'),split=splitTool(tool('RECTANG','Rectangle'),[alternative],'Shapes'),draw=group('Draw',[split]);
  layout.set([draw],{tab:'Home'});const menu=split.querySelector('.ribbon-split-menu'),toggle=split.querySelector('[data-ribbon-split-toggle]');
  layout.setMode('buttons');layout.open(draw);
  assert.equal(menu.hidden,false);assert.equal(toggle.hidden,true);assert.equal(menu.getAttribute('role'),'group');assert.equal(alternative.getAttribute('role'),null);
  assert.equal(layout.popup.tools.contains(alternative),true);
  layout.close();assert.equal(menu.hidden,true);assert.equal(toggle.hidden,false);assert.equal(alternative.getAttribute('role'),'menuitem');
  assert.equal(menu.parentNode,split);
});
test('ribbon redraw and disposal remove transient split menus and all delegated lifetimes',t=>{
  const f=fixture(t);f.menus.open(f.toggle);f.menus.set([]);
  assert.equal(document.body.querySelector('.ribbon-command-menu'),null);assert.equal(f.menu.parentNode,f.split);
  f.events.emit('click',{target:f.toggle});assert.equal(f.menus.popup,null,'detached or unowned command groups cannot open');
  f.menus.set([f.draw]);f.menus.open(f.toggle);f.menus.dispose();
  assert.equal(document.body.querySelector('.ribbon-command-menu'),null);
  for(const name of ['click','keydown','pointerdown','focusin','scroll'])assert.equal(f.events.handlers.get(name).length,0);
});
test('split tools remain owned in floating ribbon panels across ordinary tab switches',t=>{
  const dom=ribbonDom(t),store=new RibbonCustomization({storage:{getItem:()=>null,setItem(){}}}),layout=new RibbonLayout(dom.panel,{customization:store});dom.disposeWith(()=>layout.dispose());
  const make=()=>group('Draw',[splitTool(tool('ARC','Arc'),[tool('ELLIPSE','Ellipse')],'Curves')]);
  const draw=make(),entries=[{key:'Home'},{key:'View'}],resolveGroups=name=>name==='Home'?[draw]:[];
  layout.set([draw],{tab:'Home',entries,resolveGroups});layout.floatPanel(draw);
  layout.set([],{tab:'View',entries,resolveGroups});
  const toggle=draw.querySelector('[data-ribbon-split-toggle]');dom.events.emit('click',{target:toggle});
  assert.equal(layout.splitMenus.popup?.toggle,toggle);assert(layout.containsFocus());
  layout.set([],{tab:'View',entries:[],resolveGroups});assert.equal(layout.splitMenus.popup,null);
  assert.equal(document.activeElement,dom.tab,'suspending a floated source while a split menu has focus restores the visible ribbon tab');
});
test('Home ribbon retains every drawing and modification command while sharing concise labelled primary groups',t=>{
  ribbonDom(t);const groups=ribbon('Home',{layers:[{name:'0'}]},'0',()=>{}),host=document.createElement('div');host.append(...groups);
  const names=host.querySelectorAll('[data-command]').map(node=>node.getAttribute('data-command'));
  for(const name of ['LINE','PLINE','CIRCLE','ARC','RECTANG','ELLIPSE','POLYGON','SPLINE','POINT','MOVE','COPY','STRETCH','BREAK','BREAKATPOINT','ERASE','ROTATE','MIRROR','SCALE','TRIM','EXTEND','OFFSET','FILLET','CHAMFER','ARRAY','JOIN','EXPLODE','MTEXT','DIMALIGNED','TEXT','DIMLINEAR','DIST','HATCHPOLY'])assert(names.includes(name),name);
  assert.equal(new Set(names).size,names.length,'compact menus never duplicate command controls');
  assert.equal(groups[0].querySelectorAll('.ribbon-split').length,2);
  assert.equal(host.querySelectorAll('.ribbon-icon-tool').every(node=>!!node.getAttribute('aria-label')),true);
});
test('split menus flip above low anchors and clamp to viewport edges',()=>{
  assert.deepEqual(splitMenuPosition({left:950,top:700,bottom:726},{width:180,height:96},{width:1024,height:768}),{left:840,top:601});
  assert.deepEqual(splitMenuPosition({left:6,top:80,bottom:105},{width:180,height:96},{width:1024,height:768}),{left:6,top:108});
  assert.deepEqual(splitMenuPosition({left:-20,top:0,bottom:20},{width:500,height:900},{width:390,height:600}),{left:4,top:4});
});
