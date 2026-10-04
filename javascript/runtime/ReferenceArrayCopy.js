// Array.Copy-backed List<T>.CopyTo validation used by the original attribute/entity collections.
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from './Errors.js';
export function CopyReferenceArray(items,array,index) {
  if(array==null)throw new ArgumentNullException('destinationArray');
  if(!Number.isInteger(index)||index<0)throw new ArgumentOutOfRangeException('destinationIndex',index);
  if(array.length-index<items.Count)throw new ArgumentException('Destination array was not long enough.','destinationArray');
  for(let i=0;i<items.Count;i++)array[index+i]=items.get_Item(i);
}
