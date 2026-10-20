import test from 'node:test';
import assert from 'node:assert/strict';
import {layerPattern,layerRule,filterLayers,layerFilterPreferences,saveLayerFilter} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/layer-filter-model.mjs';

const layers=[
  {id:{value:'a'},name:'A-Pipes',description:'Supply',visible:true,frozen:false,locked:false,plot:true,frozenInNewViewports:false,linetype:'DASHED',lineweight:'W25',color:'#FF0000',colorIndex:1,trueColor:false,transparency:30},
  {id:{value:'b'},name:'A-Text',description:'Notes',visible:false,frozen:true,locked:true,plot:false,frozenInNewViewports:true,linetype:'Continuous',lineweight:'Default',color:'#123456',colorIndex:7,trueColor:true,transparency:0},
  {id:{value:'c'},name:'E-Pipes',description:'Return',visible:true,frozen:false,locked:true,plot:true,frozenInNewViewports:false,linetype:'Continuous',lineweight:'W25',color:'#FF0000',colorIndex:1,trueColor:false,transparency:60},
];
const statuses=[{id:'a',uses:3,current:true},{id:'b',uses:0,current:false},{id:'c',uses:1,current:false}];
const ids=result=>result.map(layer=>layer.id.value);

test('wildcards are case insensitive, bounded and literal outside the documented grammar',()=>{
  assert.equal(layerPattern('A-Pipes','a-*'),true);
  assert.equal(layerPattern('A-Pipes','?-Pipes,E-*'),true);
  assert.equal(layerPattern('A-Pipes','~A-*'),false);
  assert.equal(layerPattern('A-Pipes','~E-*'),true);
  assert.equal(layerPattern('A[0].Pipes','A[0].*'),true);
  assert.equal(layerPattern('A0xPipes','A[0].*'),false);
  assert.equal(layerPattern('a'.repeat(4096)+'b','*a'.repeat(100)+'x'),false);
});
test('predicates within a rule AND and separate rules OR with exact property semantics',()=>{
  assert.deepEqual(ids(filterLayers(layers,{rules:[{name:'*-Pipes',visible:'yes',locked:'no',plot:'yes',linetype:'DASH*',lineweight:'W25',color:'1',transparencyMin:20,transparencyMax:40,used:'yes',current:'yes'}]},statuses)),['a']);
  assert.deepEqual(ids(filterLayers(layers,{rules:[{frozen:'yes',locked:'yes',plot:'no',frozenInNewViewports:'yes',color:'#123456',used:'no'},{description:'Return',transparencyMin:60}]},statuses)),['b','c']);
  assert.deepEqual(ids(filterLayers(layers,{rules:[{color:'7'}]},statuses)),[]);
  assert.deepEqual(ids(filterLayers(layers,{rules:[{color:'#ff0000'}]},statuses)),['a','c']);
});
test('inversion applies to the chosen filter/group before the secondary substring search',()=>{
  assert.deepEqual(ids(filterLayers(layers,{rules:[{name:'A-*'}],invert:true},statuses)),['c']);
  assert.deepEqual(ids(filterLayers(layers,{members:['b'],invert:true,search:'supply'},statuses)),['a']);
  assert.deepEqual(ids(filterLayers(layers,{members:[]},statuses)),[]);
});
test('groups follow stable IDs across rename and drop deleted IDs without matching replacement names',()=>{
  const saved=saveLayerFilter(null,'group','Selected',['a','b','a','missing'],layers);
  assert.deepEqual(saved.groups[0].members,[{id:'a',name:'A-Pipes'},{id:'b',name:'A-Text'}]);
  const changed=[{...layers[0],name:'Renamed'},{...layers[1],id:{value:'replacement'}}];
  assert.deepEqual(layerFilterPreferences(saved,changed).groups[0].members,[{id:'a',name:'Renamed'}]);
  assert.deepEqual(filterLayers(changed,{members:['b']}),[]);
});
test('saved filters are bounded, reject invalid rules, strip payloads and replace names case insensitively',()=>{
  let saved=saveLayerFilter(null,'filter',' Visible ',[{visible:'yes'}],layers);
  saved=saveLayerFilter(saved,'filter','visible',[{locked:'no'}],layers);
  assert.equal(saved.filters.length,1);
  assert.equal(saved.filters[0].rules[0].locked,'no');
  const malformed={version:1,filters:[{name:'Bad',rules:[{color:'256'}]},{name:'OK',rules:[{name:'A-*',apiKey:'secret'}]}],groups:[],apiKey:'secret'};
  const normalized=layerFilterPreferences(malformed,layers);
  assert.equal(normalized.filters.length,1);assert.equal(JSON.stringify(normalized).includes('secret'),false);
  assert.deepEqual(layerFilterPreferences({version:99,filters:[{}]},layers),{version:1,filters:[],groups:[]});
  assert.equal(layerFilterPreferences({version:1,filters:Array.from({length:100},(_,i)=>({name:String(i),rules:[{}]}))},layers).filters.length,32);
  assert.throws(()=>saveLayerFilter(null,'filter','Too many',Array(9).fill({}),layers),/1–8/);
  assert.throws(()=>saveLayerFilter(null,'filter','Empty',[],layers),/1–8/);
});
test('invalid transparency and color cannot silently become a broader filter',()=>{
  for(const rule of [{transparencyMin:-1},{transparencyMax:91},{transparencyMin:'oops'},{transparencyMax:Infinity},{transparencyMin:30,transparencyMax:20},{color:'#abc'},{color:'0'},{color:'256'}])assert.throws(()=>layerRule(rule));
  assert.throws(()=>filterLayers(Array(10001).fill(layers[0])),/10000/);
});
