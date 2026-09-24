// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Explicit adapters for every private member of the pinned DxfHelix.cs partial.
import { Helix } from '../Entities/Helix.js';
import { DxfClass } from '../DxfClass.js';
import { ReadSpline, WriteSpline } from '../../runtime/SplineIO.js';
import { ReadXDataRecord, WriteXData, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { NullReferenceException, ArgumentException, InvalidDataException, NotSupportedException, OverflowException } from '../../runtime/Errors.js';
const scalar={90:['MajorReleaseNumber','ReadInt'],91:['MaintenanceReleaseNumber','ReadInt'],40:['Radius','ReadDouble'],41:['Turns','ReadDouble'],42:['TurnHeight','ReadDouble'],290:['IsRightHanded','ReadBool'],280:['Constraint','ReadShort']};
const vector={10:['base','X'],20:['base','Y'],30:['base','Z'],11:['start','X'],21:['start','Y'],31:['start','Z'],12:['axis','X'],22:['axis','Y'],32:['axis','Z']};
export function ReadHelix(chunk,document) {
  if(chunk==null)throw new NullReferenceException();
  if(chunk.Code!==100||chunk.ReadString()!=='AcDbSpline')throw new InvalidDataException('HELIX requires its AcDbSpline subclass before AcDbHelix.');
  const spline=ReadSpline(chunk,document,true);
  if(chunk.Code!==100||chunk.ReadString()!=='AcDbHelix')throw new InvalidDataException('HELIX is missing its AcDbHelix subclass.');
  const e=new Helix(spline),seen=new Set(),v={base:e.AxisBasePoint,start:e.StartPoint,axis:e.AxisVector};chunk.Next();
  while(chunk.Code!==0) {
    const code=chunk.Code;
    if(code===1001){e.XData.Add(ReadXDataRecord(chunk,document));continue;}
    if(code===100&&chunk.ReadString()==='AcDbHelix')throw new InvalidDataException('HELIX has a duplicate AcDbHelix subclass.');
    if(scalar[code]||vector[code]){if(seen.has(code))throw new InvalidDataException('HELIX has duplicate group '+code+'.');seen.add(code);}
    try {
      if(scalar[code]){const [property,read]=scalar[code];e[property]=chunk[read]();}
      else if(vector[code]){const [key,axis]=vector[code];v[key][axis]=chunk.ReadDouble();}
      else if(code>=1000&&code<=1071)throw new InvalidDataException('HELIX extended data must begin with an application registry.');
    }catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid HELIX group '+code+' at position '+chunk.CurrentPosition+'.',error);throw error;}
    chunk.Next();
  }
  for(let c=10;c<=12;c++){const count=Number(seen.has(c))+Number(seen.has(c+10))+Number(seen.has(c+20));if(count!==0&&count!==3)throw new InvalidDataException('Incomplete HELIX vector at group '+c+'.');}
  try{e.AxisBasePoint=v.base;e.StartPoint=v.start;e.AxisVector=v.axis;}
  catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid HELIX position or axis vector.',error);throw error;}
  return e;
}
export function ValidateHelixVersions(document) {
  if(document==null)throw new NullReferenceException();if(document.DrawingVariables.AcadVer>=15)return;
  for(const block of document.Blocks)for(const e of block.Entities)if(e instanceof Helix)throw new NotSupportedException('HELIX requires AutoCAD 2007 (AC1021) or later in this writer profile. Convert explicitly with ToSpline for older profiles.');
}
export function PrepareHelixClass(document,definitions) {
  if(document==null)throw new NullReferenceException();let count=0;
  for(const block of document.Blocks)for(const e of block.Entities)if(e instanceof Helix){if(count===2147483647)throw new OverflowException();count++;}
  if(definitions==null)throw new NullReferenceException();
  if(definitions.Contains('HELIX')) {
    const entry=definitions.get_Item('HELIX');
    if(count!==0&&(entry.CppClassName!=='AcDbHelix'||!entry.IsEntity))throw new InvalidDataException('CLASS conflicts with the generated HELIX entity definition.');
    if(entry.CppClassName==='AcDbHelix'&&entry.IsEntity)entry.InstanceCount=count;
  }else if(count!==0){const entry=new DxfClass('HELIX','AcDbHelix','ObjectDBX Classes');entry.ProxyFlags=4095;entry.IsEntity=true;entry.InstanceCount=count;definitions.Add(entry);}
}
export function WriteHelix(chunk,version,e) {
  WriteSpline(chunk,version,e,false);
  chunk.Write(210,e.Normal.X);chunk.Write(220,e.Normal.Y);chunk.Write(230,e.Normal.Z);chunk.Write(100,'AcDbHelix');
  chunk.Write(90,e.MajorReleaseNumber);chunk.Write(91,e.MaintenanceReleaseNumber);
  chunk.Write(10,e.AxisBasePoint.X);chunk.Write(20,e.AxisBasePoint.Y);chunk.Write(30,e.AxisBasePoint.Z);
  chunk.Write(11,e.StartPoint.X);chunk.Write(21,e.StartPoint.Y);chunk.Write(31,e.StartPoint.Z);
  chunk.Write(12,e.AxisVector.X);chunk.Write(22,e.AxisVector.Y);chunk.Write(32,e.AxisVector.Z);
  chunk.Write(40,e.Radius);chunk.Write(41,e.Turns);chunk.Write(42,e.TurnHeight);chunk.Write(290,e.IsRightHanded);chunk.Write(280,(e.Constraint<<16)>>16);WriteXData(chunk,version,e.XData);
}
