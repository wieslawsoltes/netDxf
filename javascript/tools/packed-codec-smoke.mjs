// Executed from the offline-installed package, never from source-tree imports.
{
  const {MemoryStream}=await import('@netdxf/javascript');
  const {TextCodeValueReader}=await import('@netdxf/javascript/netDxf/IO/TextCodeValueReader.js');
  const {BinaryCodeValueReader}=await import('@netdxf/javascript/netDxf/IO/BinaryCodeValueReader.js');
  const {BinaryCodeValueWriter}=await import('@netdxf/javascript/netDxf/IO/BinaryCodeValueWriter.js');
  const text=new TextCodeValueReader('70\n42\n310\n00GG\n');text.Next();
  try{text.Next();throw new Error('Invalid hex accepted');}catch(error){if(error.name!=='FormatException'||!error.Message.includes('byte 1'))throw error;}
  if(text.ReadShort()!==42||text.CurrentPosition!==3)throw new Error('Installed failed-reader state differs.');
  const source=new MemoryStream(),writer=new BinaryCodeValueWriter(source);
  writer.Write(40,-0);writer.Write(310,Uint8Array.of(0,255));writer.Write(0,'EOF');writer.Flush();source.Position=0;
  const reader=new BinaryCodeValueReader(source);reader.Next();if(!Object.is(reader.ReadDouble(),-0))throw new Error('Installed double lost negative zero.');
  reader.Next();if(reader.ReadBytes()[1]!==255)throw new Error('Installed chunk changed.');reader.Next();if(reader.ReadString()!=='EOF'||!source.CanRead)throw new Error('Installed stream ownership differs.');
  const counts=[],large=new BinaryCodeValueWriter({Write:bytes=>counts.push(bytes.length)});counts.length=0;large.WriteString('Ω'.repeat(40000));
  if(String(counts)!=='65536,14464,1')throw new Error('Installed UTF-8 framing differs.');
}

// Section helpers come from the installed tarball, with no checkout/oracle dependencies.
{
  const api=await import('@netdxf/javascript');
  const standalone=await import('@netdxf/javascript/netDxf/IO/DxfThumbnailImage.js');
  const {TextCodeValueWriter}=await import('@netdxf/javascript/netDxf/IO/TextCodeValueWriter.js');
  const {TextCodeValueReader}=await import('@netdxf/javascript/netDxf/IO/TextCodeValueReader.js');
  if(standalone.DxfThumbnailImage!==api.DxfThumbnailImage)throw new Error('Preview standalone identity differs.');
  const host={text:'',WriteLine(v){this.text+=String(v)+'\n';}},writer=new TextCodeValueWriter(host);
  const bytes=Uint8Array.from({length:257},(_,i)=>i);api.DxfThumbnailImage.Write(writer,bytes);
  const reader=new TextCodeValueReader(host.text);reader.Next();reader.Next();
  const loaded=api.DxfThumbnailImage.Read(reader);
  if(loaded.length!==bytes.length||loaded.some((v,i)=>v!==bytes[i])||reader.ReadString()!=='ENDSEC')throw new Error('Packed preview transport failed.');
  const data=new api.DxfTransport.EntityCommonDataReader();
  api.DxfTransport.ReadEntityCommonData({Code:160,ReadLong:()=>0n},18,data);data.Complete();
  if(data.ProxyGraphics.length!==0)throw new Error('Packed proxy metadata failed.');
  const background=new api.MTextBackgroundFill();background.ScaleFactor=null;background.ColorIndex=null;
  const tags=[];api.DxfTransport.WriteMTextBackground({Write:(c,v)=>tags.push([c,v])},18,background);
  if(tags.length!==3||background.ScaleFactor!==null||background.ColorIndex!==null)throw new Error('Packed background output mutated defaults.');
}

