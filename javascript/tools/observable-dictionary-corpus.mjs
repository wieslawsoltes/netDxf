// Deterministic input-only scenarios. No native outputs or expected snapshots are embedded.
export function observableDictionaryCorpus() {
  const all=[];
  const add=(name,category,steps,extra={})=>all.push({name:'dictionary/'+name,category,request:{op:'observable-dictionary',keyType:'string',valueType:'int',steps,...extra}});
  const put=(key,value,more={})=>({method:'Add',key,value,...more});
  for(const constructor of ['default','capacity','comparer','capacity-comparer'])for(const capacity of [-1,0,1,4,8,20])
    add(`constructor/${constructor}/${capacity}`,'constructors',[{method:'TryGetValue',key:'a'},put('a',1)],{constructor,capacity});
  for(const comparer of [undefined,'constant','throw-hash','throw-equals','ignore-case'])for(const capacity of [0,1,8])
    add(`lookup/${comparer??'default'}/${capacity}`,'lookup',[
      ...['ContainsKey','TryGetValue','Get','Remove'].map(method=>({method,key:'missing'})),
      {method:'Set',key:'absent',value:4},put(null,2),put('A',1),put('a',2),put('B',3),
      {method:'Get',key:'A'},{method:'Set',key:'a',value:7},
      {method:'ContainsPair',key:'A',value:7},{method:'RemovePair',key:'A',value:7},
      {method:'Remove',key:'A'},put('C',4),{method:'Clear'},{method:'ContainsKey',key:'C'}
    ],{capacity,...(comparer?{comparer}:{})});
  for(const method of ['Add','AddPair','Set','Remove','Clear'])for(const mode of ['', 'cancel-add','cancel-remove','throw-before-add','throw-add','throw-before-remove','throw-remove'])for(const uncancel of [false,true])
    add(`events/${method}/${mode||'normal'}/${uncancel}`,'events',[put('a',1),put('b',2),{method,key:method==='Set'||method==='Remove'?'a':'c',value:3,mode}],{uncancel});
  for(const view of ['pairs','keys','values'])for(const mutation of ['set','remove','clear','add','duplicate','cancel','remove-add']){
    const edit={set:[{method:'Set',key:'a',value:9}],remove:[{method:'Remove',key:'b'}],clear:[{method:'Clear'}],add:[put('c',3)],duplicate:[put('b',5)],cancel:[put('c',3,{mode:'cancel-add'})],'remove-add':[{method:'Remove',key:'b'},put('c',4)]}[mutation];
    add(`iterator/${view}/${mutation}`,'enumeration',[{method:'Enumerate',view},{method:'MoveNext'},put('a',1),put('b',2),{method:'Enumerate',view},{method:'MoveNext'},...edit,{method:'Current'},{method:'MoveNext'},{method:'Reset'},{method:'MoveNext'},{method:'MoveNext'},{method:'MoveNext'},{method:'Dispose'}]);
  }
  for(const view of ['pairs','keys','values'])for(const length of [null,0,1,2,4])for(const index of [-1,0,1,3,5])
    add(`copy/${view}/${length}/${index}`,'views',[put('a',1),put('b',2),{method:'CopyTo',view,length,index}]);
  for(const view of ['keys','values'])add(`readonly/${view}`,'views',[put('a',1),{method:'ViewAdd',view,key:'b',value:2},{method:'ViewRemove',view,key:'a',value:1},{method:'ViewClear',view},{method:'ViewContains',view,key:'a',value:1},{method:'Remove',key:'a'},{method:'ViewContains',view,key:'a',value:1}]);
  for(const method of ['Add','Set','Remove','Clear'])for(const event of ['before-add','add','before-remove','remove'])for(const inner of ['Add','Set','Remove','Clear'])
    add(`reentry/${method}/${event}/${inner}`,'reentrancy',[put('a',1),put('b',2),{method,key:method==='Add'?'c':'a',value:3,reenter:{event,method:inner,key:inner==='Add'?'d':'a',value:4}},{method:'TryGetValue',key:'a'}]);
  add('clear/cancellation','reentrancy',[...Array.from({length:6},(_,key)=>put(key,key)),{method:'Clear',mode:'cancel-even-remove'},put(7,7),{method:'Clear'}],{keyType:'int'});
  const ref=id=>({ref:id}),probe=(id,group,hash,reflexive=true)=>({id,value:{probe:{id,group,hash,reflexive}}});
  const pool=[probe('a','same',1),probe('b','same',1),probe('c','other',1),probe('n','nan',1,false),{id:'s',value:{string:'same'}},{id:'t',value:{string:'same'}},{id:'i',value:{int:17}},{id:'j',value:{int:17}},{id:'v',value:{vector2:[1,2]}},{id:'w',value:{vector2:[1,2]}}];
  for(const valueType of ['probe','object','string','Vector2']) {
    const [first,equal,other]=valueType==='string'?[ref('s'),ref('t'),{string:'other'}]:valueType==='Vector2'?[ref('v'),ref('w'),{vector2:[3,4]}]:[ref('a'),ref('b'),ref('c')];
    add(`identity/${valueType}`,'identity',[put('a',first),{method:'ContainsPair',key:'a',value:equal},{method:'ContainsValue',value:equal},{method:'RemovePair',key:'a',value:equal},{method:'RemovePair',key:'a',value:other},{method:'Set',key:'a',value:first},{method:'RemovePair',key:'a',value:first},{method:'RemovePair',key:'missing',value:first}],{pool,valueType});
  }
  add('identity/boxed-objects','identity',[put('a',ref('i')),{method:'ContainsPair',key:'a',value:ref('j')},{method:'RemovePair',key:'a',value:ref('j')},{method:'RemovePair',key:'a',value:ref('i')},put('b',null),{method:'RemovePair',key:'b',value:null}],{pool,valueType:'object'});
  add('identity/empty-string','identity',[put('a',''),{method:'RemovePair',key:'a',value:''},put('a','same'),{method:'RemovePair',key:'a',value:'same'}],{valueType:'string'});
  add('identity/nonreflexive-value','identity',[put('a',ref('n')),{method:'ContainsValue',value:ref('n')},{method:'ContainsPair',key:'a',value:ref('n')},{method:'RemovePair',key:'a',value:ref('n')}],{pool,valueType:'probe'});
  add('keys/mutable-hash','keys',[put(ref('a'),1),{method:'ContainsKey',key:ref('b')},put(ref('b'),2),{method:'Mutate',id:'a',hash:99},{method:'ContainsKey',key:ref('a')},{method:'Get',key:ref('a')},{method:'Clear'},put(ref('a'),3),{method:'Mutate',id:'a',hash:1},{method:'Get',key:ref('a')}],{pool,keyType:'probe'});
  add('keys/nonreflexive','keys',[put(ref('n'),1),put(ref('n'),2),{method:'ContainsKey',key:ref('n')},{method:'Remove',key:ref('n')},{method:'Clear'}],{pool,keyType:'probe'});
  add('keys/value-copy','keys',[put(ref('v'),ref('w')),{method:'Mutate',id:'v',x:7},{method:'Mutate',id:'w',x:9},{method:'Get',key:{vector2:[1,2]}},{method:'ContainsKey',key:ref('v')},{method:'RemovePair',key:{vector2:[1,2]},value:{vector2:[1,2]}}],{pool,keyType:'Vector2',valueType:'Vector2'});
  add('keys/floating-point','keys',[...['0000000000000000','8000000000000000','7FF8000000000042','FFF8000000000001','7FF0000000000000','FFF0000000000000'].flatMap((double,i)=>[put({double},i),{method:'ContainsKey',key:{double}},{method:'Get',key:{double}}])],{keyType:'double'});
  for(const comparer of [undefined,'constant','modulo'])for(let seed=1;seed<=48;seed++){
    let state=seed;const rand=n=>{state=(Math.imul(state,1664525)+1013904223)>>>0;return state%n;},steps=[];
    for(let i=0;i<100;i++){
      const method=['Add','AddPair','Set','Remove','RemovePair','ContainsPair','Get','ContainsKey','ContainsValue','TryGetValue','Clear'][rand(11)];
      steps.push({method,key:rand(20),value:rand(100),mode:['','cancel-add','cancel-remove','throw-add','throw-remove'][rand(5)]});
    }
    add(`random/${comparer??'default'}/${seed}`,'randomized',steps,{keyType:'int',valueType:'int',capacity:seed%10,...(comparer?{comparer}:{})});
  }
  return all;
}
