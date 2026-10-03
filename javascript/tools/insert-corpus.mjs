// Source-independent request descriptions. Do not store expected results here.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const N=(type,args=[],id,signature,nonPublic=false)=>({kind:'new',type,args,id,...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const ix=(target,index,id)=>({kind:'index',target,args:[I(index)],id});
const b=()=>N('Blocks.Block',['B'],'b');
const att=(tag='TAG',id='a')=>N('Entities.AttributeDefinition',[tag],id);
const base=(attribute=true)=>[b(),G('b','Entities','entities'),N('Entities.Line',[V('Vector3',1,2,3),V('Vector3',4,-2,5)],'line'),C('entities','Add',[R('line')]),S('b','Origin',V('Vector3',.5,-1,2)),
 ...(attribute?[att(),S('a','Position',V('Vector3',2,3,0)),S('a','Value','P-101'),G('b','AttributeDefinitions','defs'),C('defs','Add',[R('a')])]:[]),N('Entities.Insert',[R('b'),V('Vector3',10,-20,5)],'i')];
const matrix=m=>({new:'Matrix3',args:m.map(D)});
const identity=[1,0,0,0,1,0,0,0,1],matrices=[identity,[2,0,0,0,3,0,0,0,4],[-2,0,0,0,3,0,0,0,1],[1,.5,0,0,1,0,0,0,1],[0,0,0,0,1,0,0,0,1],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]];
export function insertCorpus(){
 const out=[],add=(name,steps)=>out.push({name:'insert/'+name,category:'insert',request:{steps}});
 add('constructors',[b(),N('Entities.Insert',[null],'bad',['Blocks.Block']),N('Entities.Insert',[R('b')],'i'),C('i','Clone'),C('i','Explode'),N('Entities.Insert',[R('b'),V('Vector2',1,2)],'xy'),N('Entities.Insert',[R('b'),V('Vector3',1,2,3)],'xyz')]);
 for(const member of ['ColumnCount','RowCount'])for(const value of [-32768,-1,0,1,2,32767])add(member+'/'+value,[...base(),S('i',member,{short:value}),snap('i'),C('i','Clone')]);
 for(const member of ['ColumnSpacing','RowSpacing','Rotation'])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1,1e308,Infinity,NaN])add(member+'/'+D(value).double,[...base(),S('i',member,D(value)),snap('i'),C('i','Clone')]);
 for(const s of [[0,1,1],[1,0,1],[1,1,0],[1e-13,1,1],[-2,3,4],[Infinity,1,1],[NaN,1,1]])add('scale/'+out.length,[...base(),S('i','Scale',V('Vector3',...s)),G('i','Scale','copy'),S('copy','X',D(77)),snap('i'),C('i','GetTransformation'),C('i','Clone')]);
 for(const from of [0,1,4,6,24])for(const to of [0,1,4,6,24])add('units/'+from+'/'+to,[...base(false),G('b','Record','record'),S('record','Units',E('Units.DrawingUnits',from)),{kind:'set',type:'Entities.Insert',member:'DefaultInsUnits',value:E('Units.DrawingUnits',to)},C('i','GetTransformation'),C('i','GetTransformation',[E('Units.DrawingUnits',to)]),C('i','Explode')]);
 for(const normal of [[0,0,1],[0,0,-1],[1,2,3]])for(const angle of [0,35,90])for(const scale of [[1,1,1],[2,-3,4]])for(const spacing of [[7.5,-4.25],[0,0]])add('array/'+out.length,[...base(),S('i','Normal',V('Vector3',...normal)),S('i','Rotation',D(angle)),S('i','Scale',V('Vector3',...scale)),S('i','ColumnCount',{short:3}),S('i','RowCount',{short:2}),S('i','ColumnSpacing',D(spacing[0])),S('i','RowSpacing',D(spacing[1])),C('i','TransformAttributes'),C('i','GetGridPosition',[I(1),I(2)]),C('i','ExplodeCell',[I(1),I(2)]),C('i','Explode'),C('i','Clone')]);
 for(const multiple of [false,true])for(const angle of [0,35])for(const normal of [[0,0,1],[1,2,3]])for(const [mi,m] of matrices.entries())add('transform/'+out.length,[...base(),S('i','ColumnCount',{short:multiple?2:1}),S('i','RowCount',{short:multiple?3:1}),S('i','ColumnSpacing',D(4)),S('i','RowSpacing',D(-2)),S('i','Normal',V('Vector3',...normal)),S('i','Rotation',D(angle)),S('i','Scale',V('Vector3',2,-3,4)),C('i','TransformAttributes'),C('i','TransformBy',[matrix(m),V('Vector3',7,-11,13)]),snap('i'),C('i','GetGridPosition',[I(0),I(multiple?1:0)]),C('i','ExplodeCell',[I(0),I(0)]),C('i','Clone')]);
 for(const row of [-1,0,1,2])for(const col of [-1,0,1,2])add('indexes/'+row+'/'+col,[...base(),S('i','RowCount',{short:2}),S('i','ColumnCount',{short:2}),C('i','GetGridPosition',[I(row),I(col)]),C('i','ExplodeCell',[I(row),I(col)])]);
 for(const type of ['Entities.Circle','Entities.Arc','Entities.Polyline2D','Entities.MLine'])for(const scale of [[2,2,2],[2,3,1],[-2,3,1]]){
 const args=type==='Entities.Circle'?[V('Vector3',1,2,0),D(2)]:type==='Entities.Arc'?[V('Vector3',1,2,0),D(2),D(20),D(230)]:[A('Vector2',[V('Vector2',0,0),V('Vector2',3,0),V('Vector2',4,2)])];
 add('conversion/'+out.length,[b(),G('b','Entities','entities'),N(type,args,'shape'),C('entities','Add',[R('shape')]),N('Entities.Insert',[R('b')],'i'),S('i','Scale',V('Vector3',...scale)),C('i','Explode'),snap('shape')]);
 }
 for(const operation of ['remove','add','replace','value','detach-definition'])for(const failure of [false,true]){
 const steps=[...base(),G('i','Attributes','attributes'),ix('attributes',0,'old'),S('old','Handle','AB',true),{kind:'observe',target:'i',member:operation==='add'?'AttributeAdded':'AttributeRemoved',observer:'event',throw:failure}];
 if(operation==='remove')steps.push(C('defs','Remove',['TAG']));
 if(operation==='add')steps.push(att('SECOND','second'),C('defs','Add',[R('second')]));
 if(operation==='replace')steps.push(C('defs','Remove',['TAG']),att('TAG','second'),S('second','Position',V('Vector3',7,8,9)),C('defs','Add',[R('second')]));
 if(operation==='value')steps.push(S('old','Value','USER'),S('a','Value','NEW_DEFAULT'));
 if(operation==='detach-definition')steps.push(S('old','Definition',null,true));
 steps.push(C('i','Sync'),snap('i'),snap('old'),snap('attributes'),{kind:'events'});add('sync/'+out.length,steps);
 }
 for(const state of ['empty','null','owned','two']){
 const list=state==='null'?null:{new:'List<Entities.Attribute>',args:[A('Entities.Attribute',state==='empty'?[]:state==='two'?[R('a'),R('a')]:[R('a')])]};
 const steps=[att('TAG','d'),N('Entities.Attribute',[R('d')],'a')];if(state==='owned')steps.push(b(),N('Entities.Insert',[R('b')],'owner'),{...S('a','Owner',R('owner'),true),type:'DxfObject'});
 steps.push(N('Entities.Insert',[list],'i',['List<Entities.Attribute>'],true),snap('a'),C('i','Clone'));add('internal/'+state,steps);
 }
 add('owner-units',[...base(false),b(),N('Blocks.Block',['Parent'],'parent'),G('parent','Record','record'),S('record','Units',E('Units.DrawingUnits',1)),G('parent','Entities','parentEntities'),C('parentEntities','Add',[R('i')]),C('i','GetTransformation'),C('i','Explode')]);
 add('nested',[...base(false),S('i','ColumnCount',{short:2}),S('i','ColumnSpacing',D(3)),N('Blocks.Block',['Outer',A('Entities.EntityObject',[R('i')])],'outer'),N('Entities.Insert',[R('outer'),V('Vector3',7,8,9)],'top'),S('top','ColumnCount',{short:2}),S('top','ColumnSpacing',D(4)),C('top','Explode'),C('top','Clone')]);
 add('overflow-empty',[b(),N('Entities.Insert',[R('b')],'i'),S('i','ColumnCount',{short:32767}),S('i','RowCount',{short:32767}),S('i','ColumnSpacing',D(Number.MAX_VALUE)),C('i','GetGridPosition',[I(0),I(2)]),C('i','Explode')]);
 return out;
}
