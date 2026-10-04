// Supplemental exact value-model probes. Original conformance identities are counted separately.
import { XDataCode } from '../index.js';
import { D, I, R, A, V } from './geometry-corpus.mjs';
const S=value=>({short:value}),B=value=>({byte:value});
const create=(type,args,id='a',signature)=>({kind:'new',type,args,id,...(signature?{signature}:{})});
const call=(type,member,args,signature,id)=>({kind:'call',type,member,args,signature,...(id?{id}:{})});
const method=(target,member,args=[],signature=[],id)=>({kind:'call',target,member,args,signature,...(id?{id}:{})});
export function valueCorpus(){
  const corpus=[],add=(name,category,steps)=>corpus.push({name,category,request:{steps}});
  for(let index=-1;index<=257;index++)add(`color/index/${index}`,'colors',[
    call('AciColor','FromCadIndex',[S(index)],['Int16'],'a'),create('AciColor',[S(index)],'b',['Int16']),
    ...(index>=0&&index<=256?[method('a','ToString'),method('a','Clone',[],[],'clone'),method('a','ToColor'),
      call('AciColor','ToTrueColor',[R('a')],['AciColor']),call('AciColor','ToHsl',[R('a')],['AciColor']),
      call('AciColor','ToHsl',[R('a'),{out:true}],['AciColor','Vector3&']),
      call('AciColor','ToHsl',[R('a'),{out:true},{out:true},{out:true}],['AciColor','Double&','Double&','Double&']),
      {kind:'set',target:'clone',member:'Index',value:S(3)},{kind:'snapshot',target:'a'},method('a','Equals',[R('clone')],['AciColor'])]:[]),
  ]);
  for(let i=0;i<512;i++){
    const rgb=[(i*37)%256,(i*79)%256,(i*149)%256];
    add(`color/rgb/${i}`,'colors',[
      create('AciColor',rgb.map(B),'a',['Byte','Byte','Byte']),create('AciColor',[A('Byte',rgb.map(B))],'b',['Byte[]']),
      method('a','Equals',[R('b')],['AciColor']),method('a','ToString'),method('a','ToColor',[],[],'rgba'),
      create('AciColor',[R('rgba')],'copy',['Color']),call('AciColor','RgbToAci',rgb.map(B),['Byte','Byte','Byte']),
      call('AciColor','ToHsl',[R('a')],['AciColor'],'hsl'),call('AciColor','FromHsl',[R('hsl')],['Vector3']),
      call('AciColor','ToTrueColor',[R('a')],['AciColor'],'rgb'),call('AciColor','FromTrueColor',[R('rgb')],['Int32']),
      {kind:'set',target:'a',member:'UseTrueColor',value:false},{kind:'snapshot',target:'a'},method('a','ToString')]);
  }
  for(let r=0;r<=8;r++)for(let g=0;g<=8;g++)for(let b=0;b<=8;b++){
    const args=[r/8,g/8,b/8].map(D);
    add(`color/normalized/${r}/${g}/${b}`,'colors',[
      create('AciColor',args,'a',['Double','Double','Double']),create('AciColor',[A('Double',args)],'b',['Double[]']),
      call('AciColor','FromHsl',args,['Double','Double','Double'])]);
  }
  for(const property of ['ByLayer','ByBlock','Red','Yellow','Green','Cyan','Blue','Magenta','Default','DarkGray','LightGray'])
    add(`color/defaults/${property}`,'colors',[
      {kind:'get',type:'AciColor',member:property,id:'a'},method('a','ToColor'),method('a','Clone',[],[],'b'),
      {kind:'set',target:'b',member:'Index',value:S(2)},{kind:'get',type:'AciColor',member:property},{kind:'snapshot',target:'a'}]);
  for(const value of [-Infinity,-1,0,Number.MIN_VALUE,0.5/255,1.5/255,2.5/255,1,2,Infinity,NaN]){
    const args=[D(value),D(0.25),D(0.75)];
    add(`color/input/${D(value).double}`,'colors',[
      create('AciColor',args,'a',['Double','Double','Double']),call('AciColor','FromHsl',args,['Double','Double','Double']),
      call('AciColor','FromHsl',[D(0.5),D(value),D(0.75)],['Double','Double','Double']),
      call('AciColor','FromHsl',[D(0.5),D(0.25),D(value)],['Double','Double','Double'])]);
  }
  for(const type of ['Byte','Double'])for(let size=0;size<=4;size++)add(`color/array/${type}/${size}`,'guards',[
    create('AciColor',[A(type,Array.from({length:size},(_,i)=>type==='Byte'?B(i):D(i/4)))],'a',[type+'[]'])]);
  add('color/null-and-color-input','guards',[
    create('AciColor',[null],'a',['Byte[]']),create('AciColor',[null],'a',['Double[]']),
    call('AciColor','ToTrueColor',[null],['AciColor']),call('AciColor','ToHsl',[null],['AciColor']),
    create('AciColor',[],'a',[]),method('a','Equals',[null],['AciColor']),
    call('Color','FromArgb',[I(12),I(23),I(34),I(45)],['Int32','Int32','Int32','Int32'],'rgba'),
    method('a','FromColor',[R('rgba')],['Color']),{kind:'snapshot',target:'a'}]);
  for(const text of [null,'',' ','\u0085','\ufeff','\u200b','a\0b','a\rb','a\nb','東京','normal']){
    add(`class/validation/${JSON.stringify(text)}`,'metadata',[
      create('DxfClass',[text,'CPP','APP'],'a',['String','String','String']),
      create('DxfClass',['DXF',text,'APP'],'a',['String','String','String']),
      create('DxfClass',['DXF','CPP',text],'a',['String','String','String'])]);
  }
  add('class/clone-and-edits','metadata',[
    create('DxfClass',['DXF','CPP',''],'a',['String','String','String']),
    {kind:'set',target:'a',member:'ProxyFlags',value:I(-2147483648)},
    {kind:'set',target:'a',member:'InstanceCount',value:I(2147483647)},
    {kind:'set',target:'a',member:'WasProxy',value:true},{kind:'set',target:'a',member:'IsEntity',value:true},
    method('a','Clone',[],[],'b'),{kind:'set',target:'b',member:'InstanceCount',value:null},
    {kind:'set',target:'b',member:'InstanceCount',value:I(-1)},
    {kind:'set',target:'b',member:'ApplicationName',value:'changed'},{kind:'snapshot',target:'a'},{kind:'snapshot',target:'b'}]);
  const cls=(name,cpp)=>({new:'DxfClass',args:[name,cpp,'APP'],signature:['String','String','String']});
  add('class/ordered-collection','metadata',[
    create('DxfClassCollection',[],'classes',[]),create('DxfClass',['FIRST','CPP','APP'],'first',['String','String','String']),
    method('classes','Add',[R('first')],['DxfClass']),method('classes','Add',[cls('SECOND','CppSecond')],['DxfClass']),
    {kind:'snapshot',target:'classes'},{kind:'index',target:'classes',args:['FIRST']},
    method('classes','Add',[cls('THIRD','CPP')],['DxfClass']),method('classes','Add',[cls('FIRST','CppThird')],['DxfClass']),
    {kind:'set-index',target:'classes',args:[I(0)],value:cls('REPLACED','CPP')},
    method('classes','Contains',['FIRST'],['String']),method('classes','Contains',['REPLACED'],['String']),
    method('classes','TryGetValue',['SECOND',{out:true}],['String','DxfClass&']),
    method('classes','TryGetValue',['NONE',{out:true}],['String','DxfClass&']),
    method('classes','Remove',['REPLACED'],['String']),method('classes','Add',[R('first')],['DxfClass']),
    method('classes','IndexOf',[R('first')],['DxfClass']),{kind:'snapshot',target:'classes'},
    method('classes','Clear'),method('classes','Add',[cls('SECOND','CppSecond')],['DxfClass']),{kind:'snapshot',target:'classes'}]);
  for(const code of Object.values(XDataCode)){
    const payload=code===1004?A('Byte',[B(0),B(255)]):code===1002?'{':code===1005?'FFFF':code<1010?'Zażółć':code===1070?S(-1):code===1071?I(-123):D(.123456789012345);
    add(`xdata/valid/${code}`,'xdata',[create('XDataRecord',[{enum:'XDataCode',value:code},payload],'a',['XDataCode','Object']),
      ...(code!==1001?[method('a','ToString'),{kind:'get',target:'a',member:'Value'}]:[]),
      create('XDataRecord',[{enum:'XDataCode',value:code},null],'null',['XDataCode','Object'])]);
  }
  for(const value of ['', '0','FFFFFFFFFFFFFFFF','0000000000000000000','00000000000000000001','10000000000000000','+1','0x1',' G ',' 1 ','\tFFFF\r','FFFF\0','FFFF \0','0\u0085','1\0\0','1\0 '])
    add('xdata/handles/'+JSON.stringify(value),'xdata',[create('XDataRecord',[{enum:'XDataCode',value:1005},value],'a',['XDataCode','Object'])]);
  for(const size of [0,1,127,128,255,256])for(const code of [1000,1003,1004])add(`xdata/length/${code}/${size}`,'xdata',[
    create('XDataRecord',[{enum:'XDataCode',value:code},code===1004?A('Byte',Array(size).fill(B(42))):'x'.repeat(size)],'a',['XDataCode','Object'])]);
  for(const value of [D(NaN),D(Infinity),D(-Infinity),D(-0)])add('xdata/nonfinite/'+value.double,'xdata',[
    create('XDataRecord',[{enum:'XDataCode',value:1040},value],'a',['XDataCode','Object']),method('a','ToString')]);
  for(const code of [-1,0,1006,1043,1072])add(`xdata/unknown/${code}`,'xdata',[
    create('XDataRecord',[{enum:'XDataCode',value:code},'payload'],'a',['XDataCode','Object']),method('a','ToString')]);
  add('class/nullable-overloads','metadata',[
    create('DxfClassCollection',[],'classes',[]),
    ...['Contains','Remove'].flatMap(m=>['String','DxfClass'].map(sig=>method('classes',m,[null],[sig]))),
    {kind:'index',target:'classes',args:['missing']},method('classes','Insert',[I(-1),null],['Int32','DxfClass'])]);
  add('color/empty-value','colors',[{kind:'get',type:'Color',member:'Empty',id:'empty'},create('Color',[],'zero',[]),create('AciColor',[R('empty')],'aci',['Color']),method('aci','ToColor')]);
  return corpus;
}
