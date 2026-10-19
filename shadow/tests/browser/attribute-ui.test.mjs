import test from 'node:test';
import assert from 'node:assert/strict';
import {attributeTools} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/attribute-ui.mjs';
import {attributeChanges} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/attribute-contracts.mjs';
import {installDom,byLabel,byText,gate,tick} from './ui-fixture.mjs';

const attribute={id:{value:'a:1'},tag:'TAG',prompt:'Tag',value:'original',multiline:false,constant:false,duplicateTag:false,
  textStyle:'Standard',alignment:'BaselineLeft',height:2,rotation:0,width:0,widthFactor:1,obliqueAngle:0,backward:false,upsideDown:false,
  layer:'0',linetype:'ByLayer',lineweight:'ByLayer',color:'ByLayer',invisible:false,positionLocked:false,lineSpacing:1,exactSpacing:false};
function fixture(t,multiline=false,editGate=null) {
  const root=installDom(t),answer=gate(),reads=gate(),edits=[];
  const resources={layers:[{name:'0'},{name:'Process'}],textStyles:[{status:{name:'Standard'}},{status:{name:'Review'}}],linetypes:[{status:{name:'ByLayer'}},{status:{name:'ByBlock'}}]};
  let data={revision:1,insert:{value:'insert:1'},blockName:'Pump',attributes:[{...attribute,multiline,alignment:multiline?'TopLeft':'BaselineLeft'}]};
  const client={key:'doc/1',state:{revision:1},busy:0,read:path=>path==='resources'?Promise.resolve(resources):reads.promise};
  const edit=async(...args)=>{
    edits.push(args);if(editGate)await editGate.promise;
    data={...data,revision:2,attributes:data.attributes.map(a=>({...a,...args[1].attributes.find(x=>x.attribute.value===a.id.value)}))};
    client.key='doc/2';client.state={revision:2};client.read=async path=>path==='resources'?resources:data;return {state:{revision:2}};
  };
  const open=attributeTools({client,edit,editable(){},dialog:(title,children)=>{root.replaceChildren(...children);return answer.promise;}});
  return {root,answer,reads,edits,client,open,data};
}
test('attribute editor submits only authored differences using object identities',async t=>{
  const f=fixture(t),pending=f.open();f.reads.resolve(f.data);await tick();
  assert.deepEqual(byLabel(f.root,'Attribute text style').children.map(node=>node.value),['Standard','Review']);
  assert.deepEqual(byLabel(f.root,'Attribute linetype').children.map(node=>node.value),['ByLayer','ByBlock']);
  byLabel(f.root,'Attribute value').value='P-101';byLabel(f.root,'Attribute height').value='4';
  f.answer.resolve(true);await pending;
  assert.deepEqual(f.edits,[['attributes',{insert:{value:'insert:1'},attributes:[{attribute:{value:'a:1'},value:'P-101',height:4}]},{expectedKey:'doc/1'}]]);
});
test('unchanged editor creates no mutation',async t=>{
  const f=fixture(t),pending=f.open();f.reads.resolve(f.data);await tick();f.answer.resolve(true);await pending;assert.equal(f.edits.length,0);
});
test('Apply keeps editor open and refreshed OK does not duplicate the edit',async t=>{
  const f=fixture(t),pending=f.open();f.reads.resolve(f.data);await tick();
  byLabel(f.root,'Attribute value').value='Applied';await byText(f.root,'Apply attributes').click();
  assert.equal(f.edits.length,1);assert.equal(byLabel(f.root,'Attribute value').value,'Applied');
  f.answer.resolve(true);await pending;assert.equal(f.edits.length,1);
});
for(const accepted of [true,false])test(`closing during Apply waits without a duplicate edit (${accepted?'OK':'Cancel'})`,async t=>{
  const editGate=gate(),f=fixture(t,false,editGate),pending=f.open();f.reads.resolve(f.data);await tick();
  byLabel(f.root,'Attribute value').value='Applied';const applying=byText(f.root,'Apply attributes').click();
  assert.equal(f.edits.length,1);f.answer.resolve(accepted);
  let settled=false;pending.then(()=>{settled=true;});await tick();assert.equal(settled,false);
  editGate.resolve();await applying;await pending;assert.equal(f.edits.length,1);
});
test('multiline editing preserves hidden fallback controls and encodes paragraphs',async t=>{
  const f=fixture(t,true),pending=f.open();f.reads.resolve(f.data);await tick();
  assert.equal(byLabel(f.root,'Attribute width factor'),undefined);byLabel(f.root,'Multiline attribute value').value='first\nsecond';
  f.answer.resolve(true);await pending;assert.equal(f.edits[0][1].attributes[0].value,'first\\Psecond');
});
test('stale selection or drawing cannot submit the open attribute editor',async t=>{
  const f=fixture(t),pending=f.open();f.reads.resolve(f.data);await tick();byLabel(f.root,'Attribute value').value='stale';
  f.client.key='other';f.answer.resolve(true);await assert.rejects(pending,/drawing or selection changed/);assert.equal(f.edits.length,0);
});
test('late discovery cannot open an attribute editor over a new document',async t=>{
  const f=fixture(t),pending=f.open();f.client.key='other';f.reads.resolve(f.data);await pending;assert.equal(f.root.children.length,0);
});
test('pure attribute contract refuses malformed numbers, colors, constants and single-line breaks',()=>{
  for(const input of [{height:'NaN'},{height:''},{color:'red'},{color:'999'},{value:'first\nsecond'},{alignment:'invalid'}])
    assert.throws(()=>attributeChanges(attribute,input));
  assert.throws(()=>attributeChanges({...attribute,constant:true},{value:'changed'}));
  assert.deepEqual(attributeChanges(attribute,{value:'original',height:'2',color:'ByLayer'}),null);
});
