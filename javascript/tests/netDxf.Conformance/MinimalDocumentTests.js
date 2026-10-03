// Port of pinned MinimalDocumentTests.cs; low-level tags author independent minimal inputs.
import { DxfDocument, MemoryStream, Vector3, Line, Circle, View, UCS, Layout, Layer, Linetype } from '../../index.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { Run, Check, Equal, Near, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';

function single(values){const all=Array.from(values);Equal(1,all.length,'Single');return all[0];}
export function RegisterMinimalDocumentTests(){for(const version of SupportedVersions)for(const binary of [false,true]){
  const suffix=`${VersionName(version)}/${BooleanName(binary)}`;
  Run('minimal/header-only/'+suffix,()=>CheckMinimalDocument(version,binary,false,false));
  Run('minimal/empty-sections/'+suffix,()=>CheckMinimalDocument(version,binary,false,true));
  Run('minimal/implicit-tables/'+suffix,()=>CheckMinimalDocument(version,binary,true,false));
  Run('minimal/implicit-tables-empty-objects/'+suffix,()=>CheckMinimalDocument(version,binary,true,true));
  Run('minimal/omitted-objects/'+suffix,()=>CheckOmittedObjects(version,binary,false));
  Run('minimal/empty-objects/'+suffix,()=>CheckOmittedObjects(version,binary,true));
  Run('minimal/blocks-without-tables/'+suffix,()=>CheckMinimalBlock(version,binary));
}}
export function CheckMinimalDocument(version,binary,entity,emptySections){
  const input=CreateMinimalDocument(version,binary,entity,emptySections),output=new MemoryStream();
  try{const loaded=DxfDocument.Load(input);Check(loaded!==null,'Minimal DXF failed to load.');Check(input.CanRead,'Load closed its caller-owned stream.');CheckInitializedDocument(loaded);Equal(version,loaded.DrawingVariables.AcadVer,'minimal version');Equal(entity?1:0,Array.from(loaded.Entities.Lines).length,'minimal LINE count');
    if(entity){const line=single(loaded.Entities.Lines);Equal(new Vector3(1,2,3),line.StartPoint,'authored start');Equal(new Vector3(4,5,6),line.EndPoint,'authored end');Equal('200',line.Handle,'authored entity handle');Equal('ImplicitLayer',line.Layer.Name,'implicit layer');Equal(7,line.Layer.Color.Index,'implicit layer color');Equal(Linetype.DefaultName,line.Layer.Linetype.Name,'implicit linetype');Equal('authored payload',line.XData.get_Item('MINIMAL_DXF').XDataRecord.get_Item(0).Value,'implicit application registry');Check(loaded.GetObjectByHandle('200')===line,'Entity handle did not resolve.');Check(line.Owner===loaded.Layouts.get_Item('Model').AssociatedBlock,'Entity was not assigned to model space.');}
    Check(loaded.Save(output,binary),'Recovered minimal document failed to save.');output.Position=0;const reloaded=DxfDocument.Load(output);Check(reloaded!==null,'Recovered minimal document failed to reload.');CheckInitializedDocument(reloaded);Equal(entity?1:0,Array.from(reloaded.Entities.Lines).length,'minimal second round trip');
  }finally{output.Dispose();input.Dispose();}
}
export function CheckInitializedDocument(document){
  Check(['ApplicationRegistries','Blocks','DimensionStyles','Layers','Linetypes','TextStyles','ShapeStyles','UCSs','Views','VPorts'].every(n=>document[n]!=null),'A symbol-table collection was not initialized.');
  Check(['Groups','Layouts','MlineStyles','ImageDefinitions','UnderlayDgnDefinitions','UnderlayDwfDefinitions','UnderlayPdfDefinitions','RasterVariables'].every(n=>document[n]!=null),'An object collection was not initialized.');
  const model=document.Layouts.get_Item('Model');Check(model!=null&&model.AssociatedBlock!=null,'Default model-space graph is missing.');Check(model.AssociatedBlock.Record.Layout===model,'Model-space ownership is inconsistent.');
  Check(document.Layers.Contains(Layer.Default.Name),'Default layer is missing.');Check(document.Linetypes.Contains(Linetype.Continuous.Name),'Continuous linetype is missing.');Equal('Model',document.Entities.ActiveLayout,'active layout');
}
export function CreateMinimalDocument(version,binary,entity,emptySections,block=false){
  const stream=new MemoryStream(),writer=binary?new BinaryCodeValueWriter(stream):new TextCodeValueWriter(stream),tag=(c,v)=>writer.Write(c,v);
  tag(0,'SECTION');tag(2,'HEADER');tag(9,'$ACADVER');tag(1,HeaderVersion(version));tag(9,'$DWGCODEPAGE');tag(3,'ANSI_1252');tag(9,'$HANDSEED');tag(5,'1000');tag(0,'ENDSEC');
  if(emptySections){tag(0,'SECTION');tag(2,'TABLES');tag(0,'ENDSEC');tag(0,'SECTION');tag(2,'BLOCKS');tag(0,'ENDSEC');}
  if(block){tag(0,'SECTION');tag(2,'BLOCKS');tag(0,'BLOCK');tag(5,'203');tag(100,'AcDbEntity');tag(8,'BlockLayer');tag(100,'AcDbBlockBegin');tag(2,'Component');tag(70,0);tag(10,0);tag(20,0);tag(30,0);tag(3,'Component');tag(1,'');
    tag(0,'LINE');tag(5,'201');tag(100,'AcDbEntity');tag(8,'BlockLayer');tag(100,'AcDbLine');tag(10,0);tag(20,0);tag(30,0);tag(11,1);tag(21,2);tag(31,3);
    tag(0,'ENDBLK');tag(5,'204');tag(100,'AcDbEntity');tag(8,'BlockLayer');tag(100,'AcDbBlockEnd');tag(0,'ENDSEC');}
  if(entity||emptySections){tag(0,'SECTION');tag(2,'ENTITIES');
    if(block){tag(0,'INSERT');tag(5,'200');tag(100,'AcDbEntity');tag(8,'ImplicitLayer');tag(100,'AcDbBlockReference');tag(2,'Component');tag(10,-1);tag(20,2);tag(30,3);}
    else if(entity){tag(0,'LINE');tag(5,'200');tag(100,'AcDbEntity');tag(8,'ImplicitLayer');tag(100,'AcDbLine');tag(10,1);tag(20,2);tag(30,3);tag(11,4);tag(21,5);tag(31,6);tag(1001,'MINIMAL_DXF');tag(1000,'authored payload');}tag(0,'ENDSEC');}
  if(emptySections){tag(0,'SECTION');tag(2,'OBJECTS');tag(0,'ENDSEC');}tag(0,'EOF');writer.Flush();stream.Position=0;return stream;
}
export function CheckMinimalBlock(version,binary){
  const input=CreateMinimalDocument(version,binary,true,false,true),output=new MemoryStream();
  try{const loaded=DxfDocument.Load(input);Check(loaded!==null,'Block-only tables fixture failed.');CheckInitializedDocument(loaded);const insert=single(loaded.Entities.Inserts);
    Equal(new Vector3(-1,2,3),insert.Position,'minimal insert position');Equal('Component',insert.Block.Name,'minimal block name');const child=single(Array.from(insert.Block.Entities).filter(e=>e instanceof Line));
    Equal(new Vector3(1,2,3),child.EndPoint,'minimal block geometry');Check(child.Owner===insert.Block,'Nested entity has no block owner.');Check(loaded.GetObjectByHandle('201')===child,'Nested entity handle changed.');
    Check(loaded.Save(output,binary),'Block fixture could not save.');output.Position=0;const roundTrip=DxfDocument.Load(output);Check(roundTrip!==null,'Block fixture could not reload.');Equal('Component',single(roundTrip.Entities.Inserts).Block.Name,'reloaded block reference');
  }finally{output.Dispose();input.Dispose();}
}
export function CheckOmittedObjects(version,binary,emptySection){
  const original=new DxfDocument(version);original.Entities.Add(new Line(Vector3.Zero,new Vector3(1,2,3)));const view=new View('KeepView');view.Width=32;view.Height=24;original.Views.Add(view);const ucs=new UCS('KeepUcs');ucs.Elevation=4.5;original.UCSs.Add(ucs);original.Layouts.Add(new Layout('Review'));original.Entities.ActiveLayout='Review';original.Entities.Add(new Circle(new Vector3(7,8,9),2.5));original.Entities.ActiveLayout='Model';
  const lineHandle=single(original.Entities.Lines).Handle,viewHandle=original.Views.get_Item('KeepView').Handle,generated=new MemoryStream(),modified=new MemoryStream(),output=new MemoryStream();
  try{Check(original.Save(generated,binary),'Source fixture save failed.');generated.Position=0;const reader=binary?new BinaryCodeValueReader(generated):new TextCodeValueReader(new TextDecoder().decode(generated.ToArray())),tags=[];
    do{reader.Next();tags.push([reader.Code,reader.Value]);}while(!(reader.Code===0&&reader.Value==='EOF'));
    const start=tags.findIndex(([c,v])=>c===2&&v==='OBJECTS')-1;Check(start>=0,'Fixture has no OBJECTS section.');const end=tags.findIndex(([c,v],i)=>i>=start+2&&c===0&&v==='ENDSEC');Check(end>start,'Fixture has no OBJECTS terminator.');
    if(emptySection)tags.splice(start+2,end-start-2);else tags.splice(start,end-start+1);const writer=binary?new BinaryCodeValueWriter(modified):new TextCodeValueWriter(modified);for(const [c,v] of tags)writer.Write(c,v);writer.Flush();modified.Position=0;
    const loaded=DxfDocument.Load(modified);Check(loaded!==null,'Omitted OBJECTS failed to load.');CheckInitializedDocument(loaded);Equal(2,loaded.Layouts.Count,'reconstructed layout count');Check(single(loaded.Entities.Lines).Handle===lineHandle,'Existing LINE handle changed.');Check(loaded.Views.get_Item('KeepView').Handle===viewHandle,'Existing VIEW handle changed.');Near(4.5,loaded.UCSs.get_Item('KeepUcs').Elevation,'existing UCS metadata');
    const paper=single(Array.from(loaded.Layouts).filter(l=>l.IsPaperSpace)),circle=single(Array.from(paper.AssociatedBlock.Entities).filter(e=>e instanceof Circle));Near(2.5,circle.Radius,'recovered paper-space geometry');Check(circle.Owner===paper.AssociatedBlock,'Paper-space owner changed.');Check(loaded.GetObjectByHandle(circle.Handle)===circle,'Paper-space handle no longer resolves.');Check(loaded.Save(output,binary),'Recovered drawing could not save.');output.Position=0;Check(DxfDocument.Load(output)!==null,'Recovered drawing could not reload.');
  }finally{output.Dispose();modified.Dispose();generated.Dispose();}
}
