// Deterministic input-only payloads; independent native methods supply observations.
import { entityBodyRow } from './entity-body-io-corpus.mjs';
const h=name=>({h:name}),r=ref=>({ref}),s=method=>({method});
const xdata=[[1001,'OUTPUT_APP'],[1000,'literal\\U+005CU+0041 Ω'],[1004,{bytes:[0,255]}],[1070,7]];
export const plotPacket=[[100,'AcDbPlotSettings'],[1,'Page Ω'],[2,'Device'],[4,'A4'],[6,'View'],[7,'Style.ctb'],
  [40,-0],[41,2],[42,3],[43,4],[44,210],[45,297],[46,3],[47,4],[48,1],[49,2],[140,200],[141,280],[142,1],[143,2],
  [70,16],[72,1],[73,2],[74,1],[75,16],[76,2],[77,5],[78,300],[147,.5],[148,-0],[149,1],[333,'0']];
export function geoPacket({definition='EPSG^J4326',host=h('block'),points=3,faces=1}={}) {
  return [[100,'AcDbGeoData'],[90,2],[330,host],[70,3],[10,-0],[20,2],[30,3],[11,4],[21,5],[31,6],[40,1],[91,6],[41,1],[92,6],[210,0],[220,0],[230,1],[12,0],[22,1],
    [95,1],[141,1],[294,true],[142,-0],[143,6371000],[301,definition],[302,'rss'],[305,'from'],[306,'to'],[307,'coverage'],[93,points],
    ...Array.from({length:points},(_,i)=>[[13,i],[23,i+1],[14,i+2],[24,i+3]]).flat(),[96,faces],...Array.from({length:faces},()=>[[97,0],[98,1],[99,2]]).flat()];
}
export const sunPacket=[[100,'AcDbSun'],[90,1],[290,true],[63,7],[421,0xabcdef],[40,-0],[291,true],[91,2451545],[92,0],[292,false],[70,1],[71,256],[280,1]];
const steps={
  OutputSettings:[s('read'),s('register'),s('resolve'),s('validate'),s('write'),s('database-validate')],
  GeoData:[s('read'),{method:'register',owner:'ext',name:'ACAD_GEOGRAPHICDATA'},s('resolve'),s('prepare'),s('write'),s('database-validate')],
  Sun:[s('read'),{method:'register',owner:'port'},{method:'add-sun',owner:r('port'),handle:h('item')},s('resolve'),s('prepare'),s('write'),{method:'write-ref',owner:r('port')},s('database-validate')]
};
export function environmentIOCorpus() {
  const all=[];
  const add=(name,kind,tags,commands=steps[kind],version=18,mode='text',extra={})=>all.push({name:`environment-io/${mode}/${kind}/${name}`,request:{op:'environment-io',kind,mode,version,tags:tags.map(([c,v])=>c===421&&typeof v==='number'?[c,{int:v}]:entityBodyRow(c,v)),steps:commands,...extra}});
  const sunRecord=(body=sunPacket,header=[[5,'F00'],[330,h('port')]])=>[...header,...body];
  for(const mode of ['text','binary','legacy']) {
    for(const [kind,packet] of [['OutputSettings',plotPacket],['GeoData',geoPacket()],['Sun',sunRecord()]]) {
      for(const version of [13,14,15,16,17,18]){
        add('profile/'+version,kind,packet,steps[kind],version,mode);
        add('xdata/'+version,kind,[...packet,...xdata],steps[kind],version,mode);
      }
      for(let i=0;i<packet.length;i++) {
        add('without/'+i,kind,packet.filter((_,j)=>j!==i),steps[kind],18,mode);
        add('duplicate/'+i,kind,[...packet.slice(0,i+1),packet[i],...packet.slice(i+1)],steps[kind],18,mode);
      }
      for(const trailing of [[[100,'PrivateClass']],[[101,'PrivateData']],[[999,'comment']],[[300,'private']],[[1000,'orphan']],[[1001,'APP'],[90,1]]])add('trailing/'+JSON.stringify(trailing),kind,[...packet,...trailing],steps[kind],18,mode);
      for(const start of [-1,0,1,packet.length,packet.length+1])add('start/'+start,kind,packet,[{method:'read',start},s('write')],18,mode,kind==='Sun'?{payloadOnly:true}:{});
      add('wrong-target-write',kind,packet,[{method:'write',target:'leaf'}],18,mode);
    }
    for(const [code,values] of [[70,[-32768,32767]],[72,[-1,0,2,3]],[73,[-1,3,4]],[74,[-1,5,6]],[75,[-1,32,33]],[76,[-1,3,4]],[77,[-1,5,6]],[78,[-1,0,99,100,32767]],[142,[-0,-1,0,1e-100,1e100]],[143,[-0,-1,0,1e-100,1e100]],[147,[-0,-1,0,1e-100,1e100]]])for(const value of values)
      add(`scalar/${code}/${Object.is(value,-0)?'-0':value}`,'OutputSettings',plotPacket.map(([c,v])=>[c,c===code?value:v]),steps.OutputSettings,18,mode);
    for(const code of [1,2,4,6,7])for(const value of ['','Ω東京','literal\\U+005CU+0041','\\U+000A','\\U+000D','\\U+0000','\\U+D800','😀'])
      add(`text/${code}/${value}`,'OutputSettings',plotPacket.map(([c,v])=>[c,c===code?value:v]),steps.OutputSettings,13,mode);
    for(const handle of ['0','00','0000','0000000000000000','AA',h('leaf'),h('line'),h('style'),h('block')])add('shade/'+JSON.stringify(handle),'OutputSettings',plotPacket.map(([c,v])=>[c,c===333?handle:v]),steps.OutputSettings,18,mode);
    add('minimal','OutputSettings',[[100,'AcDbPlotSettings']],steps.OutputSettings,18,mode);
    add('unknown-kind','OutputSettings',plotPacket,[s('read'),s('write')],18,mode,{type:'OTHER'});
    for(const values of [[],[0],[1],[-1],[2],[0,0]])add('wipeout/'+JSON.stringify(values),'OutputSettings',[[100,'AcDbWipeoutVariables'],...values.map(v=>[70,v])],steps.OutputSettings,18,mode,{type:'WIPEOUTVARIABLES'});
    for(const [code,values]of [[90,[0,1,3]],[70,[-1,0,1,2,4]],[40,[-0,-1,0,1e-300]],[41,[-0,-1,0,1e100]],[91,[-1,0,24,25]],[92,[-1,0,24,25]],[95,[0,1,2,3,4,5]],[141,[-0,-1,0,1e100]],[143,[-1,-0,0,1e100]],[93,[-1,2147483647]],[96,[-1,2147483647]],[97,[-1,3]],[98,[-1,3]],[99,[-1,3]]])for(const value of values)
      add(`scalar/${code}/${Object.is(value,-0)?'-0':value}`,'GeoData',geoPacket().map(([c,v])=>[c,c===code?value:v]),steps.GeoData,18,mode);
    for(const axis of ['up','north'])add('zero/'+axis,'GeoData',geoPacket().map(([c,v])=>[c,(axis==='up'?[210,220,230]:[12,22]).includes(c)?0:v]),steps.GeoData,18,mode);
    for(const counts of [[0,0],[1,0],[2,0],[5,3]])add('mesh/'+counts,'GeoData',geoPacket({points:counts[0],faces:counts[1]}),steps.GeoData,18,mode);
    for(const host of ['0','AA',h('line'),h('leaf'),h('style')])add('host/'+JSON.stringify(host),'GeoData',geoPacket({host}),steps.GeoData,18,mode);
    for(const sequence of [[[303,'first'],[301,'last']],[[303,'none-final']],[[301,'final'],[303,'after']],[[301,'first'],[301,'second']],[[303,'\\U+'],[301,'03A9^J']]]){
      add('definition/'+JSON.stringify(sequence),'GeoData',geoPacket().flatMap(([c,v])=>c===301?sequence:[[c,v]]),steps.GeoData,18,mode);
    }
    for(const [code,values]of [[63,[-1,0,256,257]],[421,[-1,0,0xffffff,0x1000000]],[40,[-0,-1,0,1e100]],[91,[-2147483648,2147483647]],[92,[-2147483648,2147483647]],[70,[-1,0,2,3]],[71,[0,63,64,65,128,4096,4097]],[280,[-1,0,255,256]]])for(const value of values)
      add(`scalar/${code}/${Object.is(value,-0)?'-0':value}`,'Sun',sunRecord(sunPacket.map(([c,v])=>[c,c===code?value:v])),steps.Sun,18,mode);
    for(const body of [sunPacket.filter(([c])=>c!==421),sunPacket.map(([c,v])=>[c,c===90?7:v]),[...sunPacket,[100,'AcDbSun']],[[100,'PrivateSun']],[]])add('variant/'+JSON.stringify(body),'Sun',sunRecord(body),steps.Sun,18,mode);
    const headers={identityMissing:[],identityRepeat:[[5,'F00'],[5,'F01']],ownerRepeat:[[5,'F00'],[330,h('port')],[330,h('port')]],private:[[5,'F00'],[102,'{PRIVATE'],[310,{bytes:[1,2]}],[102,'}']],reactors:[[5,'F00'],[102,'{ACAD_REACTORS'],[330,h('port')],[330,h('leaf')],[102,'}']],extension:[[5,'F00'],[102,'{ACAD_XDICTIONARY'],[360,h('ext')],[102,'}']],unclosed:[[5,'F00'],[102,'{PRIVATE']],nested:[[5,'F00'],[102,'{A'],[102,'{B'],[102,'}'],[102,'}']],badControl:[[5,'F00'],[102,'}']],badReactor:[[5,'F00'],[102,'{ACAD_REACTORS'],[360,h('leaf')],[102,'}']],badExtension:[[5,'F00'],[102,'{ACAD_XDICTIONARY'],[102,'}']]};
    for(const [name,header]of Object.entries(headers))add('header/'+name,'Sun',sunRecord(sunPacket,header),steps.Sun,18,mode);
    for(const host of ['port','view','viewport','line','leaf'])for(const version of [14,15,16,18]) {
      add(`owner/${host}/${version}`,'Sun',sunRecord(),[s('read'),{method:'register',owner:host},{method:'add-sun',owner:r(host),handle:h('item')},s('resolve'),{method:'write-ref',owner:r(host)},s('write')],version,mode);
    }
  }
  for(const kind of ['OutputSettings','GeoData','Sun']) {
    const packet=kind==='OutputSettings'?plotPacket:kind==='GeoData'?geoPacket():sunRecord();
    for(const at of [1,2,3,4,5,7,11,16,25,40,55,72,100])add('callback-throw/'+at,kind,packet,[...steps[kind].slice(0,kind==='Sun'?4:3),{method:'write',hooks:[{at,kind:'throw'}]}]);
    for(const action of ['ambiguous','unaccept','discard','replace','unregister']) {
      const victim=kind==='OutputSettings'?'leaf':kind==='GeoData'?'block':'port';
      const data=kind==='OutputSettings'?plotPacket.map(([c,v])=>[c,c===333?h('leaf'):v]):packet;
      add('source/'+action,kind,data,[...steps[kind].slice(0,kind==='Sun'?3:2),{method:'source',target:victim,action},s('resolve'),s('write')]);
    }
  }
  for(const length of [0,1,248,249,250,251,252,253,254,255,256,510,511,512,65536])for(const suffix of ['x','\\U+03A9','😀','^J','\\U+ZZZZ'])
    add(`split/${length}/${suffix}`,'GeoData',[],[{method:'split',text:'a'.repeat(length)+suffix}]);
  for(const definition of ['','Ω'.repeat(80),'a'.repeat(252)+'😀'+'東京','x'.repeat(255)+'^J','\\U+005CU+0041','\\U+D800'])for(const version of [13,16,18])add('definition-escape/'+version+'/'+definition,'GeoData',geoPacket({definition}),steps.GeoData,version);
  for(const [code,text]of [[302,'^J'],[305,'\\U+000A'],[306,'\\U+000D'],[307,'\\U+0000']])add('field-text/'+code,'GeoData',geoPacket().map(([c,v])=>[c,c===code?text:v]));
  for(const name of [null,'Other','ACAD_GEOGRAPHICDATA'])add('changed-alias/'+name,'GeoData',geoPacket(),[s('read'),steps.GeoData[1],s('resolve'),{method:'alias',name},s('resolve'),s('database-validate')]);
  for(const handle of [null,'','0','00','0000000000000000000000','0\0','00\0\0','0 ',' 0','0x0','-0','G','ffffffffffffffff','10000000000000000'])add('null-handle/'+JSON.stringify(handle),'Sun',[],[{method:'null-handle',handle}]);
  for(const owner of ['port','view','line'])for(const handle of ['0','00',null,'','F00'])add('empty-owner/'+owner+'/'+handle,'Sun',[],[{method:'add-sun',owner:r(owner),handle},s('resolve'),{method:'write-ref',owner:r(owner)}]);
  add('duplicate-owner','Sun',[],[{method:'add-sun',owner:r('port'),handle:'0'},{method:'add-sun',owner:r('port'),handle:'1'},s('resolve')]);
  add('unowned-typed','Sun',sunRecord(),[s('read'),{method:'register',owner:'port'},s('resolve')]);
  const sequences=[[[100,'AcDbViewport'],[361,'A']],[[100,'Private'],[361,'A'],[100,'AcDbViewport'],[361,'B']],[[102,'{A'],[100,'Private'],[102,'{B'],[361,'A'],[102,'}'],[102,'}'],[361,'B']],[[1001,'APP'],[100,'AcDbViewport'],[361,'A']],[[102,'}'],[361,'A']],[[102,'{APP'],[1001,'APP'],[102,'}'],[361,'A']]];
  for(let n=0;n<sequences.length;n++)add('public-context/'+n,'Sun',[],sequences[n].map(([code,value])=>({method:'observe',code,value})));
  for(const kind of ['Sun','GeoData'])for(const cpp of [kind==='Sun'?'AcDbSun':'AcDbGeoData','Other'])for(const entity of [false,true])for(const typed of [false,true]) {
    const name=kind==='Sun'?'SUN':'GEODATA',packet=kind==='Sun'?sunRecord():geoPacket();
    add(`class/${cpp}/${entity}/${typed}`,kind,packet,[...(typed?steps[kind].slice(0,2):[]),{method:'opaque',code:name},{method:'class',name,cpp,entity,count:99},s('prepare')]);
  }
  for(const [kind,target,property,value,at]of [['OutputSettings','settings','ViewName','changed',4],['OutputSettings','settings','ShadePlotObject',r('leaf'),64],['GeoData','item','DesignPoint',{vector:[7,8,9]},12],['Sun','item','TrueColor',null,8]])
    add(`callback-set/${property}`,kind,kind==='OutputSettings'?plotPacket:kind==='GeoData'?geoPacket():sunRecord(),[...steps[kind].slice(0,kind==='Sun'?4:3),{method:'write',hooks:[{at,kind:'set',target,property,value}]}]);
  for(const target of ['leaf','foreignLeaf','style','foreignStyle'])for(const version of [13,14,15,18])add(`layout-shade/${target}/${version}`,'OutputSettings',[],[{method:'set',target:'layoutPlot',property:'ShadePlotObject',value:r(target)},s('validate')],version);
  for(const tags of [[],[[100,'Wrong']],[[100,'AcDbPlotSettings'],[333,h('leaf')],[142,0]],[[100,'AcDbPlotSettings'],[90,0]],plotPacket])add('direct-parse/'+JSON.stringify(tags),'OutputSettings',tags,[s('parse'),{method:'write-plot'}]);
  for(const code of [93,96,97])add('geo-tag/'+code,'GeoData',[[93,0],[96,0]],[{method:'geo-tag',code},{method:'geo-tag',code}]);
  let state=0x61df8173;const rnd=()=>((state=(Math.imul(state,1664525)+1013904223)>>>0)/4294967296);
  for(let n=0;n<64;n++) {
    const packet=geoPacket({definition:'CRS-'+n+'^JΩ',points:3,faces:1}).map(([c,v])=>[c,[10,20,30,11,21,31,142,13,23,14,24].includes(c)?(rnd()-.5)*1e6:v]);
    add('random/'+n,'GeoData',packet);
  }
  return all;
}
