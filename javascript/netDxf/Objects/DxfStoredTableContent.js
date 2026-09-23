// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { DxfStoredTableContentValue, LegacyScalarEnd } from './DxfStoredTableContent.Value.js';
import { TableSnapshot, TableHandleMap, IsTableReference, ValidTableUtf16, DecodeTableText } from '../../runtime/TablePayload.js';
import { TableContentState, SetTableContentState } from '../../runtime/TableContentState.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView, RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { GeometryRecordTagCount } from './DxfStoredTableGeometry.Edit.js';
import { InstallTableContentEditing } from './DxfStoredTableContent.Edit.js';
import { FormatException, NotSupportedException } from '../../runtime/Errors.js';
const names = ['AcDbLinkedData','AcDbLinkedTableData','AcDbFormattedTableData','AcDbTableContent'];
const frames = new Set(['CELLCONTENT','CELLMARGIN','CONTENTFORMAT','DATAMAP','FORMATTEDCELLCONTENT','FORMATTEDTABLEDATACELL','FORMATTEDTABLEDATACOLUMN','FORMATTEDTABLEDATAROW','GRIDFORMAT','LINKEDTABLEDATACELL','LINKEDTABLEDATACOLUMN','LINKEDTABLEDATAROW','TABLECELL','TABLECOLUMN','TABLEFORMAT','TABLEROW']);
const malformed = message => { throw new FormatException(message); };
function dataMapEnd(tags, start) {
  if (start+1 >= tags.length || tags[start+1].Code !== 90) return null;
  const count = tags[start+1].Value;
  if (count === 0) { if (start+2 >= tags.length || tags[start+2].Code !== 309 || tags[start+2].Value !== 'DATAMAP_END') malformed('TABLECONTENT empty DATAMAP framing is invalid.'); return start+2; }
  if (count !== 1 || start+5 >= tags.length || tags[start+2].Code !== 300 || tags[start+3].Code !== 301 || tags[start+3].Value !== 'DATAMAP_VALUE') return null;
  if (start+6 < tags.length && tags[start+4].Code === 90 && tags[start+4].Value === 2 && tags[start+5].Code === 140 && tags[start+6].Code === 309 && tags[start+6].Value === 'DATAMAP_END') return start+6;
  if (tags[start+4].Code !== 93 || tags[start+5].Code !== 90) return null;
  let at = start+6;
  while (at < tags.length && !(tags[at].Code === 304 && tags[at].Value === 'ACVALUE_END') && !(tags[at].Code === 309 && tags[at].Value === 'DATAMAP_END')) at++;
  if (at === tags.length || tags[at].Code !== 304) malformed('TABLECONTENT has an unterminated DATAMAP AcValue packet.');
  at++; return at < tags.length && tags[at].Code === 309 && tags[at].Value === 'DATAMAP_END' ? at : null;
}
function validateFrames(input, valueStarts = null) {
  const tags = Array.from(input), stack = [], outer = []; let project = true;
  for (let at=1; at<tags.length; at++) {
    const tag=tags[at], text=typeof tag.Value === 'string' ? tag.Value : null;
    if (tag.Code===300 && text==='VALUE' && stack.at(-1)==='CELLCONTENT' && at+2<tags.length && tags[at+1].Code===93 && tags[at+2].Code===90) {
      while (++at < tags.length && !(tags[at].Code===304 && tags[at].Value==='ACVALUE_END')) {}
      if (at===tags.length) malformed('TABLECONTENT has an unterminated stored AcValue packet.'); continue;
    }
    if (tag.Code===300 && text==='VALUE' && stack.at(-1)==='CELLCONTENT') { const end=LegacyScalarEnd(tags,at); if (end!==null) { at=end; continue; } }
    if (tag.Code===1 && text.endsWith('_BEGIN') && frames.has(text.slice(0,-6))) {
      if (text==='DATAMAP_BEGIN') { const end=dataMapEnd(tags,at); if (end===null) return null; at=end; continue; }
      if (project && valueStarts!==null && text==='CELLCONTENT_BEGIN' && stack.length===2 && stack.at(-1)==='LINKEDTABLEDATACELL' && stack[0]==='LINKEDTABLEDATAROW' && at>0 && tags[at-1].Code===302 && tags[at-1].Value==='CONTENT') valueStarts.push(at);
      if (stack.length>=64) malformed('TABLECONTENT packet nesting exceeds the supported storage limit.'); stack.push(text.slice(0,-6));
    } else if (tag.Code===309 && text.endsWith('_END') && frames.has(text.slice(0,-4))) {
      if (!stack.length || stack.pop()!==text.slice(0,-4)) malformed('TABLECONTENT has mismatched stored packet framing.');
    } else if (!stack.length) outer.push(tag);
    if (tag.Code===1 && text.endsWith('_BEGIN') && !frames.has(text.slice(0,-6)) || tag.Code===309 && text.endsWith('_END') && !frames.has(text.slice(0,-4))) { project=false; if (valueStarts!==null) valueStarts.length=0; }
  }
  if (stack.length) malformed('TABLECONTENT has an unterminated stored packet.'); return outer;
}
export class DxfStoredTableContentSubclass {
  constructor(name,tags) { this.Name=name; this.Tags=TableSnapshot(tags); Object.freeze(this); }
}
/** Internal retained loader constructor and Resolve, not typed DXF admission. */
export class DxfStoredTableContent extends DxfDatabaseObject {
  static get MaximumPayloadTags() { return 1048576; }
  static get MaximumEditedStringLength() { return 1048576; }
  constructor(source,input,decode=DecodeTableText) {
    super('TABLECONTENT'); const tags=Array.from(input), starts=[];
    tags.forEach((t,i)=>{if(t.Code===100)starts.push(i);});
    if(starts.length!==4 || starts[0]!==0) malformed('TABLECONTENT requires its four ordered subclass packets.');
    const packets=starts.map((start,i)=>{if(tags[start].Value!==names[i])malformed('TABLECONTENT subclass order is invalid.');return new DxfStoredTableContentSubclass(names[i],tags.slice(start,starts[i+1]??tags.length));});
    const state={source,version:source.DrawingVariables.AcadVer,payload:TableSnapshot(tags),subclasses:TableSnapshot(packets),name:null,description:null,columns:null,rows:null,style:null,styleHandle:null,sourceOwner:null,resolved:false,editing:false,reentered:false,references:new ReferenceList(),handles:new TableHandleMap()};
    const linked=Array.from(packets[0].Tags), terminal=Array.from(packets[3].Tags);
    if(linked.length===3 && linked[1].Code===1 && linked[2].Code===300) { state.name=decode(linked[1].Value);state.description=decode(linked[2].Value); }
    if(terminal.length!==2 || terminal[1].Code!==340) malformed('TABLECONTENT requires one terminal table-style handle.'); state.styleHandle=terminal[1].Value;
    const body=Array.from(packets[1].Tags),valueStarts=[],outer=validateFrames(body,valueStarts);
    if(outer!==null && outer.length>=3 && outer[0].Code===90) {
      let at=1,columns=0,rows=0;
      while(at<outer.length && outer[at].Code===300 && outer[at].Value==='COLUMN') { columns++;at++; }
      if(at<outer.length && outer[at].Code===91) {
        const rowCount=outer[at++].Value;
        while(at<outer.length && outer[at].Code===301 && outer[at].Value==='ROW') { rows++;at++; }
        if(at===outer.length-1 && outer[at].Code===92) {
          const columnCount=outer[0].Value;
          if(columnCount<0 || rowCount<0 || columnCount!==columns || rowCount!==rows) malformed('TABLECONTENT outer row or column count does not match its stored packets.');
          state.columns=columnCount;state.rows=rowCount;
        }
      }
    }
    state.values=TableSnapshot(state.columns===null || state.rows===null ? [] : valueStarts.map(at=>DxfStoredTableContentValue.TryRead(body,at,starts[1],state.version,decode)).filter(v=>v!==null));
    validateFrames(packets[2].Tags); SetTableContentState(this,state);
  }
  get SourceVersion(){return TableContentState(this).version;}
  get Payload(){return TableContentState(this).payload;}
  get Subclasses(){return TableContentState(this).subclasses;}
  get Name(){return TableContentState(this).name;}
  get Description(){return TableContentState(this).description;}
  get ColumnCount(){return TableContentState(this).columns;}
  get RowCount(){return TableContentState(this).rows;}
  get StoredValues(){return TableContentState(this).values;}
  get TableStyle(){return TableContentState(this).style;}
  get References(){return ReadOnlyReferenceView(TableContentState(this).references);}
  get DatabaseReferences(){const s=TableContentState(this);return Array.from(s.references).filter(item=>s.source.GetObjectByHandle(item.Handle)===item);}
  get AllocationReservations(){return this.Payload;}
  CloneShell(){throw new NotSupportedException('Stored TABLECONTENT cloning requires its complete application schema.');}
  Resolve(resolve) {
    const s=TableContentState(this);
    for(const tag of this.Payload) {
      if(!IsTableReference(tag) || BigInt('0x'+tag.Value)===0n)continue;
      const target=resolve(tag.Value);if(target==null)malformed('TABLECONTENT requires an exact source reference identity: '+tag.Value);
      s.handles.set(tag.Value,target);s.references.Add(target);
    }
    if(BigInt('0x'+s.styleHandle)!==0n) { const target=resolve(s.styleHandle); s.style=target instanceof DxfDatabaseObject?target:null;if(s.style===null || s.style.CodeName!=='TABLESTYLE')malformed('TABLECONTENT terminal group 340 must identify a TABLESTYLE.'); }
    s.sourceOwner=this.Owner;if(s.sourceOwner===null || s.source.GetObjectByHandle(s.sourceOwner.Handle)!==s.sourceOwner)malformed('TABLECONTENT requires a registered source owner.');
    const ancestry=new Set();
    for(let ancestor=s.sourceOwner;ancestor!==null && ancestor!==s.source;ancestor=ancestor.Owner) {
      if(ancestor===this || ancestry.has(ancestor))malformed('TABLECONTENT source ownership contains a cycle.');ancestry.add(ancestor);
      if(s.source.GetObjectByHandle(ancestor.Handle)!==ancestor)malformed('TABLECONTENT source ancestry contains an unregistered object.');
    }
    s.resolved=true;
  }
  ValidateDatabaseSchema(database,errors) {
    const s=TableContentState(this);
    if(!s.resolved || database.Document!==s.source){errors.Add('Stored TABLECONTENT must remain in its source document.');return;}
    if(s.source.DrawingVariables.AcadVer!==s.version)errors.Add('Stored TABLECONTENT conversion requires complete schema regeneration.');
    if(this.Owner!==s.sourceOwner || s.source.GetObjectByHandle(s.sourceOwner.Handle)!==s.sourceOwner)errors.Add('Stored TABLECONTENT source ownership changed.');
    for(const [handle,target] of s.handles)if(s.source.StoredTableHandleTarget(handle)!==target)errors.Add('A stored TABLECONTENT dependency is no longer registered: '+handle);
    if(GeometryRecordTagCount(this,this.Payload.Count)>1048576)errors.Add('Stored TABLECONTENT and common metadata exceed the record tag limit.');
    for(const tag of this.Payload)if(typeof tag.Value==='string' && !ValidTableUtf16(tag.Value))errors.Add('Stored TABLECONTENT contains invalid UTF-16 text.');
  }
}
InstallTableContentEditing(DxfStoredTableContent);
RegisterDatabaseModel('DxfStoredTableContent',DxfStoredTableContent);
