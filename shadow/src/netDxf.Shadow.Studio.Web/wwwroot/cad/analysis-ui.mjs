import { el } from './ui.mjs';
import { fit } from './core.mjs';
import { healthOptions,moveProposal,healthSummary,previewCurrent,visibleIssues } from './analysis-contracts.mjs';

/** Source-backed health and edit review. The host decides which palette renders these functions. */
export function analysisTools({client,edit,viewport,report,editable}){
  if(!document.querySelector('link[data-cad-analysis]'))document.head.append(el('link',{rel:'stylesheet',href:new URL('./analysis.css',import.meta.url).href,'data-cad-analysis':'1'}));
  let healthReceipt=null,healthKey='',healthFeedback='',epoch=0,previewUrls=[],refreshHealth=null;const values={tinyLength:1e-7,gapTolerance:.01,maximumIssues:500};
  const action=(label,fn,disabled=false)=>el('button',{type:'button',text:label,disabled,onclick:()=>Promise.resolve().then(fn).catch(report)});
  const row=(label,control)=>el('label',{class:'property-row'},[el('span',{text:label}),control]);
  const number=(label,value,change)=>el('input',{type:'number',step:'any',value,'aria-label':label,oninput:e=>change(e.target.value)});
  function health(container){
    const generation=++epoch;container.replaceChildren();
    const output=el('div',{'aria-live':'polite'}),search=el('input',{type:'search',placeholder:'Filter findings','aria-label':'Filter health findings'}),severity=el('select',{'aria-label':'Health severity'},['all','error','warning','information'].map(value=>el('option',{value,text:value})));
    const run=action('Check drawing',async()=>{editable();const key=client.key;run.disabled=true;try{const response=await client.read('health',{body:{revision:client.state.revision,options:healthOptions(values)}});if(generation!==epoch||key!==client.key||!response)return;healthReceipt=response;healthKey=key;healthFeedback='';show();}finally{run.disabled=false;}});
    container.append(el('p',{class:'intro',text:'Review invalid geometry, exact duplicates, small endpoint gaps, unused definitions and font references. Checks never change source objects.'}),
      row('Tiny length',number('Tiny length',values.tinyLength,v=>values.tinyLength=v)),row('Gap tolerance',number('Gap tolerance',values.gapTolerance,v=>values.gapTolerance=v)),row('Maximum findings',number('Maximum findings',values.maximumIssues,v=>values.maximumIssues=v)),run,el('div',{class:'analysis-filters'},[search,severity]),output);
    refreshHealth=()=>{if(generation===epoch&&output.isConnected)show();};
    search.addEventListener('input',show);severity.addEventListener('change',show);show();
    function show(){output.replaceChildren();if(!healthReceipt||healthKey!==client.key){output.append(el('p',{class:'hint',text:'Run checks for the current drawing revision.'}));return;}
      const data=healthReceipt.report,summary=healthSummary(data);output.append(el('p',{class:summary.complete?'hint':'diagnostic',text:`${summary.errors} errors · ${summary.warnings} warnings · ${summary.information} information${summary.complete?'':' · incomplete: a check budget was reached'}`}));
      if(healthFeedback)output.append(el('p',{class:'hint',text:healthFeedback}));
      const items=visibleIssues(data,search.value,severity.value);if(!items.length)output.append(el('p',{text:'No findings match the selected filter.'}));
      for(const issue of items){const card=el('section',{class:'analysis-issue'},[el('b',{text:issue.code}),el('p',{text:issue.message}),el('small',{text:[issue.object?.value,issue.path].filter(Boolean).join(' · ')})]);
        card.append(action('Select and focus',async()=>{editable();if(healthKey!==client.key)throw new Error('The drawing changed. Run the checks again.');const selectedReceipt=healthReceipt,response=await edit('health/select',{reportId:selectedReceipt.reportId,issueId:issue.id},{expectedKey:healthKey});if(!response?.cad||healthReceipt!==selectedReceipt)return;client.adopt(response.cad);healthKey=client.key;healthFeedback=response.message||'Selection updated.';if(response.bounds){const camera=fit(response.bounds,viewport.size);Object.assign(viewport.camera,camera);viewport.changed();}refreshHealth?.();}));output.append(card);}
      output.append(el('details',{},[el('summary',{text:'Check coverage and limitations'}),...data.coverage.map(text=>el('p',{class:'hint',text})),...data.limitations.map(text=>el('p',{class:'hint',text}))]));
    }
  }
  function clearUrls(){for(const url of previewUrls)URL.revokeObjectURL(url);previewUrls=[];}
  function previews(container){
    const generation=++epoch;refreshHealth=null;clearUrls();container.replaceChildren();const output=el('div',{'aria-live':'polite'});let delta=[0,0,0],loadGeneration=0;
    const refresh=action('Refresh proposals',load);container.append(el('p',{class:'intro',text:'Inspect proposed agent or drawing edits before applying. All operations in one proposal become one undo command. Native source validation runs on Apply.'}),refresh,
      el('details',{},[el('summary',{text:'Preview a move of the current selection'}),...['X','Y','Z'].map((axis,i)=>row(axis,number(`Preview move ${axis}`,0,value=>delta[i]=value))),action('Create move preview',async()=>{editable();await propose(moveProposal(...delta));await load();})]),output);load().catch(report);
    async function load(){
      const request=++loadGeneration,key=client.key;
      const valid=()=>request===loadGeneration&&generation===epoch&&key===client.key&&output.isConnected;
      refresh.disabled=true;
      try{const data=await client.read('edit-previews');if(!valid()||!data)return;clearUrls();output.replaceChildren();if(!data.length)output.append(el('p',{class:'hint',text:'No pending edit proposals. Agents can create typed proposals through the edit-preview tool.'}));for(const item of data){if(!valid())return;await show(item,key,valid);}}
      catch(error){if(valid())throw error;}
      finally{if(request===loadGeneration)refresh.disabled=false;}
    }
    async function show(item,key,valid){const current=previewCurrent(item,client.state),summary=item.summary,after=healthSummary(item.afterHealth),before=healthSummary(item.beforeHealth),card=el('section',{class:'analysis-preview'},[
      el('b',{text:item.description}),el('p',{text:`${summary.added} added · ${summary.changed} changed · ${summary.removed} removed source objects`}),el('p',{class:'hint',text:item.validation}),el('p',{class:after.errors?'diagnostic':'hint',text:`Health errors: ${before.errors} → ${after.errors}; warnings: ${before.warnings} → ${after.warnings}${after.complete&&before.complete?'':' (checks incomplete)'}`})]);output.append(card);
      if(!current)card.append(el('p',{class:'diagnostic',text:'This proposal is stale. Recreate it from the current drawing, selection and view.'}));
      for(const library of item.blockLibraries||[]){
        const details=el('details',{},[el('summary',{text:`${library.name}: resource decisions${library.detailsTruncated?' (first 500)':''}`})]);
        for(const resource of library.resources)details.append(el('p',{class:'hint',text:`${resource.action} ${resource.table}: ${resource.sourceName} → ${resource.targetName}`}));
        card.append(details);
      }
      for(const sync of item.attributeSynchronizations||[]){
        card.append(el('p',{class:'attribute-warning',text:`${sync.blockName}: ${sync.referenceCount} block references. ${sync.retained} attributes reset to definition formatting/properties; assigned values retained. ${sync.added} added, ${sync.removed} removed. ${sync.removedXData} INSERT XData records deleted. ${sync.textConversions} text mode conversions.`}));
        const details=el('details',{},[el('summary',{text:`Attribute reset details${sync.detailsTruncated?' (truncated; inspect source objects for complete values)':''}`})]);
        for(const reference of sync.references||[]){
          details.append(el('p',{class:'hint',text:`INSERT ${reference.insert.value}`}));
          for(const removed of reference.removed||[])details.append(el('p',{text:`Remove ${removed.tag} (${removed.attribute.value}): ${removed.multilineValue??removed.fallbackValue}${removed.multilineValue!=null?` · fallback: ${removed.fallbackValue}`:''}`}));
          if(reference.removedXData?.length)details.append(el('p',{text:`Delete XData: ${reference.removedXData.join(', ')}`}));
          for(const conversion of reference.textConversions||[])details.append(el('p',{text:conversion}));
        }
        card.append(details);
      }
      const images=el('div',{class:'analysis-comparison'});card.append(images);
      if(current)for(const [label,isAfter]of [['Before',false],['After',true]]){const figure=el('figure',{},[el('figcaption',{text:label})]);images.append(figure);try{const blob=await client.read(`edit-preview/render?${new URLSearchParams({previewId:item.previewId,after:String(isAfter),width:'640',height:'400',backend:'svg'})}`,{blob:true});if(!valid()||!blob)return;const url=URL.createObjectURL(blob);previewUrls.push(url);figure.append(el('img',{src:url,alt:`${label}: ${item.description}`}));}catch(error){if(!valid())return;figure.append(el('p',{class:'diagnostic',text:`Preview unavailable: ${error.message}`}));}}
      if(!valid())return;
      if(current)for(const library of item.blockLibraries||[]){
        const figure=el('figure',{},[el('figcaption',{text:`Definition: ${library.name}`})]);images.append(figure);
        try{const blob=await client.read(`edit-preview/render?${new URLSearchParams({previewId:item.previewId,after:'true',block:library.block.value,width:'640',height:'400',backend:'svg'})}`,{blob:true});
          if(!valid()||!blob)return;const url=URL.createObjectURL(blob);previewUrls.push(url);figure.append(el('img',{src:url,alt:`Imported definition: ${library.name}`}));
        }catch(error){if(!valid())return;figure.append(el('p',{class:'diagnostic',text:`Definition preview unavailable: ${error.message}`}));}
      }
      if(!valid())return;
      card.append(el('details',{},[el('summary',{text:`Source changes${summary.detailsTruncated?' (details truncated)':''}`}),...summary.details.map(change=>el('p',{class:'hint',text:`${change.kind} ${change.type} ${change.object.value}: ${change.properties.join(', ')}`}))]),el('div',{class:'toolbar'},[
        action('Apply as one edit',async()=>{editable();await edit('edit-preview/apply',{previewId:item.previewId},{expectedKey:key});await load();},!current),
        action('Discard',async()=>{await edit('edit-preview/discard',{previewId:item.previewId},{expectedKey:key});await load();})]));
    }
  }
  function workflows(container){
    const generation=++epoch;refreshHealth=null;clearUrls();const name=el('input',{value:'Drawing workflow','aria-label':'Workflow name',maxLength:200}),status=el('div',{'aria-live':'polite'}),results=el('div',{'aria-live':'polite'}),upload=el('input',{type:'file',accept:'.json,application/json','aria-label':'Import workflow JSON'});
    let saved=null;
    async function refresh(){const data=await client.read('workflow/status');if(generation!==epoch||!data)return;status.replaceChildren(el('p',{text:`${data.recording?'Recording':'Stopped'} · ${data.steps} successful steps${data.name?' · '+data.name:''}`}));if(data.error)status.append(el('p',{class:'diagnostic',text:data.error}));}
    async function replay(scenario){editable();results.replaceChildren(el('p',{text:'Replaying in an isolated drawing…'}));const result=await edit('workflow/replay',{scenario});if(generation!==epoch||!result)return;results.replaceChildren(el('p',{class:result.passed?'hint':'diagnostic',text:`${result.passed?'Passed':'Failed'} · ${result.steps.length} steps${result.error?' · '+result.error:''}`}));for(const step of result.steps){const card=el('section',{class:'analysis-issue'},[el('b',{text:`${step.index+1}. ${step.operation} · ${step.passed?'passed':'failed'}`})]);if(step.error)card.append(el('p',{text:step.error}));for(const diff of step.differences)card.append(el('p',{text:`${diff.path}: expected ${diff.expected}; actual ${diff.actual}`}));results.append(card);}}
    upload.addEventListener('change',()=>Promise.resolve().then(async()=>{const file=upload.files[0];if(!file)return;if(file.size>32*1024*1024)throw new Error('Workflow files are limited to 32 MiB.');saved=JSON.parse(await file.text());await replay(saved);}).catch(report));
    container.replaceChildren(el('p',{class:'intro',text:'Record successful drawing operations and replay them in a separate drawing. Each step checks source geometry, resources, selection and parsed SVG geometry. No agent provider is called.'}),row('Recording name',name),
      el('div',{class:'toolbar'},[action('Start recording',async()=>{editable();const value=await edit('workflow/start',{name:name.value});if(value?.cad)client.adopt(value.cad);await refresh();}),action('Stop',async()=>{await edit('workflow/stop');await refresh();}),action('Refresh status',refresh)]),status,
      el('p',{class:'hint',text:'The baseline contains local DXF data. Recording start uses normal DXF export synchronization. Unsupported operations stop recording explicitly; earlier steps remain exportable. Workflows require matching SHX resources and modules.'}),
      el('div',{class:'toolbar'},[action('Export recording',async()=>{saved=await client.read('workflow/export');if(!saved)return;const blob=new Blob([JSON.stringify(saved,null,2)],{type:'application/json'}),url=URL.createObjectURL(blob),a=el('a',{href:url,download:'drawing-workflow.json'});a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}),action('Replay recording',async()=>{saved=await client.read('workflow/export');if(saved)await replay(saved);})]),row('Import and replay',upload),
      el('details',{},[el('summary',{text:'Record a DXF save/reload check'}),el('p',{class:'hint',text:'This saves and reloads the current live drawing. Reload clears its undo history and selection; the recorded replay runs the same operation.'}),action('Save and reload DXF',async()=>{await edit('workflow/roundtrip');await refresh();})]),results);refresh().catch(report);
  }
  async function propose(proposal){editable();return edit('edit-preview',proposal,{expectedKey:client.key});}
  return {health,previews,workflows,propose,destroy(){epoch++;refreshHealth=null;clearUrls();}};
}
