// Deterministic inputs only. The corpus does not embed parser results or exceptions.
const call=method=>({method});
const casts=['ReadByte','ReadBytes','ReadShort','ReadInt','ReadLong','ReadBool','ReadDouble','ReadString','ReadHex','ToString'];
const readSteps=(count=3)=>Array.from({length:count},()=>[call('Next'),...casts.map(call)]).flat();
const text=rows=>rows.flatMap(row=>row.map(String)).join('\n')+'\n';
const utf16=value=>({utf16:Array.from({length:value.length},(_,i)=>value.charCodeAt(i))});
const sentinel=Array.from(new TextEncoder().encode('AutoCAD Binary DXF\r\n\x1a\0'));
const encode=string=>Array.from(new TextEncoder().encode(string));
const group=(code,legacy)=>legacy?(code<255?[code]:[255,code&255,(code>>8)&255]):[code&255,(code>>8)&255];
const scalar=(type,value)=>{const bytes=new Uint8Array(type==='int16'?2:type==='int32'?4:8),view=new DataView(bytes.buffer);if(type==='double')view.setFloat64(0,value,true);else if(type==='int16')view.setInt16(0,value,true);else if(type==='int32')view.setInt32(0,value,true);else view.setBigInt64(0,BigInt(value),true);return Array.from(bytes);};
const tag=(code,bytes,legacy=false)=>[...group(code,legacy),...bytes];
const stringTag=(code,value,legacy=false)=>tag(code,[...encode(value),0],legacy);
const boolTag=(code,value,legacy=false)=>tag(code,[value],legacy);
const TYPES=[['string',[0,1,5,9,100,101,102,300,309,410,419,430,439,470,479,999,1000,1001,1002,1003,1006,1009]],
  ['double',[10,39,40,59,110,119,120,129,130,139,140,149,210,239,460,469,1010,1059]],
  ['int16',[60,79,170,179,270,279,280,289,370,379,380,389,400,409,1060,1070]],
  ['int32',[90,99,420,429,440,459,1071]],['int64',[160,169]],['bool',[290,299]],['bytes',[310,319,1004]],
  ['handle',[105,320,329,330,369,390,399,480,481,1005]]];
