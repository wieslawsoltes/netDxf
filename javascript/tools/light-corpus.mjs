// Supplemental requests only. The pinned C# assembly provides every expected result.
import { D, I, R, V, A, E } from './geometry-corpus.mjs';
const N=(type='Entities.Light',args=[],id='p')=>({kind:'new',type,args,id});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const label=value=>Object.is(value,-0)?'-0':String(value);
const matrix=values=>({new:'Matrix3',args:values.map(D)});
const pair=(light,name)=>({new:'Objects.DxfLightListEntry',args:[R(light),name]});
const authored=()=>[N(),S('p','Position',V('Vector3',1,-2,3)),S('p','Target',V('Vector3',4,5,-6)),S('p','AttenuationStartLimit',D(3)),S('p','AttenuationEndLimit',D(2))];
export function lightCorpus() {
 const out=[],add=(name,steps)=>out.push({name:'light/'+name,category:'light',request:{steps}});
 add('defaults',[N(),C('p','ToString'),C('p','Clone',[],'copy'),snap('copy')]);
 for(const value of [null,'','name','A\tB','A\0B','A\nB','A\rB','日本😀','A\\U+0041','x'.repeat(300),{utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[0xd800,65]}])
  add('names/'+out.length,[N(),S('p','Name','before'),S('p','Name',value),snap('p'),C('p','Clone')]);
 for(const member of ['Intensity','AttenuationStartLimit','AttenuationEndLimit','HotspotAngle','FalloffAngle'])for(const value of [-Infinity,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,0.5,90,360,720,Number.MAX_VALUE,Infinity,NaN])
  add(member+'/'+label(value),[...authored(),S('p',member,D(value)),snap('p'),C('p','Clone')]);
 for(const member of ['VersionNumber','ShadowMapSize','ShadowMapSoftness'])for(const value of [-32768,-1,0,1,32767,...(member!=='ShadowMapSoftness'?[2147483647]:[])])
  add(member+'/'+value,[N(),S('p',member,member==='ShadowMapSoftness'?{short:value}:I(value)),snap('p'),C('p','Clone')]);
 for(const [member,type] of [['LightType','LightType'],['AttenuationType','LightAttenuationType'],['ShadowType','LightShadowType']])for(const value of [-1,0,1,2,3,4,2147483647])
  add(member+'/'+value,[N(),S('p',member,E('Entities.'+type,value)),snap('p'),C('p','Clone')]);
 for(const member of ['Position','Target'])for(const xyz of [[-0,0,-0],[1,2,3],[Number.MIN_VALUE,0,0],[Number.MAX_VALUE,0,0],[NaN,1,2],[1,Infinity,2],[1,2,-Infinity]])
  add(member+'/'+out.length,[N(),S('p',member,V('Vector3',...xyz)),G('p',member,'v'),S('v','X',D(99)),snap('p'),C('p','Clone')]);
 for(let mask=0;mask<16;mask++) {
  const steps=[N()];['IsOn','PlotGlyph','UseAttenuationLimits','CastShadows'].forEach((key,i)=>steps.push(S('p',key,Boolean(mask&(1<<i)))));
  steps.push(C('p','Clone'));add('flags/'+mask,steps);
 }
 const transforms=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[0,-2,0,2,0,0,0,0,2],[2,0,0,0,2,0,0,0,3],
  [1,0.5,0,0,1,0,0,0,1],[0,0,0,0,0,0,0,0,0],[1,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1],[Infinity,0,0,0,Infinity,0,0,0,Infinity]];
 for(const scale of [Number.MIN_VALUE,1e-308,1e-150,0.25,3,-2,1e150,1e308])transforms.push([scale,0,0,0,scale,0,0,0,scale]);
 for(const delta of [5e-11,1e-10,2e-10]){transforms.push([1,0,0,0,1+delta,0,0,0,1]);transforms.push([1,delta,0,0,1,0,0,0,1]);}
 for(const [i,m] of transforms.entries())for(const translation of [[0,0,0],[3,-4,5],[NaN,0,0],[Infinity,0,0]])
  add(`transform/${i}/${out.length}`,[...authored(),C('p','Clone',[],'before'),C('p','TransformBy',[matrix(m),V('Vector3',...translation)]),snap('p'),snap('before'),C('p','Clone')]);
 for(const member of ['Position','Target','AttenuationStartLimit','AttenuationEndLimit'])
  add('overflow/'+member,[...authored(),S('p',member,member==='Position'||member==='Target'?V('Vector3',Number.MAX_VALUE,0,0):D(Number.MAX_VALUE)),C('p','TransformBy',[matrix([2,0,0,0,2,0,0,0,2]),V('Vector3',3,4,5)]),snap('p')]);
 add('matrix4-affine-top-rows',[...authored(),C('p','TransformBy',[{new:'Matrix4',args:[0,-2,0,3,2,0,0,4,0,0,-2,5,9,8,7,6].map(D)}]),snap('p')]);
 // Scaled quaternion rotation matrices with varied ordinary, tiny and large scales.
 let seed=0x4c494748;const next=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return (seed%2001-1000)/1000;};
 for(let i=0;i<64;i++){
  let [x,y,z,w]=[next(),next(),next(),next()];const n=Math.sqrt(x*x+y*y+z*z+w*w);x/=n;y/=n;z/=n;w/=n;
  const scale=[1,2,-3,1e-100,1e100][i%5],m=[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)].map(v=>v*scale);
  add('seeded/'+i,[...authored(),C('p','TransformBy',[matrix(m),V('Vector3',next(),next(),next())]),snap('p'),C('p','Clone')]);
 }
 return out;
}
export function lightListCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'light-list/'+name,category:'light-list',request:{steps}});
 for(const value of [-2147483648,-1,0,1,42,2147483647])add('version/'+value,[N('Objects.DxfLightList',[I(value)]),S('p','StoredVersion',I(-value-1)),snap('p'),C('p','CloneShell',[],'copy',true),snap('copy')]);
 for(const value of [null,'',' ','name','stored alias','日本😀','A\tB','A\0B','A\rB','A\nB',{utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[0xd800,65]},{utf16:[0xd800,0xdc00]}])
  add('name/'+out.length,[N('Entities.Light',[],'light'),N('Objects.DxfLightListEntry',[R('light'),value]),N('Objects.DxfLightListEntry',[null,value],'invalid')]);
 for(const member of ['Insert','set_Item'])for(const index of [-1,0,1,2])for(const invalid of [false,true])
  add(`${member}/${index}/${invalid}`,[N('Entities.Light',[],'light'),N('Objects.DxfLightList',[I(0)]),G('p','Entries','entries'),C('entries','Add',[pair('light','first')]),C('entries',member,[I(index),invalid?null:pair('light','second')]),snap('p')]);
 add('independent-names-order',[N('Entities.Light',[],'a'),N('Entities.Light',[],'b'),N('Objects.DxfLightList',[I(42)]),G('p','Entries','entries'),
  C('entries','Add',[pair('a','alias')]),C('entries','Add',[pair('b','')]),C('entries','Add',[pair('a','duplicate')]),S('a','Name','new actual name'),snap('p'),C('p','CloneShell',[],'copy',true),snap('copy')]);
 for(const mapping of ['valid','null','wrong-type','late-failure']) {
  const steps=[N('Entities.Light',[],'a'),N('Entities.Light',[],'b'),N('Entities.Light',[],'replacement'),N('Entities.Point',[],'wrong'),N('Objects.DxfLightList',[I(7)]),G('p','Entries','entries'),C('entries','Add',[pair('a','A')]),C('entries','Add',[pair('b','B')]),C('p','CloneShell',[],'copy',true)];
  const first=mapping==='null'?null:R(mapping==='wrong-type'?'wrong':'replacement'),second=R(mapping==='late-failure'?'wrong':'replacement');
  steps.push(C('p','CopyDatabaseReferencesTo',[R('copy'),{resolver:[[R('a'),first],[R('b'),second]]}],null,true),snap('p'),snap('copy'));add('mapping/'+mapping,steps);
 }
 return out;
}
