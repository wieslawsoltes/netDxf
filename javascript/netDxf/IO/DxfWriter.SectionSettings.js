// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfSectionSettings } from '../Objects/DxfSectionSettings.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export function WriteSectionSettingsPayload(chunk,version,item) {
  if(!(item instanceof DxfSectionSettings))return false;
  if(version<15)throw new NotSupportedException('Typed SECTIONSETTINGS requires DXF 2007 or later.');
  item.ValidateValues();const text=value=>EncodeDxfDatabaseText(value,version);
  chunk.Write(100,'AcDbSectionSettings');chunk.Write(90,item.SectionType);chunk.Write(91,item.TypeSettings.Count);
  for(const type of item.TypeSettings) {
    chunk.Write(1,'SectionTypeSettings');chunk.Write(90,type.SectionType);chunk.Write(91,type.GenerationOptions);
    chunk.Write(92,type.SourceObjects.Count);for(const source of type.SourceObjects)chunk.Write(330,source?.Handle??'0');
    chunk.Write(331,type.DestinationBlock?.Handle??'0');chunk.Write(1,text(type.DestinationFileName));chunk.Write(93,type.GeometrySettings.Count);
    if(!type.RepeatGeometryMarkers)chunk.Write(2,'SectionGeometrySettings');
    for(const value of type.GeometrySettings) {
      if(type.RepeatGeometryMarkers)chunk.Write(2,'SectionGeometrySettings');
      chunk.Write(90,value.SectionType);chunk.Write(91,value.GeometryValue);chunk.Write(92,value.Flags);
      chunk.Write(value.ColorCode,value.ColorIndex);chunk.Write(8,text(value.LayerName));chunk.Write(6,text(value.LinetypeName));
      chunk.Write(40,value.LinetypeScale);chunk.Write(1,text(value.PlotStyleName));chunk.Write(370,value.Lineweight);
      chunk.Write(70,value.FaceTransparency);chunk.Write(71,value.EdgeTransparency);chunk.Write(72,value.HatchPatternType);
      chunk.Write(2,text(value.HatchPatternName));chunk.Write(41,value.HatchAngle);chunk.Write(42,value.HatchScale);chunk.Write(43,value.HatchSpacing);
      chunk.Write(3,'SectionGeometrySettingsEnd');
    }
    chunk.Write(3,'SectionTypeSettingsEnd');
  }
  return true;
}
