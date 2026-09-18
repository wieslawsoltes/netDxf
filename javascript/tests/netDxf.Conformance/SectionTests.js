// Complete detached SectionValues case; native IO and registered ownership cases remain unported.
import { Section, Vector3, Matrix3, Matrix4 } from '../../index.js';
import { Run, Equal, Check } from './TestHarness.js';
function SectionReject(action){let rejected=false;try{action();}catch{rejected=true;}Check(rejected,'SECTION input or mutation was not rejected');}
function SectionExample(codeName='SECTION'){
  const section=new Section(codeName);Object.assign(section,{Name:String.raw`東京 Literal\U+0041`,State:4,Flags:17,VerticalDirection:new Vector3(1,2,3),TopHeight:5.25,BottomHeight:-15.5,IndicatorTransparency:70,StoredIndicatorColor:256,StoredNativeIndicatorColor:9,IndicatorColorName:'Book$Color'});
  section.Vertices.Add(new Vector3(1,2,3));section.Vertices.Add(new Vector3(-4,5,6));section.BackLineVertices.Add(new Vector3(9,8,7));return section;
}
export function RegisterSectionTests(){Run('section/api/stored-values',SectionValues);}
export function SectionValues(){
  const section=SectionExample(),clone=section.Clone();Equal(section.Name,clone.Name,'Section clone text');Equal(section.VerticalDirection,clone.VerticalDirection,'Section clone independent vector');
  clone.Vertices.set_Item(0,Vector3.Zero);Check(!Vector3.Equals(section.Vertices.get_Item(0),clone.Vertices.get_Item(0)),'Section clone shared vertices');
  for(const value of [NaN,Infinity,-Infinity]){SectionReject(()=>{section.TopHeight=value;});SectionReject(()=>section.Vertices.Add(new Vector3(value,0,0)));}
  Equal(2,section.Vertices.Count,'Rejected vertex mutated collection');SectionReject(()=>{section.Name='bad\ud800';});SectionReject(()=>{section.IndicatorColorName='bad\nline';});
  SectionReject(()=>section.TransformBy(Matrix3.Identity,Vector3.UnitX));section.TransformBy(Matrix3.Identity,Vector3.Zero);section.TransformBy(Matrix4.Identity);
  const absent=new Section();Equal('SECTIONOBJECT',absent.CodeName,'Native default spelling');SectionReject(()=>new Section('section'));Check(absent.StoredIndicatorColor===null&&absent.StoredNativeIndicatorColor===null&&absent.IndicatorColorName===null,'New section invented indicator fields');
}