// Entity body codecs are exercised solely from the installed package.
{
  const a=await import('@netdxf/javascript');
  const {ReadOleFrame,WriteOleFrame}=await import('@netdxf/javascript/netDxf/IO/DxfOleFrame.js');
  const {TextCodeValueReader}=await import('@netdxf/javascript/netDxf/IO/TextCodeValueReader.js');
  const {TextCodeValueWriter}=await import('@netdxf/javascript/netDxf/IO/TextCodeValueWriter.js');
  if(ReadOleFrame!==a.DxfTransport.ReadOleFrame)throw new Error('Installed entity codec barrel identity differs.');
  const frame=new a.OleFrame(Uint8Array.of(1,2,3)),text={value:'',WriteLine(v){this.value+=String(v??'')+'\n';},Flush(){}};
  const writer=new TextCodeValueWriter(text);WriteOleFrame(writer,18,frame);writer.Write(0,'ENDSEC');writer.Flush();
  const reader=new TextCodeValueReader(text.value);reader.Next();const loaded=ReadOleFrame(reader,new a.DxfDocument(18));
  if(loaded.BinaryDataLength!==3||loaded.GetBinaryData()[2]!==3||reader.Value!=='ENDSEC')throw new Error('Installed OLE body roundtrip failed.');
}

// Primitive and inherited HELIX codecs resolve from the offline-installed tarball.
{
  const a=await import('@netdxf/javascript');
  const {WriteHelix}=await import('@netdxf/javascript/netDxf/IO/DxfHelix.js');
  const {BinaryCodeValueReader}=await import('@netdxf/javascript/netDxf/IO/BinaryCodeValueReader.js');
  const {BinaryCodeValueWriter}=await import('@netdxf/javascript/netDxf/IO/BinaryCodeValueWriter.js');
  if(WriteHelix!==a.DxfTransport.WriteHelix)throw new Error('HELIX standalone differs.');
  const d=new a.DxfDocument(18),line=new a.Line(new a.Vector3(-0,2,3),a.Vector3.UnitX),bytes=new a.MemoryStream();
  const writer=new BinaryCodeValueWriter(bytes);a.DxfTransport.WriteLine(writer,18,line);writer.Write(0,'ENDSEC');writer.Flush();bytes.Position=0;
  const reader=new BinaryCodeValueReader(bytes);reader.Next();const copy=a.DxfTransport.ReadLine(reader,d);
  if(!Object.is(copy.StartPoint.X,-0)||reader.Value!=='ENDSEC')throw new Error('Primitive body roundtrip differs.');
  const spline=new a.Spline([a.Vector3.Zero,a.Vector3.UnitX,a.Vector3.UnitY,new a.Vector3(1,1,1)],null,3),helix=new a.Helix(spline);
  d.Entities.Add(helix);a.DxfTransport.PrepareHelixClass(d,d.Classes);
  if(d.Classes.get_Item('HELIX').InstanceCount!==1)throw new Error('HELIX class not prepared.');
  const output=[];WriteHelix({Write:(c,v)=>output.push([c,v])},18,helix);
  if(!output.some(([c,v])=>c===100&&v==='AcDbHelix'))throw new Error('HELIX body missing.');
}

// Database-body parsing and physical-source identity use installed modules only.
{
  const a=await import('@netdxf/javascript');
  const io=await import('@netdxf/javascript/runtime/DatabasePayloadIO.js');
  const standalone=await import('@netdxf/javascript/netDxf/IO/DxfReader.Containers.js');
  if(standalone.ReadContainerPayload!==a.DxfTransport.ReadContainerPayload)throw new Error('Installed database adapter identity differs.');
  const d=new a.DxfDocument(18),line=new a.Line();d.Entities.Add(line);
  const ctx=new io.DatabaseIOContext(d),id=new io.SourceRecordIdentity();id.Handle=BigInt('0x'+line.Handle);id.IdentitySeen=true;
  ctx.sourceObjectIdentities.add(id.Handle);ctx.RecordSourceObject(line,id);
  const record=new io.DatabaseRecord(),tags=[[100,'AcDbIdBuffer'],[330,line.Handle],[330,'0'],[330,line.Handle]].map(([c,v])=>new a.DxfTag(c,v));
  io.ReadContainerPayload(ctx,record,'IDBUFFER',tags,0);io.ResolveContainerReferences(ctx,record);
  if(record.Object.References.Count!==3||record.Object.References.get_Item(1)!==null)throw new Error('Installed IDBUFFER references differ.');
  const output=[];io.WriteContainerPayload({Write:(c,v)=>output.push([c,v])},record.Object);
  if(output.length!==4||output[2][1]!=='0')throw new Error('Installed IDBUFFER output differs.');
  id.Ambiguous=true;if(ctx.GetObjectBySourceHandle(line.Handle)!==null)throw new Error('Installed ambiguous source identity accepted.');
}

