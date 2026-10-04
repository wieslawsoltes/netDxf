// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Section } from '../Entities/Section.js';
import { DxfDatabaseObject } from '../Objects/DxfDatabaseObject.js';
import { Vector3 } from '../Vector3.js';
import { DxfTag } from './DxfTag.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { ReadXDataRecord, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { ArgumentException, InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
export class SectionParser {
  constructor(tags){this.tags=tags;this.at=0;}
  get Code(){return this.at===this.tags.length?-1:this.tags[this.at].Code;}
  Take(code){if(this.Code!==code)throw new InvalidDataException('Invalid SECTION packet at tag '+this.at+': expected '+code+', found '+this.Code+'.');return this.tags[this.at++].Value;}
  Vector(code){const x=this.Take(code),y=this.Take(code+10),z=this.Take(code+20);return new Vector3(x,y,z);}
  Vertices(section,back) {
    const count=this.Take(back?93:92);
    if(count<0||count>Section.MaximumVertices||count*3>this.tags.length-this.at)
      throw new InvalidDataException('SECTION vertex count is negative, excessive or exceeds the available packet.');
    const vertices=back?section.BackLineVertices:section.Vertices;
    for(let i=0;i<count;i++)vertices.Add(this.Vector(back?12:11));
  }
  Read(section) {
    try {
      section.State=this.Take(90);section.Flags=this.Take(91);section.Name=DecodeDxfText(this.Take(1));
      section.VerticalDirection=this.Vector(10);section.TopHeight=this.Take(40);section.BottomHeight=this.Take(41);section.IndicatorTransparency=this.Take(70);
      const colors=new Set();
      while([62,63,411].includes(this.Code)) {
        const code=this.Code;if(colors.has(code))throw new InvalidDataException('Duplicate SECTION indicator color field.');colors.add(code);
        if(code===62)section.StoredNativeIndicatorColor=this.Take(code);
        else if(code===63)section.StoredIndicatorColor=this.Take(code);
        else section.IndicatorColorName=DecodeDxfText(this.Take(code));
      }
      this.Vertices(section,false);this.Vertices(section,true);let settings=null;
      if(this.Code===360){section.HasSettingsField=true;const tag=new DxfTag(360,this.Take(360));settings=BigInt('0x'+tag.Value)===0n?null:tag.Value;}
      if(this.Code!==-1)throw new InvalidDataException('Unsupported or duplicate SECTION payload field '+this.Code+'.');
      return settings;
    }catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid SECTION stored value.',error);throw error;}
  }
}
export function ReadSection(context,codeName) {
  const document=context.Document,chunk=context.Chunk;
  if(document.DrawingVariables.AcadVer<15)throw new NotSupportedException('SECTION input requires R2007 or later.');
  if(chunk.Code!==100||chunk.ReadString()!=='AcDbSection')throw new InvalidDataException('SECTION requires AcDbSection subclass data.');
  const section=new Section(codeName),tags=[];section.PendingInputReferences=true;section.HasSettingsField=false;let extended=false;chunk.Next();
  while(chunk.Code!==0) {
    if(chunk.Code===1001){extended=true;section.XData.Add(ReadXDataRecord(chunk,document));continue;}
    if(extended)throw new InvalidDataException('SECTION XData must follow its complete payload.');
    if(tags.length>=6*Section.MaximumVertices+32)throw new InvalidDataException('SECTION payload exceeds the vertex admission limit.');
    tags.push(new DxfTag(chunk.Code,chunk.Value));chunk.Next();
  }
  const settings=new SectionParser(tags).Read(section);context.loadedSections.push([section,settings]);return section;
}
export function ResolveSections(context) {
  for(const [section,handle] of context.loadedSections) {
    if(handle!==null&&handle!=='0') {
      const settings=context.GetObjectBySourceHandle(handle);
      if(!(settings instanceof DxfDatabaseObject)||!['SECTIONSETTINGS','SECTION_SETTINGS'].includes(settings.CodeName))
        throw new InvalidDataException('SECTION geometry settings must resolve to a settings object.');
      if(settings.Owner!==section)throw new InvalidDataException('SECTION geometry settings must have the reciprocal section owner.');
      section.GeometrySettings=settings;
    }
    section.PendingInputReferences=false;section.Validate(context.Document);
  }
}
