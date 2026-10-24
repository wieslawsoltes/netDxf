import {test} from 'node:test';
import assert from 'node:assert/strict';
import {commandKeytips,matchingKeytips,tabKeytips} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-keytips.mjs';
import {ribbonCommandIdentity} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/ribbon-context.mjs';
import {ribbonDom} from './ribbon-dom-fixture.mjs';

test('command KeyTips are mnemonic, deterministic, unique and never prefix-ambiguous',()=>{
  const labels=['Line','Polyline','Circle','Copy','Command','Current layer',...Array.from({length:150},()=> 'Extra tool')];
  const codes=commandKeytips(labels);assert.equal(codes[0],'LI');assert.equal(codes[1],'PO');assert.equal(new Set(codes).size,labels.length);assert(codes.every(code=>code.length===2));
  assert.deepEqual(commandKeytips(labels),codes);const items=codes.map(code=>({code}));assert.deepEqual(matchingKeytips(items,'li'),[{code:'LI'}]);assert(matchingKeytips(items,'C').length>2);
});
test('many module and custom tabs use a reserved overflow prefix without stealing built-in tips',()=>{
  const ids=[...Array.from({length:80},(_,i)=>`module:${i}`),'Home','Insert','Manage'],codes=tabKeytips(ids);
  assert.equal(codes.at(-3),'H');assert.equal(codes.at(-2),'N');assert.equal(codes.at(-1),'M');assert.equal(new Set(codes).size,ids.length);
  assert(codes.every(code=>typeof code==='string'));assert(!codes.includes('F'));assert(!codes.includes('Z'));
  assert(codes.some(code=>code.startsWith('Z')));for(const code of codes)assert(!codes.some(other=>other!==code&&other.startsWith(code)));
});
test('ribbon context resolves exact native, custom and module catalog identities',t=>{
  ribbonDom(t);
  for(const [attribute,value,expected]of [['data-command','LINE','command:LINE'],['data-action','graph-query:path','action:graph-query:path'],['data-ribbon-command-id','action:undo','action:undo'],['data-command-id','command:module.tool','command:module.tool']]){
    const button=document.createElement('button'),child=document.createElement('span');button.setAttribute(attribute,value);button.append(child);assert.equal(ribbonCommandIdentity(child),expected);
  }
  assert.equal(ribbonCommandIdentity(document.createElement('button')),null);
});
