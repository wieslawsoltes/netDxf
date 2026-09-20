// Input descriptions only. The unchanged .NET assembly supplies every expected result.
import { D,I,R,V,E,A } from './geometry-corpus.mjs';
const N=(type='PlotSettings',args=[],id='p',hidden=false)=>({kind:'new',type:'Objects.'+type,args,id,...(hidden?{nonPublic:true}:{})});
const S=(member,value,target='p')=>({kind:'set',target,member,value});
const G=(member,id,target='p',hidden=false)=>({kind:'get',target,member,id,...(hidden?{nonPublic:true}:{})});
const C=(member,args=[],id,target='p',hidden=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(hidden?{nonPublic:true}:{})});
const snap=(target='p')=>({kind:'snapshot',target});
const equal=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const errors=()=>({kind:'new',type:'List<String>',args:[],id:'errors'});
const validate=(target='p')=>[errors(),{kind:'call',type:'Objects.PlotSettings',member:'ValidateValues',args:[R(target),R('errors')],nonPublic:true},snap('errors')];
const extremes=[-Infinity,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1e-100,.5,1,1e308,Infinity,NaN];
const label=value=>value===null?'null':D(value).double;
export function outputSettingsCorpus(){
  const out=[],add=(name,steps)=>out.push({name:'output-settings/'+name,category:'output-settings',request:{steps}});
  add('defaults',[N(),C('Clone',[],'q'),...validate(),snap(),snap('q')]);
  add('margin-default',[N('PaperMargin'),C('ToString'),N('PaperMargin',[D(1),D(2),D(3),D(4)],'q'),C('Equals',[R('q')]),C('Equals',[R('p')])]);
  for(const member of ['Left','Bottom','Right','Top'])for(const value of extremes)add(`margin/${member}/${label(value)}`,[N('PaperMargin'),S(member,D(value)),snap(),{kind:'new',type:'Objects.PlotSettings',args:[],id:'settings'},S('PaperMargin',R('p'),'settings'),S(member,D(99)),snap('settings'),G('PaperMargin','m','settings'),S(member,D(77),'m'),snap('settings')]);
  for(const member of ['PrintScaleNumerator','PrintScaleDenominator','StandardScaleFactor'])for(const value of extremes)add(`${member}/${label(value)}`,[N(),S(member,D(value)),snap(),C('Clone'),...validate()]);
  for(const code of [-32768,-1,0,1,15,16,25,31,32,33,32767])add('scale/'+code,[N(),S('StandardScaleType',{short:code}),S('ScaleToFit',false),snap(),S('ScaleToFit',false),snap(),S('ScaleToFit',true),snap(),C('Clone')]);
  add('independent-scales',[N(),S('PrintScaleNumerator',D(2.5)),S('PrintScaleDenominator',D(17.75)),S('StandardScaleType',{short:25}),S('StandardScaleFactor',D(-.125)),snap(),S('ScaleToFit',true),snap(),S('StandardScaleFactor',null),snap(),C('Clone')]);
  const strings=[null,'','plain','Zażółć 東京 😀',' leading trailing ','a\nb','a\rb','a\0b',{utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[0xd800,0xdc00]},{utf16:[65,0xd800,66]}];
  for(const member of ['PageSetupName','PlotterName','PaperSizeName','ViewName','CurrentStyleSheet'])for(let i=0;i<strings.length;i++)add(`text/${member}/${i}`,[N(),S(member,strings[i]),C('Clone'),...validate()]);
  for(const member of ['PaperSize','Origin','WindowBottomLeft','WindowUpRight','PaperImageOrigin'])for(const value of extremes)for(const component of [0,1]){
    const v=[2,3];v[component]=value;
    add(`vector/${member}/${component}/${label(value)}`,[N(),S(member,V('Vector2',...v)),G(member,'vector'),S('X',D(99),'vector'),snap(),C('Clone'),...validate()]);
  }
  const enums={Flags:'PlotFlags',PlotType:'PlotType',PaperUnits:'PlotPaperUnits',PaperRotation:'PlotRotation',ShadePlotMode:'ShadePlotMode',ShadePlotResolutionMode:'ShadePlotResolutionMode'};
  for(const [member,type]of Object.entries(enums))for(const value of [-32769,-32768,-1,0,1,2,3,4,5,6,32,688,32767,32768])add(`enum/${member}/${value}`,[N(),S(member,E('Objects.'+type,value)),snap(),C('Clone'),...validate()]);
  for(const dpi of [-32768,-1,0,99,100,300,777,32767])add('dpi/'+dpi,[N(),S('ShadePlotDPI',{short:dpi}),snap(),C('Clone'),...validate()]);
  for(const target of ['Objects.DxfXRecord','Objects.DxfDictionary','Tables.Layer','Entities.Line','Entities.AttributeDefinition','Entities.Attribute']){
    const args=target.endsWith('Layer')?['L']:target.endsWith('AttributeDefinition')?['TAG']:target.endsWith('.Attribute')?[{new:'Entities.AttributeDefinition',args:['TAG']}]:[];
    add('shade/'+target,[N(),{kind:'new',type:target,args,id:'target'},S('ShadePlotObject',R('target')),snap(),C('Clone',[],'q'),G('ShadePlotObject','a'),G('ShadePlotObject','b','q'),equal('a','b'),...validate()]);
  }
  add('shade-rejected-edit',[N(),N('DxfXRecord',[],'reference'),S('ShadePlotObject',R('reference')),{kind:'new',type:'Entities.Line',args:[],id:'line'},S('ShadePlotObject',R('line')),G('ShadePlotObject','kept'),equal('reference','kept')]);
  for(const type of ['DxfPlotSettingsObject','DxfWipeoutVariables']){
    add(type+'/defaults',[N(type),C('CloneShell',[],'q','p',true),equal('p','q'),errors(),C('ValidateDatabaseSchema',[null,R('errors')],null,'p',true),snap('errors')]);
    add(type+'/dictionary-owner',[N(type),N('DxfDictionary',[],'owner'),{kind:'set',target:'p',member:'Owner',value:R('owner'),nonPublic:true},errors(),C('ValidateDatabaseSchema',[null,R('errors')],null,'p',true),snap('errors'),snap()]);
  }
  add('page/null',[N('DxfPlotSettingsObject',[null])]);
  add('page/deep-payload-shared-target',[N(),N('DxfXRecord',[],'reference'),S('ShadePlotObject',R('reference')),S('StandardScaleFactor',D(-0)),N('DxfPlotSettingsObject',[R('p')],'page'),G('Settings','settings','page'),S('PrintScaleNumerator',D(99)),snap('page'),C('CloneShell',[],'q','page',true),G('Settings','clone','q'),equal('settings','clone'),G('ShadePlotObject','a','settings'),equal('a','reference'),S('StandardScaleFactor',null,'clone'),snap('page')]);
  for(const mapped of [false,true])add('page/reference-map/'+mapped,[N('DxfPlotSettingsObject'),G('Settings','settings'),N('DxfXRecord',[],'a'),N('DxfXRecord',[],'b'),...(mapped?[S('ShadePlotObject',R('a'),'settings')]:[]),C('CloneShell',[],'q','p',true),C('CopyDatabaseReferencesTo',[R('q'),{resolver:[mapped?[R('a'),R('b')]:[null,null]]}],null,'p',true),snap(),snap('q')]);
  for(const frame of [false,true])add('wipeout/'+frame,[N('DxfWipeoutVariables'),S('DisplayFrame',frame),C('CloneShell',[],'q','p',true),S('DisplayFrame',!frame,'q'),snap(),snap('q')]);
  for(const member of ['DisplayQuality','Units'])for(const value of [-1,0,1,2,3,4,5,6,7,8,9,10,11,32767])add(`raster/${member}/${value}`,[N('RasterVariables',[null],'p',true),S(member,E(member==='Units'?'Units.ImageUnits':'Objects.ImageDisplayQuality',value)),snap(),C('ToString')]);
  for(const frame of [false,true])add('raster/frame/'+frame,[N('RasterVariables',[null],'p',true),S('DisplayFrame',frame),snap()]);
  add('aggregate-validation-order',[N(),S('PageSetupName',null),S('PlotterName','bad\n'),S('PaperMargin',{new:'Objects.PaperMargin',args:[D(NaN),D(Infinity),D(1),D(2)]}),S('PrintScaleNumerator',D(Infinity)),S('PrintScaleDenominator',D(Infinity)),S('Flags',E('Objects.PlotFlags',32768)),S('PaperUnits',E('Objects.PlotPaperUnits',-1)),...validate(),snap()]);
  add('validate-null',[errors(),{kind:'call',type:'Objects.PlotSettings',member:'ValidateValues',args:[null,R('errors')],nonPublic:true},snap('errors')]);
  return out;
}
