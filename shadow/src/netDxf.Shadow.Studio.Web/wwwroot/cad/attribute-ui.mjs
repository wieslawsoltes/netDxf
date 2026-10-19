import {el} from './ui.mjs';
import {lineweights} from './resource-contracts.mjs';
import {attributeAlignments,attributeChanges,attributeId} from './attribute-contracts.mjs';

/** Shared native/WASM instance editor. Apply and OK each submit one atomic batch. */
export function attributeTools({client,edit,dialog,editable}) {
  return async function open() {
    editable();if(client.busy)throw new Error('Finish the current edit before editing attributes.');
    let key=client.key;
    const [initial,resources]=await Promise.all([client.read(`attributes?revision=${client.state.revision}`),client.read('resources')]);
    if(!initial||!resources||key!==client.key)return;
    let data=initial,attributes=data.attributes||[];
    if(!attributes.length)throw new Error('The selected block has no instance attributes.');
    const id=attributeId(data.insert),drafts=new Map(),controls=new Map();
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
      if(key==='value'){node.rows=selected.multiline?6:2;node.maxLength=1_000_000;node.readOnly=selected.constant;}
      if(type==='number')node.step='any';controls.set(key,node);panel.append(row(label,node));return node;
    }
    const names=(table,current)=>[...new Set([current,...(resources[table]||[]).map(r=>r.name??r.status?.name)])].filter(Boolean);
    function choose(attribute){
      selected=attribute;controls.clear();panels.forEach(p=>p.replaceChildren());
      if(!attribute){details.textContent='No matching attributes.';warning.textContent='';return;}
      list.value=attributeId(attribute.id);details.textContent=`${data.blockName} · ${attribute.tag} · ${attribute.multiline?'multiline':'single-line'}${attribute.constant?' · constant':''}`;
      warning.textContent=attribute.duplicateTag?'Duplicate tag: changes target this attribute by its object identity.':'';
      panels[0].append(el('p',{class:'hint',text:`Tag: ${attribute.tag}\nPrompt: ${attribute.prompt||'—'}`}));
      field(panels[0],'value',attribute.multiline?'Multiline attribute value':'Attribute value');
      if(attribute.multiline)panels[0].append(el('p',{class:'hint',text:'MTEXT formatting controls are preserved. New lines become paragraph breaks. The single-line fallback value is retained.'}));
      field(panels[1],'textStyle','Attribute text style','text',names('textStyles',attribute.textStyle));
      field(panels[1],'alignment','Attribute justification','text',attributeAlignments.slice(0,attribute.multiline?9:15));
      field(panels[1],'height','Attribute height','number');field(panels[1],'rotation','Attribute rotation','number');
      field(panels[1],'width',attribute.multiline?'Attribute boundary width':'Attribute fit width','number');
      if(attribute.multiline){field(panels[1],'lineSpacing','Attribute line spacing','number');field(panels[1],'exactSpacing','Exact attribute line spacing','checkbox');}
      else{field(panels[1],'widthFactor','Attribute width factor','number');field(panels[1],'obliqueAngle','Attribute oblique angle','number');field(panels[1],'backward','Attribute backwards','checkbox');field(panels[1],'upsideDown','Attribute upside down','checkbox');}
      field(panels[2],'layer','Attribute layer','text',names('layers',attribute.layer));field(panels[2],'linetype','Attribute linetype','text',names('linetypes',attribute.linetype));
      field(panels[2],'lineweight','Attribute lineweight','text',['ByLayer','ByBlock',...lineweights]);field(panels[2],'color','Attribute color');
      field(panels[2],'invisible','Attribute invisible','checkbox');field(panels[2],'positionLocked','Attribute position locked','checkbox');
      panels[2].append(el('p',{class:'hint',text:'Color: ByLayer, ByBlock, ACI 1–255, or #RRGGBB. Plot-style assignment and annotation contexts are not available in this editor.'}));
      showTab(tab);
    }
    function filter(){
      const term=search.value.toLowerCase(),visible=attributes.filter(a=>`${a.tag} ${a.prompt} ${a.value}`.toLowerCase().includes(term));
      list.replaceChildren(...visible.map(a=>el('option',{value:attributeId(a.id),text:`${a.duplicateTag?'⚠ ':''}${a.tag} — ${a.value.slice(0,60)}`})));
      choose(visible.find(a=>attributeId(a.id)===attributeId(selected?.id))||visible[0]||null);
    }
    list.addEventListener('change',()=>{remember();choose(attributes.find(a=>attributeId(a.id)===list.value));});
    search.addEventListener('input',()=>{remember();filter();});
    async function apply(){
      editable();if(key!==client.key||client.busy)throw new Error('The drawing or selection changed. Reopen the attribute editor.');
      remember();const changes=attributes.map(a=>drafts.has(attributeId(a.id))?attributeChanges(a,drafts.get(attributeId(a.id))):null).filter(Boolean);
      if(!changes.length){status.textContent='No attribute changes.';return;}
      const result=await edit('attributes',{insert:{value:id},attributes:changes},{expectedKey:key});
      if(!result?.state)throw new Error('The drawing changed while applying attributes. Reopen the editor.');
      const currentKey=client.key,fresh=await client.read(`attributes?revision=${result.state.revision}`);
      if(!fresh||currentKey!==client.key||attributeId(fresh.insert)!==id)throw new Error('The selected block changed after applying attributes. Reopen the editor.');
      key=currentKey;data=fresh;attributes=fresh.attributes;drafts.clear();filter();status.textContent=`Applied ${changes.length} attribute changes as one undo step.`;
    }
    const editorBody=el('div',{class:'resource-manager attribute-editor'},[
      el('aside',{},[search,list,details,warning]),el('div',{class:'resource-fields'},[el('div',{class:'attribute-tabs',role:'tablist','aria-label':'Attribute settings'},tabs),...panels])]);
    const applyButton=el('button',{type:'button',text:'Apply attributes',onclick:async()=>{
      if(applying||closed)return;applying=true;applyButton.disabled=true;editorBody.inert=true;
      status.textContent='Applying attribute changes…';
      applyTask=(async()=>{try{await apply();}catch(error){status.textContent=error.message;}finally{
        applying=false;applyButton.disabled=false;editorBody.inert=false;
      }})();await applyTask;
    }});
    filter();showTab(0);
    const accepted=await dialog('Enhanced attribute editor',[editorBody,applyButton,status],'OK');
    closed=true;if(applyTask)await applyTask;if(accepted)await apply();
  };
}
