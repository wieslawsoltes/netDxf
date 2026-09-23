// Byte inputs only. The reader under test supplies all errors and positions.
export function binaryDiagnosticInputs() {
  const result=[],signature=[...new TextEncoder().encode('AutoCAD Binary DXF\r\n\x1a\0')];
  for(const legacy of [false,true])for(const origin of [0,9])for(const lead of [false,true]){
    const group=code=>legacy?(code<255?[code]:[255,code&255,code>>>8]):[code&255,code>>>8];
    const add=(name,code,payload)=>result.push({name:`codec/${legacy}/${origin}/${lead}/${name}`,step:{method:'dependency-codec',legacy,origin,reads:lead?2:1,
      bytes:[...Array(origin).fill(77),...signature,...(lead?[...group(1),65,66,0]:[]),...group(code),...payload]}});
    for(const code of [5,105,330,331,360,390,480,1005])for(const value of ['','G','0x12','12345678901234567',' \u2003 '])add('handle/'+code+'/'+JSON.stringify(value),code,[...new TextEncoder().encode(value),0]);
    for(const code of [10,40,110,140,210,460,1010])for(const bits of ['7FF0000000000000','FFF0000000000000','7FF8000000000042']){
      const bytes=new Uint8Array(8);new DataView(bytes.buffer).setBigUint64(0,BigInt('0x'+bits),true);add('double/'+code+'/'+bits,code,[...bytes]);
    }
    for(const code of [290,299])for(const value of [2,255])add('bool/'+code+'/'+value,code,[value]);
  }
  return result;
}
