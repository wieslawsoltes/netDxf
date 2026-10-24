import test from 'node:test';
import assert from 'node:assert/strict';
import { adjacentTab, commandWindowRect, shellPreferences } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-shell-model.mjs';
import { workspaceProfileLayout, changeWorkspaceProfiles } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-profiles.mjs';
import { exportWorkspace, parseWorkspace } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-transfer.mjs';

test('shell restores presentation choices without adopting drawing settings or executable properties',()=>{
  const source={version:1,indicators:['grid','unknown','osnap','grid'],statusLabels:true,coordinates:false,
    workspaceName:' Review ',commandWindow:{mode:'float',width:700,height:180,x:100,y:200,credentials:'discard'},
    settings:{grid:false},documentId:'private',script:'discard'};
  const result=shellPreferences(source);
  assert.deepEqual(result.indicators,['grid','osnap']);assert.equal(result.statusLabels,true);assert.equal(result.coordinates,false);
  assert.equal(result.workspaceName,'Review');assert.equal(result.commandWindow.mode,'float');
  assert(!JSON.stringify(result).includes('private'));assert(!JSON.stringify(result).includes('discard'));assert(!('settings'in result));
  source.indicators.push('ortho');assert.deepEqual(result.indicators,['grid','osnap']);
});
test('empty indicator choices remain intentionally empty; malformed and future preferences recover defaults',()=>{
  assert.deepEqual(shellPreferences({indicators:[]}).indicators,[]);
  for(const value of [null,[],{version:20},{indicators:'grid',statusLabels:'true',coordinates:'false'}])assert.deepEqual(shellPreferences(value),shellPreferences());
  assert.equal(shellPreferences({workspaceName:'\u0000unsafe'}).workspaceName,'Drafting');
});
test('command windows from larger monitors are reachable in small and narrow hosts',()=>{
  const value={mode:'float',x:15000,y:18000,width:1600,height:800};
  for(const bounds of [{width:1280,height:700},{width:390,height:350},{width:100,height:70}]){
    const rect=commandWindowRect(value,bounds);
    assert(rect.x>=4&&rect.y>=4);assert(rect.width>0&&rect.height>0);
    assert(rect.x+rect.width<=bounds.width-4);assert(rect.y+rect.height<=bounds.height-4);
  }
  const rect=commandWindowRect({x:NaN,y:Infinity,width:-1,height:Infinity},{width:800,height:600});
  for(const number of Object.values(rect))assert(Number.isFinite(number));
});
test('saved and transferred workspaces preserve floating command and status preferences through normalization',()=>{
  const saved=changeWorkspaceProfiles(null,{type:'save',name:'Layout review',layout:{shell:{version:1,
    indicators:['grid','osnap'],coordinates:false,statusLabels:true,commandWindow:{mode:'float',x:140,y:220,width:650,height:170}}}},['Properties']);
  const source=saved.profiles[0],restored=parseWorkspace(exportWorkspace(source,['Properties']),['Properties']);
  assert.deepEqual(restored.layout,source.layout);assert.equal(restored.layout.shell.commandWindow.mode,'float');
  assert.equal(restored.layout.shell.commandWindow.width,650);assert.equal(restored.layout.shell.coordinates,false);
  assert.equal(workspaceProfileLayout({},['Properties']).shell.commandWindow.mode,'dock');
  assert.throws(()=>exportWorkspace({name:'Invalid',layout:{shell:{version:3}}},['Properties']),/Unsupported shell version/);
});
test('document and paper tabs wrap from both ends and ignore editing keys',()=>{
  const tabs=['Model','A1','A2'];
  assert.equal(adjacentTab(tabs,'Model','ArrowLeft'),'A2');assert.equal(adjacentTab(tabs,'A2','ArrowRight'),'Model');
  assert.equal(adjacentTab(tabs,'A1','Home'),'Model');assert.equal(adjacentTab(tabs,'Model','End'),'A2');
  assert.equal(adjacentTab(tabs,'A1','Delete'),null);assert.equal(adjacentTab([],'Model','ArrowRight'),null);
});
