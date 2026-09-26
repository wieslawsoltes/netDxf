// Complete original common-metadata admission case. Other cases include typed IO
// or native fixtures and are not shortened or counted as ported.
import { DxfDocument, DxfVersion, Section, Line, Vector3, XData, ApplicationRegistry, XDataRecord, XDataCode, DxfXRecord, DxfTag } from '../../index.js';
import { Run, Check, Equal } from './TestHarness.js';
const SectionReject=action=>{let rejected=false;try{action();}catch{rejected=true;}Check(rejected,'SECTION input or mutation was not rejected');};
export function RegisterSectionLifecycleTests(){Run('section/lifecycle/common-metadata-admission',SectionCommonMetadata);}
export function SectionCommonMetadata(){
  const doc=new DxfDocument(DxfVersion.AutoCad2018),line=new Line(Vector3.Zero,Vector3.UnitX);doc.Entities.Add(line);
  const invalid=new Section(),missing=new XData(new ApplicationRegistry('SECTION_APP'));missing.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,'FFFFFF'));invalid.XData.Add(missing);
  const count=doc.Entities.All.Count;SectionReject(()=>doc.Entities.Add(invalid));Equal(count,doc.Entities.All.Count,'Invalid metadata changed entity membership');Check(invalid.Handle===null&&invalid.Owner===null,'Invalid metadata consumed entity identity');
  const section=new Section(),data=new XData(new ApplicationRegistry('SECTION_APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,line.Handle));section.XData.Add(data);doc.Entities.Add(section);
  SectionReject(()=>section.Clone());const clone=doc.Objects.CloneSection(section,section.Owner);Equal(line.Handle,clone.XData.get_Item('SECTION_APP').XDataRecord.get_Item(0).Value,'Bare section graph clone mapped XData');
  const blocker=new DxfXRecord();blocker.Data.Add(new DxfTag(330,clone.Handle));doc.Objects.Root.Add('SECTION_BLOCKER',blocker);
  Check(!doc.Entities.Remove(clone),'Ordinary section removal invalidated incoming metadata');SectionReject(()=>doc.Objects.EraseSection(clone));
  blocker.Data.Clear();Check(doc.Entities.Remove(clone),'An unowned unreferenced section could not be removed');
}
