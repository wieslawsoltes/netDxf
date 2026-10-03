import { NullReferenceException } from '../../runtime/Errors.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { AcisEntity } from '../Entities/AcisEntity.js';
import { AcisSatChunk } from '../Entities/AcisSatChunk.js';
import { Body } from '../Entities/Body.js';
import { Region } from '../Entities/Region.js';
import { Solid3D } from '../Entities/Solid3D.js';
import { ArgumentException, InvalidDataException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { ReadXDataRecord, WriteXData, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
export function ReadAcisEntity(chunk, document, entityCode) {
  const version=document.DrawingVariables.AcadVer;
  if(version>=17)throw new NotSupportedException('DXF 2013+ ACIS requires SAB and ACDSDATA lifecycle support; use DxfRawDocument to preserve the complete file.');
  if(chunk.Code!==100 || chunk.ReadString()!=='AcDbModelerGeometry')throw new InvalidDataException('ACIS entities require AcDbModelerGeometry.');
  const entity=entityCode==='BODY'?new Body():entityCode==='REGION'?new Region():new Solid3D(),parts=[],xdata=[];
  let hasVersion=false,historySubclass=false,hasHistory=false,ended=false,total=0,lineLength=0;
  chunk.Next();
  while(chunk.Code!==0) {
    const code=chunk.Code;
    if(code===1001){ended=true;xdata.push(ReadXDataRecord(chunk,document));continue;}
    if(ended)throw new InvalidDataException('Unexpected ACIS data after XData.');
    switch(code) {
      case 70:
        if(hasVersion || parts.length>0 || historySubclass)throw new InvalidDataException('Misplaced or duplicate ACIS modeler format version.');
        if(chunk.ReadShort()!==1)throw new InvalidDataException('Unsupported ACIS modeler format version; use DxfRawDocument for opaque preservation.');
        hasVersion=true;break;
      case 1:case 3: {
        if(!hasVersion || historySubclass || code===3 && parts.length===0)throw new InvalidDataException('Misplaced ACIS SAT chunk.');
        const text=chunk.ReadString();if(code===1)lineLength=0;
        if(parts.length>=AcisEntity.MaximumSatChunks || text.length>AcisEntity.MaximumSatCharacters-total || text.length>AcisEntity.MaximumSatLineCharacters-lineLength)
          throw new InvalidDataException('ACIS SAT payload limit exceeded.');
        total+=text.length;lineLength+=text.length;
        try{parts.push(new AcisSatChunk(code,text));}catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid ACIS SAT chunk.',error);throw error;}
        break;
      }
      case 100:
        if(!(entity instanceof Solid3D) || historySubclass || !hasVersion || parts.length===0 || version<15 || chunk.ReadString()!=='AcDb3dSolid')throw new InvalidDataException('Unsupported or misplaced ACIS subclass.');
        historySubclass=true;break;
      case 350:
        if(!historySubclass || hasHistory)throw new InvalidDataException('Misplaced or duplicate ACIS history handle.');
        {const handle=chunk.ReadString();if(handle!=='0')throw new NotSupportedException('Live ACIS history requires an unsupported object graph; use DxfRawDocument to preserve it.');entity.HistoryHandle=handle;hasHistory=true;}break;
      default:throw new InvalidDataException('Unsupported ACIS group '+code+'; use DxfRawDocument for private extensions.');
    }
    chunk.Next();
  }
  if(!hasVersion || parts.length===0)throw new InvalidDataException('ACIS modeler format version and SAT payload are required.');
  try{entity.SetEncodedSatChunks(parts);}catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid encoded ACIS SAT payload.',error);throw error;}
  for(const data of xdata)entity.XData.Add(data);return entity;
}
export function ValidateAcisEntities(document){
  const version=document.DrawingVariables.AcadVer;
  for(const block of document.Blocks)for(const entity of block.Entities){
    if(!(entity instanceof AcisEntity))continue;
    if(version>=17)throw new NotSupportedException('SAT ACIS entities support DXF 2000 through 2010 only. DXF 2013+ requires SAB and ACDSDATA lifecycle support.');
    if(entity.EncodedSatChunks.Count===0)throw new InvalidOperationException('An ACIS entity requires a SAT payload before saving.');
    if(entity instanceof Solid3D && entity.HistoryHandle!==null && version<15)throw new NotSupportedException('Explicit ACIS history metadata is qualified for DXF 2007 through 2010 only.');
  }
}
export function WriteAcisEntity(chunk, version, entity){
  chunk.Write(100,'AcDbModelerGeometry');if(entity==null)throw new NullReferenceException();chunk.Write(70,entity.ModelerFormatVersion);
  for(const part of entity.EncodedSatChunks)chunk.Write(part.GroupCode,part.Text);
  if(entity instanceof Solid3D && version>=15){chunk.Write(100,'AcDb3dSolid');if(entity.HistoryHandle!==null)chunk.Write(350,entity.HistoryHandle);}
  WriteXData(chunk,version,entity.XData);
}
