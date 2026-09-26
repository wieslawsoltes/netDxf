// Port of original generic insertion cases. HATCH integration cases remain unported.
import { ObservableCollection } from '../../netDxf/Collections/ObservableCollection.js';
import { ArgumentException, ArgumentOutOfRangeException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';

export function RegisterObservableCollectionInsertTests() {
  for (const index of [0, 1, 3]) {
    Run(`observable-insert/generic/${index}`, () => ObservableInsertGeneric(index, 'success'));
    Run(`observable-insert/cancel/${index}`, () => ObservableInsertGeneric(index, 'cancel'));
    Run(`observable-insert/throw/${index}`, () => ObservableInsertGeneric(index, 'throw'));
  }
  for (const index of [-1,4]) Run(`observable-insert/invalid/${index}`, () => ObservableInsertGeneric(index,'invalid'));
  Run('observable-insert/empty', () => ObservableInsertGeneric(0,'empty'));
  Run('observable-insert/removal-events', ObservableInsertRemovalEvents);
}
export function ObservableInsertGeneric(index, mode) {
  const items = new ObservableCollection(); if (mode !== 'empty') items.AddRange([10,20,30]);
  const before = items.ToArray(), events = [];
  items.BeforeAddItem.Add((_,e) => {
    Equal(before,items.ToArray(),'BeforeAdd sees unchanged sequence'); events.push('before-add:'+e.Item);
    if (mode === 'cancel') e.Cancel = true;
    if (mode === 'throw') throw new NotSupportedException('Caller refuses insertion');
  });
  items.AddItem.Add((_,e) => { events.push('add:'+e.Item); Equal(99,items.get_Item(index),'Add sees inserted item'); });
  items.BeforeRemoveItem.Add((_,e) => events.push('before-remove:'+e.Item));
  items.RemoveItem.Add((_,e) => events.push('remove:'+e.Item));
  if (mode === 'invalid') Throws(ArgumentOutOfRangeException,()=>items.Insert(index,99));
  else if (mode === 'cancel') Throws(ArgumentException,()=>items.Insert(index,99));
  else if (mode === 'throw') Throws(NotSupportedException,()=>items.Insert(index,99));
  else items.Insert(index,99);
  if (mode === 'success' || mode === 'empty') {
    const expected = before.slice(); expected.splice(index,0,99); Equal(expected,items.ToArray());
    Equal(['before-add:99','add:99'],events);
  } else {
    Equal(before,items.ToArray()); Equal(mode === 'invalid' ? [] : ['before-add:99'],events);
  }
}
export function ObservableInsertRemovalEvents() {
  const items = new ObservableCollection(); items.AddRange([10,20,30]); const events = [];
  items.BeforeRemoveItem.Add((_,e)=>events.push('before:'+e.Item)); items.RemoveItem.Add((_,e)=>events.push('removed:'+e.Item));
  Check(items.Remove(20)); Equal([10,30],items.ToArray()); Equal(['before:20','removed:20'],events);
}
