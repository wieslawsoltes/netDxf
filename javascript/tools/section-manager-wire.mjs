// Test-only input/observation adapter. All lifecycle rules live in production APIs.
import * as api from '../index.js';
import { InvalidOperationException } from '../runtime/Errors.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
export function managerSnapshot(manager) {
  const ref = item => item === null ? null : {type:item.constructor.name,handle:item.Handle,owner:item.Owner?.Handle??null};
  return {common:ref(manager),version:manager.SourceVersion,update:manager.RequiresFullUpdate,erased:manager.IsErased,
    registered:manager.Database!==null,sections:Array.from(manager.Sections,ref),
    tags:Array.from(manager.Tags,tag=>({code:tag.Code,value:tag.Value})),reactors:Array.from(manager.PersistentReactors,ref)};
}
export function managerCall(step, target, read, values) {
  const members = step.members === null ? null : step.members.map(read), count=step.repeat ?? members?.length ?? 0;
  const counters=[0,0,0,0,0]; if(step.log) values.set(step.log,counters);
  const invoke=source=>step.action==='replace'?target.ReplaceSections(source,step.flag):target.CreateSectionManager(source,step.flag);
  function hook(stage) {
    for(const action of step.hooks??[]) {
      if(action.stage!==stage)continue;
      if(action.kind==='throw')throw new InvalidOperationException('Injected caller failure.');
      if(action.kind==='reenter'||action.kind==='catch-reenter') {
        try{invoke([]);}catch(error){if(action.kind==='reenter')throw error;counters[4]++;}
      } else {
        const object=read(action.target);
        if(action.kind==='set')object[action.member]=read(action.value);
        else object[action.member](...(action.args??[]).map(read));
      }
    }
  }
  const source=members===null?null:{GetEnumerator(){counters[0]++;hook('get');let at=-1;
    return {MoveNext(){counters[1]++;hook('move');return ++at<count;},get Current(){counters[2]++;hook('current');return members[at%members.length];},Dispose(){counters[3]++;hook('dispose');}};
  }};
  return invoke(source);
}
export function managerLoad(step, document, read) {
  const members=step.members.map(read), handles=step.handles??members.map(member=>member.Handle);
  const tags=[new api.DxfTag(100,'AcDbSectionManager'),new api.DxfTag(70,step.flag?1:0),new api.DxfTag(90,handles.length),...handles.map(h=>new api.DxfTag(330,h))];
  const manager=new api.DxfStoredSectionManager(document,step.code??'SECTIONMANAGER',tags,!!step.flag,handles);
  const db=document.Objects;manager.Owner=db.Root;manager.PersistentReactors.Add(db.Root);db.Register(manager,false);
  db.Root.AddLoaded(step.anchor??'ACAD_SECTION_MANAGER',manager,!!step.hardOwner);
  manager.Resolve(handle=>document.GetObjectByHandle(handle));return manager;
}
