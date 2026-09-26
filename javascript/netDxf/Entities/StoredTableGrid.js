// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { TableSnapshot } from '../../runtime/TablePayload.js';
import { InvalidDataException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const fail = text => {throw new InvalidDataException(text);};
/** Read-only fixed-grid projection; no inferred geometry, style or field evaluation. */
export class StoredTableGrid {
  constructor(rows,columns,heights,widths,cells) { Object.assign(this,{RowCount:rows,ColumnCount:columns,RowHeights:TableSnapshot(heights),ColumnWidths:TableSnapshot(widths),Cells:TableSnapshot(cells)});Object.freeze(this); }
  get_Item(row,column) {
    if(row<0 || row>=this.RowCount)throw new ArgumentOutOfRangeException('row');
    if(column<0 || column>=this.ColumnCount)throw new ArgumentOutOfRangeException('column');
    return this.Cells.get_Item(row*this.ColumnCount+column);
  }
  static TryRead(source,decode,version) {
    const tags=Array.from(source),start=tags.findIndex(t=>t.Code===100 && t.Value==='AcDbTable');
    if(start<0 || tags.slice(start+1).some(t=>t.Code===100))return null;
    const first=tags.findIndex((t,i)=>i>start && t.Code===171);if(first<0)return null;
    const head=tags.slice(start+1,first),schema=head.filter(t=>t.Code===90);
    if(schema.length!==1 || schema[0].Value!==22)return null;
    const singleInt=code=>{const found=head.filter(t=>t.Code===code);if(found.length!==1)fail('TABLE has missing or duplicate dimension fields.');return found[0].Value;};
    const rows=singleInt(91),columns=singleInt(92);
    if(rows<=0 || columns<=0 || rows*columns>1000000)fail('TABLE fixed-grid dimensions are invalid or exceed one million cells.');
    const heights=head.filter(t=>t.Code===141).map(t=>t.Value),widths=head.filter(t=>t.Code===142).map(t=>t.Value);
    if(heights.length!==rows || widths.length!==columns || heights.some(v=>v<=0) || widths.some(v=>v<=0))fail('TABLE fixed-grid dimensions disagree with stored row heights or column widths.');
    const starts=[];tags.forEach((t,i)=>{if(i>=first && t.Code===171)starts.push(i);});
    if(starts.length!==rows*columns)fail('TABLE fixed-grid cell count disagrees with its dimensions.');
    return new this(rows,columns,heights,widths,starts.map((at,i)=>new StoredTableCell(tags.slice(at,starts[i+1]??tags.length),decode,version)));
  }
}
export class StoredTableCell {
  constructor(input,decode,version) {
    const body=Array.from(input);Object.assign(this,{Tags:TableSnapshot(body),StoredType:body[0].Value,HasFieldReference:false,ValueType:null,StoredFlags:null,HasLiteralValue:false,LiteralValue:null});
    this.#read(body,decode,version);Object.freeze(this);
  }
  #read(body,decode,version) {
    if(this.StoredType!==1)return;
    let scope=false,depth=0;
    for(const tag of body){
      if(tag.Code===102){if(tag.Value.startsWith('{'))depth++;else if(tag.Value==='}' && depth>0)depth--;continue;}
      if(depth>0)continue;
      if(tag.Code===301 && tag.Value==='CELL_VALUE'){scope=true;continue;}
      if(tag.Code===304 && tag.Value==='ACVALUE_END'){scope=false;continue;}
      if(!scope && tag.Code===344 && BigInt('0x'+tag.Value)!==0n)this.HasFieldReference=true;
    }
    if(this.HasFieldReference)return;
    const marker=body.findIndex(t=>t.Code===301 && t.Value==='CELL_VALUE');
    if(marker>=0){
      if(body.filter(t=>t.Code===301 && t.Value==='CELL_VALUE').length!==1)return;
      const end=body.findIndex((t,i)=>i>marker && t.Code===304 && t.Value==='ACVALUE_END');if(end<0)fail('TABLE cell has an unterminated ACVALUE envelope.');
      const values=body.slice(marker+1,end),types=values.filter(t=>t.Code===90),flags=values.filter(t=>t.Code===93);
      if(types.length!==1)fail('TABLE cell has missing or duplicate value types.');this.ValueType=types[0].Value;
      if(flags.length!==1)fail('TABLE cell has missing or duplicate value flags.');this.StoredFlags=flags[0].Value;
      if(this.StoredFlags&1)return;
      const code=this.ValueType===4?1:this.ValueType===2?140:this.ValueType===1?91:-1;
      if(this.ValueType===0){this.HasLiteralValue=true;return;}if(code<0)return;
      const value=values.filter(t=>t.Code===code);if(value.length!==1)fail('TABLE literal value is missing or repeated.');
      this.LiteralValue=typeof value[0].Value==='string'?decode(value[0].Value):value[0].Value;this.HasLiteralValue=true;
    }else if(version===14){
      const text=[];let depth=0;
      for(const tag of body){if(tag.Code===102){if(tag.Value.startsWith('{'))depth++;else if(tag.Value==='}'&&depth>0)depth--;continue;}if(depth===0&&(tag.Code===1||tag.Code===2))text.push(tag);}
      if(!text.length||text.at(-1).Code!==1||text.filter(t=>t.Code===1).length!==1)return;
      if(text.some(t=>t.Code===2&&t.Value.length!==250)||text.at(-1).Value.length>=250)return;
      this.ValueType=4;this.LiteralValue=decode(text.map(t=>t.Value).join(''));this.HasLiteralValue=true;
    }
  }
}
