// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Source-matched MTEXT direct, embedded and legacy-linked column transport.
import * as api from '../../index.js';
import { MTextColumns } from '../Entities/MTextColumns.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { Copy, DotNetMath } from '../../runtime/GeometryRuntime.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { WriteXDataRecords, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { InvalidDataException, InvalidOperationException, NotSupportedException, ArgumentException } from '../../runtime/Errors.js';
const at=(list,index)=>list.get_Item?list.get_Item(index):list[index];
const count=list=>list.Count??list.length;
const starts=['ACAD_MTEXT_COLUMN_INFO_BEGIN','ACAD_MTEXT_COLUMNS_BEGIN','ACAD_MTEXT_DEFINED_HEIGHT_BEGIN'];
export function TryReadMTextColumnTag(chunk,columns,definedHeight) {
  const code=chunk.Code;
  if(code===46){definedHeight.value=chunk.ReadDouble();MTextColumns.NonNegative(definedHeight.value,'DefinedHeight');chunk.Next();return true;}
  if(![75,76,78,79,48,49].includes(code)&&!(code===50&&columns.value!==null&&columns.value.Type===2&&!columns.value.AutoHeight))return false;
  if(columns.value===null){columns.value=new MTextColumns();columns.value.Storage=0;}
  const c=columns.value;if(c.Storage!==0)throw new InvalidDataException('Mixed MTEXT column representations.');
  switch(code) {
    case 75:c.Type=chunk.ReadShort();break;
    case 76:c.Count=chunk.ReadShort();break;
    case 78:c.FlowReversed=ReadColumnFlag(chunk.ReadShort());break;
    case 79:c.AutoHeight=ReadColumnFlag(chunk.ReadShort());break;
    case 48:c.Width=chunk.ReadDouble();break;
    case 49:c.Gutter=chunk.ReadDouble();break;
    case 50:{const n=chunk.ReadDouble();if(n!==c.Count||!Number.isFinite(n)||n!==Math.trunc(n))throw new InvalidDataException('Invalid MTEXT column height count.');
      for(let i=0;i<n;i++){chunk.Next();if(chunk.Code!==50)throw new InvalidDataException('Truncated MTEXT column height sequence.');c.Heights.Add(chunk.ReadDouble());}break;}
  }
  chunk.Next();return true;
}
export function ReadColumnFlag(value){if(value!==0&&value!==1)throw new InvalidDataException('Invalid MTEXT column boolean flag.');return value===1;}
export function ReadMTextEmbeddedColumns(chunk) {
  if(chunk.ReadString()!=='Embedded Object')throw new InvalidDataException('Unrecognized MTEXT embedded object marker.');
  const c=new MTextColumns(),seen=new Set(),direction=api.Vector3.Zero,position=api.Vector3.Zero;c.Storage=2;let n=0,totalWidth=0,hasType=false;
  chunk.Next();while(chunk.Code!==0&&chunk.Code!==1001) {
    const code=chunk.Code;if(code!==46){if(seen.has(code))throw new InvalidDataException('Duplicate MTEXT embedded field: '+code);seen.add(code);}
    switch(code) {
      case 10:direction.X=chunk.ReadDouble();break;case 20:direction.Y=chunk.ReadDouble();break;case 30:direction.Z=chunk.ReadDouble();break;
      case 11:position.X=chunk.ReadDouble();break;case 21:position.Y=chunk.ReadDouble();break;case 31:position.Z=chunk.ReadDouble();break;
      case 40:c.EmbeddedReferenceWidth=chunk.ReadDouble();break;
      case 70:if(chunk.ReadShort()!==1)throw new InvalidDataException('Unsupported MTEXT embedded object version.');break;
      case 41:c.DefinedHeight=chunk.ReadDouble();break;
      case 42:totalWidth=chunk.ReadDouble();MTextColumns.NonNegative(totalWidth,'TotalWidth');break;
      case 43:c.TotalHeight=chunk.ReadDouble();break;
      case 71:c.Type=chunk.ReadShort();hasType=true;break;
      case 72:n=chunk.ReadShort();break;
      case 73:c.AutoHeight=ReadColumnFlag(chunk.ReadShort());break;
      case 74:c.FlowReversed=ReadColumnFlag(chunk.ReadShort());break;
      case 44:c.Width=chunk.ReadDouble();break;
      case 45:c.Gutter=chunk.ReadDouble();break;
      case 46:c.Heights.Add(chunk.ReadDouble());break;
      case 101:throw new InvalidDataException('Multiple embedded objects on one MTEXT are unsupported.');
    }
    chunk.Next();
  }
  if(!hasType)throw new InvalidDataException('MTEXT embedded object has no column type.');
  for(const code of [70,41,42,43,71,72,44,45,73,74])if(!seen.has(code))throw new InvalidDataException('Missing MTEXT embedded field: '+code);
  for(const first of [10,11]){const components=[first,first+10,first+20].filter(v=>seen.has(v)).length;if(components!==0&&components!==3)throw new InvalidDataException('An embedded MTEXT vector requires all three components: '+first);}
  if(seen.has(10))c.EmbeddedTextDirection=direction;if(seen.has(11))c.EmbeddedInsertionPoint=position;
  if(c.Type===0)n=1;
  else if(c.Type===2&&c.AutoHeight&&n===0){const inferred=(totalWidth+c.Gutter)/(c.Width+c.Gutter);
    if(!Number.isFinite(inferred)||inferred<1||inferred>32767||Math.abs(inferred-DotNetMath.Round(inferred))>1e-5)throw new InvalidDataException('Cannot infer an integral automatic MTEXT column count from total width.');n=DotNetMath.Round(inferred);}
  c.Count=n;c.StoredTotalWidth=totalWidth;c.Validate();return c;
}
export function ColumnShort(records,index){if(index>=count(records)||at(records,index).Code!==1070)throw new InvalidDataException('Expected MTEXT column XDATA integer.');return at(records,index).Value;}
export function ColumnReal(records,index){if(index>=count(records)||at(records,index).Code!==1040)throw new InvalidDataException('Expected MTEXT column XDATA real.');return at(records,index).Value;}
export function ReadMTextColumnXData(text) {
  const box={};if(!text.XData.TryGetValue('ACAD',box))return;const acad=box.value,sections=new Map(),retained=[];let active=null,section=null;
  for(const record of acad.XDataRecord){const marker=record.Code===1000&&typeof record.Value==='string'?record.Value:null;
    if(starts.includes(marker)){if(active!==null||sections.has(marker))throw new InvalidDataException('Duplicate or nested MTEXT column XDATA section.');section=marker;active=[];sections.set(marker,active);continue;}
    if(active!==null){if(marker===section.replaceAll('_BEGIN','_END')){active=null;section=null;}else active.push(record);}else retained.push(record);
  }
  if(active!==null)throw new InvalidDataException('Unterminated MTEXT column XDATA section.');let c=null;
  if(sections.has(starts[0])) {
    if(text.Columns!==null)throw new InvalidDataException('Mixed MTEXT column representations.');c=new MTextColumns();c.Storage=1;const info=sections.get(starts[0]);
    for(let i=0;i<info.length;i++) {
      const code=ColumnShort(info,i++);if(i>=info.length)throw new InvalidDataException('Truncated MTEXT column XDATA field.');
      switch(code){case 75:c.Type=ColumnShort(info,i);break;case 76:c.Count=ColumnShort(info,i);break;
        case 78:c.FlowReversed=ReadColumnFlag(ColumnShort(info,i));break;case 79:c.AutoHeight=ReadColumnFlag(ColumnShort(info,i));break;
        case 48:c.Width=ColumnReal(info,i);break;case 49:c.Gutter=ColumnReal(info,i);break;
        case 50:{const n=ColumnShort(info,i);if(n<0)throw new InvalidDataException('Negative MTEXT column height count.');for(let h=0;h<n;h++)c.Heights.Add(ColumnReal(info,++i));break;}
        default:throw new InvalidDataException('Unsupported MTEXT column XDATA field.');}
    }
  }
  if(sections.has(starts[1])){const links=sections.get(starts[1]);if(c===null)throw new InvalidDataException('MTEXT linked columns have no column definition.');
    if(links.length<2||ColumnShort(links,0)!==47||ColumnShort(links,1)<1)throw new InvalidDataException('Invalid MTEXT linked column header.');
    for(let i=2;i<links.length;i++){if(links[i].Code!==1005)throw new InvalidDataException('Expected MTEXT linked column handle.');if(links[i].Value!=='0')c.PendingHandles.Add(links[i].Value);}}
  if(sections.has(starts[2])){const height=sections.get(starts[2]);if(height.length!==2||ColumnShort(height,0)!==46)throw new InvalidDataException('Invalid MTEXT defined height XDATA.');text.DefinedHeight=ColumnReal(height,1);}
  if(c!==null){c.DefinedHeight=text.DefinedHeight??0;c.TotalHeight=c.DefinedHeight;for(const height of c.Heights)c.TotalHeight=DotNetMath.Max(c.TotalHeight,height);text.Columns=c;}
  if(sections.size!==0){acad.XDataRecord.Clear();acad.XDataRecord.AddRange(retained);if(retained.length===0)text.XData.Remove('ACAD');}
}
export function ResolveMTextColumnLinks(document) {
  const claimed=new Set();for(const block of document.Blocks)for(const main of block.Entities) {
    if(!(main instanceof api.MText)||main.Columns===null)continue;const c=main.Columns;
    for(const handle of c.PendingHandles){const linked=document.GetObjectByHandle(handle);
      if(!(linked instanceof api.MText)||linked===main||linked.Owner!==main.Owner||linked.Columns!==null||claimed.has(linked))throw new InvalidDataException('MTEXT column link is missing, duplicated, nested, or belongs to another block: '+handle);
      claimed.add(linked);c.LinkedColumns.Add(linked);}
    c.PendingHandles.Clear();if(c.Storage===1&&c.LinkedColumns.Count!==0)c.Count=c.LinkedColumns.Count+1;
    if(c.Storage===1&&c.Count!==c.LinkedColumns.Count+1)throw new InvalidDataException('MTEXT legacy column count has missing linked entities.');
    try{c.Validate();}catch(error){if(!(error instanceof ArgumentException)&&!(error instanceof InvalidOperationException))throw error;throw WrappedInvalidData('Invalid MTEXT column definition.',error);}
  }
}
export function ValidateMTextColumns(document) {
  const version=document.DrawingVariables.AcadVer,claimed=new Set();let acadNeeded=false;
  for(const block of document.Blocks)for(const text of block.Entities){if(!(text instanceof api.MText))continue;
    if(text.DefinedHeight!==null&&version<api.DxfVersion.AutoCad2007)acadNeeded=true;const c=text.Columns,box={};
    if((c!==null||text.DefinedHeight!==null)&&text.XData.TryGetValue('ACAD',box))for(const record of box.value.XDataRecord)
      if(record.Code===1000&&starts.includes(record.Value))throw new InvalidOperationException('Typed MTEXT column data cannot be combined with raw ACAD column sections.');
    if(c===null)continue;c.Validate();
    if(c.PendingHandles.Count!==0)throw new InvalidOperationException('MTEXT contains unresolved linked column handles.');
    if(c.Storage===2&&version<api.DxfVersion.AutoCad2018)throw new NotSupportedException('Embedded MTEXT columns require DXF 2018. Use ConvertToLinkedColumns with explicit text partitions before down-saving.');
    if(c.Storage===1&&version>=api.DxfVersion.AutoCad2018)throw new NotSupportedException('Legacy linked MTEXT columns require a pre-2018 document. Use ConvertToEmbeddedColumns and explicitly replace all old entities.');
    if(c.Storage===0&&version<api.DxfVersion.AutoCad2007)throw new NotSupportedException('Direct MTEXT column tags require the 2007 or later writer profile.');
    if(c.Storage===1){acadNeeded=true;if(c.Count!==c.LinkedColumns.Count+1)throw new InvalidOperationException('MTEXT linked column count does not match Count.');
      for(const linked of c.LinkedColumns){if(linked===text||linked.Owner!==text.Owner||linked.Handle===null||claimed.has(linked))throw new InvalidOperationException('Every linked MTEXT must be a distinct entity in the same document block as its first column.');claimed.add(linked);}}
  }
  if(acadNeeded)document.ApplicationRegistries.Add(api.ApplicationRegistry.Default);
}
export function WriteMTextColumnDefinition(chunk,document,text,direction) {
  direction=Copy(direction);const c=text.Columns,defined=c===null?text.DefinedHeight:c.DefinedHeight;
  if(defined!==null&&document.DrawingVariables.AcadVer>=api.DxfVersion.AutoCad2007)chunk.Write(46,defined);
  if(c===null||c.Storage===1)return;
  if(c.Storage===0){chunk.Write(75,c.Type);chunk.Write(76,c.Count);chunk.Write(78,c.FlowReversed?1:0);chunk.Write(79,c.AutoHeight?1:0);chunk.Write(48,c.Width);chunk.Write(49,c.Gutter);
    if(c.Heights.Count!==0){chunk.Write(50,c.Heights.Count);for(const height of c.Heights)chunk.Write(50,height);}return;}
  chunk.Write(101,'Embedded Object');chunk.Write(70,1);
  const embeddedDirection=c.EmbeddedTextDirection??direction,embeddedPosition=c.EmbeddedInsertionPoint??text.Position;
  chunk.Write(10,embeddedDirection.X);chunk.Write(20,embeddedDirection.Y);chunk.Write(30,embeddedDirection.Z);chunk.Write(11,embeddedPosition.X);chunk.Write(21,embeddedPosition.Y);chunk.Write(31,embeddedPosition.Z);
  chunk.Write(40,c.EmbeddedReferenceWidth??text.RectangleWidth);chunk.Write(41,c.DefinedHeight);chunk.Write(42,c.StoredTotalWidth??c.TotalWidth);chunk.Write(43,c.TotalHeight);
  chunk.Write(71,c.Type);chunk.Write(72,c.Type===2&&c.AutoHeight?0:c.Count);chunk.Write(44,c.Width);chunk.Write(45,c.Gutter);chunk.Write(73,c.AutoHeight?1:0);chunk.Write(74,c.FlowReversed?1:0);
  for(const height of c.Heights)chunk.Write(46,height);
}
export function AddColumnField(records,code,value){records.Add(new api.XDataRecord(1070,code));records.Add(new api.XDataRecord(value instanceof BoxedScalar&&value.Type==='Int16'?1070:1040,value instanceof BoxedScalar&&['Int16','Double'].includes(value.Type)?value.Value:value));}
export function WriteMTextColumnXData(chunk,document,text) {
  const records=new ReferenceList(),version=()=>document.DrawingVariables.AcadVer;
  for(const app of text.XData.AppIds){if(OrdinalIgnoreCaseEquals(app,'ACAD'))records.AddRange(text.XData.get_Item(app).XDataRecord);else WriteXDataRecords(chunk,version,app,text.XData.get_Item(app).XDataRecord);}
  const c=text.Columns,marker=name=>records.Add(new api.XDataRecord(1000,name)),integer=(code,value)=>AddColumnField(records,code,new BoxedScalar('Int16',value));
  if(c!==null&&c.Storage===1){marker(starts[0]);integer(75,c.Type);integer(79,c.AutoHeight?1:0);integer(76,c.Count);integer(78,c.FlowReversed?1:0);AddColumnField(records,48,c.Width);AddColumnField(records,49,c.Gutter);
    if(c.Heights.Count!==0){integer(50,c.Heights.Count);for(const height of c.Heights)records.Add(new api.XDataRecord(1040,height));}
    marker('ACAD_MTEXT_COLUMN_INFO_END');marker(starts[1]);integer(47,c.Count);for(const linked of c.LinkedColumns)records.Add(new api.XDataRecord(1005,linked.Handle));marker('ACAD_MTEXT_COLUMNS_END');}
  const defined=c===null?text.DefinedHeight:c.DefinedHeight;
  if(defined!==null&&document.DrawingVariables.AcadVer<api.DxfVersion.AutoCad2018&&(c===null||c.Type!==2||c.AutoHeight)){marker(starts[2]);AddColumnField(records,46,defined);marker('ACAD_MTEXT_DEFINED_HEIGHT_END');}
  if(records.Count!==0)WriteXDataRecords(chunk,version,'ACAD',records);
}
