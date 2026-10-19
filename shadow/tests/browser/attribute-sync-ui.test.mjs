import test from 'node:test';
import assert from 'node:assert/strict';
import {synchronizeAttributes} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/attribute-sync-ui.mjs';
import {installDom,byLabel,gate,tick} from './ui-fixture.mjs';

function fixture(t){
  const root=installDom(t),answer=gate(),calls=[],palettes=[];
  const client={key:'doc/1',busy:0,data:{blocks:[{id:'b1',name:'First'},{id:'b2',name:'Selected'}],selectedEntities:['i1'],items:[{id:'i1',block:'b2'}]}};
  const open=()=>synchronizeAttributes({client,editable(){},edit:async(...args)=>{calls.push(args);return {previewId:'review'};},
    showPalette:name=>palettes.push(name),dialog:(_title,body)=>{root.replaceChildren(...body);return answer.promise;}});
  return {root,answer,calls,palettes,client,open};
}
test('ATTSYNC uses selected definition and creates a proposal without applying it',async t=>{
  const f=fixture(t),pending=f.open();await tick();assert.equal(byLabel(f.root,'Block to synchronize').value,'b2');
  f.answer.resolve(true);await pending;
  assert.deepEqual(f.calls,[['edit-preview',{description:'ATTSYNC Selected',operations:[{kind:'attribute-sync',attributeSync:{block:{value:'b2'},convertTextModes:false}}]},{expectedKey:'doc/1'}]]);
  assert.deepEqual(f.palettes,['Edit previews']);
});
test('ATTSYNC text conversion is explicit and Cancel makes no proposal',async t=>{
  const f=fixture(t),cancel=f.open();f.answer.resolve(false);await cancel;assert.equal(f.calls.length,0);
  const g=fixture(t),pending=g.open();await tick();byLabel(g.root,'Allow attribute text mode conversion').checked=true;
  g.answer.resolve(true);await pending;assert.equal(g.calls[0][1].operations[0].attributeSync.convertTextModes,true);
});
test('ATTSYNC refuses a stale open dialog and a busy editor',async t=>{
  const f=fixture(t),pending=f.open();f.client.key='doc/2';f.answer.resolve(true);await assert.rejects(pending,/drawing or selection changed/);
  assert.equal(f.calls.length,0);f.client.busy=1;await assert.rejects(f.open(),/Finish the current edit/);
});
