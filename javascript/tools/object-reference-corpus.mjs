// Deterministic reference-accounting inputs. The unchanged native class supplies expected observations.
import { I, R } from './geometry-corpus.mjs';
const make=(type,args,id)=>({kind:'new',type,args,id});
const call=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const snap=(target='refs')=>({kind:'snapshot',target});
const ref=(item,uses)=>({new:'DxfObjectReference',args:[item,I(uses)]});
const batch=entries=>call('refs','Add',[{array:'DxfObjectReference',values:entries}],undefined,['IEnumerable<DxfObjectReference>']);
const add=item=>call('refs','Add',[item],undefined,['DxfObject']);
const remove=item=>call('refs','Remove',[item]);
const get=(target,member,id)=>({kind:'get',target,member,id});
export function objectReferenceCorpus() {
  const cases=[],append=(name,steps)=>cases.push({name:'object-reference/'+name,category:name.split('/')[0],request:{steps}});
  for (const identity of [false,true]) {
    const setup=[make('Collections.DxfObjectReferences',[identity],'refs'),make('Tables.Layer',['Shared'],'a'),make('Tables.Layer',['Shared'],'b'),make('Tables.Layer',['shared'],'lower'),make('Tables.Layer',['Other'],'c'),make('Entities.Line',[],'line'),make('Tables.VPort',['View'],'v1'),make('Tables.VPort',['View'],'v2')];
    append(`defaults/${identity}`,[...setup,call('refs','IsEmpty'),call('refs','ToList',[],'list'),call('list','Clear'),snap()]);
    append(`identity/${identity}`,[...setup,add(R('a')),add(R('b')),add(R('lower')),add(R('v1')),add(R('v2')),snap(),call('refs','ToList',[],'list'),{kind:'index',target:'list',args:[I(0)],id:'entry'},get('entry','Reference','original'),{kind:'reference-equals',args:[R('original'),R('a')]},remove(R('b')),snap(),remove(R('a')),snap()]);
    append(`nulls/${identity}`,[...setup,add(null),call('refs','Add',[null],undefined,['IEnumerable<DxfObjectReference>']),batch([ref(R('a'),2),null,ref(R('c'),4)]),snap(),batch([ref(null,1)]),snap(),remove(null),snap()]);
    append(`snapshot/${identity}`,[...setup,add(R('a')),call('refs','ToList',[],'old'),{kind:'index',target:'old',args:[I(0)],id:'oldEntry'},add(R('a')),call('refs','ToList',[],'fresh'),{kind:'index',target:'fresh',args:[I(0)],id:'newEntry'},{kind:'reference-equals',args:[R('oldEntry'),R('newEntry')]},get('oldEntry','Uses','uses'),call('old','Clear'),snap(),call('fresh','Clear'),snap()]);
    append(`rename/${identity}`,[...setup,add(R('a')),{kind:'set',target:'a',member:'Name',value:'Renamed'},snap(),remove(R('a')),snap(),add(R('a')),snap(),{kind:'set',target:'a',member:'Name',value:'Shared'},remove(R('a')),snap()]);
    for (const uses of [-2147483648,-2,-1,0,1,2,2147483647]) append(`counts/${identity}/${uses}`,[...setup,batch([ref(R('a'),uses)]),snap(),add(R('a')),snap(),remove(R('a')),snap(),remove(R('a')),snap(),batch([ref(R('a'),uses),ref(R('a'),uses)]),snap()]);
    append(`slot-reuse/${identity}`,[...setup,add(R('a')),add(R('c')),add(R('line')),remove(R('c')),remove(R('a')),add(R('v1')),add(R('v2')),snap()]);
    for(let seed=1;seed<=48;seed++) {
      let state=seed;const next=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n;
      const keys=['a','b','lower','c','line','v1','v2'],steps=[...setup];
      for(let i=0;i<100;i++) {
        const key=R(keys[next(keys.length)]),operation=next(5);
        steps.push(operation===0?remove(key):operation===1?batch([ref(key,[-2147483648,-1,0,1,2147483647][next(5)])]):add(key));
        steps.push(snap());
      }
      append(`randomized/${identity}/${seed}`,steps);
    }
  }
  return cases;
}
