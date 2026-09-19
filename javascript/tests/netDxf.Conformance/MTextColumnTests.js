// Complete detached model/conversion/transform cases only. Document graph and wire cases remain unported.
import { MText, MTextColumns, MTextColumnType, MTextColumnStorage, Vector3, Matrix3 } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Near, Throws } from './TestHarness.js';
export function RegisterMTextColumnTests() {
  Run('mtext/columns/model-validation',MTextColumnsModelValidation);
  Run('mtext/columns/clone-conversion',MTextColumnsCloneConversion);
  Run('mtext/columns/transform-guards',MTextColumnsTransforms);
}
export function ColumnModel(mode,storage) {
  const c=new MTextColumns();Object.assign(c,{Type:mode===0?MTextColumnType.Static:MTextColumnType.Dynamic,Storage:storage,Count:3,AutoHeight:mode===1,FlowReversed:true,
    Width:12.5,Gutter:1.75,DefinedHeight:mode===2?0:20.25,TotalHeight:mode===2?28.75:20.25});
  if(mode===2)c.Heights.AddRange([20.25,28.75,0]);return c;
}
export function MTextColumnsModelValidation() {
  Check(new MText().Columns===null,'Invented default columns.');let c=ColumnModel(0,MTextColumnStorage.Embedded);
  for(const bad of [-1,0,NaN,Infinity,-Infinity])Throws(ArgumentOutOfRangeException,()=>{c.Width=bad;});
  for(const bad of [-1,NaN,Infinity,-Infinity]) {
    Throws(ArgumentOutOfRangeException,()=>{c.Gutter=bad;});Throws(ArgumentOutOfRangeException,()=>{c.DefinedHeight=bad;});
    Throws(ArgumentOutOfRangeException,()=>{c.TotalHeight=bad;});Throws(ArgumentOutOfRangeException,()=>{new MText().DefinedHeight=bad;});
  }
  for(const bad of [-1,0,32768,2147483647])Throws(ArgumentOutOfRangeException,()=>{c.Count=bad;});
  Throws(ArgumentOutOfRangeException,()=>{c.Type=3;});Throws(ArgumentOutOfRangeException,()=>{c.Storage=3;});
  c.AutoHeight=true;Throws(InvalidOperationException,()=>c.Validate());c.AutoHeight=false;
  c.Heights.Add(1);Throws(InvalidOperationException,()=>c.Validate());c.Heights.Clear();
  c=ColumnModel(2,MTextColumnStorage.Embedded);c.Heights.set_Item(0,NaN);Throws(ArgumentOutOfRangeException,()=>c.Validate());
  c.Heights.set_Item(0,0);Throws(ArgumentOutOfRangeException,()=>c.Validate());c.Heights.set_Item(0,20.25);
  c.Heights.RemoveAt(2);Throws(InvalidOperationException,()=>c.Validate());
}
export function MTextColumnsCloneConversion() {
  const source=new MText('FIRSTSECONDTHIRD',new Vector3(17,-11,3),2.5);source.Rotation=23;source.Columns=ColumnModel(2,MTextColumnStorage.Embedded);
  const legacy=source.ConvertToLinkedColumns(['FIRST','SECOND','THIRD']);
  Equal(3,legacy.Count,'converted entities');Equal('FIRSTSECONDTHIRD',source.Value,'source modified');
  Equal(2,legacy.get_Item(0).Columns.LinkedColumns.Count,'links');Equal('SECOND',legacy.get_Item(1).Value,'partition');
  Check(Array.from(legacy).every(c=>c.Owner===null && c.Handle===null),'Conversion returned owned entities.');
  const cloned=legacy.get_Item(0).Clone();Check(cloned.Columns.LinkedColumns.get_Item(0)!==legacy.get_Item(1),'Clone shared linked entity.');
  cloned.Columns.LinkedColumns.get_Item(0).Value='changed';cloned.Columns.Heights.set_Item(0,100);
  Equal('SECOND',legacy.get_Item(1).Value,'Clone mutated linked text');Near(20.25,legacy.get_Item(0).Columns.Heights.get_Item(0),'Clone mutated heights');
  const embedded=legacy.get_Item(0).ConvertToEmbeddedColumns();Equal(source.Value,embedded.Value,'lossless recombination');
  Equal(MTextColumnStorage.Embedded,embedded.Columns.Storage,'embedded storage');Equal(0,embedded.Columns.LinkedColumns.Count,'embedded links');
  Throws(ArgumentException,()=>source.ConvertToLinkedColumns(['FIRST','SECOND','wrong']));Throws(ArgumentException,()=>source.ConvertToLinkedColumns([source.Value]));
  Throws(ArgumentNullException,()=>source.ConvertToLinkedColumns(null));
  const cycle=ColumnModel(0,MTextColumnStorage.LegacyLinked),child=new MText();child.Columns=cycle;cycle.LinkedColumns.Add(child);
  Throws(InvalidOperationException,()=>cycle.Clone());
}
export function MTextColumnsTransforms() {
  const text=new MText('abcdef',Vector3.Zero,2);text.Columns=ColumnModel(2,MTextColumnStorage.Embedded);
  text.TransformBy(Matrix3.Scale(2),new Vector3(1,2,3));Near(25,text.Columns.Width,'scaled width');Near(57.5,text.Columns.Heights.get_Item(1),'scaled manual height');
  Near(4,text.Height,'scaled text');Equal(new Vector3(1,2,3),text.Position,'translated position');const position=text.Position;
  Throws(NotSupportedException,()=>text.TransformBy(Matrix3.Scale(2,1,1),Vector3.Zero));Throws(NotSupportedException,()=>text.TransformBy(Matrix3.Scale(-1,1,1),Vector3.Zero));
  Equal(position,text.Position,'Rejected transform changed position');const linked=text.ConvertToLinkedColumns(['ab','cd','ef']);
  Throws(NotSupportedException,()=>linked.get_Item(0).TransformBy(Matrix3.Identity,Vector3.UnitX));
}
