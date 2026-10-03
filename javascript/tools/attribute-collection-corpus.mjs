// Original collection calls plus post-state and event observations from each implementation.
import {D,I,R,V,A} from './geometry-corpus.mjs';
const N=(type,args=[],id='p',extra={})=>({kind:'new',type,args,id,...extra});
const C=(target,member,args=[],id,extra={})=>({kind:'call',target,member,args,...(id?{id}:{}),...extra});
const G=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const snap=target=>({kind:'snapshot',target});
const observe=target=>['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'].map(member=>({kind:'observe',target,member,observer:member}));
const at=(target,index,id)=>({kind:'index',target,args:[I(index)],id});
const put=(target,key,value)=>({kind:'set-index',target,args:[key],value,...(key===null?{signature:['String']}:{})});
const defs=()=>[N('Entities.AttributeDefinition',['A'],'a'),N('Entities.AttributeDefinition',['b'],'b'),N('Entities.AttributeDefinition',['a'],'other')];
const entities=()=>[N('Entities.Line',[],'a'),N('Entities.Circle',[],'b'),N('Entities.Line',[],'other')];
export function attributeCollectionCorpus(){
 const out=[],add=(name,steps,category='attribute-collections')=>out.push({name:'attribute-collections/'+name,category,request:{steps}});
 for(const value of [-1,0,1,10])for(const type of ['EntityCollection','AttributeDefinitionDictionary'])add(`capacity/${type}/${value}`,[N('Collections.'+type,[I(value)]),G('p','Count'),G('p','IsReadOnly')]);
 for(const action of ['Add','Remove','Insert','RemoveAt','Clear','Replace'])for(const index of [-1,0,1,2,3])for(const useNull of [false,true]){
  const steps=[...entities(),N('Collections.EntityCollection'),...observe('p'),C('p','Add',[R('a')]),C('p','Add',[R('b')])];
  const item=useNull?null:R('other');
  if(action==='Add')steps.push(C('p','Add',[item]));
  if(action==='Remove')steps.push(C('p','Remove',[item],null,{signature:['Entities.EntityObject']}));
  if(action==='Insert')steps.push(C('p','Insert',[I(index),item]));
  if(action==='RemoveAt')steps.push(C('p','RemoveAt',[I(index)]));
  if(action==='Replace')steps.push(put('p',I(index),item));
  if(action==='Clear')steps.push(C('p','Clear'));
  steps.push(snap('p'),{kind:'events'});add(`entities/${action}/${index}/${useNull}`,steps,'entity-collections');
 }
 for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])for(const cancel of [false,true])for(const failure of [false,true])for(const action of ['Add','Insert','Remove','Replace','Clear']){
  const steps=[...entities(),N('Collections.EntityCollection'),C('p','Add',[R('a')]),C('p','Add',[R('b')]),...observe('p'),{kind:'observe',target:'p',member:event,observer:'fault',cancel,throw:failure}];
  if(action==='Add')steps.push(C('p','Add',[R('other')]));
  if(action==='Insert')steps.push(C('p','Insert',[I(0),R('other')]));
  if(action==='Remove')steps.push(C('p','Remove',[R('a')],null,{signature:['Entities.EntityObject']}));
  if(action==='Replace')steps.push(put('p',I(0),R('other')));
  if(action==='Clear')steps.push(C('p','Clear'));
  steps.push(snap('p'),{kind:'events'});add(`entity-events/${event}/${cancel}/${failure}/${action}`,steps,'entity-collections');
 }
 for(const items of [null,[],[R('a'),R('b'),R('a')]])for(const member of ['AddRange','Remove'])add(`entity-range/${member}/${out.length}`,[...entities(),N('Collections.EntityCollection'),...observe('p'),C('p','Add',[R('a')]),C('p',member,[items===null?null:A('Entities.EntityObject',items)],null,{signature:['IEnumerable<Entities.EntityObject>']}),snap('p'),{kind:'events'}],'entity-collections');
 for(const action of ['add','remove','replace','clear','self-add','self-remove']){
  const steps=[...entities(),N('Collections.EntityCollection'),C('p','Add',[R('a')]),C('p','Add',[R('b')]),C('p','GetEnumerator',[],'it'),G('it','Current'),C('it','MoveNext'),G('it','Current')];
  if(action==='add')steps.push(C('p','Add',[R('other')]));
  if(action==='remove')steps.push(C('p','Remove',[R('a')],null,{signature:['Entities.EntityObject']}));
  if(action==='replace')steps.push(put('p',I(0),R('other')));
  if(action==='clear')steps.push(C('p','Clear'));
  if(action==='self-add')steps.push(C('p','AddRange',[R('p')]));
  if(action==='self-remove')steps.push(C('p','Remove',[R('p')],null,{signature:['IEnumerable<Entities.EntityObject>']}));
  steps.push(C('it','MoveNext'),G('it','Current'),snap('p'));add('entity-iterator/'+action,steps,'entity-collections');
 }
 for(const key of [null,'A','a','b','missing',''])for(const action of ['get','set','remove','contains','add','try']){
  const steps=[...defs(),N('Collections.AttributeDefinitionDictionary'),...observe('p'),C('p','Add',[R('a')]),C('p','Add',[R('b')])];
  if(action==='get')steps.push({kind:'index',target:'p',args:[key],signature:['String']});
  if(action==='set')steps.push(put('p',key,R('other')));
  if(action==='remove')steps.push(C('p','Remove',[key]));
  if(action==='contains')steps.push(C('p','ContainsTag',[key]));
  if(action==='add')steps.push(C('p','Add',[R('other')]));
  if(action==='try')steps.push(C('p','TryGetValue',[key,{out:'Entities.AttributeDefinition'}],null,{signature:['String','Entities.AttributeDefinition&']}));
  steps.push(snap('p'),{kind:'events'});add(`dictionary/${action}/${key}`,steps);
 }
 for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])for(const cancel of [false,true])for(const failure of [false,true])for(const action of ['add','remove','replace','clear']){
  const steps=[...defs(),N('Collections.AttributeDefinitionDictionary'),C('p','Add',[R('a')]),C('p','Add',[R('b')]),...observe('p'),{kind:'observe',target:'p',member:event,observer:'fault',cancel,throw:failure}];
  if(action==='add')steps.push(N('Entities.AttributeDefinition',['C'],'c'),C('p','Add',[R('c')]));
  if(action==='remove')steps.push(C('p','Remove',['A']));
  if(action==='replace')steps.push(put('p','A',R('other')));
  if(action==='clear')steps.push(C('p','Clear'));
  steps.push(snap('p'),{kind:'events'});add(`dictionary-events/${event}/${cancel}/${failure}/${action}`,steps);
 }
 for(const action of ['add','remove','replace','clear']){
  const steps=[...defs(),N('Collections.AttributeDefinitionDictionary'),C('p','Add',[R('a')]),C('p','Add',[R('b')]),G('p','Tags','keys'),G('p','Values','values'),C('p','GetEnumerator',[],'it'),C('it','MoveNext'),G('it','Current')];
  if(action==='add')steps.push(N('Entities.AttributeDefinition',['C'],'c'),C('p','Add',[R('c')]));
  if(action==='remove')steps.push(C('p','Remove',['A']));
  if(action==='replace')steps.push(put('p','a',R('other')));
  if(action==='clear')steps.push(C('p','Clear'));
  steps.push(C('it','MoveNext'),G('it','Current'),snap('p'),snap('keys'),snap('values'));add('dictionary-iterator/'+action,steps);
 }
 add('dictionary-slots',[...defs(),N('Collections.AttributeDefinitionDictionary'),C('p','Add',[R('a')]),C('p','Add',[R('b')]),C('p','Remove',['A']),N('Entities.AttributeDefinition',['C'],'c'),C('p','Add',[R('c')]),snap('p'),G('p','Tags')]);
 for(const tag of [null,'','TAG','tag','other'])for(const raw of [false,true])for(const withNull of [false,true]){
  const steps=[N('Entities.AttributeDefinition',['TAG'],'d'),raw?N('Entities.Attribute',['TAG'],'a',{nonPublic:true,signature:['String']}):N('Entities.Attribute',[R('d')],'a'),N('Collections.AttributeCollection',[A('Entities.Attribute',withNull?[null,R('a')]:[R('a')])]),C('p','AttributeWithTag',[tag]),C('p','Contains',[R('a')]),C('p','IndexOf',[R('a')]),snap('p')];
  add(`attributes/${tag}/${raw}/${withNull}`,steps);
 }
 for(const size of [null,0,1,3])for(const index of [-1,0,1,4])for(const type of ['AttributeCollection','EntityCollection']){
  const isAttribute=type==='AttributeCollection';
  const steps=isAttribute?[N('Entities.AttributeDefinition',['TAG'],'d'),N('Entities.Attribute',[R('d')],'a')]:entities();
  steps.push(N('Collections.'+type,isAttribute?[A('Entities.Attribute',[R('a')])]:[]));if(!isAttribute)steps.push(C('p','Add',[R('a')]));
  // Make a reusable typed array through an internal holder-free read step.
  steps.push({kind:'value',value:size===null?null:A(isAttribute?'Entities.Attribute':'Entities.EntityObject',Array(size).fill(null)),id:'array'},C('p','CopyTo',[R('array'),I(index)]),snap('array'));add(`copy/${type}/${size}/${index}`,steps);
 }
 return out;
}
