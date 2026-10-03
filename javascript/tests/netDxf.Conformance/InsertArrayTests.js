// Complete detached original tests. Typed transport/document cases remain unregistered.
import {Block,Insert,Line,Text,AttributeDefinition,Vector3,Matrix3,MathHelper,CoordinateSystem,XData,XDataRecord,XDataCode,ApplicationRegistry} from '../../index.js';
import {ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Near,Throws} from './TestHarness.js';
const normals=[Vector3.UnitZ,Vector3.Negate(Vector3.UnitZ),Vector3.UnitX,new Vector3(1,2,3)];
const angles=[0,35,90,315];
const add=Vector3.Add,sub=Vector3.Subtract,mm=Matrix3.Multiply;
export function RegisterInsertArrayTests(){
 Run('insert-array/model',InsertArrayModel);Run('insert-array/lazy-large-grid',InsertArrayLarge);Run('insert-array/reject-unrepresentable-transform',InsertArrayInvalidTransform);
 for(const n of normals)for(const a of angles){Run(`insert-array/geometry/${n.ToString()}/${a}`,()=>InsertArrayGeometry(n,a));Run(`insert-array/transforms/${n.ToString()}/${a}`,()=>InsertArrayTransform(n,a));}
}
export function ArrayInsert(attribute=true){
 const b=new Block('ArrayComponent');b.Origin=new Vector3(.5,-1,2);b.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,-2,5)));
 if(attribute){const a=new AttributeDefinition('TAG');a.Value='P-101';a.Position=new Vector3(2,3,0);a.Height=2;b.AttributeDefinitions.Add(a);}
 const i=new Insert(b,new Vector3(10,-20,5));Object.assign(i,{ColumnCount:3,RowCount:2,ColumnSpacing:7.5,RowSpacing:-4.25,Scale:new Vector3(2,-3,4),Rotation:35});
 const x=new XData(new ApplicationRegistry('ARRAY_TEST'));x.XDataRecord.Add(new XDataRecord(XDataCode.String,'array metadata'));i.XData.Add(x);return i;
}
export function CheckArrayFields(a,b){Equal(a.ColumnCount,b.ColumnCount,'column count');Equal(a.RowCount,b.RowCount,'row count');Near(a.ColumnSpacing,b.ColumnSpacing,'column spacing');Near(a.RowSpacing,b.RowSpacing,'row spacing');}
function ArrayNear(a,b,name){Near(a.X,b.X,name+' X');Near(a.Y,b.Y,name+' Y');Near(a.Z,b.Z,name+' Z');}
export function InsertArrayModel(){
 let i=new Insert(new Block('Empty'));Equal(1,i.RowCount,'default rows');Equal(1,i.ColumnCount,'default columns');Near(0,i.RowSpacing,'default row spacing');Near(0,i.ColumnSpacing,'default column spacing');Check(!i.IsMultiple&&i.InstanceCount===1,'Default INSERT is not a singleton.');
 for(const invalid of [0,-1,-32768]){Throws(ArgumentOutOfRangeException,()=>{i.RowCount=invalid;});Throws(ArgumentOutOfRangeException,()=>{i.ColumnCount=invalid;});}
 for(const invalid of [NaN,-Infinity,Infinity]){Throws(ArgumentOutOfRangeException,()=>{i.RowSpacing=invalid;});Throws(ArgumentOutOfRangeException,()=>{i.ColumnSpacing=invalid;});}
 Equal(1,i.InstanceCount,'Rejected counts changed state');Throws(ArgumentOutOfRangeException,()=>i.GetGridPosition(-1,0));Throws(ArgumentOutOfRangeException,()=>i.GetGridPosition(0,1));Throws(ArgumentOutOfRangeException,()=>i.ExplodeCell(1,0));
 i.ColumnCount=32767;i.RowCount=32767;Equal(1073676289,i.InstanceCount,'Grid count overflowed');i.ColumnSpacing=Number.MAX_VALUE;Throws(InvalidOperationException,()=>i.GetGridPosition(0,2));Equal(0,Array.from(i.ExplodeEnumerable()).length,'Empty large block produced geometry.');
 i=ArrayInsert();const q=i.Clone();CheckArrayFields(i,q);q.RowCount=4;q.ColumnSpacing=99;Equal(2,i.RowCount,'Clone rows changed source');Near(7.5,i.ColumnSpacing,'Clone spacing changed source');Check(i.Block!==q.Block&&i.Attributes.get_Item(0)!==q.Attributes.get_Item(0),'Clone shares mutable geometry/attributes.');
}
function ArrayExpectedOffset(i,row,col){
 const radians=i.Rotation*Math.PI/180,x=col*i.ColumnSpacing,y=row*i.RowSpacing;
 const ocs=new Vector3(x*Math.cos(radians)-y*Math.sin(radians),x*Math.sin(radians)+y*Math.cos(radians),0);
 return MathHelper.Transform(ocs,i.Normal,CoordinateSystem.Object,CoordinateSystem.World);
}
export function InsertArrayGeometry(normal,angle){
 for(const [dx,dy] of [[7.5,-4.25],[-2,3],[0,0],[0,2]]){
  const i=ArrayInsert();i.Normal=normal;i.Rotation=angle;i.ColumnSpacing=dx;i.RowSpacing=dy;i.TransformAttributes();const transform=i.GetTransformation(),child=i.Block.Entities.get_Item(0),exploded=i.Explode();Equal(12,exploded.Count,'Array explosion did not retain every logical cell');Equal(12,Array.from(i.ExplodeEnumerable()).length,'Lazy explosion cell count');
  for(let row=0;row<i.RowCount;row++)for(let col=0;col<i.ColumnCount;col++){
   const offset=ArrayExpectedOffset(i,row,col);ArrayNear(add(i.Position,offset),i.GetGridPosition(row,col),'grid insertion point');
   const cell=i.ExplodeCell(row,col);Equal(2,cell.Count,'Cell explosion count');const line=Array.from(cell).find(e=>e instanceof Line),text=Array.from(cell).find(e=>e instanceof Text);
   ArrayNear(add(add(i.Position,offset),mm(transform,sub(child.StartPoint,i.Block.Origin))),line.StartPoint,'cell start');ArrayNear(add(add(i.Position,offset),mm(transform,sub(child.EndPoint,i.Block.Origin))),line.EndPoint,'cell end');ArrayNear(add(i.Attributes.get_Item(0).Position,offset),text.Position,'array attribute placement');Equal('P-101',text.Value,'Array attribute value');Check(line.Owner===null&&line.Handle===null,'Exploded geometry retained database identity.');ArrayNear(line.StartPoint,exploded.get_Item(2*(row*i.ColumnCount+col)).StartPoint,'row-major explosion order');
  }
  const first=exploded.get_Item(0);first.StartPoint=new Vector3(999,999,999);Check(!child.StartPoint.Equals(first.StartPoint)&&!exploded.get_Item(2).StartPoint.Equals(first.StartPoint),'Cells share mutable geometry.');
 }
}
export function InsertArrayLarge(){
 const i=ArrayInsert(false);i.RowCount=32767;i.ColumnCount=32767;i.Rotation=0;i.Scale=new Vector3(1,1,1);i.RowSpacing=2;i.ColumnSpacing=3;
 const iterator=i.ExplodeEnumerable()[Symbol.iterator]();Equal(1,iterator.next().done?0:1,'Large grid was not lazily consumable');iterator.return();ArrayNear(add(i.Position,new Vector3(32766*3,32766*2,0)),i.GetGridPosition(32766,32766),'Last large-grid cell');Equal(1,i.ExplodeCell(32766,32766).Count,'Indexed cell expansion materialized the whole grid');
}
export function InsertArrayTransform(normal,angle){
 for(const scale of [new Vector3(2,3,4),new Vector3(-2,3,1),new Vector3(2,-3,-4),new Vector3(.5,.5,.5)]){
  const i=ArrayInsert();i.Normal=normal;i.Rotation=angle;i.TransformAttributes();const basis=mm(MathHelper.ArbitraryAxis(i.Normal),Matrix3.RotationZ(angle*MathHelper.DegToRad));const transform=mm(mm(mm(mm(Matrix3.RotationX(.35),Matrix3.RotationY(-.47)),basis),Matrix3.Scale(scale)),basis.Transpose()),translation=new Vector3(7,-11,13);
  const before=Array.from(i.Explode()).filter(x=>x instanceof Line).map(l=>[l.StartPoint,l.EndPoint]),points=Array.from({length:6},(_,c)=>i.GetGridPosition(Math.trunc(c/3),c%3)),attribute=i.Attributes.get_Item(0).Position;
  i.TransformBy(transform,translation);const after=Array.from(i.Explode()).filter(x=>x instanceof Line);Equal(before.length,after.length,'Transform changed array count');
  for(let cell=0;cell<6;cell++){ArrayNear(add(mm(transform,points[cell]),translation),i.GetGridPosition(Math.trunc(cell/3),cell%3),'transformed grid point');ArrayNear(add(mm(transform,before[cell][0]),translation),after[cell].StartPoint,'transformed start');ArrayNear(add(mm(transform,before[cell][1]),translation),after[cell].EndPoint,'transformed end');}
  ArrayNear(add(mm(transform,attribute),translation),i.Attributes.get_Item(0).Position,'transformed attribute');
 }
}
export function InsertArrayInvalidTransform(){
 for(const transform of [new Matrix3(1,.5,0,0,1,0,0,0,1),Matrix3.Scale(0,1,1),Matrix3.Zero]){
  const i=ArrayInsert();i.Rotation=0;const before=i.Clone();Throws(NotSupportedException,()=>i.TransformBy(transform,new Vector3(1,2,3)));CheckArrayFields(before,i);Check(before.Position.Equals(i.Position),'Rejected transform moved INSERT');Check(before.Scale.Equals(i.Scale),'Rejected transform changed scale');Check(before.Normal.Equals(i.Normal),'Rejected transform changed normal');Check(before.Attributes.get_Item(0).Position.Equals(i.Attributes.get_Item(0).Position),'Rejected transform moved attribute');
 }
}
