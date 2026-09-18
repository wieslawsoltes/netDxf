// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { DxfObject } from '../DxfObject.js';
import { Vector3 } from '../Vector3.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ImmutableCellView, IsAncestor } from '../../runtime/DatabaseModel.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';
export const DxfDataCellType=Object.freeze({Integer:1,Double:2,String:3,Point:4,ObjectId:5,HardOwner:6,SoftOwner:7,HardPointer:8,SoftPointer:9,Boolean:10,Vector:11});
export class DxfDataColumn {
  #type;#name;#values;
  constructor(type,name,values) {
    RequireInteger(type,1,11,'type');DxfDataColumn.CheckText(name);
    if(values==null)throw new ArgumentNullException('values');
    const snapshot=[];
    for(const value of values) {
      if(snapshot.length===DxfDataTable.MaximumCells)throw new ArgumentException('The DATATABLE cell admission limit was exceeded.','values');
      DxfDataColumn.CheckValue(type,value);const cell=value instanceof BoxedScalar?value.Value:value;snapshot.push(type===1&&cell===0?0:Copy(cell));
    }
    this.#type=type;this.#name=name;this.#values=ImmutableCellView(snapshot);
  }
  get Type(){return this.#type;}get Name(){return this.#name;}get Values(){return this.#values;}
  get OwnsObjects(){return this.Type===6||this.Type===7;}get HasReferences(){return this.Type>=5&&this.Type<=9;}
  static CheckText(value) {
    if(value==null)throw new ArgumentNullException('value');
    if(/[\0\r\n]/.test(value))throw new ArgumentException('DATATABLE text must be a single line without NUL.');
    for(let i=0;i<value.length;i++) {
      const n=value.charCodeAt(i);
      if(n>=0xd800&&n<=0xdbff){const next=value.charCodeAt(++i);if(!(next>=0xdc00&&next<=0xdfff))throw new ArgumentException('DATATABLE text contains an unpaired surrogate.');}
      else if(n>=0xdc00&&n<=0xdfff)throw new ArgumentException('DATATABLE text contains an unpaired surrogate.');
    }
  }
  static CheckValue(type,value) {
    const boxed = value instanceof BoxedScalar ? value.Type : null;
    if (boxed !== null) {
      if (type === 3) DxfDataColumn.CheckText(null);
      if (!((type === 1 && boxed === 'Int32') || (type === 2 && boxed === 'Double'))) throw new ArgumentException('The DATATABLE value does not match its stored column type.','value');
      value = value.Value;
    }
    let valid;
    switch(type) {
      case 1:valid=Number.isInteger(value)&&value>=-2147483648&&value<=2147483647;break;
      case 2:valid=typeof value==='number'&&Number.isFinite(value);break;
      case 3:DxfDataColumn.CheckText(typeof value==='string'?value:null);return;
      case 4:case 11:valid=value instanceof Vector3&&Number.isFinite(value.X)&&Number.isFinite(value.Y)&&Number.isFinite(value.Z);break;
      case 10:valid=typeof value==='boolean';break;
      case 6:case 7:valid=value===null||value instanceof DxfDatabaseObject;break;
      default:valid=value===null||value instanceof DxfObject;
    }
    if(!valid)throw new ArgumentException('The DATATABLE value does not match its stored column type.','value');
    if(value instanceof DxfDatabaseObject&&value.IsErased)throw new InvalidOperationException('An erased object cannot be referenced by a new DATATABLE column.');
  }
}
export class DxfDataTable extends DxfDatabaseObject {
  static get MaximumCells(){return 1048576;}
  #columns=ImmutableCellView([]);#name='';#rows=0;
  constructor(){super('DATATABLE');}
  get StoredVersion(){return 2;}get Name(){return this.#name;}set Name(value){DxfDataColumn.CheckText(value);this.#name=value;}
  get RowCount(){return this.#rows;}get Columns(){return this.#columns;}
  SetColumns(rowCount,values){this.#setColumns(rowCount,values,false);}
  SetLoadedColumns(rowCount,values){this.#setColumns(rowCount,values,true);}
  #setColumns(rowCount,values,imported) {
    if(values==null)throw new ArgumentNullException('values');
    const snapshot=[];
    for(const column of values) {
      if(snapshot.length===DxfDataTable.MaximumCells)throw new ArgumentException('The DATATABLE column admission limit was exceeded.');
      if(column==null)throw new ArgumentException('A DATATABLE column cannot be null.');snapshot.push(column);
    }
    if(this.IsErased)throw new InvalidOperationException('An erased DATATABLE cannot be edited.');
    if(!Number.isInteger(rowCount)||rowCount<0||rowCount>DxfDataTable.MaximumCells||rowCount*snapshot.length>DxfDataTable.MaximumCells)throw new ArgumentOutOfRangeException('rowCount');
    if(snapshot.some(c=>c.Values.Count!==rowCount))throw new ArgumentException('Every DATATABLE column must contain exactly RowCount values.');
    const owned=new Set();
    for(const column of snapshot)for(const cell of column.Values) {
      if(!(cell instanceof DxfObject))continue;
      if(cell instanceof DxfDatabaseObject&&cell.IsErased)throw new InvalidOperationException('An erased object cannot be referenced.');
      if(column.OwnsObjects) {
        if(owned.has(cell))throw new ArgumentException('A DATATABLE object cannot occupy more than one ownership slot.');owned.add(cell);
        if(cell.Owner!==null&&cell.Owner!==this)throw new ArgumentException('A DATATABLE ownership target already has another owner.');
        if(IsAncestor(cell,this))throw new ArgumentException('DATATABLE ownership cannot form a cycle.');
        if(cell.Database!==this.Database)throw new ArgumentException("Owned cells must share the table's registration state and database.");
        if(imported&&cell.Owner!==this)throw new ArgumentException('A loaded DATATABLE ownership slot must be reciprocal.');
      }
      if(this.Database!==null)this.Database.CheckRegistered(cell);
    }
    const previous=new Set(this.DeclaredOwnedObjects);
    if(this.Database!==null&&!imported&&(previous.size!==owned.size||Array.from(previous).some(v=>!owned.has(v))))throw new InvalidOperationException('Changing the owned-object set of a registered DATATABLE requires a detached replacement graph.');
    for(const child of previous)if(!owned.has(child))child.Owner=null;
    for(const child of owned)child.Owner=this;
    this.#columns=ImmutableCellView(snapshot);this.#rows=rowCount;
  }
  get DeclaredOwnedObjects(){return Array.from(this.#columns).filter(c=>c.OwnsObjects).flatMap(c=>Array.from(c.Values).filter(v=>v instanceof DxfDatabaseObject));}
  get DatabaseReferences(){return Array.from(this.#columns).filter(c=>c.HasReferences).flatMap(c=>Array.from(c.Values).filter(v=>v instanceof DxfObject));}
  CloneShell(){const copy=new DxfDataTable();copy.Name=this.Name;return copy;}
  CopyDatabaseReferencesTo(clone,resolve){clone.SetColumns(this.RowCount,Array.from(this.#columns,c=>new DxfDataColumn(c.Type,c.Name,Array.from(c.Values,v=>v instanceof DxfObject?resolve(v):v))));}
  ValidateDatabaseSchema(database,errors){if(database.Document.DrawingVariables.AcadVer<14)errors.Add('Typed DATATABLE requires R2004 or later.');if(this.RowCount<0||Array.from(this.#columns).some(c=>c.Values.Count!==this.RowCount))errors.Add('DATATABLE is not rectangular.');}
}
