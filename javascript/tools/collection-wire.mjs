import { ObservableCollection } from '../netDxf/Collections/ObservableCollection.js';
import { NotSupportedException } from '../runtime/Errors.js';
export function collectionCall(input) {
  return input.scenarios.map(scenario=>{
    const items=new ObservableCollection(0,'int');items.AddRange(scenario.initial);let events=[],mode='',iterator;
    items.BeforeAddItem.Add((_,e)=>{events.push('before-add:'+e.Item);if(mode==='cancel-add')e.Cancel=true;if(mode==='throw-add')throw new NotSupportedException('injected');});
    items.AddItem.Add((_,e)=>events.push('add:'+e.Item));
    items.BeforeRemoveItem.Add((_,e)=>{events.push('before-remove:'+e.Item);if(mode==='cancel-remove')e.Cancel=true;if(mode==='throw-remove')throw new NotSupportedException('injected');});
    items.RemoveItem.Add((_,e)=>events.push('remove:'+e.Item));
    return scenario.operations.map(op=>{
      events=[];mode=op.mode??'';let result=null,error=null,paramName=null;
      try { switch(op.method) {
        case 'Add':items.Add(op.value);break;
        case 'AddRange':items.AddRange(op.values);break;
        case 'AddSelf':items.AddRange(items);break;
        case 'Insert':items.Insert(op.index,op.value);break;
        case 'Remove':result=items.Remove(op.value);break;
        case 'RemoveAt':items.RemoveAt(op.index);break;
        case 'Clear':items.Clear();break;
        case 'Get':result=items.get_Item(op.index);break;
        case 'Set':items.set_Item(op.index,op.value);break;
        case 'Reverse':items.Reverse();break;
        case 'Sort':items.Sort();break;
        case 'SortModulo':items.Sort((a,b)=>(a%5)-(b%5));break;
        case 'SortRange':items.Sort(op.index,op.count,null);break;
        case 'Contains':result=items.Contains(op.value);break;
        case 'IndexOf':result=items.IndexOf(op.value);break;
        case 'Enumerate':iterator=items.GetEnumerator();result=iterator.Current;break;
        case 'MoveNext':result={moved:iterator.MoveNext(),current:iterator.Current};break;
        case 'Reset':iterator.Reset();result=iterator.Current;break;
        case 'CopyTo':{const array=Array(op.length).fill(0);items.CopyTo(array,op.index);result=array;break;}
        default:throw new Error('Unknown collection operation.');
      }}catch(e){error=e.name;paramName=e.ParamName??null;}
      return {result,error,paramName,items:items.ToArray(),events};
    });
  });
}
