import { el } from './ui.mjs';
import { resourceName } from './resource-contracts.mjs';
const row=(label,node)=>el('label',{class:'form-row'},[el('span',{text:label}),node]);
const input=(label,type='text')=>el('input',{'aria-label':label,type});
/** LAS remains local text; previews fence every async step with the opened drawing receipt. */
export async function importLayerState({client,edit,dialog,editable,key,resources}) {
  const file=input('LAS file','file');file.accept='.las,text/plain';
  const name=input('Imported layer state name'),overwrite=input('Replace existing layer state','checkbox');
  const check=()=>{editable();if(client.key!==key||client.busy)throw new Error('The drawing changed during LAS import. Preview again.');};
  if(!await dialog('Import layer state',[row('LAS file',file),row('Name (blank keeps LAS name)',name),row('Replace existing state',overwrite),
    el('p',{class:'hint',text:'Preview validates a UTF-8 LAS file up to 2 MiB. Import stores a state without restoring drawing layers. Missing resources require explicit mappings or prior creation/loading.'})],'Preview LAS'))return;
  check();const selected=file.files?.[0];if(!selected)throw new Error('Choose a LAS file.');
  if(selected.size>2*1024*1024)throw new Error('LAS files are limited to 2 MiB.');
  const text=await selected.text();check();
  const options={...(name.value.trim()?{name:resourceName(name.value)}:{}),overwrite:overwrite.checked,layers:[],linetypes:[]};
  for(let attempt=0;attempt<16;attempt++){
    const preview=await client.read('layer-state/preview',{body:{revision:client.state.revision,text,options}});
    if(!preview||client.key!==key)return;check();
    const stateName=el('input',{'aria-label':'Preview layer state name',value:preview.name,maxLength:255});
    const replace=el('input',{'aria-label':'Preview replace existing state',type:'checkbox',checked:options.overwrite});
    const mappings=[];
    const children=[el('p',{text:`${preview.name}: ${preview.layerCount} saved layers · current layer ${preview.currentLayer}${preview.replacesExisting?' · replaces an existing state':''}`}),
      row('Imported name',stateName),row('Replace existing state',replace),el('p',{class:'hint',text:preview.notice})];
    const seen=new Set();
    for(const conflict of preview.conflicts||[]){
      const identity=`${conflict.kind}:${conflict.source}`.toLowerCase();if(seen.has(identity))continue;seen.add(identity);
      children.push(el('p',{class:'hint',text:conflict.message}));
      if(!['layer','linetype'].includes(conflict.kind))continue;
      const resourcesForKind=conflict.kind==='layer'?(resources.layers||[]).map(l=>l.name):(resources.linetypes||[]).map(l=>l.status.name).filter(n=>!['bylayer','byblock'].includes(n.toLowerCase()));
      const target=el('select',{'aria-label':`Map ${conflict.kind} ${conflict.source}`},[el('option',{value:'',text:'Choose existing resource'}),...resourcesForKind.map(n=>el('option',{value:n,text:n}))]);
      target.value='';children.push(row(`Map ${conflict.source}`,target));mappings.push({...conflict,target});
    }
    if(!await dialog('LAS import preview',children,preview.token?'Import saved state':'Preview mappings'))return;
    check();const renamed=resourceName(stateName.value),changed=renamed!==preview.name||replace.checked!==options.overwrite;
    if(preview.token&&!changed){await edit('layer-state/import',{token:preview.token},{expectedKey:key});return;}
    options.name=renamed;options.overwrite=replace.checked;
    for(const mapping of mappings){if(!mapping.target.value)continue;const key=mapping.kind==='layer'?'layers':'linetypes';options[key]=options[key].filter(m=>m.source.toLowerCase()!==mapping.source.toLowerCase());options[key].push({source:mapping.source,target:mapping.target.value});}
  }
  throw new Error('LAS preview limit reached. Reopen import to continue.');
}
