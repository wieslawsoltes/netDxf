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
