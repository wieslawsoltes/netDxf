import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collapsedPanels, chromePreferences, flyoutPosition } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-layout.mjs';

test('ribbon retains full panels when space is available and collapses trailing panels first',()=>{
  assert.deepEqual(collapsedPanels([240,300,130],700),[false,false,false]);
  assert.deepEqual(collapsedPanels([240,300,130],600),[false,false,true]);
  assert.deepEqual(collapsedPanels([240,300,130],450),[false,true,true]);
  assert.deepEqual(collapsedPanels([240,300,130],240),[true,true,true]);
});
test('all Home panels fit at the narrow Studio panel width without losing any tool',()=>{
  const widths=[350,470,170,220,150,130,80], collapsed=collapsedPanels(widths,435);
  assert(collapsed.every(Boolean));
  assert(widths.reduce((sum,width,index)=>sum+(collapsed[index]?56:width),0)+12<=435);
  assert.deepEqual(collapsedPanels([],20),[]);
  assert.deepEqual(collapsedPanels([40,160],100),[false,true]);
});
test('command line, command history and ribbon visibility restore independently',()=>{
  assert.deepEqual(chromePreferences(),{ribbonMode:'full',commandLineVisible:true,commandHistoryVisible:true});
  assert.deepEqual(chromePreferences({ribbonMinimized:true,commandLineVisible:false,commandHistoryVisible:false}),{ribbonMode:'tabs',commandLineVisible:false,commandHistoryVisible:false});
  assert.equal(chromePreferences({commandHistoryVisible:false}).commandLineVisible,true);
  assert.equal(chromePreferences({commandLineVisible:false}).commandHistoryVisible,true);
  assert.deepEqual(chromePreferences({ribbonMinimized:'true',commandLineVisible:'false'}),chromePreferences());
});

test('ribbon modes restore safely and migrate the old minimized preference',()=>{
  for(const ribbonMode of ['full','buttons','titles','tabs'])assert.equal(chromePreferences({ribbonMode,ribbonMinimized:true}).ribbonMode,ribbonMode);
  assert.equal(chromePreferences({ribbonMode:'invalid'}).ribbonMode,'full');
});
test('panel flyouts stay on screen at the right and bottom edges',()=>{
  assert.deepEqual(flyoutPosition({left:950,bottom:700},{width:300,height:180},{width:1024,height:768}),{left:720,top:584});
  assert.deepEqual(flyoutPosition({left:-20,bottom:5},{width:600,height:900},{width:400,height:600}),{left:4,top:4});
});
