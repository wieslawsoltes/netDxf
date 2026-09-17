// Supplemental independent-oracle inputs. Never used as production logic or expected outputs.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
import { bytesToBase64 } from './wire.mjs';
const N=(type,args=[],id='p',signature)=>({kind:'new',type,args,id,...(signature?{signature}:{})});
const G=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const snap=target=>({kind:'snapshot',target});
const Q=(target,args,id)=>({kind:'index',target,args,...(id?{id}:{})});
const txt=(name='T',font='simplex.shx')=>({new:'Tables.TextStyle',args:[name,font]});
const shp=(name='S',file='ltypeshp.shx')=>({new:'Tables.ShapeStyle',args:[name,file]});
const font=(family,flags)=>({new:'Tables.TextStyleFontData',args:[family,I(flags)]});
const en=(type,value)=>E('Tables.'+type,value);
const short=value=>({short:value});
const color=property=>({static:'AciColor',property});
const record=(code,value)=>({new:'XDataRecord',args:[E('XDataCode',code),value]});
const observe=(target,member,observer,extra={})=>({kind:'observe',target,member,observer,...extra});
const events=()=>({kind:'events'});
function shx(entries) {
  // Authored input bytes: a header/index followed by supplied opaque payloads.
  const text=new TextEncoder().encode('AutoCAD-86 shapes 1.0\r\n\u001a\0'), bytes=new Uint8Array(30+entries.length*4+entries.reduce((n,e)=>n+e.payload.length,0));
  bytes.set(text);const view=new DataView(bytes.buffer);view.setInt16(28,entries.length,true);
  let at=30+entries.length*4;
  entries.forEach((entry,i)=>{view.setInt16(30+i*4,entry.number,true);view.setInt16(32+i*4,entry.size??entry.payload.length,true);bytes.set(entry.payload,at);at+=entry.payload.length;});return bytes;
}
export function styleCorpus(linFixtures=[],shapeFixture=null,newLine='\n') {
  const corpus=[],add=(name,category,steps)=>corpus.push({name,category,request:{steps}});
  for(const type of ['TextStyle','ShapeStyle','Linetype','Layer'])for(const name of [null,'',' ',' \t ','P','MiXeD','0','Standard',' standard ','ByLayer','ByBlock','Continuous','a/b','a|b','Zażółć 😀']) {
    const args=type==='TextStyle'?[name,'simplex.shx']:type==='ShapeStyle'?[name,'shape.shx']:[name];
    const steps=[N('Tables.'+type,args)];
    if(name && name.trim() && !/[\/|]/.test(name))steps.push(C('p','HasReferences'),C('p','GetReferences'),C('p','Clone',[],'q'),S('q','Name','RENAMED'),snap('p'),snap('q'));
    add(`construct/${type}/${JSON.stringify(name)}`,'style-model',steps);
  }
  for(const type of ['TextStyle','ShapeStyle','Layer'])add(`preset/${type}`,'style-model',[
    {kind:'get',type:'Tables.'+type,member:'Default',id:'p',...(type==='ShapeStyle'?{nonPublic:true}:{})},C('p','Clone',[],'q'),C('p','Clone',['CUSTOM'],'other'),snap('q'),snap('other')]);
  for(const preset of ['ByLayer','ByBlock','Continuous','Center','DashDot','Dashed','Dot'])add(`preset/Linetype/${preset}`,'style-model',[
    {kind:'get',type:'Tables.Linetype',member:preset,id:'p'},C('p','Clone',[],'q'),{kind:'lin-save',target:'q',newLine},snap('p')]);
  for(const family of [null,'','Family','Times New Roman','日本😀','x'.repeat(255),'x'.repeat(256)])for(const flags of [0,1,2,3,255])
    add(`font/family/${family?.length??'null'}/${flags}`,'font-model',[
      N('Tables.TextStyle',["T",family,en('FontStyle',flags)]),N('Tables.TextStyleFontData',[family,I((flags<<24)|0x123456)],'data')]);
  for(const file of [null,'','font.ttf','font.TTF','font.shx','font.SHX','font.txt','font.ſhx','font.ttＦ','.ttf','folder/font.shx','folder.name/font','font.','x\0.ttf','fonts\\asian.shx']) {
    add(`font/file/${JSON.stringify(file)}`,'font-model',[
      N('Tables.TextStyle',['T',file]),N('Tables.TextStyle',['A','asian.shx'],'a'),S('a','BigFont','big.shx'),S('a','ExtendedFontData',font('Family',-21691178)),
      S('a','FontFile',file),snap('a'),C('a','Clone')]);
    add(`shape/file/${JSON.stringify(file)}`,'shape-model',[N('Tables.ShapeStyle',['S',file]),N('Tables.ShapeStyle',['A','old.shx'],'a'),S('a','File',file),snap('a')]);
    for(const original of ['font.ttf','asian.shx'])add(`font/big/${original}/${JSON.stringify(file)}`,'font-model',[
      N('Tables.TextStyle',['T',original]),S('p','BigFont',file),snap('p'),C('p','Clone')]);
  }
  for(const member of ['Height','WidthFactor','ObliqueAngle','LastHeight'])for(const value of [-Infinity,-100,-85,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,0.009,0.01,1,85,100,100.1,NaN,Infinity])
    add(`font/${member}/${Object.is(value,-0)?'-0':value}`,'font-guards',[N('Tables.TextStyle',['T','simplex.shx']),S('p',member,D(value)),snap('p'),C('p','Clone')]);
  for(const flags of [-32768,-1,0,1,4,16,64,0x4074,0x4175,32767])for(const type of ['TextStyle','ShapeStyle'])
    add(`flags/${type}/${flags}`,'font-model',[N('Tables.'+type,['S','s.shx']),S('p','Flags',en('TextStyleFlags',flags)),S('p','TextGenerationFlags',short(flags)),S('p','LastHeight',D(-0)),snap('p'),C('p','Clone',[],'q'),S('q','LastHeight',null),snap('p'),snap('q')]);
  for(const flags of [-2147483648,-21691178,0,1,0x123456,0x03000000,2147483647])for(const value of [0,1,2,3,255])add(`font/bits/${flags}/${value}`,'font-prefix',[
    N('Tables.TextStyle',['T','font.ttf']),S('p','ExtendedFontData',font('Family',flags)),S('p','FontStyle',en('FontStyle',value)),snap('p'),C('p','Clone',[],'q'),S('q','FontFamilyName','New family'),snap('q'),snap('p')]);
  for(const tail of [false,true])add(`font/prefix/${tail}`,'font-prefix',[
    N('Tables.TextStyle',['T','font.ttf']),S('p','ExtendedFontData',font('Family',-21691178)),G('p','XData','dict'),Q('dict',['ACAD'],'xd'),G('xd','XDataRecord','records'),
    {kind:'set-index',target:'records',args:[I(0)],value:record(1000,'Direct edit')},snap('p'),
    C('records','Add',[record(tail?1000:1002,tail?'Tail':'{')]),C('records','Add',[record(1071,I(99))]),
    S('p','ExtendedFontData',null),snap('p'),S('p','FontFile','new.ttf'),snap('p'),C('p','Clone',[],'q'),snap('q')]);
  for(const mode of [0,1,2])add(`layer/state/${mode}`,'layer-model',[
    N('Tables.Layer',['L']),S('p','Description','Description'),S('p','IsVisible',false),S('p','IsFrozen',true),S('p','IsLocked',true),S('p','Plot',false),
    ...(mode?[S('p','Transparency',{new:'Transparency',args:[short(mode===1?0:35)]})]:[]),C('p','Clone',[],'q'),snap('q'),snap('p')]);
  for(const member of ['Color','Linetype','Transparency'])add(`layer/null/${member}`,'layer-guards',[N('Tables.Layer',['L']),S('p',member,null),snap('p')]);
  for(const name of ['ByLayer','ByBlock','Red','Blue'])add(`layer/color/${name}`,'layer-guards',[N('Tables.Layer',['L']),S('p','Color',color(name)),snap('p')]);
  for(const value of [-3,-2,-1,0,35,211])add(`layer/lineweight/${value}`,'layer-guards',[N('Tables.Layer',['L']),S('p','Lineweight',E('Lineweight',value)),snap('p')]);
  for(const type of ['TextStyle','Linetype','Layer','ShapeStyle'])for(const failure of [false,true]) {
    const args=['TextStyle','ShapeStyle'].includes(type)?['ORIGINAL','s.shx']:['ORIGINAL'];
    add(`rename/${type}/${failure}`,'style-events',[N('Tables.'+type,args),observe('p','NameChanged','rename',{throw:failure}),S('p','Name','RENAMED'),snap('p'),events(),{kind:'unobserve',observer:'rename'},S('p','Name','FINAL'),snap('p')]);
  }
  for(const kind of ['Text','Shape']) {
    const type='Tables.Linetype'+kind+'Segment', styletype=kind==='Text'?'TextStyle':'ShapeStyle', event=kind+'StyleChanged';
    for(const length of [-1,-0,0,1,NaN,Infinity])add(`segment/${kind}/${Object.is(length,-0)?'-0':length}`,'segment-model',[
      N(type,kind==='Text'?['TEXT',txt(),D(1)]:['ZIG',shp()]),S('p','Length',D(length)),S('p','Offset',V('Vector2',2,3)),S('p','Rotation',D(-450)),S('p','Scale',D(-2)),
      C('p','Clone',[],'q'),G('q','Style','style'),S('style','Name','CHANGED'),G('q','Offset','offset'),S('offset','X',D(99)),snap('p'),snap('q'),
      S('p',kind==='Text'?'Text':'Name',null),S('p','Style',null),snap('p')]);
    for(const failure of [false,true])add(`segment/events/${kind}/${failure}`,'style-events',[
      N(type,kind==='Text'?['TEXT',txt(),D(1)]:['ZIG',shp()]),N('Tables.'+styletype,['Replacement','r.shx'],'replacement'),N('Tables.'+styletype,['Proposed','p.shx'],'proposed'),
      observe('p',event,'segment',{replace:R('replacement'),throw:failure}),S('p','Style',R('proposed')),snap('p'),events()]);
    for(const remove of ['Remove','Clear'])add(`segment/forwarding/${kind}/${remove}`,'style-events',[
      N('Tables.Linetype',['L']),N(type,kind==='Text'?['TEXT',txt(),D(1)]:['ZIG',shp()],'s'),G('p','Segments','segments'),
      observe('p','LinetypeSegmentAdded','add'),observe('p','LinetypeSegmentRemoved','remove'),observe('p','Linetype'+kind+'SegmentStyleChanged','changed'),
      C('segments','Add',[R('s')]),C('segments','Add',[R('s')]),S('s','Style',kind==='Text'?txt('A'):shp('A')),
      C('segments',remove,remove==='Remove'?[R('s')]:[]),S('s','Style',kind==='Text'?txt('B'):shp('B')),snap('p'),events()]);
  }
  for(const event of ['LinetypeSegmentAdded','LinetypeSegmentRemoved'])add(`segment/failure/${event}`,'style-events',[
    N('Tables.Linetype',['L']),N('Tables.LinetypeTextSegment',['TEXT',txt(),D(1)],'s'),G('p','Segments','segments'),
    observe('p','LinetypeTextSegmentStyleChanged','style'),observe('p',event,'throw',{throw:true}),
    C('segments','Add',[R('s')]),C('segments','Remove',[R('s')]),S('s','Style',txt('NEW')),snap('p'),events()]);
  add('segment/null-cancellation','segment-model',[N('Tables.Linetype',['L']),G('p','Segments','segments'),C('segments','Add',[null]),snap('p')]);
  const linTexts=['','*L,d','*L,d\n','*L,d\n\n','*L,d\nA','*L,d\nB,1,-1','*bad',' *L, indented\nA,1',
    '*L, desc, commas\r\nA,.25,-.125,0\r\n','*L,d\nA,1\n*L,second\nA,2','*L,d\nA,',
    '*L,d\nA,1,[','*L,d\nA,1,[ZIG]','*L,d\nA,1,[ZIG,f.shx]','*L,d\nA,1,["quoted, text",STANDARD,S=.25,R=30,X=-1,Y=2],-.5'];
  for(const token of ['', '0x10','1e','.','1 2','oops','1_000','-0','.5','1.','1e309','-1e309','NaN','+NaN','-NaN','Infinity','+Infinity','-Infinity'])linTexts.push(`*L,d\nA,${token}`);
  for(const rotation of ['A=90','R=1.5F','U=100G','R=-30D','X=-0','Y=NaN','S=0','S=-1','S=Infinity','Z=whatever','R=','X','', 'R=30x'])
    linTexts.push(`*L,d\nA,.5,[ZIG,shapes.shx,${rotation}],-.25`);
  for(const [i,text] of linTexts.entries())add(`lin/syntax/${i}`,'lin-text',[{kind:'lin-names',text},{kind:'lin-load',text,patternName:'l',id:'p'}]);
  for(const fixture of linFixtures) {
    add(`lin/${fixture.name}/names`,'lin-fixtures',[{kind:'lin-names',text:fixture.text}]);
    const names=fixture.text.split(/\r\n|\r|\n/).filter(line=>line.startsWith('*')).map(line=>line.slice(1,line.indexOf(',')));
    names.forEach((name,i)=>add(`lin/${fixture.name}/${i}/${name}`,'lin-fixtures',[{kind:'lin-load',text:fixture.text,patternName:name,id:'p'},C('p','Clone',[],'q'),{kind:'lin-save',target:'q',newLine}]));
  }
  const samples=[new Uint8Array(),new Uint8Array(20),new Uint8Array(24),shx([]),shx([{number:1,payload:[65,0]}]),
    shx([{number:1,payload:[65,0,0]},{number:2,payload:[66,0,0]}]),
    shx([{number:1,payload:[65,0]},{number:1,payload:[66,0]}]),
    shx([{number:1,payload:[65,0]},{number:2,payload:[65,0]}]),
    shx([{number:-1,payload:[255,0]}]),shx([{number:1,size:0,payload:[65,0]}]),shx([{number:1,size:30,payload:[65,0]}]),
    shx([{number:1,payload:[65,66]}])];
  if(shapeFixture) {
    samples.push(shapeFixture);
    for(const length of [1,20,21,23,24,25,26,27,28,29,30,31,32,45,53,54,55,56,60,80,100,shapeFixture.length-1])samples.push(shapeFixture.slice(0,length));
    for(const at of [0,20,28,29,32,33]){const mutated=shapeFixture.slice();mutated[at]=255;samples.push(mutated);}
  }
  samples.forEach((bytes,i)=>{
    const base={bytes:bytesToBase64(bytes)};
    add(`shape/input/${i}`,'shape-input',[
      {kind:'shape-names',...base},
      ...['A','B','a','TRACK1','zig','absent',null,''].flatMap(name=>['ShapeNumber','ContainsShapeName','StaticContainsShapeName'].map(member=>({kind:'shape-query',...base,member,name}))),
      ...[-1,0,1,2,130,131,32767].map(number=>({kind:'shape-query',...base,member:'ShapeName',number}))]);
  });
  return corpus;
}
