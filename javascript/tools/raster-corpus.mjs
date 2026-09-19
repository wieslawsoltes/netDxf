// Inputs only. All expected states, exception classes and bits come from the pinned C# assembly.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',signature)=>({kind:'new',type,args,id,...(signature?{signature}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id)=>({kind:'call',target,member,args,...(id?{id}:{})});
const snap=target=>({kind:'snapshot',target});
const args=(file='drawing.png',width=16,height=8,horizontal=96,vertical=120,units=5)=>[file,I(width),D(horizontal),I(height),D(vertical),E('Units.ImageResolutionUnits',units)];
const definition=(name='D',...params)=>({new:'Objects.ImageDefinition',args:[name,...args(...params)]});
const image=()=>N('Entities.Image',[definition(),V('Vector3',1,2,3),D(4),D(2)]);
const clipping=()=>({new:'ClippingBoundary',args:[D(-1),D(-2),D(4),D(5)]});
const polygon=()=>A('Vector2',[V('Vector2',0,0),V('Vector2',3,0),V('Vector2',4,5),V('Vector2',-1,2)]);
const makeRecord=(code,value)=>({new:'XDataRecord',args:[E('XDataCode',code),value]});
const label=value=>Object.is(value,-0)?'-0':String(value);
export function rasterCorpus() {
  const out=[],add=(name,steps)=>out.push({name:'raster/'+name,category:'raster',request:{steps}});
  for(const [i,file] of [null,'','a.png','folder/a.png','folder.name\\b.any','a.','a','a\0b.png','\0a.png','a|b.png','日本😀.tif'].entries()) {
    add(`definition/implicit/${i}`,[N('Objects.ImageDefinition',args(file))]);
    add(`definition/file/${i}`,[N('Objects.ImageDefinition',['D',...args()]),S('p','File',file),snap('p'),C('p','Clone')]);
  }
  for(const name of [null,'',' ','D',' A/B ','日本😀','A|B'])add('definition/name/'+JSON.stringify(name),[N('Objects.ImageDefinition',[name,...args()])]);
  for(const [member,index] of [['Width',1],['Height',3]])for(const value of [-2147483648,-1,0,1,2147483647]) {
    const values=args();values[index]=I(value);
    add(`definition/${member}/${value}`,[N('Objects.ImageDefinition',['D',...values]),N('Objects.ImageDefinition',['Q',...args()],'q'),S('q',member,I(value)),snap('q'),C('q','Clone')]);
  }
  for(const [member,index] of [['HorizontalResolution',2],['VerticalResolution',4]])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,0.125,96,Infinity,NaN]) {
    const values=args();values[index]=D(value);
    add(`definition/${member}/${label(value)}`,[N('Objects.ImageDefinition',['D',...values]),N('Objects.ImageDefinition',['Q',...args()],'q'),S('q',member,D(value)),snap('q'),C('q','Clone')]);
  }
  for(const initial of [-1,0,2,5,32767])for(const value of [-1,0,2,5,32767])for(const resolution of [96,Number.MIN_VALUE,Infinity,NaN])
    add(`definition/units/${initial}/${value}/${resolution}`,[N('Objects.ImageDefinition',['D',...args('a.png',16,8,resolution,resolution,initial)]),
      S('p','ResolutionUnits',E('Units.ImageResolutionUnits',value)),snap('p'),S('p','ResolutionUnits',E('Units.ImageResolutionUnits',value)),snap('p'),C('p','Clone')]);
  add('definition/metadata-clone',[N('Objects.ImageDefinition',['D',...args()]),C('p','HasReferences'),C('p','GetReferences'),C('p','ToString'),
    C('p','Clone',['NEW'],'q'),S('q','File','new.jpg'),snap('p'),snap('q')]);
  for(const position of [V('Vector2',1,2),V('Vector3',1,2,3)]) {
    add('image/overload/'+out.length,[N('Entities.Image',[definition(),position,V('Vector2',3,4)]),C('p','Clone')]);
    add('image/overload/'+out.length,[N('Entities.Image',[definition(),position,D(3),D(4)]),C('p','Clone')]);
  }
  add('image/null-definition',[N('Entities.Image',[null,V('Vector3',1,2,3),D(3),D(4)]),image(),S('p','Definition',null),snap('p')]);
  for(const member of ['Width','Height'])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1,Infinity,NaN]) {
    const sizes=member==='Width'?[D(value),D(2)]:[D(4),D(value)];
    add(`image/${member}/${label(value)}`,[N('Entities.Image',[definition(),V('Vector3',1,2,3),...sizes]),image(),S('p',member,D(value)),snap('p'),C('p','Clone')]);
  }
  for(const member of ['Uvector','Vvector'])for(const [x,y] of [[0,0],[-0,0],[1e-13,0],[1e-12,0],[Number.MIN_VALUE,0],[3,4],[-2,3],[Infinity,1],[NaN,1],[1,NaN]])
    add(`image/${member}/${out.length}`,[image(),S('p',member,V('Vector2',x,y)),snap('p'),C('p','Clone',[],'q'),snap('q')]);
  for(const angle of [-Infinity,-450,-0,0,30,90,360,Infinity,NaN])add('image/rotation/'+label(angle),[image(),S('p','Rotation',D(angle)),snap('p'),S('p','Rotation',D(angle)),snap('p'),C('p','Clone')]);
  for(const member of ['Brightness','Contrast','Fade'])for(const value of [-32768,-1,0,1,50,100,101,32767])
    add(`image/${member}/${value}`,[image(),S('p',member,{short:value}),snap('p'),C('p','Clone')]);
  for(const flags of [-32768,-1,0,1,2,4,8,15,32767])add('image/flags/'+flags,[image(),S('p','DisplayOptions',E('Entities.ImageDisplayFlags',flags)),S('p','Clipping',true),C('p','Clone')]);
  add('image/boundary-reset',[image(),G('p','ClippingBoundary','before'),S('p','Definition',definition('REPLACEMENT','b.jpg',64,32)),snap('p'),
    G('p','ClippingBoundary','same'),{kind:'reference-equals',args:[R('before'),R('same')]},S('p','ClippingBoundary',null),snap('p'),C('p','Clone',[],'q'),
    G('p','ClippingBoundary','a'),G('q','ClippingBoundary','b'),{kind:'reference-equals',args:[R('a'),R('b')]}]);
  for(const replacement of [null,definition('REPLACEMENT')])for(const failure of [false,true])
    add(`image/event/${replacement===null}/${failure}`,[image(),{kind:'observe',target:'p',member:'ImageDefinitionChanged',observer:'definition',replace:replacement,throw:failure},
      S('p','Definition',definition('PROPOSED')),snap('p'),{kind:'events'},S('p','ClippingBoundary',null),snap('p'),C('p','Clone')]);
  for(const handle of [null,'','0','ABCD','not-a-handle'])add('reactor/'+JSON.stringify(handle),[N('Objects.ImageDefinitionReactor',[handle]),C('p','ToString')]);
  for(const signature of ['ClippingBoundary','IEnumerable<Vector2>'])add('wipeout/null/'+signature,[N('Entities.Wipeout',[null],'p',[signature])]);
  const constructors=[[D(1),D(2),D(3),D(4)],[V('Vector2',3,4),V('Vector2',-1,-2)],[clipping()],[polygon()]];
  for(const [i,a] of constructors.entries())add('wipeout/constructor/'+i,[N('Entities.Wipeout',a),C('p','Clone'),S('p','ClippingBoundary',null),snap('p')]);
  for(const vertices of [[],[V('Vector2',0,0)],[V('Vector2',0,0),V('Vector2',1,1)]])add('wipeout/vertices/'+vertices.length,[N('Entities.Wipeout',[A('Vector2',vertices)])]);
  for(const elevation of [-Infinity,-3,-0,0,Number.MIN_VALUE,2,Infinity,NaN])add('wipeout/elevation/'+label(elevation),[N('Entities.Wipeout',[clipping()]),S('p','Elevation',D(elevation)),C('p','Clone')]);
  const matrices=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],
    [2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[1,0,0,0,0,0,0,0,1],[NaN,0,0,0,1,0,0,0,1]];
  for(const type of ['Image','Wipeout'])for(const [i,m] of matrices.entries())for(const normal of [[0,0,1],[0,3,4],[2,-3,4]])for(const boundary of [clipping(),{new:'ClippingBoundary',args:[polygon()]}])
    add(`${type}/transform/${i}/${normal}/${out.length}`,[type==='Image'?image():N('Entities.Wipeout',[boundary]),S('p','Normal',V('Vector3',...normal)),S('p','ClippingBoundary',boundary),
      ...(type==='Image'?[S('p','Rotation',D(37))]:[S('p','Elevation',D(3))]),C('p','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',3,-4,5)]),snap('p'),C('p','Clone')]);
  for(const type of ['Image','Wipeout'])add(type+'/matrix4',[type==='Image'?image():N('Entities.Wipeout',[clipping()]),
    C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap('p')]);
  return out;
}
