import test from 'node:test';
import assert from 'node:assert/strict';
import {attributeTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/attribute-ui.mjs';
import {definitionTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/attribute-definition-ui.mjs';
import {definitionChanges,definitionBatch} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/attribute-definition-contracts.mjs';
import {installDom,byLabel,byText,gate,tick} from './ui-fixture.mjs';

const item=tag=>({id:{value:tag},tag,prompt:'Prompt',value:'Default',multiline:false,constant:false,
  textStyle:'Standard',alignment:'BaselineLeft',height:2.5,rotation:0,width:1,widthFactor:1,obliqueAngle:0,
  backward:false,upsideDown:false,layer:'0',linetype:'ByLayer',lineweight:'ByLayer',color:'ByLayer',invisible:false,
  positionLocked:false,lineSpacing:1,exactSpacing:false,verify:false,preset:false});
function fixture(t,{constant=false,version='AutoCad2018'}={}) {
  const root=installDom(t),answer=gate(),edits=[];
  let data={block:{value:'block'},name:'PUMP',referenceCount:2,currentSpaceCount:1,drawingVersion:version,
    definitions:[{appearance:{...item('A'),constant},verify:false,preset:false},{appearance:item('B'),verify:false,preset:false}]};
  const resources={layers:[{name:'0'}],textStyles:[{status:{name:'Standard'}}],linetypes:[{status:{name:'ByLayer'}}]};
  const client={key:'doc/1',state:{revision:1},busy:0,data:{blocks:[{id:'block',name:'PUMP'}],selectedEntities:['insert'],items:[{id:'insert',block:'block'}]},
    read:async path=>path==='resources'?resources:data};
  const edit=async(path,body,options)=>{
    edits.push([path,body,options]);const changes=body.definition.changes;
    const removed=new Set((changes.remove||[]).map(id=>id.value));
    let defs=data.definitions.filter(d=>!removed.has(d.appearance.id.value)).map(d=>{
      const change=changes.edits?.find(e=>e.definition.value===d.appearance.id.value);
      return change?{...d,...change,appearance:{...d.appearance,...change.appearance,...(change.tag?{tag:change.tag}:{}),...(change.prompt?{prompt:change.prompt}:{})}}:d;
    });
    if(changes.order)defs=changes.order.map(id=>defs.find(d=>d.appearance.id.value===id.value));
    data={...data,definitions:defs};client.key='doc/2';client.state.revision=2;return {state:{revision:2}};
  };
  const options={client,edit,editable(){},dialog:(title,children)=>{root.replaceChildren(...children);return answer.promise;}};
  return {root,answer,edits,client,options,open:attributeTools({...options,definitionBlock:'block'})};
}
test('definition contracts allow constant defaults and retain unchanged imported tag casing',()=>{
  const original={...item('lower'),constant:true};assert.equal(definitionChanges(original,{tag:'lower',value:'Default'}),null);
  assert.deepEqual(definitionChanges(original,{tag:'new',value:'New default',verify:true}),
    {definition:{value:'lower'},appearance:{attribute:{value:'lower'},value:'New default'},tag:'NEW',verify:true});
  assert.throws(()=>definitionChanges(original,{tag:'bad tag'}));
  assert.throws(()=>definitionBatch([item('A'),item('B')],[item('A'),item('B')],new Map([['A',{tag:'b'}]]),new Set()));
});
test('BATTMAN edits metadata and appearance in one batch and keeps Apply open',async t=>{
  const f=fixture(t),pending=f.open();await tick();byLabel(f.root,'Attribute tag').value='equipment';
  byLabel(f.root,'Attribute default').value='P-101';byLabel(f.root,'Attribute height').value='4';
  assert.equal(byLabel(f.root,'Apply changes to existing references').checked,true);await byText(f.root,'Apply attributes').click();
  assert.equal(f.edits.length,1);assert.equal(f.edits[0][0],'attribute-definitions');
  const operation=f.edits[0][1].definition;assert.equal(operation.updateExisting,true);assert.equal(operation.changes.edits[0].tag,'EQUIPMENT');
  assert.equal(operation.changes.edits[0].appearance.height,4);assert.equal(byLabel(f.root,'Attribute tag').value,'EQUIPMENT');
  f.answer.resolve(true);await pending;assert.equal(f.edits.length,1);
});
test('BATTMAN stages order and removal; Cancel discards the staged batch',async t=>{
  const f=fixture(t),pending=f.open();await tick();await byText(f.root,'Move down').click();
  assert.deepEqual(byLabel(f.root,'Block attributes').children.map(n=>n.value),['B','A']);
  await byText(f.root,'Remove definition').click();assert.equal(byText(f.root,'Remove definition').disabled,true);
  f.answer.resolve(false);await pending;assert.equal(f.edits.length,0);
});
test('constant definition values are editable but their move controls are disabled',async t=>{
  const f=fixture(t,{constant:true}),pending=f.open();await tick();assert.equal(byLabel(f.root,'Attribute default').readOnly,false);
  assert.equal(byText(f.root,'Move down').disabled,true);f.answer.resolve(false);await pending;
});
test('Sync is explicit and works without staged field edits',async t=>{
  const f=fixture(t),pending=f.open();await tick();await byText(f.root,'Sync references').click();
  assert.deepEqual(f.edits[0][1].definition,{block:{value:'block'},changes:{},updateExisting:true});
  f.answer.resolve(false);await pending;
});
test('ATTDEF creates a local definition with explicit block coordinates and modes',async t=>{
  const f=fixture(t),pending=definitionTools(f.options).create();await tick();
  byLabel(f.root,'Definition tag').value='SERVICE';byLabel(f.root,'Definition default').value='Water';
  byLabel(f.root,'Definition insertion point').value='4,6';byLabel(f.root,'Multiple lines definition').checked=true;
  f.answer.resolve(true);await pending;const created=f.edits[0][1].definition.changes.add[0];
  assert.equal(created.tag,'SERVICE');assert.deepEqual(created.position,{x:4,y:6,z:0});assert.equal(created.multiline,true);assert.equal(created.positionLocked,true);
});
test('ATTDEF reflects old DXF profile capabilities and refuses stale dialog submission',async t=>{
  const f=fixture(t,{version:'AutoCad2000'}),pending=definitionTools(f.options).create();await tick();
  assert.equal(byLabel(f.root,'Multiple lines definition').disabled,true);assert.equal(byLabel(f.root,'Lock definition location').disabled,true);
  f.client.key='other';f.answer.resolve(true);await assert.rejects(pending,/drawing changed/);assert.equal(f.edits.length,0);
});
