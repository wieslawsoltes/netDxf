// Port of the original pure model/collection tests. Typed DXF integration remains unported.
import { DxfClass } from '../../netDxf/DxfClass.js';
import { DxfClassCollection } from '../../netDxf/Collections/DxfClassCollection.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws } from './TestHarness.js';
export function RegisterClassDefinitionTests() {
  Run('classes/model',ClassDefinitionModel);Run('classes/collection',ClassDefinitionCollection);
}
export function CheckClass(expected,actual) {
  for(const key of ['Name','CppClassName','ApplicationName','ProxyFlags','InstanceCount','WasProxy','IsEntity'])Equal(expected[key],actual[key],key);
}
export function ClassDefinitionModel() {
  const c=new DxfClass('CUSTOM','AcDbCustom','');Equal(0,c.ProxyFlags);Check(c.InstanceCount===null&&!c.WasProxy&&!c.IsEntity);
  for(const value of ['',' ','bad\0name','bad\rname','bad\nname']) {
    Throws(ArgumentException,()=>new DxfClass(value,'Cpp','App'));Throws(ArgumentException,()=>new DxfClass('Name',value,'App'));
  }
  Throws(ArgumentNullException,()=>new DxfClass(null,'Cpp','App'));Throws(ArgumentNullException,()=>new DxfClass('Name',null,'App'));
  Throws(ArgumentNullException,()=>{c.ApplicationName=null;});Throws(ArgumentException,()=>{c.ApplicationName='bad\0app';});
  for(const value of [-1,-2147483648])Throws(ArgumentOutOfRangeException,()=>{c.InstanceCount=value;});
  c.InstanceCount=2147483647;c.ProxyFlags=-2147483648;c.WasProxy=true;c.IsEntity=true;
  const clone=c.Clone();CheckClass(c,clone);clone.ApplicationName='changed';clone.InstanceCount=null;clone.ProxyFlags=-1;clone.WasProxy=false;
  Equal('',c.ApplicationName);Equal(2147483647,c.InstanceCount);Check(c.WasProxy);Equal(-2147483648,c.ProxyFlags);
  // CLASS remains an independent metadata object, with no handle/owner inheritance.
  Equal(Object.prototype,Object.getPrototypeOf(DxfClass.prototype));Check(!('Handle' in c));
}
export function ClassDefinitionCollection() {
  const classes=new DxfClassCollection(),first=new DxfClass('FIRST','CppFirst','App'),second=new DxfClass('SECOND','CppSecond','App');
  classes.Add(first);classes.Add(second);Check(first===classes.get_Item('FIRST'));
  Throws(ArgumentException,()=>classes.Add(new DxfClass('FIRST','DifferentCpp','App')));
  Throws(ArgumentException,()=>classes.Add(new DxfClass('Different','CppFirst','App')));
  Throws(ArgumentNullException,()=>classes.Add(null));
  Throws(ArgumentException,()=>classes.set_Item(1,new DxfClass('THIRD','CppFirst','App')));
  Throws(ArgumentException,()=>classes.set_Item(1,new DxfClass('FIRST','CppThird','App')));
  Equal(2,classes.Count);Check(second===classes.get_Item(1));
  classes.set_Item(0,new DxfClass('REPLACED','CppFirst','New app'));Check(!classes.Contains('FIRST')&&classes.Contains('REPLACED'));
  Check(classes.Remove('REPLACED'));classes.Add(first);Check(first===classes.get_Item(1));classes.Clear();
  classes.Add(new DxfClass('SECOND','CppSecond','Again'));Equal(1,classes.Count);
}
