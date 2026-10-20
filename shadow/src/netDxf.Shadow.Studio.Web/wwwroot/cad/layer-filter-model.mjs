/** UI-local layer discovery only. These preferences never change native layer flags. */
export const layerFilterLimits=Object.freeze({filters:32,groups:32,rules:8,members:10000,pattern:255});
export const layerFilterFields=Object.freeze(['visible','frozen','locked','plot','frozenInNewViewports','used','current']);
export const layerId=value=>typeof value==='string'?value:value?.value;
const bounded=(value,max=255)=>typeof value==='string'?value.slice(0,max):'';
const tri=value=>['yes','no'].includes(value)?value:'any';
const number=value=>{
  if(value==null||typeof value==='string'&&!value.trim())return null;
  if(!['string','number'].includes(typeof value)||!Number.isFinite(Number(value)))throw new Error('Transparency filter must be a finite number.');
  return Number(value);
};
const name=value=>bounded(value,80).trim();

export function layerRule(value={}) {
  const result={name:bounded(value.name),description:bounded(value.description),linetype:bounded(value.linetype),
    lineweight:bounded(value.lineweight,16),color:bounded(value.color,16).toUpperCase(),
    transparencyMin:number(value.transparencyMin),transparencyMax:number(value.transparencyMax)};
  for(const field of layerFilterFields)result[field]=tri(value[field]);
  for(const field of ['transparencyMin','transparencyMax'])if(result[field]!==null&&(result[field]<0||result[field]>90))throw new Error('Transparency filter must be between 0 and 90.');
  if(result.transparencyMin!==null&&result.transparencyMax!==null&&result.transparencyMin>result.transparencyMax)throw new Error('Transparency filter minimum exceeds maximum.');
  if(result.color&&!/^(?:#[0-9A-F]{6}|(?:[1-9]\d{0,2}))$/.test(result.color))throw new Error('Filter color uses #RRGGBB or indexed color 1–255.');
  if(result.color[0]!=='#'&&Number(result.color)>255)throw new Error('Indexed filter colors are 1–255.');
  return result;
}
/** Small wildcard grammar: * any text, ? one character, commas OR alternatives, ~ excludes an alternative. */
export function layerPattern(value,pattern) {
  if(!pattern)return true;
  const text=String(value??'').slice(0,4096).toLowerCase();
  return bounded(pattern).split(',').slice(0,32).some(term=>{
    const inverted=term.startsWith('~');if(inverted)term=term.slice(1);term=term.toLowerCase();
    // Greedy wildcard matching avoids arbitrary regex evaluation/backtracking.
    let t=0,p=0,star=-1,retry=0;
    while(t<text.length) {
      if(p<term.length&&(term[p]==='?'||term[p]===text[t])){t++;p++;}
      else if(term[p]==='*'){star=p++;retry=t;}
      else if(star>=0){p=star+1;t=++retry;}
      else return inverted;
    }
    while(term[p]==='*')p++;
    return (p===term.length)!==inverted;
  });
}
export function layerMatches(layer,rule,status={}) {
  return matches(layer,layerRule(rule),status);
}
function matches(layer,rule,status={}) {
  for(const field of ['name','description','linetype'])if(!layerPattern(layer[field],rule[field]))return false;
  if(rule.lineweight&&layer.lineweight!==rule.lineweight)return false;
  if(rule.color&&(rule.color[0]==='#'?String(layer.color).toUpperCase()!==rule.color:layer.trueColor||layer.colorIndex!==Number(rule.color)))return false;
  if(rule.transparencyMin!==null&&layer.transparency<rule.transparencyMin||rule.transparencyMax!==null&&layer.transparency>rule.transparencyMax)return false;
  for(const field of layerFilterFields) {
    const flag=field==='used'?status.uses>0:field==='current'?status.current===true:layer[field]===true;
    if(rule[field]!=='any'&&flag!==(rule[field]==='yes'))return false;
  }
  return true;
}
export function filterLayers(layers,{rules=[{}],members=null,invert=false,search=''}={},statuses=[]) {
  if(!Array.isArray(layers)||layers.length>10000)throw new Error('Layer filtering supports at most 10000 layers.');
  const byId=new Map(statuses.map(status=>[layerId(status.id),status]));
  const ids=members===null?null:new Set(members.slice(0,layerFilterLimits.members).map(layerId));
  const normalized=rules.slice(0,layerFilterLimits.rules).map(layerRule),query=bounded(search).toLocaleLowerCase();
  return layers.filter(layer=>{
    const match=ids?ids.has(layerId(layer.id)):normalized.some(rule=>matches(layer,rule,byId.get(layerId(layer.id))));
    return (match!==!!invert)&&(!query||`${layer.name} ${layer.description||''}`.toLocaleLowerCase().includes(query));
  });
}
export function layerFilterPreferences(value,layers=[]) {
  const result={version:1,filters:[],groups:[]};
  if(value?.version!==1)return result;
  const byId=new Map(layers.map(layer=>[layerId(layer.id),layer]));
  const seen=new Set();
  for(const item of (Array.isArray(value.filters)?value.filters:[]).slice(0,32)) {
    const label=name(item?.name),key=label.toLocaleLowerCase();if(!label||seen.has(key)||!Array.isArray(item.rules))continue;
    try {const rules=item.rules.slice(0,8).map(layerRule);if(!rules.length)continue;result.filters.push({name:label,rules});seen.add(key);}catch{/* Invalid saved predicates never become broader filters. */}
  }
  seen.clear();
  for(const item of (Array.isArray(value.groups)?value.groups:[]).slice(0,32)) {
    const label=name(item?.name),key=label.toLocaleLowerCase();if(!label||seen.has(key)||!Array.isArray(item.members))continue;
    const members=[],ids=new Set();
    for(const source of item.members.slice(0,10000)) {
      const id=layerId(source?.id),layer=byId.get(id);
      if(!layer||ids.has(id))continue;ids.add(id);members.push({id,name:layer.name});
    }
    result.groups.push({name:label,members});seen.add(key);
  }
  return result;
}
export function saveLayerFilter(value,kind,label,content,layers) {
  const result=layerFilterPreferences(value,layers),key=kind==='group'?'groups':kind==='filter'?'filters':null;
  if(!key)throw new Error('Choose a layer property filter or group.');
  if(!Array.isArray(content)||content.length>(key==='groups'?10000:8)||key==='filters'&&!content.length)throw new Error('Use 1–8 filter rules or at most 10000 group members.');
  label=name(label);if(!label)throw new Error('Enter a filter or group name.');
  const index=result[key].findIndex(item=>item.name.toLocaleLowerCase()===label.toLocaleLowerCase());
  if(index<0&&result[key].length>=32)throw new Error('Keep at most 32 layer filters and 32 groups.');
  const item=key==='groups'?{name:label,members:[...new Set(content.map(layerId))].map(id=>({id}))}:
    {name:label,rules:content.map(layerRule)};
  if(index<0)result[key].push(item);else result[key][index]=item;
  return layerFilterPreferences(result,layers);
}
