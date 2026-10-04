// Supplemental requests only. Expected state and errors come from the pinned C# assembly.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p')=>({kind:'new',type,args,id});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id)=>({kind:'call',target,member,args,...(id?{id}:{})});
const snap=target=>({kind:'snapshot',target});
const def=(kind='Pdf',name='Drawing')=>({new:`Objects.Underlay${kind}Definition`,args:[name,`drawing.${kind.toLowerCase()}`]});
const create=(kind='Pdf')=>N('Entities.Underlay',[def(kind),V('Vector3',1,2,3),D(2)]);
const clip=()=>({new:'ClippingBoundary',args:[V('Vector2',-1,-2),V('Vector2',4,5)]});
export function underlayCorpus() {
  const out=[],add=(name,steps)=>out.push({name:`underlay/${name}`,category:'underlay',request:{steps}});
  for(const kind of ['Pdf','Dgn','Dwf']) {
    const type=`Objects.Underlay${kind}Definition`,ext=kind.toLowerCase();
    const files=[null,'',`drawing.${ext}`,`UPPER.${ext.toUpperCase()}`,`.${ext}`,`folder.name/drawing.${ext}`,`folder.name\\drawing.${ext}`,
      `\0bad.${ext}`,`a\0b.${ext}`,`a|b.${ext}`,`日本😀.${ext}`,`drawing.${ext}.`,`drawing.${ext} `,'wrong.txt'];
    for(const [i,file] of files.entries()) {
      add(`${kind}/implicit/${i}`,[N(type,[file])]);
      add(`${kind}/file/${i}`,[N(type,['DEF',`a.${ext}`]),S('p','File',file),snap('p'),C('p','Clone')]);
    }
    for(const name of [null,'',' ','\tNAME\r\n','a/b','a|b','Case','日本😀'])
      add(`${kind}/name/${JSON.stringify(name)}`,[N(type,[name,`a.${ext}`])]);
    add(`${kind}/references-clone`,[N(type,[`folder/file.${ext}`]),C('p','HasReferences'),C('p','GetReferences'),C('p','ToString'),C('p','Clone',[],'copy'),
      C('p','Clone',['RENAMED'],'renamed'),{kind:'reference-equals',args:[R('p'),R('copy')]},snap('copy'),snap('renamed')]);
    if(kind!=='Dwf') for(const value of [null,'','2','Sheet A','日本😀','a\0b'])
      add(`${kind}/metadata/${JSON.stringify(value)}`,[N(type,['D',`a.${ext}`]),S('p',kind==='Pdf'?'Page':'Layout',value),C('p','Clone',[],'q'),snap('q'),snap('p')]);
    for(const scale of [-Infinity,-1,-0,0,Number.MIN_VALUE,1e-13,1,2,Infinity,NaN])
      add(`${kind}/constructor-scale/${Object.is(scale,-0)?'-0':scale}`,[N('Entities.Underlay',[def(kind),V('Vector3',1,2,3),D(scale)])]);
    for(const args of [[def(kind)],[def(kind),V('Vector3',1,2,3)],[null]]) add(`${kind}/overload/${out.length}`,[N('Entities.Underlay',args)]);
    for(const member of ['Contrast','Fade']) for(const value of [-32768,-1,0,19,20,50,80,81,100,101,32767])
      add(`${kind}/${member}/${value}`,[create(kind),S('p',member,{short:value}),snap('p'),C('p','Clone')]);
    for(const [x,y] of [[0,1],[1,0],[-0,1],[1e-13,2],[1e-12,2],[2,-3],[Infinity,1],[NaN,2],[1,NaN]])
      add(`${kind}/scale/${out.length}`,[create(kind),S('p','Scale',V('Vector2',x,y)),snap('p'),C('p','Clone')]);
    for(const value of [-Infinity,-450,-0,0,37,720,Infinity,NaN])
      add(`${kind}/rotation/${Object.is(value,-0)?'-0':value}`,[create(kind),S('p','Rotation',D(value)),snap('p'),C('p','Clone')]);
    for(const flags of [0,1,2,4,8,16,31,32767])
      add(`${kind}/flags/${flags}`,[create(kind),S('p','DisplayOptions',E('Entities.UnderlayDisplayFlags',flags)),C('p','Clone')]);
    add(`${kind}/value-copies`,[create(kind),G('p','Position','position'),S('position','X',D(99)),G('p','Scale','scale'),S('scale','Y',D(99)),snap('p')]);
    add(`${kind}/boundary-clone`,[create(kind),S('p','ClippingBoundary',clip()),C('p','Clone',[],'q'),G('p','ClippingBoundary','a'),G('q','ClippingBoundary','b'),
      {kind:'reference-equals',args:[R('a'),R('b')]},G('p','Definition','da'),G('q','Definition','db'),{kind:'reference-equals',args:[R('da'),R('db')]},S('q','ClippingBoundary',null),snap('p'),snap('q')]);
    for(const proposed of ['Pdf','Dgn','Dwf'])for(const replacement of ['Pdf','Dgn','Dwf',null])for(const failure of [false,true]) {
      const newValue=replacement===null?null:def(replacement,'REPLACED');
      add(`${kind}/event/${proposed}/${replacement}/${failure}`,[create(kind),
        {kind:'observe',target:'p',member:'UnderlayDefinitionChanged',observer:'definition',replace:newValue,throw:failure},
        S('p','Definition',def(proposed,'PROPOSED')),snap('p'),{kind:'events'},C('p','Clone')]);
    }
    const matrices=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[1,0,0,0,-1,0,0,0,1],
      [0,-1,0,1,0,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]];
    for(const [i,m] of matrices.entries())for(const normal of [[0,0,1],[0,3,4]])for(const scale of [[2,3],[-2,3],[2,-3]])
      add(`${kind}/transform/${i}/${normal}/${scale}`,[create(kind),S('p','Normal',V('Vector3',...normal)),S('p','Scale',V('Vector2',...scale)),S('p','Rotation',D(37)),S('p','ClippingBoundary',clip()),
        C('p','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',3,-4,5)]),snap('p'),C('p','Clone')]);
    add(`${kind}/matrix4`,[create(kind),C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap('p')]);
  }
  return out;
}
