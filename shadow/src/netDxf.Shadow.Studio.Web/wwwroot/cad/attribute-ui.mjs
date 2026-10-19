import {el} from './ui.mjs';
import {lineweights} from './resource-contracts.mjs';
import {attributeAlignments,attributeChanges,attributeId} from './attribute-contracts.mjs';
import {definitionBatch} from './attribute-definition-contracts.mjs';

/** Shared native/WASM instance editor. Apply and OK each submit one atomic batch. */
export function attributeTools({client,edit,dialog,editable,definitionBlock=null}) {
  return async function open() {
    editable();if(client.busy)throw new Error('Finish the current edit before editing attributes.');
    const manager=definitionBlock!==null;
    const readPath=revision=>manager?`attribute-definitions?revision=${revision}&block=${encodeURIComponent(definitionBlock)}`:`attributes?revision=${revision}`;
    const rows=data=>manager?(data.definitions||[]).map(d=>({...d.appearance,verify:d.verify,preset:d.preset})):data.attributes||[];
    let key=client.key;
    const [initial,resources]=await Promise.all([client.read(readPath(client.state.revision)),client.read('resources')]);
    if(!initial||!resources||key!==client.key)return;
    let data=initial,attributes=rows(data),originalAttributes=attributes.slice();
    if(!attributes.length)throw new Error(manager?'This definition has no attributes. Use ATTDEF to create one.':'The selected block has no instance attributes.');
    const id=attributeId(manager?data.block:data.insert),drafts=new Map(),controls=new Map(),removed=new Set();
    const list=el('select',{'aria-label':'Block attributes',size:12}),search=el('input',{'aria-label':'Find attributes',type:'search'});
    const details=el('p',{class:'hint'}),warning=el('p',{class:'attribute-warning',role:'status'}),status=el('p',{class:'hint',role:'status'});
    const panels=['Attribute','Text options','Properties'].map(name=>el('section',{role:'tabpanel','aria-label':name}));
    let selected=null,tab=0,applying=false,closed=false,applyTask=null;
    const tabs=panels.map((panel,index)=>el('button',{type:'button',role:'tab',text:['Attribute','Text options','Properties'][index],id:`attribute-tab-${index}`,
      onclick:()=>showTab(index),onkeydown:event=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?2:(tab+(event.key==='ArrowRight'?1:2))%3;showTab(next);tabs[next].focus();}}}));
    function showTab(index){tab=index;panels.forEach((p,i)=>{p.hidden=i!==index;p.setAttribute('aria-labelledby',`attribute-tab-${i}`);tabs[i].setAttribute('aria-selected',String(i===index));tabs[i].setAttribute('tabindex',i===index?'0':'-1');});}
    const row=(name,node)=>el('label',{class:'form-row'},[el('span',{text:name}),node]);
    function remember(){if(selected){const values={};for(const [name,node]of controls)values[name]=node.getAttribute('type')==='checkbox'?node.checked:node.value;drafts.set(attributeId(selected.id),values);}}
    function field(panel,key,label,type='text',options=null){
      const value=drafts.get(attributeId(selected.id))?.[key]??selected[key];
      const node=options?el('select',{'aria-label':label},options.map(v=>el('option',{value:v,text:v}))):el(key==='value'?'textarea':'input',{'aria-label':label,type:key==='value'?undefined:type});
      if(type==='checkbox')node.checked=!!value;else node.value=String(value??'');
      if(key==='value'){node.rows=selected.multiline?6:2;node.maxLength=1_000_000;node.readOnly=!manager&&selected.constant;}
      if(type==='number')node.step='any';controls.set(key,node);panel.append(row(label,node));return node;
    }
    const names=(table,current)=>[...new Set([current,...(resources[table]||[]).map(r=>r.name??r.status?.name)])].filter(Boolean);
    function choose(attribute){
      selected=attribute;controls.clear();panels.forEach(p=>p.replaceChildren());
      if(!attribute){details.textContent='No matching attributes.';warning.textContent='';if(manager)updateButtons();return;}
      list.value=attributeId(attribute.id);details.textContent=`${manager?data.name:data.blockName} · ${attribute.tag} · ${attribute.multiline?'multiline':'single-line'}${attribute.constant?' · constant':''}`;
      warning.textContent=attribute.duplicateTag?'Duplicate tag: changes target this attribute by its object identity.':'';
      if(manager){field(panels[0],'tag','Attribute tag');field(panels[0],'prompt','Attribute prompt');field(panels[0],'verify','Verify attribute','checkbox');field(panels[0],'preset','Preset attribute','checkbox');
        panels[0].append(el('p',{class:'hint',text:`Constant: ${attribute.constant?'Yes':'No'} · Multiple lines: ${attribute.multiline?'Yes':'No'} (set when created)`}));}
      else panels[0].append(el('p',{class:'hint',text:`Tag: ${attribute.tag}\nPrompt: ${attribute.prompt||'—'}`}));
      field(panels[0],'value',manager?'Attribute default':attribute.multiline?'Multiline attribute value':'Attribute value');
      if(attribute.multiline)panels[0].append(el('p',{class:'hint',text:'MTEXT formatting controls are preserved. New lines become paragraph breaks. The single-line fallback value is retained.'}));
      field(panels[1],'textStyle','Attribute text style','text',names('textStyles',attribute.textStyle));
      field(panels[1],'alignment','Attribute justification','text',attributeAlignments.slice(0,attribute.multiline?9:15));
      field(panels[1],'height','Attribute height','number');field(panels[1],'rotation','Attribute rotation','number');
      field(panels[1],'width',attribute.multiline?'Attribute boundary width':'Attribute fit width','number');
      if(attribute.multiline){field(panels[1],'lineSpacing','Attribute line spacing','number');field(panels[1],'exactSpacing','Exact attribute line spacing','checkbox');}
      else{field(panels[1],'widthFactor','Attribute width factor','number');field(panels[1],'obliqueAngle','Attribute oblique angle','number');field(panels[1],'backward','Attribute backwards','checkbox');field(panels[1],'upsideDown','Attribute upside down','checkbox');}
      field(panels[2],'layer','Attribute layer','text',names('layers',attribute.layer));field(panels[2],'linetype','Attribute linetype','text',names('linetypes',attribute.linetype));
      field(panels[2],'lineweight','Attribute lineweight','text',['ByLayer','ByBlock',...lineweights]);field(panels[2],'color','Attribute color');
      field(panels[2],'invisible','Attribute invisible','checkbox');
      const lock=field(panels[2],'positionLocked','Attribute position locked','checkbox');
      if(data.drawingVersion&&!['AutoCad2010','AutoCad2013','AutoCad2018'].includes(data.drawingVersion))lock.disabled=true;
      panels[2].append(el('p',{class:'hint',text:'Color: ByLayer, ByBlock, ACI 1–255, or #RRGGBB. Plot-style assignment and annotation contexts are not available in this editor.'}));
      showTab(tab);if(manager)updateButtons();
    }
    function filter(){
      const term=search.value.toLowerCase(),visible=attributes.filter(a=>`${a.tag} ${a.prompt} ${a.value}`.toLowerCase().includes(term));
      list.replaceChildren(...visible.map(a=>el('option',{value:attributeId(a.id),text:`${a.duplicateTag?'⚠ ':''}${a.tag} — ${a.value.slice(0,60)}`})));
      choose(visible.find(a=>attributeId(a.id)===attributeId(selected?.id))||visible[0]||null);
    }
    list.addEventListener('change',()=>{remember();choose(attributes.find(a=>attributeId(a.id)===list.value));});
    search.addEventListener('input',()=>{remember();filter();});
    const updateExisting=el('input',{type:'checkbox','aria-label':'Apply changes to existing references'});
    updateExisting.checked=true;
    async function apply(forceSync=false){
      editable();if(key!==client.key||client.busy)throw new Error('The drawing or selection changed. Reopen the attribute editor.');
      remember();const changes=manager?definitionBatch(originalAttributes,attributes,drafts,removed):attributes.map(a=>drafts.has(attributeId(a.id))?attributeChanges(a,drafts.get(attributeId(a.id))):null).filter(Boolean);
      if(manager?!changes&&!forceSync:!changes.length){status.textContent='No attribute changes.';return;}
      const body=manager?{definition:{block:{value:id},changes:changes||{},updateExisting:forceSync||updateExisting.checked}}:{insert:{value:id},attributes:changes};
      const result=await edit(manager?'attribute-definitions':'attributes',body,{expectedKey:key});
      if(!result?.state)throw new Error('The drawing changed while applying attributes. Reopen the editor.');
      const currentKey=client.key,fresh=await client.read(readPath(result.state.revision));
      if(!fresh||currentKey!==client.key||attributeId(manager?fresh.block:fresh.insert)!==id)throw new Error('The selected block changed after applying attributes. Reopen the editor.');
      key=currentKey;data=fresh;attributes=rows(fresh);originalAttributes=attributes.slice();drafts.clear();removed.clear();filter();
      status.textContent=manager?'Applied definition changes as one undo step.':`Applied ${changes.length} attribute changes as one undo step.`;
    }
    const move=delta=>{remember();const at=attributes.indexOf(selected),to=at+delta;if(at<0||selected.constant||to<0||to>=attributes.length)return;attributes.splice(at,1);attributes.splice(to,0,selected);filter();};
    const up=el('button',{type:'button',text:'Move up',onclick:()=>move(-1)}),down=el('button',{type:'button',text:'Move down',onclick:()=>move(1)});
    const remove=el('button',{type:'button',text:'Remove definition',onclick:()=>{if(!selected||attributes.length<2)return;remember();removed.add(attributeId(selected.id));attributes=attributes.filter(a=>a!==selected);selected=null;filter();}});
    function updateButtons(){const index=attributes.indexOf(selected);up.disabled=!selected||selected.constant||index===0;down.disabled=!selected||selected.constant||index===attributes.length-1;remove.disabled=!selected||attributes.length<2;}
    const editorBody=el('div',{class:'resource-manager attribute-editor'},[
      el('aside',{},[search,list,details,warning,...(manager?[el('div',{class:'toolbar'},[up,down,remove])]:[])]),el('div',{class:'resource-fields'},[el('div',{class:'attribute-tabs',role:'tablist','aria-label':'Attribute settings'},tabs),...panels])]);
    const applyButton=el('button',{type:'button',text:'Apply attributes',onclick:async()=>{
      if(applying||closed)return;applying=true;applyButton.disabled=true;editorBody.inert=true;
      status.textContent='Applying attribute changes…';
      applyTask=(async()=>{try{await apply();}catch(error){status.textContent=error.message;}finally{
        applying=false;applyButton.disabled=false;editorBody.inert=false;
      }})();await applyTask;
    }});
    const syncButton=el('button',{type:'button',text:'Sync references',onclick:async()=>{
      if(applying||closed)return;applying=true;syncButton.disabled=true;applyButton.disabled=true;editorBody.inert=true;
      applyTask=(async()=>{try{await apply(true);}catch(error){status.textContent=error.message;}finally{applying=false;syncButton.disabled=false;applyButton.disabled=false;editorBody.inert=false;}})();await applyTask;
    }});
    filter();showTab(0);
    const managerOptions=manager?[el('p',{class:'hint',text:`${data.referenceCount} references in drawing; ${data.currentSpaceCount} in current space.`}),
      row('Apply changes to existing references',updateExisting),el('p',{class:'attribute-warning',text:'Updating or synchronizing existing references keeps assigned values, resets formatting to the definition, removes orphan attributes and clears INSERT XData. Undo restores the batch. Use ATTSYNC for a separate review preview.'}),syncButton]:[];
    const accepted=await dialog(manager?'Block attribute manager (BATTMAN)':'Enhanced attribute editor',[editorBody,...managerOptions,applyButton,status],'OK');
    closed=true;if(applyTask)await applyTask;if(accepted)await apply();
  };
}
