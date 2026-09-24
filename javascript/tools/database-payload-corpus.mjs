// Deterministic input packets, never native expected outputs.
import { entityBodyRow as tag } from './entity-body-io-corpus.mjs';
const h=(name,extra={})=>({h:name,...extra}),s=method=>({method});
const headers=[[5,'C0'],[330,h('root')]];
export const dataPacket=(columns=[],rows=0)=>[[100,'AcDbDataTable'],[70,2],[90,columns.length],[91,rows],[1,'Table'],...columns.flatMap(([type,name,values])=>[[92,type],[2,name],...values])];
const identity=[1,0,0,0,0,1,0,0,0,0,1,0];
export const spatialPacket=(front=false,back=false)=>[[100,'AcDbFilter'],[100,'AcDbSpatialFilter'],[70,2],[10,-1],[20,-1],[10,1],[20,1],[71,1],[72,front?1:0],[73,back?1:0],...(front?[[40,-0]]:[]),...(back?[[41,-0]]:[]),...[...identity,...identity].map(value=>[40,value])];
const scalars={1:[[93,-2147483648],[93,2147483647]],2:[[40,-0],[40,1e250]],3:[[3,'literal\\U+005CU+0041'],[3,'Zażółć 東京']],4:[[10,-0],[20,2],[30,3],[10,4],[20,5],[30,6]],10:[[71,0],[71,1]],11:[[11,-0],[21,2],[31,3],[11,4],[21,5],[31,6]]};
const xd=[[1001,'OBJECT_APP'],[1000,'A\\U+005CU+0041'],[1002,'{'],[1070,4],[1071,-4],[1005,'00AB'],[1002,'}']];
const defaultSteps=[s('read'),s('register'),s('resolve'),s('write'),s('snapshot')];
export function databasePayloadCorpus(){
  const all=[];
  const add=(name,kind,tags,steps=defaultSteps,extra={})=>all.push({name:'database-payload/'+name,request:{op:'database-payload-io',kind,mode:'text',version:18,tags:tags.map(([c,v])=>tag(c,v)),steps,...extra}});
  const base=[
    ['id','Container',[[100,'AcDbIdBuffer'],[330,h('line')],[330,'0'],[330,h('line',{pad:2,lower:true})]],{type:'IDBUFFER'}],
    ['sort','Container',[[100,'AcDbSortentsTable'],[330,h('block')],[331,h('line')],[5,'ABC'],[331,h('light')],[5,'0']],{type:'SORTENTSTABLE'}],
    ['spatial','Container',spatialPacket(true,true),{type:'SPATIAL_FILTER'}],
    ['lights','LightList',[[100,'AcDbLightList'],[90,-1],[90,2],[5,h('light')],[1,'Authored'],[5,h('light')],[1,'Duplicate reference']],{}],
    ['layer-filter','LayerFilterPointer',[...headers,[100,'AcDbFilter'],[100,'AcDbLayerFilter'],[8,'0'],[8,'Layer\\U+03A9'],[8,'0']],{type:'LAYER_FILTER'}],
    ['pointer','LayerFilterPointer',headers,{type:'OBJECT_PTR'}],
    ['table','DataTable',[...headers,...dataPacket([[1,'Integer',scalars[1]],[2,'Double',scalars[2]],[3,'String',scalars[3]],[4,'Point',scalars[4]],[10,'Boolean',scalars[10]],[11,'Vector',scalars[11]]],2)],{}],
    ['index','LayerIndex',[...headers,[100,'AcDbIndex'],[40,-0],[100,'AcDbLayerIndex'],[8,'Layer'],[360,h('buffer')],[90,1]],{}],
  ];
  const indexSteps=[s('read'),s('register'),{method:'owner',target:'buffer',owner:'item'},s('resolve'),s('write'),s('snapshot')];
  for(const [name,kind,tags,extra]of base){
    for(const mode of ['text','binary','legacy'])for(const version of [13,14,15,16,17,18])add(`${name}/profile/${mode}/${version}`,kind,tags,kind==='LayerIndex'?indexSteps:defaultSteps,{...extra,mode,version});
    for(let length=0;length<=tags.length;length++)add(`${name}/prefix/${length}`,kind,tags.slice(0,length),[s('read'),s('resolve'),s('write')],extra);
    for(let at=0;at<tags.length;at++){
      add(`${name}/missing/${at}`,kind,tags.filter((_,i)=>i!==at),[s('read'),s('resolve'),s('write')],extra);
      add(`${name}/duplicate/${at}`,kind,tags.flatMap((v,i)=>i===at?[v,v]:[v]),[s('read'),s('resolve'),s('write')],extra);
    }
    for(const code of [1,100,102,300,999])add(`${name}/unknown/${code}`,kind,[...tags,[code,code===100?'AcDbUnknown':code===102?'{APP':'private']],defaultSteps,extra);
    for(const sequence of [xd,[[1001,'']],[[1001,'APP'],[1000,'a'],[1001,'APP'],[1000,'b']],[[1001,'APP'],[1070,1],[8,'not xdata']],[[1001,'APP'],[1003,'\\U+000ALayer']]]){
      add(`${name}/xdata/${JSON.stringify(sequence)}`,kind,[...tags,...sequence],kind==='LayerIndex'?indexSteps:defaultSteps,extra);
    }
    add(`${name}/read-repeat`,kind,tags,[s('read'),s('read'),s('register'),s('resolve'),s('resolve'),s('write')],extra);
    for(const at of [1,2,3,4,7,9,14,27,44])add(`${name}/writer-throw/${at}`,kind,tags,[...(kind==='LayerIndex'?indexSteps.slice(0,-2):defaultSteps.slice(0,-2)),{method:'write',hooks:[{at,kind:'throw'}]},s('write')],extra);
    if(kind!=='Container')for(const declared of ['compatible','incompatible','entity']){
      const names={DataTable:['DATATABLE','AcDbDataTable'],LightList:['LIGHTLIST','AcDbLightList'],LayerIndex:['LAYER_INDEX','AcDbLayerIndex'],LayerFilterPointer:[extra.type,extra.type==='OBJECT_PTR'?'CAseDLPNTableRecord':'AcDbLayerFilter']};
      const [cls,cpp]=names[kind];
      const command={method:'class',name:cls,cpp:declared==='incompatible'?'Different':cpp,entity:declared==='entity',app:'preserve application',count:73};
      add(`${name}/class/${declared}`,kind,tags,[command,s('prepare'),s('read'),s('register'),s('prepare'),s('prepare')],extra);
    }
  }
  // No numeric canonicalization is silently substituted for source lexical null rules.
  for(const name of ['line','style','light','buffer','root'])for(const spelling of [{},{pad:3,lower:true}])for(const action of ['ambiguous','unaccept','discard','replace']){
    for(const [kind,body,extra]of [
      ['Container',[[100,'AcDbIdBuffer'],[330,h(name,spelling)]],{type:'IDBUFFER'}],
      ['DataTable',[...headers,...dataPacket([[5,'Reference',[[331,h(name,spelling)]]]],1)],{}],
    ])add(`source/${kind}/${name}/${JSON.stringify(spelling)}/${action}`,kind,body,[s('read'),s('register'),{method:'lookup',handle:h(name,spelling)},{method:'source',target:name,action},{method:'lookup',handle:h(name,spelling)},s('resolve'),{method:'source',target:name,action:'validate'},s('write')],extra);
  }
  for(const value of ['0','00','0000000000000000','ABCDEF']){
    add('id/null/'+value,'Container',[[100,'AcDbIdBuffer'],[330,h('line')],[330,value]],[s('read'),s('register'),s('resolve'),s('resolve'),s('write')],{type:'IDBUFFER'});
    add('light/null/'+value,'LightList',[[100,'AcDbLightList'],[90,1],[90,2],[5,h('light')],[1,'First'],[5,value],[1,'Second']],[s('read'),s('register'),s('resolve'),s('resolve'),s('write')]);
  }
  for(const front of [false,true])for(const back of [false,true]){
    const body=spatialPacket(front,back);
    for(const mode of ['text','binary','legacy'])add(`spatial/flags/${front}/${back}/${mode}`,'Container',body,defaultSteps,{type:'SPATIAL_FILTER',mode});
    for(const [code,value]of [[70,-1],[70,32767],[71,-1],[71,2],[72,2],[73,2],[41,4],[210,0],[220,0],[230,0],[11,3],[21,3],[31,3],[40,2],[30,0],[20,0]])add(`spatial/scalar/${front}/${back}/${code}/${value}`,'Container',[...body.filter(t=>t[0]!==code),[code,value]],[s('read'),s('write')],{type:'SPATIAL_FILTER'});
  }
  // Dedicated DATATABLE public dimensions, scalar types and unknown-envelope admission.
  for(let type=1;type<=11;type++)for(const count of [0,1,2,4]){
    const own=type===6||type===7,refs=type>=5&&type<=9,group={5:331,6:360,7:350,8:340,9:330}[type];
    let values=[];
    for(let j=0;j<count;j++)values.push(...(refs?[[group,j===0?h(own?'leaf':'line'):'0']]:scalars[type].slice(0,[4,11].includes(type)?3:1)));
    const body=[...headers,...dataPacket([[type,'Column',values]],count)];
    add(`table/type/${type}/${count}`,'DataTable',body,[s('read'),s('register'),...(own&&count?[{method:'owner',target:'leaf',owner:'item'}]:[]),s('resolve'),s('write'),s('resolve')]);
    if(own&&count)add(`table/owner-invalid/${type}/${count}`,'DataTable',body);
  }
  for(const columns of [-1,0,1,1048576,1048577,2147483647])for(const rows of [-1,0,1,1048576,1048577,2147483647])add(`table/dimensions/${columns}/${rows}`,'DataTable',[...headers,[100,'AcDbDataTable'],[70,2],[90,columns],[91,rows],[1,'Bounds']],[s('read'),s('write')]);
  for(const type of [-1,0,12,2147483647])add('table/unknown-type/'+type,'DataTable',[...headers,...dataPacket([[type,'Private',[[300,'private']]]],1)]);
  for(const value of [0,1,3,-1,32767])add('table/unknown-version/'+value,'DataTable',[...headers,...dataPacket().map(([c,v])=>[c,c===70?value:v])]);
  for(const text of ['','\\U+000A','\\U+0000','\\U+D800','\\U+DC00','\\U+D83D\\U+DE00','literal\\U+005CU+0041']){
    add('table/name/'+text,'DataTable',[...headers,...dataPacket([[3,text,[[3,text]]]],1).map(([c,v])=>[c,c===1?text:v])]);
    add('light/name/'+text,'LightList',[[100,'AcDbLightList'],[90,0],[90,1],[5,h('light')],[1,text]]);
    add('filter/name/'+text,'LayerFilterPointer',[...headers,[100,'AcDbFilter'],[100,'AcDbLayerFilter'],[8,text]],defaultSteps,{type:'LAYER_FILTER'});
  }
  const headerCases=[[],[[5,'C1']],[[330,h('root')]],[[330,'CAFE']],[[102,'{ACAD_REACTORS'],[330,h('line')],[102,'}']],[[102,'{ACAD_REACTORS'],[360,'AB'],[102,'}']],[[102,'{ACAD_XDICTIONARY'],[360,'ABC'],[102,'}']],[[102,'{ACAD_XDICTIONARY'],[360,'ABC'],[360,'ABD'],[102,'}']],[[102,'{PRIVATE'],[330,'DEAD'],[102,'}']],[[102,'}']],[[102,'{A'],[102,'{B'],[102,'}'],[102,'}']],[[102,'{A'],[330,'A']]];
  for(const [name,kind,tags,extra]of base.filter(v=>['DataTable','LayerIndex','LayerFilterPointer'].includes(v[1])))for(let i=0;i<headerCases.length;i++)add(`${name}/header/${i}`,kind,[...headers,...headerCases[i],...tags.slice(2)],kind==='LayerIndex'?indexSteps:defaultSteps,extra);
  const idx=base.find(v=>v[0]==='index')[2];
  for(const mutation of ['bad-count','zero','duplicate-owner','same-numeric-owner','private-before','private-after','wrong-marker','missing-time']){
    let tags=idx.slice();
    if(mutation==='bad-count')tags=tags.map(([c,v])=>[c,c===90?2:v]);
    if(mutation==='zero')tags=tags.map(([c,v])=>[c,c===360?'0':v]);
    if(mutation==='duplicate-owner'||mutation==='same-numeric-owner')tags.push([8,'Layer2'],[360,h('buffer',mutation==='same-numeric-owner'?{pad:1}:{})],[90,1]);
    if(mutation==='private-before')tags.splice(5,0,[90,0]);
    if(mutation==='private-after')tags.push([100,'Private'],[300,'data']);
    if(mutation==='wrong-marker')tags[2]=[100,'Private'];if(mutation==='missing-time')tags=tags.filter(([c])=>c!==40);
    add('index/variant/'+mutation,'LayerIndex',tags,indexSteps);
  }
  let random=0x628acd11;const next=n=>(random=(Math.imul(random,1664525)+1013904223)>>>0)%n;
  for(let run=0;run<80;run++){
    const rows=next(9),cols=1+next(8),columns=[];
    for(let col=0;col<cols;col++){
      const type=[1,2,3,4,5,8,9,10,11][next(9)],values=[];
      for(let row=0;row<rows;row++){
        const scalar=type===1?next(20000)-10000:type===2?(next(10000)-5000)/128:type===3?'Row '+row+' Ω\\U+0041':0;
        if([4,11].includes(type)){const c=type===4?10:11;values.push([c,next(100)],[c+10,-0],[c+20,-next(100)]);}
        else values.push([({1:93,2:40,3:3,5:331,8:340,9:330,10:71})[type],type===10?next(2):[5,8,9].includes(type)?(next(2)?h('line'):'0'):scalar]);
      }
      columns.push([type,'Column '+col,values]);
    }
    add('table/random/'+run,'DataTable',[...headers,...dataPacket(columns,rows)],defaultSteps,{mode:run%2?'binary':'text'});
  }
  return all;
}
