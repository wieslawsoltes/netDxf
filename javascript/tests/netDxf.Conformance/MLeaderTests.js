// Complete original in-memory cases. Cases requiring typed IO or producer fixtures
// remain unported; no original case is shortened to its in-memory prefix.
import * as api from '../../index.js';
import { Run, Check, Equal } from './TestHarness.js';
import { ArgumentException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const {DxfDocument,DxfVersion,DxfMLeaderStyle,MultiLeader,MLeaderMTextContent,TextStyle,Linetype,Block,Vector3,Matrix3,Matrix4}=api;
const uses=values=>Array.from(values).reduce((sum,r)=>sum+r.Uses,0);
export function NewMLeader(version) {
  const doc=new DxfDocument(version),style=new DxfMLeaderStyle();style.Properties.TextStyle=doc.TextStyles.get_Item('Standard');
  doc.Objects.AddMLeaderStyle('Authored',style);const leader=new MultiLeader();leader.Properties.Style=style;
  leader.Properties.TextStyle=doc.TextStyles.get_Item('Standard');leader.Properties.LeaderLinetype=doc.Linetypes.get_Item('Continuous');leader.Properties.ContentType=0;
  return [doc,leader];
}
function MLeaderThrows(action,message){let threw=false;try{action();}catch(error){if(error instanceof ArgumentException||error instanceof InvalidOperationException||error instanceof NotSupportedException)threw=true;else throw error;}Check(threw,message);}
export function RegisterMLeaderTests(){
  Run('mleader/api/foreign-reference-isolation',MLeaderForeignReferences);
  Run('mleader/api/invalid-values-and-transform',MLeaderInvalidValues);
  Run('mleader/api/exact-transform-boundary',MLeaderExactTransforms);
  Run('mleader/api/combined-graphics-references',MLeaderCombinedReferences);
  Run('mleader/api/style-graph-clone',MLeaderStyleGraphClone);
  for(const kind of ['text','line','block'])for(const action of ['attach','detach','move','collision'])Run('mleader/api/rename-callback/'+kind+'/'+action,()=>MLeaderRenameCallback(kind,action));
  for(const kind of ['text','line','block'])Run('mleader/api/throwing-rename/'+kind,()=>MLeaderThrowingRename(kind));
}
export function MLeaderForeignReferences(){
  const [doc,leader]=NewMLeader(DxfVersion.AutoCad2018),foreign=new DxfDocument(DxfVersion.AutoCad2018);
  const ts=doc.TextStyles.Add(new TextStyle('OnlyDynamic','txt.shx')),ft=foreign.TextStyles.Add(new TextStyle('OnlyDynamic','txt.shx'));
  const lt=doc.Linetypes.Add(new Linetype('OnlyDynamic')),fl=foreign.Linetypes.Add(new Linetype('OnlyDynamic'));
  const block=doc.Blocks.Add(new Block('OnlyDynamic')),fb=foreign.Blocks.Add(new Block('OnlyDynamic'));
  leader.Properties.TextStyle=ts;leader.Properties.LeaderLinetype=lt;leader.Properties.ArrowHead=block.Record;doc.Entities.Add(leader);
  Check(!doc.TextStyles.Remove(ft)&&!doc.Linetypes.Remove(fl)&&!doc.Blocks.Remove(fb),'Foreign same-name removal bypassed dynamic references');
  MLeaderThrows(()=>{leader.Properties.TextStyle=ft;},'Foreign STYLE accepted');MLeaderThrows(()=>{leader.Properties.LeaderLinetype=fl;},'Foreign LTYPE accepted');MLeaderThrows(()=>{leader.Properties.ArrowHead=fb.Record;},'Foreign BLOCK accepted');
  Check(doc.TextStyles.get_Item(ts.Name)===ts&&foreign.TextStyles.get_Item(ft.Name)===ft,'Foreign removal changed identity maps');
  const clone=leader.Clone(),count=foreign.Entities.All.Count;MLeaderThrows(()=>foreign.Entities.Add(clone),'Cross-document copy needs explicit references');Equal(count,foreign.Entities.All.Count,'Failed add mutated collection');Check(clone.Handle===null&&clone.Owner===null,'Failed add mutated identity');
  const mappedStyle=new DxfMLeaderStyle();mappedStyle.Properties.TextStyle=ft;foreign.Objects.AddMLeaderStyle('Mapped',mappedStyle);
  clone.Properties.Style=mappedStyle;clone.Properties.TextStyle=ft;clone.Properties.LeaderLinetype=fl;clone.Properties.ArrowHead=fb.Record;foreign.Entities.Add(clone);clone.Validate();
  Check(clone.Properties.Style===mappedStyle&&leader.Properties.TextStyle===ts,'Explicit mapping changed source references');
  leader.Properties.TextStyle=doc.TextStyles.get_Item('Standard');leader.Properties.LeaderLinetype=doc.Linetypes.get_Item('Continuous');leader.Properties.ArrowHead=null;
  Check(doc.TextStyles.Remove(ts)&&doc.Linetypes.Remove(lt)&&doc.Blocks.Remove(block),'Released dynamic references remain');
}
export function MLeaderInvalidValues(){
  const [doc,leader]=NewMLeader(DxfVersion.AutoCad2007);doc.Entities.Add(leader);
  MLeaderThrows(()=>{leader.Properties.Scale=NaN;},'Nonfinite scalar accepted');MLeaderThrows(()=>{leader.Context.BasePoint=new Vector3(1,Infinity,0);},'Nonfinite vector accepted');
  const text=new MLeaderMTextContent();MLeaderThrows(()=>{text.Text='bad\uD800';},'Unpaired UTF16 accepted');MLeaderThrows(()=>{text.Text='bad\0';},'NUL accepted');
  leader.Properties.TextAttachmentDirection=1;MLeaderThrows(()=>leader.Validate(),'Public validation ignored registered profile');leader.Properties.TextAttachmentDirection=null;
  const before=leader.Context.BasePoint;MLeaderThrows(()=>leader.TransformBy(Matrix3.Identity,Vector3.UnitX),'Unsupported transform accepted');Equal(before,leader.Context.BasePoint,'Rejected transform mutated data');leader.TransformBy(Matrix3.Identity,Vector3.Zero);
}
export function MLeaderCombinedReferences(){
  const [doc,leader]=NewMLeader(DxfVersion.AutoCad2018);leader.Linetype=doc.Linetypes.get_Item('Continuous');doc.Entities.Add(leader);
  const line=doc.Linetypes.get_Item('Continuous'),references=Array.from(doc.Linetypes.GetReferences(line)).filter(r=>r.Reference===leader);
  Equal(1,references.length,'Graphics and context references must share one count entry');Equal(2,references[0].Uses,'Combined graphics/context uses');
  doc.Entities.Remove(leader);Check(!Array.from(doc.Linetypes.GetReferences(line)).some(r=>r.Reference===leader),'Removed combined reference survived');
}
export function MLeaderExactTransforms(){
  const [,leader]=NewMLeader(DxfVersion.AutoCad2018),entity=leader;leader.Context.BasePoint=new Vector3(7,11,13);
  for(const value of [Number.MIN_VALUE,1e-12,NaN,Infinity]){
    for(let row=0;row<3;row++)for(let column=0;column<3;column++){
      const matrix=Matrix3.Identity;matrix.set_Item(row,column,row===column?(!Number.isFinite(value)?value:1+1e-12):value);
      MLeaderThrows(()=>entity.TransformBy(matrix,Vector3.Zero),'Nonidentity Matrix3 accepted');
    }
    for(const offset of [new Vector3(value,0,0),new Vector3(0,value,0),new Vector3(0,0,value)])MLeaderThrows(()=>entity.TransformBy(Matrix3.Identity,offset),'Nonzero translation accepted');
    for(let row=0;row<4;row++)for(let column=0;column<4;column++){
      const matrix=Matrix4.Identity;matrix.set_Item(row,column,row===column?(!Number.isFinite(value)?value:1+1e-12):value);
      MLeaderThrows(()=>entity.TransformBy(matrix),'Nonidentity full Matrix4 accepted through base dispatch');
    }
  }
  entity.TransformBy(Matrix4.Identity);entity.TransformBy(Matrix3.Identity,Vector3.Zero);Equal(new Vector3(7,11,13),leader.Context.BasePoint,'Transform guard mutated coordinates');
}
export function MLeaderStyleGraphClone(){
  const [source,leader]=NewMLeader(DxfVersion.AutoCad2018),block=source.Blocks.Add(new Block('StyleBlock'));
  const original=leader.Properties.Style;original.Properties.ArrowHead=block.Record;original.Properties.Block=block.Record;original.Properties.Description='independent';
  const sourceDictionary=source.Objects.Root.get_Item('ACAD_MLEADERSTYLE'),local=source.Objects.Clone(sourceDictionary,source.Objects.Root,'LocalStyles'),localStyle=local.get_Item('Authored');
  Check(original!==localStyle&&original.Properties.TextStyle===localStyle.Properties.TextStyle&&localStyle.Properties.Block===block.Record,'Same-document style graph clone');
  localStyle.Properties.Description='edited';Equal('independent',original.Properties.Description,'Style clone values shared');
  const destination=new DxfDocument(DxfVersion.AutoCad2018),mappedBlock=destination.Blocks.Add(new Block('MappedBlock'));
  MLeaderThrows(()=>destination.Objects.Clone(sourceDictionary,destination.Objects.Root,'MissingMap'),'Cross-document style clone accepted foreign references');Check(!destination.Objects.Root.Contains('MissingMap'),'Failed style clone changed destination');
  const mappings=new Map([[original.Properties.TextStyle,destination.TextStyles.get_Item('Standard')],[block.Record,mappedBlock.Record]]);
  const mapped=destination.Objects.Clone(sourceDictionary,destination.Objects.Root,'MappedStyles',mappings),style=mapped.get_Item('Authored');
  Check(style.Properties.TextStyle===destination.TextStyles.get_Item('Standard')&&style.Properties.Block===mappedBlock.Record&&style.Properties.ArrowHead===mappedBlock.Record,'Style graph mappings lost exact references');
  Equal(2,uses(Array.from(destination.Blocks.GetReferences(mappedBlock)).filter(r=>r.Reference===style)),'Mapped style reference multiplicity');
  Check(source.Objects.Validate().Count===0&&destination.Objects.Validate().Count===0,'Style graph validation failed');
}
export function MLeaderRenameCallback(kind,action){
  const first=new DxfDocument(DxfVersion.AutoCad2018),second=new DxfDocument(DxfVersion.AutoCad2018);
  const New=name=>kind==='text'?new TextStyle(name,'txt.shx'):kind==='line'?new Linetype(name):new Block(name);
  const table=(doc,value)=>value instanceof TextStyle?doc.TextStyles:value instanceof Linetype?doc.Linetypes:doc.Blocks;
  const Add=(doc,value)=>table(doc,value).Add(value),Remove=(doc,value)=>table(doc,value).Remove(value);
  const Contains=(doc,name)=>(kind==='text'?doc.TextStyles:kind==='line'?doc.Linetypes:doc.Blocks).Contains(name);
  const item=New('Before');if(action!=='attach')Add(first,item);const oldRecordHandle=item instanceof Block?item.Record.Handle:null;
  item.NameChanged.Add(()=>{if(action==='attach')Add(second,item);else if(action==='detach')Check(Remove(first,item),'Callback detach');else if(action==='move'){Check(Remove(first,item),'Callback move removal');Add(second,item);}else Add(first,New('After'));});
  if(action==='collision'){MLeaderThrows(()=>{item.Name='After';},'Callback collision ignored');Equal('Before',item.Name,'Collision changed original name');Check(Contains(first,'Before')&&Contains(first,'After'),'Collision corrupted table');}
  else{
    item.Name='After';Equal('After',item.Name,'Callback rename not accepted');Check(!Contains(first,'Before')&&!Contains(second,'Before'),'Stale callback name index');
    if(action==='detach'){Check(!Contains(first,'After'),'Detached item remained indexed');Add(second,item);}
    if(oldRecordHandle!==null&&action!=='collision')Check(first.GetObjectByHandle(oldRecordHandle)===null,'Removed block record handle survived');else Check(Contains(second,'After'),'Current owner missed rename');
    if(item instanceof Block)Equal('After',item.Record.Name,'Block record name diverged');Check(Remove(second,item),'Accepted callback rename blocked normal removal');
  }
}
export function MLeaderThrowingRename(kind){
  const [doc,leader]=NewMLeader(DxfVersion.AutoCad2018);let target;
  if(kind==='text'){target=doc.TextStyles.Add(new TextStyle('Before','txt.shx'));leader.Properties.TextStyle=target;}
  else if(kind==='line'){target=doc.Linetypes.Add(new Linetype('Before'));leader.Properties.LeaderLinetype=target;}
  else{target=doc.Blocks.Add(new Block('Before'));leader.Properties.ArrowHead=target.Record;}
  doc.Entities.Add(leader);target.NameChanged.Add(()=>{throw new InvalidOperationException('observer failure');});
  MLeaderThrows(()=>{target.Name='After';},'Throwing observer ignored');Equal('Before',target.Name,'Failed rename changed name');
  const table=kind==='text'?doc.TextStyles:kind==='line'?doc.Linetypes:doc.Blocks;
  Check(table.get_Item('Before')===target&&!table.Contains('After'),'Failed rename moved index');Equal(1,uses(table.GetReferences(target)),'Failed rename lost references');
  if(kind==='text')leader.Properties.TextStyle=doc.TextStyles.get_Item('Standard');else if(kind==='line')leader.Properties.LeaderLinetype=doc.Linetypes.get_Item('Continuous');else leader.Properties.ArrowHead=null;
  Check(table.Remove(target),'Failed rename blocked release');
}