// Installed output, geographic and SUN adapters do not depend on checkout fixtures.
{
  const a=await import('@netdxf/javascript');
  const io=await import('@netdxf/javascript/runtime/DatabasePayloadIO.js');
  const standalone=await import('@netdxf/javascript/netDxf/IO/DxfReader.OutputSettings.js');
  if(standalone.ParsePlotSettings!==io.ParsePlotSettings)throw new Error('Output settings standalone identity differs.');
  const document=new a.DxfDocument(18),context=new io.DatabaseIOContext(document),handle={};
  const plot=io.ParsePlotSettings(context,[new a.DxfTag(100,'AcDbPlotSettings'),new a.DxfTag(1,'Setup'),new a.DxfTag(147,-0)],handle);
  if(plot.PageSetupName!=='Setup'||!Object.is(plot.StandardScaleFactor,-0)||handle.value!==null)throw new Error('Installed plot projection differs.');
  try{plot.ShadePlotObject=document;throw new Error('Document accepted as a shade object.');}catch(error){if(error.name!=='ArgumentException'||error.ParamName!=='value')throw error;}
  const record=new io.DatabaseRecord();
  if(!io.ReadOutputSettingsPayload(context,record,'WIPEOUTVARIABLES',[new a.DxfTag(100,'AcDbWipeoutVariables'),new a.DxfTag(70,1)],0)||!record.Object.DisplayFrame)throw new Error('Installed wipeout flags differ.');
  const text='x'.repeat(254)+'\\U+0041'+'🧪',chunks=io.SplitGeoDefinition(text);
  if(chunks.some(s=>s.length>255)||chunks.join('')!==text||chunks[1]!=='\\U+0041🧪')throw new Error('Installed geographic text chunking differs.');
  const sun=new a.DxfSun();document.Objects.SetSun(document.Viewport,sun);const fields=[];
  if(!io.WriteSunPayload({Write:(c,v)=>fields.push([c,v])},18,sun)||fields[0][1]!=='AcDbSun')throw new Error('Installed SUN payload missing.');
  const slot=[];io.WriteSunReference({Write:(c,v)=>slot.push([c,v])},18,document.Viewport);
  if(slot.length!==1||slot[0][0]!==361||slot[0][1]!==sun.Handle)throw new Error('Installed SUN owner slot differs.');
}

// Section record admission and retained FIELD headers use only installed modules.
{
  const api=await import('@netdxf/javascript');
  const io=await import('@netdxf/javascript/runtime/DatabasePayloadIO.js');
  const standalone=await import('@netdxf/javascript/netDxf/IO/DxfReader.SectionSettings.js');
  if(io.ReadSectionSettingsRecord!==standalone.ReadSectionSettingsRecord)throw new Error('Section reader export mismatch.');
  const document=new api.DxfDocument(18),context=new io.DatabaseIOContext(document),tag=(c,v)=>new api.DxfTag(c,v);
  const record=io.ReadSectionSettingsRecord(context,'SECTIONSETTINGS',[tag(5,'C00'),tag(100,'AcDbSectionSettings'),tag(90,1),tag(91,0)]);
  document.NamedObjects.Add('Settings',record.Object);io.ResolveSectionSettingsReferences(context);
  const emitted=[];if(!io.WriteSectionSettingsPayload({Write:(c,v)=>emitted.push([c,v])},18,record.Object)||emitted.length!==3)throw new Error('Section settings payload mismatch.');
  const manager=io.ReadSectionManagerRecord(context,'SECTION_MANAGER',[tag(5,'C01'),tag(100,'AcDbSectionManager'),tag(70,0),tag(90,0)]);
  document.NamedObjects.Add('ACAD_SECTION_MANAGER',manager.Object);io.ResolveSectionManagerReferences(context);io.PrepareSectionManagerClasses(document,document.Classes);
  if(document.Classes.get_Item('SECTION_MANAGER').InstanceCount!==1)throw new Error('Section manager class missing.');
  const header={};if(!io.TryReadStoredFieldHeader([tag(1,'Evaluator'),tag(2,'Text'),tag(90,0),tag(97,0)],0,4,header)||header.code!=='Text'||header.children.length!==0)throw new Error('Stored FIELD header parse mismatch.');
}
