// Supplemental inputs. No expected outputs or production algorithms are encoded here.
import { viewCorpus } from './view-corpus.mjs';
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',signature)=>({kind:'new',type,args,id,...(signature?{signature}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic}:{})});
const snapshot=target=>({kind:'snapshot',target});
const ucs=()=>N('Tables.UCS',['U']);
const vec=(x=0,y=0,z=0)=>V('Vector3',x,y,z);
const kind=value=>E('Tables.UcsOrthographicType',value);
export function coordinateCorpus() {
  const out=[],add=(name,steps)=>out.push({name:'coordinates/'+name,category:'ucs',request:{steps}});
  for(const name of [null,'',' ','U',' A ','a/b','日本😀']) {
    add('name/'+JSON.stringify(name),[N('Tables.UCS',[name])]);
    add('axes-name/'+JSON.stringify(name),[N('Tables.UCS',[name,vec(1,2,3),vec(1,0,0),vec(0,1,0)])]);
  }
  for(const x of [[0,0,0],[1,0,0],[3,0,0],[1,2,3],[Infinity,0,0],[NaN,1,0],[Number.MIN_VALUE,0,0]])
    for(const y of [[0,0,0],[0,1,0],[0,4,0],[1,0,0],[2,-1,0],[0,Infinity,0],[0,NaN,0]])
      add('axes/'+out.length,[ucs(),C('p','SetAxis',[vec(...x),vec(...y)]),snapshot('p'),C('p','Clone')]);
  for(const normal of [[0,0,1],[0,0,-1],[0,0,0],[0,3,4],[2,-3,4],[Infinity,0,0],[NaN,0,1],[Number.MIN_VALUE,0,0]]) {
    for(const rotation of [undefined,-0,0,.25,Math.PI/2,-12.5,Infinity,NaN]) {
      const args=['N',vec(1,2,3),vec(...normal),...(rotation===undefined?[]:[D(rotation)])];
      add('normal/'+out.length,[{kind:'call',type:'Tables.UCS',member:'FromNormal',args,id:'p'},C('p','GetTransformation'),C('p','Clone')]);
    }
    add('point-on-plane/'+out.length,[{kind:'call',type:'Tables.UCS',member:'FromXAxisAndPointOnXYplane',args:['Plane',vec(7,8,9),vec(...normal),vec(3,-2,1)],id:'p'},C('p','Clone')]);
  }
  for(const elevation of [-Infinity,-12.5,-0,0,Number.MIN_VALUE,1,Infinity,NaN])add('elevation/'+out.length,[ucs(),S('p','Elevation',D(elevation)),snapshot('p'),C('p','Clone')]);
  for(const from of [-1,0,1,2])for(const to of [-1,0,1,2])for(const normal of [[0,0,1],[0,3,4],[2,-3,4]])
    add('transform/'+out.length,[{kind:'call',type:'Tables.UCS',member:'FromNormal',args:['N',vec(1,2,3),vec(...normal),D(.4)],id:'p'},
      C('p','Transform',[vec(3,4,5),E('CoordinateSystem',from),E('CoordinateSystem',to)],'point',['Vector3','CoordinateSystem','CoordinateSystem']),
      C('p','Transform',[A('Vector3',[vec(3,4,5),vec(-1,0,7)]),E('CoordinateSystem',from),E('CoordinateSystem',to)],'points',['IEnumerable<Vector3>','CoordinateSystem','CoordinateSystem'])]);
  add('null-points',[ucs(),C('p','Transform',[null,E('CoordinateSystem',0),E('CoordinateSystem',1)],null,['IEnumerable<Vector3>','CoordinateSystem','CoordinateSystem'])]);
  for(const k of [-1,0,1,2,3,4,5,6,7,32767])add('origin/'+k,[ucs(),
    C('p','TryGetOrthographicOrigin',[kind(k),{out:'Vector3'}],null,['Tables.UcsOrthographicType','Vector3&']),
    C('p','SetOrthographicOrigin',[kind(k),vec(1,2,3)]),C('p','TryGetOrthographicOrigin',[kind(k),{out:'Vector3'}],null,['Tables.UcsOrthographicType','Vector3&']),
    C('p','Clone',[],'q'),C('q','RemoveOrthographicOrigin',[kind(k)]),snapshot('p'),snapshot('q')]);
  for(const n of [NaN,Infinity,-Infinity,-0,Number.MIN_VALUE])for(let axis=0;axis<3;axis++){
    const p=[1,2,3];p[axis]=n;
    add('origin-finite/'+out.length,[ucs(),C('p','SetOrthographicOrigin',[kind(1),vec(4,5,6)]),C('p','SetOrthographicOrigin',[kind(1),vec(...p)]),snapshot('p')]);
  }
  add('dictionary-slots',[ucs(),G('p','OrthographicOrigins','map'),...Array.from({length:6},(_,i)=>C('p','SetOrthographicOrigin',[kind(i+1),vec(i,0,0)])),
    snapshot('map'),C('p','RemoveOrthographicOrigin',[kind(3)]),C('p','RemoveOrthographicOrigin',[kind(1)]),
    C('p','SetOrthographicOrigin',[kind(3),vec(10,0,0)]),C('p','SetOrthographicOrigin',[kind(1),vec(20,0,0)]),snapshot('map'),C('p','Clone')]);
  for(const type of [-1,0,1,3,6,7])for(const mode of ['none','other','self'])add('base/'+type+'/'+mode,[ucs(),N('Tables.UCS',['BASE'],'b'),
    C('p','SetOrthographicBase',[{short:type},mode==='none'?null:R(mode==='self'?'p':'b')]),snapshot('p'),C('p','Clone',[],'q'),snapshot('q')]);
  for(const type of [0,1,6])for(const present of [false,true])add('loaded-base/'+type+'/'+present,[ucs(),
    C('p','SetLoadedOrthographicBase',[{short:type},null,present],null,null,true),snapshot('p'),C('p','Clone')]);
  add('copy-metadata',[ucs(),N('Tables.UCS',['BASE'],'base'),C('p','SetOrthographicBase',[{short:1},R('base')]),
    S('p','Flags',E('Tables.UcsFlags',-1)),G('p','Origin','v'),S('v','X',D(999)),C('p','Clone',[],'q'),G('p','BaseUcs','a'),G('q','BaseUcs','b'),
    {kind:'reference-equals',args:[R('a'),R('b')]},snapshot('p'),snapshot('q')]);
  for(const failure of [false,true])add('rename/'+failure,[ucs(),{kind:'observe',target:'p',member:'NameChanged',observer:'rename',throw:failure},S('p','Name','NEW'),{kind:'events'},snapshot('p')]);
  return out.concat(viewCorpus());
}
