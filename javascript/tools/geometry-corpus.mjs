// Deterministic operation corpus. This is supplemental evidence, not original-suite coverage.
import { doubleBits } from './wire.mjs';
export const D = value => ({double:doubleBits(value)});
export const I = value => ({int:value});
export const R = ref => ({ref});
export const E = (type,value) => ({enum:type,value});
export const A = (type,values) => ({array:type,values});
export const V = (type,...args) => ({new:type,args:args.map(D)});
const create = (type,args,id='a',signature) => ({kind:'new',type,args,id,...(signature?{signature}: {})});
const call = (type,member,args,signature,id) => ({kind:'call',type,member,args,signature,...(id?{id}: {})});
const method = (target,member,args=[],signature=[]) => ({kind:'call',target,member,args,signature});
let seed=0x4e445846;
function next() { seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return (seed|0)/0x1000000; }
export function geometryCorpus() {
  seed=0x4e445846;
  const corpus=[];
  const add=(name,category,steps)=>corpus.push({name,category,request:{steps}});
  for(let n=2;n<=4;n++) {
    const type='Vector'+n, keys=[...'XYZW'.slice(0,n)], sig=Array(n).fill('Double');
    const input=[Array(n).fill(0),keys.map((_,i)=>i?-0:0),Array(n).fill(1e-20),Array(n).fill(Number.MIN_VALUE),Array(n).fill(Number.MAX_VALUE),
      keys.map((_,i)=>i?Infinity:-Infinity),Array(n).fill(NaN),...Array.from({length:128},()=>keys.map(()=>next()))];
    for(let k=0;k<input.length;k++) {
      const a=input[k],b=keys.map(()=>next()),ctor=create(type,a.map(D),'a',sig),other=create(type,b.map(D),'b',sig);
      const steps=[ctor,other,method('a','Modulus'),method('a','ToArray'),method('a','GetHashCode'),method('a','ToString'),
        call(type,'IsNaN',[R('a')],[type]),call(type,'IsZero',[R('a')],[type])];
      for(const m of ['DotProduct','SquareDistance','Distance','Add','Subtract','Multiply','Divide']) steps.push(call(type,m,[R('a'),R('b')],[type,type]));
      for(const m of ['Multiply','Divide'])for(const x of [0,-0,3,0.25])steps.push(call(type,m,[R('a'),D(x)],[type,'Double']));
      steps.push(call(type,'Multiply',[D(3),R('a')],['Double',type]),call(type,'Negate',[R('a')],[type]),
        call(type,'Equals',[R('a'),R('b')],[type,type]),call(type,'Equals',[R('a'),R('a'),D(1e-12)],[type,type,'Double']),
        method('a','Equals',[R('b')],[type]),call(type,'Round',[R('a'),I(3)],[type,'Int32']),
        call(type,'Normalize',[R('a')],[type],'norm'),{kind:'snapshot',target:'a'},method('a','Normalize'),method('a','Modulus'),{kind:'snapshot',target:'a'});
      if(n<4)for(const m of ['MidPoint','AreParallel','ArePerpendicular','CrossProduct'])steps.push(call(type,m,[R('a'),R('b')],[type,type]));
      if(n===3 && k>6)steps.push({kind:'emit',target:'norm'});
      add(`vectors/${n}/algebra/${k}`,'algebra',steps);
      if(n<4 && k>6) {
        const angle=D(next());
        const trig=[ctor,other,call(type,'AngleBetween',[R('a'),R('b')],[type,type])];
        if(n===2)trig.push(call(type,'Rotate',[R('a'),angle],[type,'Double']),call(type,'Polar',[R('a'),D(4),angle],[type,'Double','Double']),call(type,'Angle',[R('a')],[type]),call(type,'Angle',[R('a'),R('b')],[type,type]));
        else trig.push(call(type,'RotateAroundAxis',[R('a'),R('b'),angle],[type,type,'Double']));
        add(`vectors/${n}/elementary/${k}`,'elementary',trig);
      }
    }
    for(const name of ['Zero',...keys.map(k=>'Unit'+k),'NaN']) add(`vectors/${n}/constants/${name}`,'algebra',[
      {kind:'get',type,member:name,id:'a'},method('a','GetHashCode'),method('a','ToString'),call(type,'Normalize',[R('a')],[type],'b'),
      {kind:'set',target:'b',member:'X',value:D(8)},{kind:'snapshot',target:'a'},{kind:'snapshot',target:'b'}]);
    for(const length of [0,1,n-1,n,n+1])add(`vectors/${n}/array/${length}`,'guards',[create(type,[A('Double',Array.from({length},(_,i)=>D(i)))],'a',['Double[]'])]);
    add(`vectors/${n}/guards`,'guards',[create(type,[]),create(type,[null],'bad',['Double[]']),
      {kind:'index',target:'a',args:[I(-1)]},{kind:'index',target:'a',args:[I(n)]},{kind:'set-index',target:'a',args:[I(-1)],value:D(2)},
      call(type,'Round',[R('a'),I(-1)],[type,'Int32']),call(type,'Round',[R('a'),I(16)],[type,'Int32'])]);
  }
  for(let n=2;n<=4;n++) {
    const type='Matrix'+n,Vt='Vector'+n,sig=Array(n*n).fill('Double');
    for(let k=0;k<128;k++) {
      const a=Array.from({length:n*n},()=>next()), b=Array.from({length:n*n},()=>next());
      const steps=[create(type,a.map(D),'a',sig),create(type,b.map(D),'b',sig),create(Vt,Array.from({length:n},()=>D(next())),'v'),
        method('a','Determinant'),method('a','Inverse'),method('a','Transpose'),method('a','GetHashCode'),method('a','ToString')];
      for(const m of ['Add','Subtract','Multiply','Equals'])steps.push(call(type,m,[R('a'),R('b')],[type,type]));
      steps.push(call(type,'Multiply',[R('a'),D(3)],[type,'Double']),call(type,'Multiply',[R('a'),R('v')],[type,Vt]),
        call(type,'Scale',[D(2)],['Double']),call(type,'Scale',Array(n===2?2:3).fill(D(2)),Array(n===2?2:3).fill('Double')),
        call(type,'Equals',[R('a'),R('a'),D(0)],[type,type,'Double']));
      if(n>2)steps.push(call(type,'Reflection',[V('Vector3',1,2,3),...(n===4?[V('Vector3',3,4,5)]:[])],Array(n===4?2:1).fill('Vector3')));
      if(n===4)steps.push(call(type,'Translation',[D(1),D(2),D(3)],['Double','Double','Double']));
      add(`matrices/${n}/algebra/${k}`,'algebra',steps);
      add(`matrices/${n}/rotation/${k}`,'elementary',(n===2?['Rotation']:['RotationX','RotationY','RotationZ']).map(m=>call(type,m,[D(next())],['Double'])));
    }
    add(`matrices/${n}/identity-and-copy`,'algebra',[
      {kind:'get',type,member:'Identity',id:'a'},create(type,[],'z'),method('z','Inverse'),
      {kind:'set',target:'a',member:'M12',value:D(5e-13)},call(type,'Multiply',[R('a'),R('z')],[type,type],'product'),
      {kind:'set',type:'MathHelper',member:'Epsilon',value:D(1e-15)}, {kind:'get',target:'a',member:'IsIdentity'},
      {kind:'set',target:'product',member:'M11',value:D(7)},{kind:'snapshot',target:'z'},
      {kind:'index',target:'a',args:[I(n),I(0)]},{kind:'index',target:'a',args:[I(0),I(n)]},
      {kind:'set-index',target:'a',args:[I(0),I(0)],value:D(2)},{kind:'snapshot',target:'a'}]);
  }
  for(const x of [-Infinity,-720,-360,-1e-13,-0,0,1e-13,0.5,1,360,721,Infinity,NaN]) {
    const steps=[];
    for(const m of ['Sign','IsOne','IsZero','NormalizeAngle'])steps.push(call('MathHelper',m,[D(x)],['Double']));
    for(const t of [-1,0,1e-12,10,Infinity,NaN])for(const m of ['Sign','IsOne','IsZero'])steps.push(call('MathHelper',m,[D(x),D(t)],['Double','Double']));
    for(const t of [0,0.01,0.1,1,3])steps.push(call('MathHelper','RoundToNearest',[D(x),D(t)],['Double','Double']));
    add(`helper/scalars/${doubleBits(x)}`,'algebra',steps);
  }
  for(let k=0;k<128;k++) {
    const points=[V('Vector3',next(),next(),next()),V('Vector3',next(),next(),next()),V('Vector3',next(),next(),next())];
    const steps=[];
    for(const m of ['PointLineDistance','PointInSegment'])steps.push(call('MathHelper',m,points,Array(3).fill('Vector3')));
    steps.push(call('MathHelper','ArbitraryAxis',[points[0]],['Vector3']),
      call('MathHelper','Transform',[points[0],points[1],E('CoordinateSystem',0),E('CoordinateSystem',1)],['Vector3','Vector3','CoordinateSystem','CoordinateSystem']),
      call('MathHelper','Transform',[points[0],points[1],{out:true}],['Vector3','Vector3','Double&']),
      call('MathHelper','Transform',[A('Vector3',points),points[1],{out:true}],['IEnumerable<Vector3>','Vector3','Double&']));
    add(`helper/projection/${k}`,'algebra',steps);
    add(`helper/bulges/${k}`,'elementary',[
      call('MathHelper','ArcFromBulge',[V('Vector2',1,2),V('Vector2',next(),next()),D(next())],['Vector2','Vector2','Double']),
      call('MathHelper','ArcToBulge',[V('Vector2',1,2),D(5),D(next()),D(next())],['Vector2','Double','Double','Double'])]);
    for(const [type,n] of [['BezierCurveQuadratic',3],['BezierCurveCubic',4]]) {
      const cp=Array.from({length:n},()=>V('Vector3',next(),next(),next()));
      const curve=[create(type,cp,'curve',Array(n).fill('Vector3')),
        {kind:'get',target:'curve',member:'StartPoint',id:'point'},{kind:'set',target:'point',member:'X',value:D(99)},{kind:'snapshot',target:'curve'}];
      for(const t of [-0,0.25,0.5,0.75,1,-1,2])for(const m of ['CalculatePoint','CalculateTangent','Split'])curve.push(method('curve',m,[D(t)],['Double']));
      curve.push(method('curve','PolygonalVertexes',[I(8)],['Int32']),method('curve','Reverse'),{kind:'snapshot',target:'curve'},method('curve','PolygonalVertexes',[I(1)],['Int32']));
      if(n===4)curve.push(call(type,'CreateFromFitPoints',[A('Vector3',cp)],['IEnumerable<Vector3>']));
      add(`curves/${type}/${k}`,'algebra',curve);
    }
  }
  for(let a=0;a<25;a++)for(let b=0;b<25;b++)add(`units/drawing/${a}/${b}`,'units',[
    call('UnitHelper','ConversionFactor',[E('DrawingUnits',a),E('DrawingUnits',b)],['DrawingUnits','DrawingUnits']),
    call('UnitHelper','ConvertUnit',[D(1.23456789012345),E('DrawingUnits',a),E('DrawingUnits',b)],['Double','DrawingUnits','DrawingUnits'])]);
  for(let a=-1;a<10;a++)for(let b=0;b<25;b++)for(const reverse of [false,true]) {
    const args=[E('ImageUnits',a),E('DrawingUnits',b)], sig=['ImageUnits','DrawingUnits'];if(reverse){args.reverse();sig.reverse();}
    add(`units/image/${a}/${b}/${reverse}`,'units',[call('UnitHelper','ConversionFactor',args,sig)]);
  }
  for(const x of [0,1,25,90,100,-1,32767,-32768])add(`transparency/${x}`,'algebra',[
    create('Transparency',[{short:x}],'a',['Int16']),call('Transparency','FromCadIndex',[{short:x}],['Int16'],'b'),
    call('Transparency','ToAlphaValue',[R('b')],['Transparency']),method('b','Clone'),method('b','ToString'),
    {kind:'set',target:'b',member:'Value',value:{short:10}},{kind:'snapshot',target:'b'}]);
  for(let byte=0;byte<256;byte++)add(`transparency/alpha/${byte}`,'algebra',[
    call('Transparency','FromAlphaValue',[I(0x2000000|byte)],['Int32'],'a'),call('Transparency','ToAlphaValue',[R('a')],['Transparency']),method('a','Clone')]);
  for(let k=0;k<64;k++) {
    const center=V('Vector2',next(),next()), a=D(next()),b=D(next()),rot=D(next());
    add(`bounds/${k}`,'algebra',[
      create('BoundingRectangle',[center,a,b],'a',['Vector2','Double','Double']),create('BoundingRectangle',[A('Vector2',[])],'empty',['IEnumerable<Vector2>']),
      method('a','PointInside',[center],['Vector2']),call('BoundingRectangle','Union',[R('a'),R('empty')],['BoundingRectangle','BoundingRectangle']),
      {kind:'get',target:'a',member:'Min',id:'min'},{kind:'set',target:'min',member:'X',value:D(9)},{kind:'snapshot',target:'a'},
      create('ClippingBoundary',[center,V('Vector2',2,3)],'clip',['Vector2','Vector2']),method('clip','Clone')]);
    add(`bounds/ellipse/${k}`,'elementary',[create('BoundingRectangle',[center,a,b,rot],'a',['Vector2','Double','Double','Double'])]);
  }
  for(const member of ['DegToRad','RadToDeg','DegToGrad','GradToDeg','HalfPI','PI','ThreeHalfPI','TwoPI'])add('helper/constant/'+member,'algebra',[{kind:'get',type:'MathHelper',member}]);
  for(let n=2;n<=4;n++){
    const type='Vector'+n,ctor=create(type,Array(n).fill(D(3)),'a',Array(n).fill('Double'));
    const steps=[ctor,create(type,Array(n).fill(D(2)),'b',Array(n).fill('Double'))];
    for(const member of ['op_Addition','op_Subtraction','op_Multiply','op_Division','op_Equality','op_Inequality'])steps.push(call(type,member,[R('a'),R('b')],[type,type]));
    steps.push(call(type,'op_UnaryNegation',[R('a')],[type]),method('a','ToString',[{culture:''}],['IFormatProvider']));
    if(n<4)steps.push(create(type,[D(3)],'scalar',['Double']));
    add('vectors/operators/'+n,'algebra',steps);
    const M='Matrix'+n,ms=[{kind:'get',type:M,member:'Identity',id:'m'},{kind:'get',type:M,member:'Zero',id:'zero'}];
    for(const member of ['op_Addition','op_Subtraction','op_Multiply','op_Equality','op_Inequality'])ms.push(call(M,member,[R('m'),R('zero')],[M,M]));
    ms.push(call(M,'Scale',[V('Vector'+(n===2?2:3),...Array(n===2?2:3).fill(2))],['Vector'+(n===2?2:3)]),method('m','ToString',[{culture:''}],['IFormatProvider']));
    add('matrices/operators/'+n,'algebra',ms);
  }
  return corpus;
}
