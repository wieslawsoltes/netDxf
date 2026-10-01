import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
// Port of pinned RasterOwnershipTests.cs. Original identities and assertions retained.
import { DxfDocument, Image, ImageDefinition, ImageUnits, ImageResolutionUnits, ImageDisplayQuality, Vector3, Block, Insert, XData, XDataRecord, XDataCode, ApplicationRegistry, MemoryStream } from '../../node-entry.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { OleSingle, OleWriteArtifact } from './Ole2FrameTests.js';
export function RegisterRasterOwnershipTests(){for(const v of SupportedVersions)for(const b of [false,true])for(const s of [0,1,2])Run(`objects/raster-owner/${VersionName(v)}/${BooleanName(b)}/${s}`,()=>RasterOwnership(v,b,s));}
export function RasterOwnership(v,b,s){
  for(const units of Object.values(ImageUnits)){
    let doc=new DxfDocument(v);doc.RasterVariables.DisplayFrame=(units&1)!==0;doc.RasterVariables.DisplayQuality=(units&2)!==0?ImageDisplayQuality.High:ImageDisplayQuality.Draft;doc.RasterVariables.Units=units;
    const metadata=new XData(new ApplicationRegistry('RASTER_OWNER_TEST'));metadata.XDataRecord.Add(new XDataRecord(XDataCode.String,'raster metadata'));doc.RasterVariables.XData.Add(metadata);
    if(s!==0){const definition=new ImageDefinition('RasterFixture','not-loaded.png',16,96,8,96,ImageResolutionUnits.Inches),image=new Image(definition,Vector3.Zero,16,8);if(s===1)doc.Entities.Add(image);else{const block=new Block('NestedImage');block.Entities.Add(image);doc.Entities.Add(new Insert(block,new Vector3(10,20,0)));doc.Entities.Add(new Image(definition,new Vector3(30,40,0),16,8));}}
    const frame=doc.RasterVariables.DisplayFrame,quality=doc.RasterVariables.DisplayQuality;
    for(let cycle=0;cycle<3;cycle++){const transport=cycle===1?!b:b,output=new MemoryStream();try{
      Check(doc.Save(output,transport),'Raster fixture save failed.');const bytes=output.ToArray();if(cycle===0&&units===ImageUnits.Unitless)OleWriteArtifact(`raster-owner-${VersionName(v)}-${BooleanName(b)}-${s}.dxf`,bytes);
      CheckRasterObjectGraph(bytes,transport,doc.RasterVariables.Handle,s===0?0:1);Check(output.CanWrite,'Raster save closed a caller-owned stream.');output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Raster fixture reload failed.');Check(output.CanRead,'Raster load closed a caller-owned stream.');
      Equal(frame,doc.RasterVariables.DisplayFrame,'Raster display frame changed');Equal(quality,doc.RasterVariables.DisplayQuality,'Raster quality changed');Equal(units,doc.RasterVariables.Units,'Raster units changed');Equal('raster metadata',OleSingle(doc.RasterVariables.XData.get_Item('RASTER_OWNER_TEST').XDataRecord).Value,'Raster metadata changed');Equal(s===0?0:1,doc.ImageDefinitions.Count,'Image definition count changed');
      if(s!==0){const d=doc.ImageDefinitions.get_Item('RasterFixture');Equal(16,d.Width,'Image definition width changed');Equal(8,d.Height,'Image definition height changed');Equal('not-loaded.png',d.File,'Image definition path changed');}
    }finally{output.Dispose();}}
  }
}
export function CheckRasterObjectGraph(bytes,binary,rasterHandle,definitionCount){
  const input=new MemoryStream(bytes);try{
    const reader=binary?new BinaryCodeValueReader(input):new TextCodeValueReader(new TextDecoder().decode(bytes)),records=[];let record=null,objects=false,sectionName=false;
    while(true){reader.Next();const code=reader.Code,value=reader.Value;if(code===0){if(record!==null){records.push(record);record=null;}if(value==='EOF')break;if(value==='SECTION'){sectionName=true;continue;}if(value==='ENDSEC'){objects=false;continue;}if(objects)record=[];}if(sectionName&&code===2){objects=value==='OBJECTS';sectionName=false;}if(record!==null)record.push({Code:code,Value:value});}
    const type=t=>t[0].Value,handle=t=>OleSingle(t.filter(x=>x.Code===5)).Value;
    const owner=tags=>{let depth=0;for(const t of tags){if(t.Code===102){if(t.Value.startsWith('{'))depth++;else if(t.Value==='}')depth--;}if(t.Code===330&&depth===0)return t.Value;}throw new Error('Missing object owner field.');};
    const entries=tags=>{const result=new Map();let name=null;for(const t of tags){if(t.Code===3)name=t.Value;else if((t.Code===350||t.Code===360)&&name!==null){Check(!result.has(name),'Duplicate dictionary name');result.set(name,t.Value);name=null;}}return result;};
    const root=records[0];Equal('DICTIONARY',type(root),'First OBJECTS record must be the named object dictionary');const rootHandle=handle(root),rootEntries=entries(root),imageDictionaryHandle=rootEntries.get('ACAD_IMAGE_DICT');Equal(rasterHandle,rootEntries.get('ACAD_IMAGE_VARS'),'Named dictionary lost raster variables reference');
    const raster=OleSingle(records.filter(r=>type(r)==='RASTERVARIABLES'));Equal(rasterHandle,handle(raster),'Raster object handle changed');Equal(rootHandle,owner(raster),'RASTERVARIABLES owner must match the dictionary containing ACAD_IMAGE_VARS');Check(owner(raster)!==imageDictionaryHandle,'Raster variables incorrectly belong to the image-definition dictionary.');
    const imageDictionary=OleSingle(records.filter(r=>handle(r)===imageDictionaryHandle));Equal(rootHandle,owner(imageDictionary),'Image dictionary owner changed');const imageEntries=entries(imageDictionary);Equal(definitionCount,imageEntries.size,'Image dictionary entries changed');const definitions=records.filter(r=>type(r)==='IMAGEDEF');Equal(definitionCount,definitions.length,'Image definition object count changed');for(const definition of definitions){Equal(imageDictionaryHandle,owner(definition),'IMAGEDEF must still belong to ACAD_IMAGE_DICT');Check(Array.from(imageEntries.values()).includes(handle(definition)),'Image definition lost its dictionary entry.');}
  }finally{input.Dispose();}
}
