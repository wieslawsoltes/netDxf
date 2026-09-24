// Input-only geometry packets; no native observations, hashes or expected values.
import { entityBodyRow } from './entity-body-io-corpus.mjs';
export const primitivePackets={
  Arc:[[100,'AcDbCircle'],[10,1],[20,2],[30,3],[40,4],[39,5],[210,1],[220,2],[230,3],[100,'AcDbArc'],[50,30],[51,220]],
  Circle:[[100,'AcDbCircle'],[10,1],[20,2],[30,3],[40,4],[39,5],[210,0],[220,0],[230,-1]],
  Ellipse:[[100,'AcDbEllipse'],[10,1],[20,2],[30,3],[11,4],[21,3],[31,0],[210,0],[220,0],[230,1],[40,.5],[41,.25],[42,5]],
  Point:[[100,'AcDbPoint'],[10,-0],[20,2],[30,3],[39,4],[50,45],[210,1],[220,2],[230,3]],
  Line:[[100,'AcDbLine'],[10,-0],[20,2],[30,3],[11,4],[21,5],[31,6],[39,7],[210,0],[220,1],[230,0]],
  Ray:[[100,'AcDbRay'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6]],
  XLine:[[100,'AcDbXline'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6]],
  Face3D:[[100,'AcDbFace'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],[12,7],[22,8],[32,9],[13,10],[23,11],[33,12],[70,15]],
  Solid:[[100,'AcDbTrace'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],[12,7],[22,8],[32,9],[13,10],[23,11],[33,12],[39,-0],[210,1],[220,2],[230,3]],
  Trace:[[100,'AcDbTrace'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],[12,7],[22,8],[32,9],[13,10],[23,11],[33,12],[39,-0],[210,1],[220,2],[230,3]],
};
export function splinePacket(degree=3,periodic=false,weights=true) {
  const n=degree+2,compact=Array.from({length:n},(_,i)=>[i,i%2,-i]);
  const controls=periodic?[...compact.slice(-degree),...compact]:compact;
  const knots=periodic?Array.from({length:n+2*degree+1},(_,i)=>i-degree):[...Array(degree+1).fill(0),.5,...Array(degree+1).fill(1)];
  return [[100,'AcDbSpline'],[70,periodic?2:0],[71,degree],[72,-32768],[73,32767],[74,-1],[42,1e-7],[43,1e-7],[44,1e-10],...knots.map(k=>[40,k]),
    ...controls.flatMap(p=>[[10,p[0]],[20,p[1]],[30,p[2]],...(weights?[[41,1]]:[])])];
}
export const helixTail=[[100,'AcDbHelix'],[90,29],[91,63],[10,-0],[20,1],[30,2],[11,3],[21,4],[31,5],[12,0],[22,0],[32,2],[40,3],[41,4],[42,-5],[290,true],[280,2]];
const xd=[[1001,'PRIMITIVE_APP'],[1000,'Za\\U+017C\\U+00F3\\U+0142\\U+0107'],[1004,{pattern:3}],[1010,-0],[1020,2],[1030,3],[1070,4],[1071,5]];
const action=method=>({method});
export function primitiveIOCorpus() {
  const result=[];
  function add(name,mode,kind,tags,steps,version=18,extra={}) {
    result.push({name:`primitive-io/${mode}/${kind}/${name}`,request:{op:'entity-body-io',mode,kind,version,tags:tags.map(([c,v])=>entityBodyRow(c,v)),steps,...extra}});
  }
  function parse(name,mode,kind,tags,version=18,steps=null) {
    add(name,mode,kind,[...tags,[0,'ENDSEC'],[0,'EOF']],steps??[action('next'),action('read'),action('write'),action('next')],version);
  }
  for(const mode of ['text','binary','legacy']) {
    for(const [kind,tags]of Object.entries(primitivePackets)) {
      for(let version=13;version<=18;version++) {
        parse(`profile/${version}`,mode,kind,tags,version);
        parse(`xdata/${version}`,mode,kind,[...tags,...xd],version);
      }
      parse('empty',mode,kind,[tags[0]]);
      parse('repeated',mode,kind,[...tags,...tags.slice(1)]);
      parse('unknown',mode,kind,[...tags,[100,'PrivateSubclass'],[300,'preserved advancement'],[102,'{opaque'],[102,'}']]);
      for(let i=1;i<tags.length;i++) {
        parse(`prefix/${i}`,mode,kind,tags.slice(0,i));
        parse(`without/${i}`,mode,kind,tags.filter((_,j)=>j!==i));
      }
      for(const code of [10,20,30,11,21,31,39,40,41,42,50,51,210,220,230])for(const value of [-0,-1,0,1e-200,1e200]) {
        parse(`scalar/${code}/${Object.is(value,-0)?'-0':value}`,mode,kind,[...tags,[code,value]]);
      }
      if(['Arc','Circle','Ellipse','Line','Point','Solid','Trace'].includes(kind))for(const normal of [[0,0,0],[0,0,-1],[0,1,0],[1,0,0],[-1,-2,-3],[1/64,0,1],[1/64-Number.EPSILON,0,1]])
        parse(`normal/${normal}`,mode,kind,[...tags,[210,normal[0]],[220,normal[1]],[230,normal[2]]]);
      add('truncated',mode,kind,tags,[action('next'),action('read')]);
      add('null-write',mode,kind,[[0,'EOF']],[action('write')]);
    }
    for(const kind of ['Spline','Helix']) {
      const tail=kind==='Helix'?helixTail:[];
      for(let degree=1;degree<=10;degree++)for(const periodic of [false,true])for(const weights of [false,true]) {
        const tags=[...splinePacket(degree,periodic,weights),...tail];
        parse(`degree/${degree}/${periodic}/${weights}`,mode,kind,tags);
        if(degree<4)parse(`metadata/${degree}/${periodic}/${weights}`,mode,kind,[...tags,...xd]);
      }
      const packet=splinePacket(),base=[...packet,...tail];
      for(let version=13;version<=18;version++)parse(`profile/${version}`,mode,kind,base,version);
      for(const code of [71,70,72,73,74,42,43,44])for(const value of [0,-1,1,2,4,10,11,32,128,1024,2050,32767]) {
        const tags=packet.map(p=>p[0]===code?[code,value]:p);parse(`field/${code}/${value}`,mode,kind,[...tags,...tail]);
      }
      for(const fields of [[[12,1]],[[32,3]],[[12,1],[22,2],[32,3]],[[32,4],[12,5],[32,6]],[[13,1],[23,2],[33,3]],[[31,3]],[[11,1],[21,2],[31,3],[11,4],[21,5],[31,6]]])
        parse(`vectors/${JSON.stringify(fields)}`,mode,kind,[...packet,...fields,...tail]);
      for(const flags of [0,1,2,4,32,64,128,256,1024,1056,1088,1152,1280,2048,2050]) {
        const fields=packet.map(p=>p[0]===70?[70,flags]:p);
        parse(`fit/${flags}`,mode,kind,[...fields,[11,1],[21,2],[31,3],[11,4],[21,5],[31,6],...tail]);
      }
      for(const field of [10,20,30,40,41])parse(`missing/${field}`,mode,kind,[...packet.filter(p=>p[0]!==field),...tail]);
      for(const degree of [-32768,-1,0])parse(`periodic-degree/${degree}`,mode,kind,[...splinePacket(3,true).map(([c,v])=>[c,c===71?degree:v]),...tail]);
      parse('partial-weights',mode,kind,[...packet.filter(p=>p[0]!==41),[41,2],...tail]);
      parse('weight-order',mode,kind,[...packet.filter(p=>p[0]!==41),...packet.filter(p=>p[0]===41),...tail]);
      parse('bad-overlap',mode,kind,[...splinePacket(3,true),[10,9],[20,9],[30,9],[41,1],...tail]);
      parse('defaults',mode,kind,[[100,'AcDbSpline'],...tail]);
      add('truncated',mode,kind,base,[action('next'),action('read')]);add('null-write',mode,kind,[[0,'EOF']],[action('write')]);
    }
    const spline=splinePacket();
    for(const [code,value]of helixTail.slice(1))parse(`duplicate/${code}`,mode,'Helix',[...spline,...helixTail,[code,value]]);
    for(const code of [10,20,30,11,21,31,12,22,32])parse(`partial/${code}`,mode,'Helix',[...spline,[100,'AcDbHelix'],[code,1]]);
    for(const [code,value]of [[90,-1],[91,-1],[40,-1],[40,-0],[41,0],[41,-1],[42,-0],[42,-1],[280,-1],[280,3],[290,false],[100,'AcDbHelix'],[1000,'orphan'],[300,'ignored']])
      parse(`invalid/${code}/${value}`,mode,'Helix',[...spline,[100,'AcDbHelix'],[code,value]]);
    parse('zero-axis',mode,'Helix',[...spline,[100,'AcDbHelix'],[12,0],[22,0],[32,0]]);
    parse('missing-subclass',mode,'Helix',spline);
    parse('wrong-first-marker',mode,'Helix',[[100,'Wrong'],...spline.slice(1),...helixTail]);
    for(const where of ['model','paper','unused'])for(const version of [13,14,15,16,17,18]) {
      const steps=[action('next'),action('read'),{method:'place',where},action('validate'),action('prepare'),action('prepare'),action('write')];
      add(`placement/${where}/${version}`,mode,'Helix',[...spline,...helixTail,[0,'ENDSEC']],steps,version,{captureClasses:true});
    }
    for(const cpp of ['AcDbHelix','Other'])for(const isEntity of [false,true])for(const placed of [false,true]) {
      add(`class/${cpp}/${isEntity}/${placed}`,mode,'Helix',[...spline,...helixTail,[0,'ENDSEC']],
        [action('next'),action('read'),...(placed?[{method:'place',where:'unused'}]:[]),{method:'class',name:'HELIX',cpp,isEntity,count:9},action('prepare')],18,{captureClasses:true});
    }
    let state=0xb3c19e27;const random=()=>((state=(Math.imul(state,1664525)+1013904223)>>>0)/4294967296);
    for(let n=0;n<80;n++)for(const kind of ['Arc','Circle','Ellipse','Line','Point','Face3D']) {
      const fields=primitivePackets[kind].map(([c,v])=>[c,typeof v!=='number'||c===70?v:c===40?(kind==='Ellipse'?.1+random()*.8:1+random()*100):((random()-.5)*1000)]);
      parse(`random/${n}`,mode,kind,fields);
    }
  }
  const vector=(x,y,z)=>({new:'Vector3',args:[x,y,z]});
  const withTangents=[...splinePacket(),[12,1],[22,2],[32,3],[13,4],[23,5],[33,6]];
  for(const [kind,tags,at,property,value] of [
    ['Line',primitivePackets.Line,4,'StartPoint',vector(7,8,9)],
    ['Circle',primitivePackets.Circle,4,'Center',vector(7,8,9)],
    ['Spline',withTangents,14,'StartTangent',null],['Spline',withTangents,20,'EndTangent',null],
    ['Spline',withTangents,14,'StartTangent',vector(9,8,7)],
  ])add(`callback/${at}/${property}/${value===null?'null':'vector'}`,'text',kind,[...tags,[0,'ENDSEC']],
    [action('next'),action('read'),{method:'write',hooks:[{at,kind:'set',property,value}]}]);
  for(const kind of [...Object.keys(primitivePackets),'Spline','Helix'])for(const at of [1,2,3,4,9,14,32]) {
    const tags=kind==='Spline'?withTangents:kind==='Helix'?[...withTangents,...helixTail]:primitivePackets[kind];
    add(`callback/throw/${at}`,'text',kind,[...tags,[0,'ENDSEC']],
      [action('next'),action('read'),{method:'write',hooks:[{at,kind:'throw'}]}]);
  }
  return result;
}
