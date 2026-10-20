export const scheduleStreams=Object.freeze(['rows','groups','issues']);
export const scheduleOffsets=Object.freeze({rows:'rowOffset',groups:'groupOffset',issues:'issueOffset'});
const id=value=>typeof value==='string'?value:value?.value;
export function scheduleRequest(module,values,state,previous=null) {
  const options={query:String(values.query??''),class:String(values.class??''),groupBy:String(values.groupBy??'class').trim(),
    groupValue:values.groupValue==null?null:String(values.groupValue),sortBy:String(values.sortBy??'tag').trim(),
    descending:values.descending===true,maximumObjects:Number(values.maximumObjects??10000)};
  if(options.query.length>256||options.class.length>128||options.groupValue?.length>8192||
    !options.groupBy||options.groupBy.length>128||!options.sortBy||options.sortBy.length>128||
    /[\u0000-\u001f\u007f]/.test(options.groupBy+options.sortBy)||
    !Number.isSafeInteger(options.maximumObjects)||options.maximumObjects<1||options.maximumObjects>100000)throw new Error('Invalid schedule query, field or scan limit.');
  const request={revision:state.revision,module,options,limit:100};
  if(previous){request.scheduleKey=previous.scheduleKey;for(const name of scheduleStreams)request[scheduleOffsets[name]]=previous[name].length;}
  return request;
}
const receiptKey=data=>JSON.stringify([data.scheduleKey,data.documentId,data.workspaceId,data.viewVersion,data.revision,data.layout,data.module,data.drawingUnits,data.options,data.complete,data.scanComplete,data.objectsVisited,data.recordsScanned,data.invalidRecords,data.limitations]);
export function mergeSchedule(previous,data) {
  if(!data||typeof data.scheduleKey!=='string'||!data.scheduleKey||previous&&receiptKey(previous)!==receiptKey(data))throw new Error('Schedule identity or query changed. Refresh the schedule.');
  const result={...data,totals:{},next:{}};
  for(const name of scheduleStreams) {
    const page=data[name],old=previous?.[name]||[],cap=name==='issues'?100:10000;
    if(!page||!Array.isArray(page.items)||!Number.isSafeInteger(page.total)||page.total<0||page.total>cap||page.offset!==old.length||
      page.offset+page.items.length>page.total||previous&&page.total!==previous.totals[name]||
      (page.nextOffset==null?page.offset+page.items.length!==page.total:page.nextOffset!==page.offset+page.items.length||page.nextOffset<=page.offset||page.nextOffset>=page.total))
      throw new Error(`Invalid schedule ${name} page.`);
    result[name]=[...old,...page.items];result.totals[name]=page.total;result.next[name]=page.nextOffset??null;
    const keys=result[name].map(row=>name==='groups'?row.value:id(row.entity));
    if(keys.some(k=>typeof k!=='string')||new Set(keys).size!==keys.length)throw new Error(`Duplicate or invalid schedule ${name} identity.`);
  }
  for(const row of data.rows.items)if(typeof row.tag!=='string'||typeof row.class!=='string'||!row.properties||typeof row.properties!=='object'||Array.isArray(row.properties)||
    !Array.isArray(row.ports)||!Array.isArray(row.references)||row.segmentLength!=null&&(!Number.isFinite(row.segmentLength)||row.segmentLength<0))throw new Error('Invalid discipline schedule row.');
  for(const row of data.groups.items)if(!Number.isSafeInteger(row.objects)||row.objects<1||!Number.isSafeInteger(row.routes)||row.routes<0||
    !Number.isSafeInteger(row.measuredSegments)||row.measuredSegments<0||!Number.isFinite(row.segmentLength)||row.segmentLength<0)throw new Error('Invalid discipline schedule group.');
  result.partial=scheduleStreams.some(name=>result[name].length<result.totals[name]);return result;
}
export function scheduleSelection(rows,activeRows=[]) {
  const roots=new Set(activeRows.map(row=>id(row.id))),entities=[...new Set(rows.map(row=>id(row.entity)))];
  return !entities.length||entities.length>500||entities.some(value=>!roots.has(value))?
    {entities:[],reason:'Select 1–500 source objects present in the active scene root list. Hidden or unloaded roots remain inspectable.'}:{entities,reason:null};
}
export function scheduleField(row,name) {
  const field=name.toLowerCase(),builtins={tag:row.tag,class:row.class,entity:id(row.entity),key:row.key,type:row.type,symbolid:row.symbolId,symbolversion:row.symbolVersion};
  return Object.hasOwn(builtins,field)?builtins[field]??'':Object.entries(row.properties||{}).find(([key])=>key.toLowerCase()===field)?.[1]??'';
}
export function scheduleCsv(scene,kind='rows') {
  if(!['rows','groups'].includes(kind))throw new Error('Choose rows or groups.');
  const quote=value=>{const text=String(value??'');return '"'+(typeof value==='string'&&/^[\s]*[=+@-]/.test(text)?"'":'')+text.replaceAll('"','""')+'"';};
  const meta=['scheduleKey','documentId','workspaceId','revision','layout','module','drawingUnits','scanComplete','complete','pagesComplete','loadedRecords','totalRecords','groupBy','query'];
  const prefix=[scene.scheduleKey,scene.documentId,scene.workspaceId,scene.revision,scene.layout,scene.module,scene.drawingUnits,scene.scanComplete,scene.complete,!scene.partial,scene[kind].length,scene.totals[kind],scene.options.groupBy,JSON.stringify(scene.options)];
  const fields=kind==='groups'?['value','objects','routes','measuredSegments','segmentLength']:['entity','type','class','key','tag','symbolId','symbolVersion','segmentLength','properties','ports','references'];
  return [[...meta,...fields],...scene[kind].map(row=>[...prefix,...fields.map(key=>key==='entity'?id(row.entity):typeof row[key]==='object'?JSON.stringify(row[key]):row[key])])]
    .map(row=>row.map(quote).join(',')).join('\r\n');
}
