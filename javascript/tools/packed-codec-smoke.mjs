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