export function codecReadersCorpus(){
  const cases=[];const add=(name,request)=>cases.push({name:'codec-readers/'+name,request:{op:'codec-readers',...request}});
  const txt=(name,rows,extra={})=>add('text/'+name,{mode:'text',text:utf16(text(rows)),steps:readSteps(rows.length),...extra});
  const values={string:['','value','true','a\0b','Ω😀','\ud800'],double:['0','-0','1.25','1e-320','-5e-324','1.7976931348623157E+308'],int16:['-32768','32767','-0'],int32:['-2147483648','2147483647','+0'],int64:['-9223372036854775808','9223372036854775807','000'],bool:['0','1','-0'],bytes:['','00ff10','aBcD'],handle:['000a','FFFFFFFFFFFFFFFF','\u0085 0012\u3000']};
  for(const [type,codes] of TYPES)for(const code of codes){
    const list=type==='string'&&code===5?values.handle:values[type];
    txt('groups/'+code,list.map(value=>[code,value]));
  }
  for(const [code,list] of [[70,['','32768','-32769','1.0','1e0','-0','+0','1\0','1\0 ','\u00a01','\t1\v']],
    [90,['2147483648','-2147483649','  +0007  ','0x7','--1','12\0']],
    [160,['9223372036854775808','-9223372036854775809','-0','1\0','1.1','\t-10\v']],
    [290,['-1','2','255','256','-0','+1','true','1\0']],
    [40,['',' ','NaN','Infinity','-Infinity','1e309','-1e309','1e-400','-1e-400','1\0','1.\0','1,000','0x10','1f','\u00851','\t1.25\v']],
    [5,['',' ','G','0x10','-10','+10','0123456789ABCDEF0','A\0','\uFEFF1','\u20031\u2003']],
    [310,['0','0G','GG','00 0',' 0','00ff0','00ffZZ','00\0\0']]])for(let i=0;i<list.length;i++)txt('invalid/'+code+'/'+i,[[1,'previous'],[code,list[i]],[70,'42']]);
  for(const code of [-32768,-1,80,89,103,104,106,109,150,159,180,209,240,269,482,998,1010+50-1+2,1072,32767])txt('unknown/'+code,[[1,'previous'],[code,'1'],[0,'EOF']]);
  for(const code of ['','bad','1.0','32768','-32769','70\0','70\0\0','70 \0','70\0 ','\u00a070','\t70\v','\0','-0'])txt('code/'+JSON.stringify(code),[[1,'previous'],[code,'1'],[0,'EOF']]);
  for(const value of ['', '70','70\n','70\n1','70\n1\n','70\r1\r','70\r\n1\r\n','\n','1\n\n','999\na\n999\nb\n0\nEOF\n'])add('text/termination/'+JSON.stringify(value),{mode:'text',text:utf16(value),steps:readSteps(4)});
  for(const skip of [false,true])txt('comments/'+skip,[[999,'first'],[999,'second'],[70,'bad'],[999,'third'],[0,'EOF']],{steps:[{method:'SkipComments',value:skip},...readSteps(5)]});
  txt('code5-mode',[[5,'BLOCK NAME'],[5,'abc'],[5,'block']],{steps:[{method:'Code5IsString',value:true},...readSteps(1),{method:'Code5IsString',value:false},...readSteps(2)]});
  for(const failAt of [1,2,3,4,5,6])txt('read-failure/'+failAt,[[1,'a'],[70,'3'],[0,'EOF']],{failAt,steps:readSteps(4)});
  txt('null-reader',[],{nullReader:true,steps:[call('Next')]});
  for(const culture of ['', 'en-US','pl-PL','tr-TR'])txt('culture/'+culture,[[40,'123.5'],[290,'1'],[310,'ABCD'],[1,'']],{culture});
  for(const legacy of [false,true])for(const seekable of [false,true])for(const fragment of [1,3,2147483647]){
    const lead=stringTag(1,'previous',legacy),end=stringTag(0,'EOF',legacy);
    const sample=[...lead,...tag(70,scalar('int16',-32768),legacy),...tag(90,scalar('int32',2147483647),legacy),...tag(160,scalar('int64','9223372036854775807'),legacy),...tag(40,scalar('double',-0),legacy),...boolTag(290,1,legacy),...tag(310,[3,0,127,255],legacy),...stringTag(5,' 00ab ',legacy),...end];
    add(`binary/types/${legacy}/${seekable}/${fragment}`,{mode:'binary',bytes:[...sentinel,...sample],legacy,seekable,fragment,steps:readSteps(10)});
    for(const [code,payload] of [[70,scalar('int16',12)],[90,scalar('int32',12)],[160,scalar('int64','12')],[40,scalar('double',12)],[1,[65,66,0]],[310,[3,1,2,3]],[290,[1]]])for(let length=0;length<payload.length;length++)add(`binary/truncate/${legacy}/${seekable}/${fragment}/${code}/${length}`,{mode:'binary',bytes:[...sentinel,...lead,...tag(code,payload.slice(0,length),legacy)],legacy,seekable,fragment,steps:readSteps(3)});
    for(const [name,code,payload] of [['bad-bool',290,[2]],['bad-handle',340,[71,0]],['infinity',40,scalar('double',Infinity)],['nan',40,scalar('double',NaN)],['unknown',80,[0,0]],['comment',999,[0]]])add(`binary/invalid/${legacy}/${seekable}/${fragment}/${name}`,{mode:'binary',bytes:[...sentinel,...lead,...tag(code,payload,legacy),...end],legacy,seekable,fragment,steps:readSteps(3)});
  }
  for(let length=0;length<sentinel.length;length++)add('binary/sentinel/'+length,{mode:'binary',bytes:sentinel.slice(0,length),steps:[call('Next')]});
  for(const extra of [{nullReader:true},{nullEncoding:true},{nullReader:true,nullEncoding:true},{readable:false}])add('binary/constructor/'+JSON.stringify(extra),{mode:'binary',bytes:sentinel,steps:[call('Next')],...extra});
  for(const failAt of [1,2,3,4,5,6,7,8,9,10])add('binary/callback/'+failAt,{mode:'binary',bytes:[...sentinel,...stringTag(1,'x'),...tag(70,scalar('int16',5)),...tag(310,[3,1,2,3])],failAt,fragment:3,steps:readSteps(6)});
  for(const encoding of ['strict','ascii','latin1','replacement'])for(const value of ['plain','Ω😀','\uFEFFvalue'])add('binary/encoding/'+encoding+'/'+value,{mode:'binary',encoding,bytes:[...sentinel,...stringTag(1,value)],steps:readSteps(2)});
  for(const culture of ['', 'en-US','pl-PL','tr-TR'])add('binary/culture/'+culture,{mode:'binary',culture,bytes:[...sentinel,...tag(40,scalar('double',123.5)),...boolTag(290,1),...tag(310,[2,0,255])],steps:readSteps(3)});
  for(const binary of [false,true])for(const offset of [0,7])for(const fragment of [1,3,2147483647])for(const version of ['AC1009','AC1015','AC1021','AC1032','ac1032','AC9999']){
    const rows=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,version],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[0,'ENDSEC'],[0,'EOF']];
    const payload=binary?[...sentinel,...rows.flatMap(([code,value])=>stringTag(code,value))]:encode(text(rows));
    add(`probe/valid/${binary}/${offset}/${fragment}/${version}`,{mode:'probe',bytes:[...Array(offset).fill(0xcc),...payload],origin:offset,fragment,steps:[call('Public'),{method:'Internal',variable:'$DWGCODEPAGE'},{method:'Internal',variable:'$ACADVER'},call('Public')]});
  }
  const probeBytes=encode(text([[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032']]));
  for(const extra of [{seekable:false},{readable:false},{nullReader:true},{failPositionSet:1},...Array.from({length:8},(_,i)=>({failAt:i+1,fragment:4}))])add('probe/failure/'+JSON.stringify(extra),{mode:'probe',bytes:probeBytes,steps:[call('Public'),{method:'Internal',variable:'$ACADVER'},call('Public')],...extra});
  for(const variable of [null,'','$ABSENT','$ACADVER'])add('probe/variable/'+variable,{mode:'probe',bytes:probeBytes,steps:[{method:'Internal',variable},call('Public')]});
  // Decimal strings near native rounding boundaries and every finite binary64 bit
  // pattern represented by a deterministic sample. Expected bits come only from C#.
  let state=0x6c0dec01;const rand=()=>state=(Math.imul(state,1664525)+1013904223)>>>0;
  for(let i=0;i<128;i++){const bits=(BigInt(rand())<<32n)|BigInt(rand()),bytes=new ArrayBuffer(8),view=new DataView(bytes);view.setBigUint64(0,bits);const value=view.getFloat64(0);if(!Number.isFinite(value))continue;txt('random-decimal/'+i,[[40,String(value)],[40,value.toExponential(17)]]);}
  return cases;
}
