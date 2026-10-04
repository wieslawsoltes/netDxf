// Supplemental request descriptions only. The .NET assembly provides every expected result.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p')=>({kind:'new',type:'Tables.'+type,args,id});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic}:{})});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic}:{})});
const snapshot=target=>({kind:'snapshot',target});
const created=type=>N(type,type==='ViewUcs'?[]:['Authored']);
const V3=(x=0,y=0,z=0)=>V('Vector3',x,y,z);
const label=value=>Object.is(value,-0)?'-0':String(value);
const equals=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
export function viewCorpus() {
  const probes=[],add=(name,steps)=>probes.push({name:'views/'+name,category:'views',request:{steps}});
  for(const type of ['View','VPort'])for(const name of [null,'',' ','\t','Name',' name ','*Active',' *aCtIvE ','*Custom','A/B','A|B','日本😀'])
    add(`${type}/name/${JSON.stringify(name)}`,[N(type,[name])]);
  for(const type of ['View','VPort','ViewUcs'])add(type+'/defaults',[created(type),C('p','Clone',[],'copy'),snapshot('copy'),equals('p','copy')]);
  add('VPort/active',[{kind:'get',type:'Tables.VPort',member:'Active',id:'p'},C('p','Clone',[],'copy'),equals('p','copy'),S('copy','Name','Other'),snapshot('copy')]);
  for(const type of ['View','VPort'])for(const fail of [false,true])add(type+'/rename/'+fail,[created(type),
    {kind:'observe',target:'p',member:'NameChanged',observer:'rename',throw:fail},S('p','Name','Renamed'),snapshot('p'),{kind:'events'},
    {kind:'unobserve',observer:'rename'},S('p','Name','Final'),snapshot('p')]);
  for(const type of ['View','VPort'])add(type+'/equality',[N(type,['SAME']),N(type,['same'],'other'),
    C('p','Equals',[R('other')],null,['Tables.TableObject']),C('p','Equals',[R('p')],null,['Tables.TableObject']),
    C('p','Equals',[null],null,['Tables.TableObject']),C('p','Clone',[],'copy'),C('p','Equals',[R('copy')],null,['Tables.TableObject']),
    C('p','CompareTo',[R('other')],null,['Tables.TableObject']),C('p','HasReferences'),C('p','GetReferences')]);
  const doubles={View:['Height','Width','LensLength','Fov','Rotation','FrontClippingPlane','BackClippingPlane'],
    VPort:['ViewHeight','ViewAspectRatio','LensLength','FrontClippingPlane','BackClippingPlane','SnapRotation','ViewTwist','UcsElevation'],ViewUcs:['Elevation']};
  for(const [type,members] of Object.entries(doubles))for(const member of members)for(const value of [-Infinity,-450,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1e-13,.5,720,Infinity,NaN])
    add(`${type}/${member}/${label(value)}`,[created(type),S('p',member,D(value)),snapshot('p'),C('p','Clone',[],'q'),snapshot('q')]);
  const vectors={View:{Target:3,ViewDirection:3,Camera:3,ViewCenter:2},
    VPort:{ViewCenter:2,SnapBasePoint:2,SnapSpacing:2,GridSpacing:2,LowerLeftCorner:2,UpperRightCorner:2,ViewDirection:3,ViewTarget:3,UcsOrigin:3,UcsXAxis:3,UcsYAxis:3},
    ViewUcs:{Origin:3,XAxis:3,YAxis:3}};
  for(const [type,members] of Object.entries(vectors))for(const [member,dimension] of Object.entries(members)) {
    for(const value of [0,-0,Number.MIN_VALUE,1e-13,3,NaN,Infinity,-Infinity])for(let component=0;component<dimension;component++) {
      const values=Array(dimension).fill(0);values[component]=value;
      add(`${type}/${member}/${component}/${label(value)}`,[created(type),S('p',member,V('Vector'+dimension,...values)),snapshot('p'),
        G('p',member,'copy'),S('copy','X',D(999)),snapshot('p'),C('p','Clone')]);
    }
  }
  const integerMembers={View:{ViewMode:'Tables.ViewModeFlags',Viewmode:'Tables.ViewModeFlags',Flags:'Tables.ViewFlags',RenderMode:'Tables.ViewRenderMode'},
    VPort:{ViewMode:'Tables.ViewModeFlags',Flags:'Tables.VPortFlags',CircleSides:null,UcsIcon:null,SnapStyle:null,SnapIsopair:null,RenderMode:'Tables.ViewRenderMode',UcsOrthographicType:null},
    ViewUcs:{OrthographicType:null}};
  for(const [type,members] of Object.entries(integerMembers))for(const [member,enumType] of Object.entries(members))for(const value of [-32768,-1,0,1,2,3,6,7,16,64,32767])
    add(`${type}/${member}/${value}`,[created(type),S('p',member,enumType?E(enumType,value):{short:value}),snapshot('p'),C('p','Clone')]);
  for(const [type,members] of Object.entries({View:['IsPaperSpace','IsCameraPlottable'],VPort:['ShowGrid','SnapMode','FastZoom','UcsPerViewport']}))
    for(const member of members)for(const value of [false,true])add(`${type}/${member}/${value}`,[created(type),S('p',member,value),C('p','Clone',[],'copy'),snapshot('copy')]);
  add('View/flag-projection',[created('View'),S('p','Flags',E('Tables.ViewFlags',-1)),S('p','IsPaperSpace',false),snapshot('p'),S('p','IsPaperSpace',true),snapshot('p')]);
  add('View/aliases',[created('View'),S('p','Camera',V3(2,3,4)),S('p','Fov',D(55)),S('p','Viewmode',E('Tables.ViewModeFlags',-2)),snapshot('p'),
    S('p','ViewDirection',V3(3,2,1)),S('p','LensLength',D(35)),S('p','ViewMode',E('Tables.ViewModeFlags',4)),G('p','Camera','camera'),G('p','Fov','fov'),G('p','Viewmode','mode')]);
  for(const type of ['View','VPort']) {
    const steps=[created(type),N('UCS',['NAMED'],'named'),N('UCS',['BASE'],'base')];
    const target=type==='View'?'bundle':'p';if(type==='View')steps.push(N('ViewUcs',[],'bundle'),S('p','Ucs',R('bundle')));
    steps.push(S(target,'NamedUcs',R('named')),S(target,'BaseUcs',R('base')),C('p','Clone',[],'q'),snapshot('p'),snapshot('q'));
    if(type==='View')steps.push(G('q','Ucs','qb'),equals('bundle','qb'),G('qb','NamedUcs','qn'),G('qb','BaseUcs','qbase'));
    else steps.push(G('q','NamedUcs','qn'),G('q','BaseUcs','qbase'));
    steps.push(equals('named','qn'),equals('base','qbase'));add(type+'/ucs-clone',steps);
  }
  add('View/ucs-ownership',[created('View'),N('View',['OTHER'],'other'),N('ViewUcs',[],'bundle'),S('p','Ucs',R('bundle')),S('p','Ucs',R('bundle')),
    S('other','Ucs',R('bundle')),snapshot('p'),snapshot('other'),G('bundle','View','owner',true),equals('p','owner'),S('p','Ucs',null),snapshot('bundle'),
    S('other','Ucs',R('bundle')),snapshot('other'),G('bundle','View','second',true),equals('other','second'),C('other','Clone',[],'copy'),snapshot('copy')]);
  for(const kind of [0,1,6])add('ViewUcs/deferred-validation/'+kind,[created('ViewUcs'),N('UCS',['BASE'],'base'),S('p','BaseUcs',R('base')),
    S('p','OrthographicType',{short:kind}),C('p','Validate',[],null,null,true),snapshot('p'),C('p','Clone')]);
  for(const type of ['View','VPort'])for(const present of [false,true])add(type+'/sun-presence/'+present,[created(type),S('p','SunHandlePresent',present,true),C('p','Clone',[],'q'),snapshot('q')]);
  for(const type of ['View','VPort'])add(type+'/sun-owned-clone-guard',[created(type),{kind:'new',type:'Objects.DxfSun',args:[],id:'sun'},S('p','Sun',R('sun'),true),
    C('p','Clone',[]),C('p','Clone',['invalid/name']),S('p','Sun',null,true),C('p','Clone')]);
  for(const present of [false,true])add('View/live-section-presence/'+present,[created('View'),...(present?[S('p','LiveSection',null)]:[]),C('p','Clone',[],'q'),snapshot('q'),
    C('q','ClearLiveSectionReference'),snapshot('p'),snapshot('q')]);
  for(const type of ['View','VPort'])add(type+'/xdata-clone',[created(type),{kind:'new',type:'XData',args:[{new:'Tables.ApplicationRegistry',args:['APP']}],id:'data'},
    G('data','XDataRecord','records'),C('records','Add',[{new:'XDataRecord',args:[E('XDataCode',1004),A('Byte',[{byte:1},{byte:2}])]}]),
    G('p','XData','dict'),C('dict','Add',[R('data')]),C('p','Clone',[],'q'),G('q','XData','qdict'),
    {kind:'index',target:'qdict',args:['APP'],id:'qdata'},equals('data','qdata'),G('qdata','XDataRecord','qrecords'),C('qrecords','Clear'),snapshot('p'),snapshot('q')]);
  return probes;
}
