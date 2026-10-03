// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { WriteMLeaderVector } from './DxfWriter.MultiLeader.js';
import { InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
const allEntities=doc=>Array.from(doc.Blocks).flatMap(block=>Array.from(block.Entities));
function prepare(definitions,name,cpp,app,isEntity,count,typed,flags){
  if(definitions.Contains(name)){
    const item=definitions.get_Item(name);
    if(item.CppClassName!==cpp||item.IsEntity!==isEntity){if(typed)throw new InvalidDataException('CLASS conflicts with '+name);return;}
    item.InstanceCount=count;
  }else if(typed){const item=new api.DxfClass(name,cpp,app);item.ProxyFlags=flags;item.IsEntity=isEntity;item.InstanceCount=count;definitions.Add(item);}
}
export function PrepareSectionClasses(doc,definitions){
  for(const name of ['SECTION','SECTIONOBJECT']){
    const count=allEntities(doc).filter(e=>e instanceof api.Section&&e.CodeName===name).length;
    prepare(definitions,name,'AcDbSection','ObjectDBX Classes',true,count,count!==0,1025);
  }
  for(const name of ['SECTIONSETTINGS','SECTION_SETTINGS']){
    const objects=Array.from(doc.Objects.Items),count=objects.filter(o=>o.CodeName===name).length;
    prepare(definitions,name,'AcDbSectionSettings','ObjectDBX Classes',false,count,objects.some(o=>o.CodeName===name&&o instanceof api.DxfSectionSettings),1024);
  }
}

export function ValidateSections(document) {
  for(const block of document.Blocks)for(const section of block.Entities)if(section instanceof api.Section)section.Validate(document);
  for(const settings of document.AddedObjects.Values)if(settings instanceof api.DxfSectionSettings) {
    if(document.DrawingVariables.AcadVer<15)throw new NotSupportedException('SECTIONSETTINGS output requires R2007 or later.');settings.ValidateValues();
  }
}
export function WriteSection(chunk,document,section) {
  const version=()=>document.DrawingVariables.AcadVer,encode=value=>EncodeDxfDatabaseText(value,version());
  chunk.Write(100,'AcDbSection');chunk.Write(90,section.State);chunk.Write(91,section.Flags);
  chunk.Write(1,encode(section.Name));WriteMLeaderVector(chunk,10,section.VerticalDirection);
  chunk.Write(40,section.TopHeight);chunk.Write(41,section.BottomHeight);chunk.Write(70,section.IndicatorTransparency);
  if(section.StoredNativeIndicatorColor!==null)chunk.Write(62,section.StoredNativeIndicatorColor);
  if(section.StoredIndicatorColor!==null)chunk.Write(63,section.StoredIndicatorColor);
  if(section.IndicatorColorName!==null)chunk.Write(411,encode(section.IndicatorColorName));
  chunk.Write(92,section.Vertices.Count);for(const vertex of section.Vertices)WriteMLeaderVector(chunk,11,vertex);
  chunk.Write(93,section.BackLineVertices.Count);for(const vertex of section.BackLineVertices)WriteMLeaderVector(chunk,12,vertex);
  if(section.HasSettingsField)chunk.Write(360,section.GeometrySettings===null?'0':section.GeometrySettings.Handle);
  WriteXData(chunk,version,section.XData);
}
