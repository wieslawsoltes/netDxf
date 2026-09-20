// Independent requests: no stored expected values, tolerances or skipped operations.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const N=(type,args=[],id='b',signature,nonPublic=false)=>({kind:'new',type,args,id,...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic:true}:{})});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const obs=(target,member,extra={})=>({kind:'observe',target,member,observer:target+member,...extra});
const b=(name='B',id='b')=>N('Blocks.Block',[name],id,['String']);
const line=(id='line')=>N('Entities.Line',[V('Vector3',1,2,3),V('Vector3',4,5,6)],id);
const att=(tag='TAG',id='att')=>N('Entities.AttributeDefinition',[tag],id);
const base=()=>[b(),line(),att(),G('b','Entities','entities'),G('b','AttributeDefinitions','attributes')];
export function blockCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'blocks/'+name,category:'blocks',request:{steps}});
 for(const name of [null,'',' ','B',' Name ','Ω 😀','A/B','*U1','*Model_Space','a\0b'])add('constructor/'+out.length,[b(name),C('b','Clone'),C('b','HasReferences'),C('b','GetReferences')]);
 for(const name of ['*U1','*T2','*A1','*Model_Space','*Paper_Space',' Standard '])for(const rename of ['Changed','CHANGED','',null,'   ','A/B'])add('internal-name/'+out.length,[N('Blocks.Block',[name,null,null,false],'b',['String','IEnumerable<Entities.EntityObject>','IEnumerable<Entities.AttributeDefinition>','Boolean'],true),S('b','Flags',E('Blocks.BlockTypeFlags',1),true),S('b','Name',rename),snap('b'),C('b','Clone'),C('b','Clone',['COPY'])]);
 for(const name of ['B','b','NEW',null,'','  '])for(const failure of [false,true])add('rename-event/'+out.length,[b(),G('b','Record','record'),obs('b','NameChanged',{throw:failure}),S('b','Name',name),snap('b'),snap('record'),{kind:'events'}]);
 for(const file of [null,'','x.dwg','x.dxf','\0bad','a\0b','"bad','a\nb'])for(const overlay of [false,true])add('xref/'+out.length,[N('Blocks.Block',['XREF',file,overlay],'b',['String','String','Boolean']),C('b','Clone',['Copy']),G('b','Entities','entities'),line(),C('entities','Add',[R('line')]),snap('b')]);
 for(const key of ['EntityAdded','EntityRemoved','AttributeDefinitionAdded','AttributeDefinitionRemoved'])for(const failure of [false,true])add('notifications/'+out.length,[...base(),obs('b',key,{throw:failure}),C('entities','Add',[R('line')]),C('attributes','Add',[R('att')]),snap('b'),snap('line'),snap('att'),C('entities','Remove',[R('line')],null,['Entities.EntityObject']),C('attributes','Remove',['TAG']),snap('b'),snap('line'),snap('att'),{kind:'events'}]);
 for(const target of ['entities','attributes'])for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])for(const cancel of [false,true])for(const failure of [false,true])add('collection-events/'+out.length,[...base(),obs(target,event,{cancel,throw:failure}),C(target,'Add',[R(target==='entities'?'line':'att')]),snap('b'),snap('line'),snap('att'),C(target,'Remove',[target==='entities'?R('line'):'TAG'],null,target==='entities'?['Entities.EntityObject']:null),snap('b'),snap('line'),snap('att'),{kind:'events'}]);
 for(const target of ['entities','attributes'])for(const mode of ['null','duplicate','foreign','locked','reactor']){
 const item=target==='entities'?'line':'att',steps=[...base(),b('FOREIGN','foreign'),G('foreign',target==='entities'?'Entities':'AttributeDefinitions','other')];
 if(mode==='foreign')steps.push(C('other','Add',[R(item)]));
 if(mode==='duplicate'||mode==='reactor')steps.push(C(target,'Add',[R(item)]));
 if(mode==='locked')steps.push(S('b','Flags',E('Blocks.BlockTypeFlags',16),true));
 if(mode==='reactor'&&target==='entities')steps.push(C('line','AddReactor',[R('b')],null,null,true));
 steps.push(C(target,'Add',[mode==='null'?null:R(item)]),snap('b'),snap(item),C(target,'Remove',[target==='entities'?R(item):'TAG'],null,target==='entities'?['Entities.EntityObject']:null),snap('b'),snap(item));
 add('admission/'+out.length,steps);
 }
 for(const index of [-1,0,1,2])for(const mode of ['insert','replace','remove'])add('indexes/'+out.length,[...base(),C('entities','Add',[R('line')]),line('second'),mode==='replace'?{kind:'set-index',target:'entities',args:[I(index)],value:R('second')}:C('entities',mode==='insert'?'Insert':'RemoveAt',mode==='insert'?[I(index),R('second')]:[I(index)]),snap('b'),snap('line'),snap('second')]);
 for(const units of [-1,0,1,4,6,24,25])add('records/'+units,[{kind:'set',type:'Blocks.BlockRecord',member:'DefaultUnits',value:E('Units.DrawingUnits',units)},b('B'),G('b','Record','record'),S('record','AllowExploding',false),S('record','ScaleUniformly',true),C('b','Clone',['C'],'copy'),snap('b'),snap('copy')]);
 for(const n of ['0','1','9223372036854775807','-1'])add('handles/'+n,[...base(),C('entities','Add',[R('line')]),C('attributes','Add',[R('att')]),C('b','AssignHandle',[{long:n}],null,null,true),snap('b'),snap('line'),snap('att')]);
 add('origin-copy',[b(),S('b','Origin',V('Vector3',-0,2,3)),G('b','Origin','v'),S('v','X',D(99)),snap('b'),C('b','Clone',['Copy'])]);
 for(const failure of [false,true])add('layer-events/'+failure,[b(),obs('b','LayerChanged',{replace:{new:'Tables.Layer',args:['REPLACEMENT']},throw:failure}),S('b','Layer',{new:'Tables.Layer',args:['PROPOSED']}),S('b','Layer',null),snap('b'),{kind:'events'}]);
 for(const slot of ['block','record','end'])add('xdata/'+slot,[...base(),C('entities','Add',[R('line')]),C('attributes','Add',[R('att')]),G('b','Record','record'),G('b','End','end',true),G(slot==='block'?'b':slot,'XData','xd'),N('XData',[{new:'Tables.ApplicationRegistry',args:['APP']}],'data'),G('data','XDataRecord','dataRecords'),C('dataRecords','Add',[{new:'XDataRecord',args:[E('XDataCode',1000),'payload']}]),C('xd','Add',[R('data')]),C('b','Clone',['COPY'],'copy'),C('dataRecords','Clear'),snap('copy'),snap('b')]);
 for(const foreign of [false,true])for(const associated of [false,true])add('hatch-adoption/'+out.length,[b(),b('OTHER','other'),N('Entities.Circle',[V('Vector3',0,0,0),D(2)],'circle'),G('other','Entities','otherEntities'),...(foreign?[C('otherEntities','Add',[R('circle')])]:[]),N('Entities.HatchBoundaryPath',[A('Entities.EntityObject',[R('circle')])],'path',['IEnumerable<Entities.EntityObject>']),N('Entities.Hatch',[{static:'Entities.HatchPattern',property:'Solid'},A('Entities.HatchBoundaryPath',[R('path')]),associated],'hatch'),G('b','Entities','entities'),C('entities','Add',[R('hatch')]),snap('b'),snap('circle'),C('entities','Remove',[R('circle')],null,['Entities.EntityObject']),C('hatch','UnLinkBoundary'),C('entities','Clear'),snap('b'),snap('circle')]);
 for(const value of [null,'Description','a\nb'])add('description/'+out.length,[b(),S('b','Description',value),C('b','Clone'),snap('b')]);
 return out;
}
