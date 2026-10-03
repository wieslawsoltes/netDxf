// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredTableContent } from '../Objects/DxfStoredTableContent.js';
import { DxfXRecord } from '../Objects/DxfDatabaseObject.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { StoredTableCell } from './StoredTableGrid.js';
import { DxfTag } from '../IO/DxfTag.js';
import { StoredTableState } from '../../runtime/StoredTableState.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function ResolveTableBacking(owner){
  const s=StoredTableState(owner),candidates=Array.from(s.source.Objects.Items).filter(o=>o.CodeName==='TABLECONTENT'&&o.Owner instanceof DxfXRecord&&o.Owner.Owner===owner.ExtensionDictionary);
  if(owner.ExtensionDictionary!==null&&candidates.length===1)s.backing=candidates[0];
}
export function TableBackingAgreement(owner){
  const s=StoredTableState(owner);if(s.backing===null||owner.Grid===null)return null;
  const payload=s.backing instanceof DxfStoredTableContent?s.backing.Payload:s.backing instanceof DxfOpaqueObject?s.backing.Tags:null;if(payload===null)return null;
  const tags=Array.from(payload),starts=[];tags.forEach((t,i)=>{if(t.Code===1&&t.Value==='LINKEDTABLEDATACELL_BEGIN')starts.push(i);});
  if(starts.length!==owner.Grid.Cells.Count)return null;let agree=true;
  for(let i=0;i<starts.length;i++){
    const cell=owner.Grid.Cells.get_Item(i);if(cell.StoredType!==1||Array.from(cell.Tags).filter(t=>t.Code===301&&t.Value==='CELL_VALUE').length!==1||![0,1,2,4].includes(cell.ValueType))return null;
    const end=tags.findIndex((t,j)=>j>starts[i]&&t.Code===309&&t.Value==='LINKEDTABLEDATACELL_END');if(end<0||i+1<starts.length&&end>=starts[i+1])return null;
    const body=tags.slice(starts[i]+1,end),counts=body.filter(t=>t.Code===95);if(counts.length!==1||counts[0].Value<0||counts[0].Value>1)return null;
    const marker=body.findIndex(t=>t.Code===300&&t.Value==='VALUE');
    if(counts[0].Value===0){if(marker>=0)return null;agree=agree&&(!cell.HasLiteralValue||cell.LiteralValue===null);continue;}
    if(marker<0||body.filter(t=>t.Code===300&&t.Value==='VALUE').length!==1)return null;
    const valueEnd=body.findIndex((t,j)=>j>marker&&t.Code===304&&t.Value==='ACVALUE_END');if(valueEnd<0)return null;
    let value;try{value=new StoredTableCell([new DxfTag(171,1),new DxfTag(301,'CELL_VALUE'),...body.slice(marker+1,valueEnd+1)],s.decode,owner.SourceVersion);}catch(error){if(error instanceof InvalidDataException)return null;throw error;}
    if(![0,1,2,4].includes(value.ValueType))return null;
    if(value.HasLiteralValue!==cell.HasLiteralValue)agree=false;
    else if(value.HasLiteralValue && (value.ValueType!==cell.ValueType && value.LiteralValue!==null || value.LiteralValue!==cell.LiteralValue))agree=false;
  }
  return agree;
}
