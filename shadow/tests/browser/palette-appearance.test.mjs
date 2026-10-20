import test from 'node:test';
import assert from 'node:assert/strict';
import { paletteAppearance, paletteOpacity, openPaletteAppearance } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-appearance.mjs';
import { paletteLayout, changePaletteLayout } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/palette-layout.mjs';
import { installDom, all, byLabel, byText } from './ui-fixture.mjs';

test('appearance normalizes untrusted preferences and rollover never becomes less visible', () => {
  assert.deepEqual(paletteAppearance(null), { opacity: 100, rolloverOpacity: 100 });
  assert.deepEqual(paletteAppearance({ opacity: -10, rolloverOpacity: 200 }), { opacity: 0, rolloverOpacity: 100 });
  assert.deepEqual(paletteAppearance({ opacity: 67.7, rolloverOpacity: 20 }), { opacity: 68, rolloverOpacity: 68 });
  assert.deepEqual(paletteAppearance({ opacity: '20', rolloverOpacity: Infinity }), { opacity: 100, rolloverOpacity: 100 });
  const layout = paletteLayout({version:3,transparencyDisabled:'true',panels:{Graph:{opacity:40,rolloverOpacity:70,secret:'excluded'}}},['Graph']);
  assert.equal(layout.transparencyDisabled, false);
  assert.equal(layout.panels.Graph.opacity, 40);
  assert(!JSON.stringify(layout).includes('secret'));
});

test('opaque docking, keyboard focus, menus and the global override take precedence over hover', () => {
  const appearance = {opacity:20,rolloverOpacity:65};
  assert.equal(paletteOpacity(appearance),1);
  assert.equal(paletteOpacity(appearance,{floating:true}),.2);
  assert.equal(paletteOpacity(appearance,{floating:true,hovered:true}),.65);
  for(const key of ['disabled','focused','menuOpen'])
    assert.equal(paletteOpacity(appearance,{floating:true,hovered:true,[key]:true}),1);
});

test('all-palette apply is immutable, persistent and independent of position locking', () => {
  const layout = paletteLayout(null,['Properties','Graph']);
  layout.locked=true;
  const next=changePaletteLayout(layout,{type:'opacity',id:'Graph',opacity:25,rolloverOpacity:75,all:true,disabled:true});
  assert.equal(layout.panels.Graph.opacity,100);
  assert.equal(next.panels.Properties.opacity,25);assert.equal(next.panels.Graph.rolloverOpacity,75);
  assert(next.transparencyDisabled);assert(next.locked);assert.deepEqual(next.groups,layout.groups);
  assert.equal(JSON.stringify(paletteLayout(next,['Properties','Graph'])),JSON.stringify(next));
  assert.equal(changePaletteLayout(next,{type:'transparency-disabled',value:false}).transparencyDisabled,false);
  for(const [opacity,rolloverOpacity] of [[90,20],[NaN,70],[10,101],[10.5,70]])
    assert.throws(()=>changePaletteLayout(layout,{type:'opacity',id:'Graph',opacity,rolloverOpacity}),/opacity/);
});

function fixture(t) {
  const root=installDom(t),create=document.createElement;
  document.body=root;
  document.createElement=tag=>{
    const item=create(tag);item.style={};item.setPointerCapture=()=>{};
    if(tag==='dialog'){item.showModal=()=>{item.open=true;};item.close=()=>{item.open=false;};}
    return item;
  };
  document.querySelector=()=>all(root).find(item=>item.tagName==='DIALOG'&&item.open)||null;
  const origin=document.createElement('button');root.append(origin);origin.focus();
  const calls=[];
  const controller=openPaletteAppearance({title:'Graph',appearance:{opacity:30,rolloverOpacity:80},onApply:value=>calls.push(value)});
  t.after(()=>controller.close());
  return {root,origin,calls,controller};
}

test('draft preview and cancel never apply preferences and restore the originating control', async t=>{
  const {root,origin,calls,controller}=fixture(t);
  const normal=byLabel(root,'Palette opacity'),rollover=byLabel(root,'Rollover opacity');
  normal.value='90';await normal.dispatch('input');assert.equal(rollover.value,'90');
  const preview=all(root).find(node=>node.getAttribute('class')==='pd-opacity-sample');
  assert.equal(preview.style.opacity,'0.9');
  await byText(root,'Hold to preview rollover').dispatch('keydown',{key:'Enter'});
  assert.deepEqual(calls,[]);
  await byText(root,'Cancel').click();assert(!controller.dialog.isConnected);assert.equal(document.activeElement,origin);
});

test('apply submits explicit all-palettes/global settings once and preserves independent preview values', async t=>{
  const {root,calls,controller}=fixture(t);
  const allPalettes=byLabel(root,'Apply transparency to all palettes'),disabled=byLabel(root,'Disable all palette transparency');
  allPalettes.checked=true;disabled.checked=true;await disabled.dispatch('input');
  assert(byLabel(root,'Palette opacity').disabled);
  await byText(root,'Apply transparency').click();
  assert.deepEqual(calls,[{opacity:30,rolloverOpacity:80,all:true,disabled:true}]);assert(!controller.dialog.isConnected);
});

test('Escape and an existing modal prevent accidental application or stacked dialogs', async t=>{
  const {root,calls,controller}=fixture(t);
  assert.equal(openPaletteAppearance({title:'Other',onApply:()=>{throw new Error();}}),null);
  await controller.dialog.dispatch('keydown',{key:'Escape',stopPropagation(){}});
  assert.deepEqual(calls,[]);assert(!all(root).some(node=>node.tagName==='DIALOG'));
});
