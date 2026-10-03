// Independent operation inputs. Expected state, exceptions and events come only from C#.
import {I,R,D,V,A,E} from './geometry-corpus.mjs';
const N=(args=[],id='g',signature,hidden=false)=>({kind:'new',type:'Objects.Group',args,id,...(signature?{signature}:{}),...(hidden?{nonPublic:true}:{})});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const G=(target,member,id)=>({kind:'get',target,member,id});
const S=(target,member,value)=>({kind:'set',target,member,value});
const snap=target=>({kind:'snapshot',target});
const eq=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const lines=()=>['a','b','c'].map((id,i)=>({kind:'new',type:'Entities.Line',args:[V('Vector3',i,0,0),V('Vector3',i+1,2,3)],id}));
const init=()=>[...lines(),N(['G',A('Entities.EntityObject',[R('a'),R('b')])]),G('g','Entities','entities')];
const observe=(target,member,id,extra={})=>({kind:'observe',target,member,observer:id,...extra});
export function groupCorpus(){
  const out=[],add=(name,steps)=>out.push({name:'group/'+name,category:'groups',request:{steps}});
  for(const name of [null,'',' ','  Named  ','Named','A/B','*A','日本😀','A\0B'])add('name/'+JSON.stringify(name),[N([name],'g',['String']),C('g','ToString'),C('g','Clone'),C('g','HasReferences'),C('g','GetReferences')]);
  for(const name of [null,'',' ','*A','A/B','Good'])for(const check of [false,true])add(`internal/${JSON.stringify(name)}/${check}`,[N([name,check],'g',['String','Boolean'],true),C('g','Clone'),S('g','Name','New'),snap('g')]);
  for(const kind of ['null','empty','one','duplicate','null-element'])for(const named of [false,true]){
    const values=kind==='null'?null:A('Entities.EntityObject',kind==='empty'?[]:kind==='one'?[R('a')]:kind==='duplicate'?[R('a'),R('a')]:[R('a'),null,R('b')]);
    add(`constructor/${kind}/${named}`,[...lines(),N(named?['GROUP',values]:[values],'g',named?['String','IEnumerable<Entities.EntityObject>']:['IEnumerable<Entities.EntityObject>']),snap('a'),snap('b'),snap('g')]);
  }
  for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem','EntityAdded','EntityRemoved'])for(const failure of [false,true])for(const cancel of [false,true])for(const action of ['add','remove','replace','insert','clear']){
    const target=event.startsWith('Entity')?'g':'entities';
    const operation=action==='replace'?{kind:'set-index',target:'entities',args:[I(0)],value:R('c')}:C('entities',{add:'Add',remove:'Remove',insert:'Insert',clear:'Clear'}[action],action==='clear'?[]:action==='insert'?[I(0),R('c')]:[R(action==='remove'?'a':'c')],undefined,action==='remove'?['Entities.EntityObject']:undefined);
    add(`event/${event}/${failure}/${cancel}/${action}`,[...init(),observe('g','EntityAdded','added'),observe('g','EntityRemoved','removed'),observe(target,event,'observer',{throw:failure,cancel}),operation,snap('g'),snap('a'),snap('b'),snap('c'),{kind:'events'}]);
  }
  for(const event of ['EntityAdded','EntityRemoved'])for(const failure of [false,true])for(const action of ['add','remove','replace','insert','clear']){
    const operation=action==='replace'?{kind:'set-index',target:'entities',args:[I(0)],value:R('c')}:C('entities',{add:'Add',remove:'Remove',insert:'Insert',clear:'Clear'}[action],action==='clear'?[]:action==='insert'?[I(0),R('c')]:[R(action==='remove'?'a':'c')],undefined,action==='remove'?['Entities.EntityObject']:undefined);
    add(`notification/${event}/${failure}/${action}`,[...init(),observe('g',event,'notification',{throw:failure}),operation,snap('g'),snap('a'),snap('b'),snap('c'),{kind:'events'}]);
  }
  for(const index of [-1,0,1,2])for(const action of ['insert','replace','remove'])add(`index/${action}/${index}`,[...init(),action==='replace'?{kind:'set-index',target:'entities',args:[I(index)],value:R('c')}:C('entities',action==='insert'?'Insert':'RemoveAt',action==='insert'?[I(index),R('c')]:[I(index)]),snap('g'),snap('a'),snap('c')]);
  for(const operation of ['Add','Insert','set_Item'])for(const item of [null,R('a')])add(`admission/${operation}/${item===null?'null':'duplicate'}`,[...init(),C('entities',operation,operation==='Add'?[item]:[I(0),item]),snap('g'),snap('a')]);
  for(const name of [null,'','Valid','A/B','*Anonymous'])add('clone/'+JSON.stringify(name),[...init(),S('g','Description','metadata'),S('g','IsSelectable',false),C('g','Clone',[name],'q',['String']),snap('g'),snap('q'),snap('a')]);
  for(const failure of [false,true])for(const name of [null,'','G','g','New','A/B'])add(`rename/${failure}/${JSON.stringify(name)}`,[N(),observe('g','NameChanged','rename',{throw:failure}),S('g','Name',name),snap('g'),{kind:'events'}]);
  add('cross-group-membership',[...init(),N(['OTHER',A('Entities.EntityObject',[R('a')])],'other'),G('other','Entities','others'),C('entities','Remove',[R('a')],undefined,['Entities.EntityObject']),snap('a'),C('others','Clear'),snap('a'),snap('g'),snap('other')]);
  add('clone-identity-isolation',[...init(),C('g','Clone',[],'q'),G('q','Entities','copied'),{kind:'index',target:'copied',args:[I(0)],id:'copy'},eq('a','copy'),S('copy','EndPoint',V('Vector3',9,8,7)),snap('a'),snap('copy'),C('copied','Clear'),snap('a'),snap('q')]);
  add('xdata-clone',[...init(),{kind:'new',type:'XData',args:[{new:'Tables.ApplicationRegistry',args:['GROUP_DATA']}],id:'xd'},G('xd','XDataRecord','records'),C('records','Add',[{new:'XDataRecord',args:[E('XDataCode',1000),'payload']}]),G('g','XData','dict'),C('dict','Add',[R('xd')]),C('g','Clone',[],'q'),G('q','XData','qd'),{kind:'index',target:'qd',args:['GROUP_DATA'],id:'data'},G('data','XDataRecord','qr'),C('qr','Clear'),snap('g'),snap('q')]);
  for(const item of [null,R('a')])add('args/'+(item===null?'null':'entity'),[...lines(),{kind:'new',type:'Objects.GroupEntityChangeEventArgs',args:[item],id:'args'},G('args','Item','item')]);
  return out;
}
