import {el} from './ui.mjs';
import {lineweights} from './resource-contracts.mjs';
import {layerId,layerRule,layerFilterFields,filterLayers,layerFilterPreferences,saveLayerFilter} from './layer-filter-model.mjs';

const row=(label,input)=>el('label',{class:'layer-filter-field'},[el('span',{text:label}),input]);
const input=(label,value='',type='text')=>el('input',{'aria-label':label,value,type});
const select=(label,options,value)=>{const node=el('select',{'aria-label':label},options.map(option=>el('option',{value:option[0],text:option[1]})));node.value=value;return node;};
const labels={visible:'On',frozen:'Frozen',locked:'Locked',plot:'Plot',frozenInNewViewports:'New viewport frozen',used:'Used',current:'Current'};

/** UI-local filters/groups over native resources; only explicit isolation and property actions edit the drawing. */
export function layerManagerTools({client,edit,dialog,editable,editLayer,report=()=>{},storage}) {
  try{storage=storage===undefined?globalThis.localStorage:storage;}catch{storage=null;}
  const check=key=>{editable();if(client.busy||key!==client.key)throw new Error('The drawing changed. Reopen the layer manager.');};
  const isolateSelection=async(mode='off')=>{editable();if(client.busy)throw new Error('Finish the current drawing operation first.');const key=client.key;await edit('layer-isolation',{isolation:{action:'isolate',mode}},{expectedKey:key});};
  const restore=async()=>{editable();if(client.busy)throw new Error('Finish the current drawing operation first.');const key=client.key;await edit('layer-isolation',{isolation:{action:'restore'}},{expectedKey:key});};
  async function open() {
    let selected=new Set(),selection='',rules=[layerRule()],searchText='',invert=false;
    while(true) {
      editable();if(client.busy)throw new Error('Finish the current drawing operation first.');
      const key=client.key,data=await client.read('resources');if(!data||key!==client.key)return;
      const layers=data.layers||[],statuses=data.layerStatus||[],byId=new Map(layers.map(layer=>[layerId(layer.id),layer]));
      selected=new Set([...selected].filter(id=>byId.has(id)));
      const storageKey=`netdxf-layer-filters-v1:${client.state.documentId}`;
      let preferences=layerFilterPreferences(null,layers);
      try {const saved=storage?.getItem(storageKey);if(saved&&saved.length<=1048576)preferences=layerFilterPreferences(JSON.parse(saved),layers);}catch{/* Invalid/local storage is optional. */}
      if(!document.querySelector('link[data-layer-manager]'))document.head.append(el('link',{rel:'stylesheet',href:new URL('./layer-manager.css',import.meta.url).href,'data-layer-manager':''}));
      const status=el('p',{class:'hint',role:'status','aria-live':'polite'}),feedback=el('p',{class:'diagnostic',role:'status'});
      const root=el('div',{class:'layer-manager'}),list=el('div',{class:'layer-filter-list'}),ruleHost=el('div',{class:'layer-filter-rules'});
      const search=input('Search layers',searchText,'search'),name=input('Layer filter or group name');
      search.maxLength=255;name.maxLength=80;
      const chooser=select('Layer filter',[],selection),inverted=el('input',{'aria-label':'Invert layer filter',type:'checkbox',checked:invert});
      const mode=select('Layer isolation mode',[['off','Turn other layers off'],['lock','Lock other layers']], 'off');
      let requested=null,page=0,matched=[],readers=[],alive=true;
      const safe=action=>()=>{try{if(!alive)return;action();}catch(error){feedback.textContent=error.message;report(error);}};
      const button=(text,action,disabled=false)=>el('button',{type:'button',text,disabled,onclick:safe(action)});
      const finish=action=>{check(key);requested=action;document.getElementById('dialog').close('ok');};
      const currentGroup=()=>selection.startsWith('g:')?preferences.groups.find(group=>group.name===selection.slice(2)):null;
      const save=value=>{
        const json=JSON.stringify(value);
        if(json.length>1048576)throw new Error('Layer filter preferences exceed the 1 MiB local storage limit.');
        if(!storage)throw new Error('Local storage is unavailable; the filter was not saved.');
        try{storage.setItem(storageKey,json);}catch{throw new Error('The layer filter could not be saved. Browser storage is unavailable or full.');}
        preferences=value;feedback.textContent='';
      };
      const collect=()=>readers.map(read=>layerRule(read()));
      const chooseOptions=()=>{chooser.replaceChildren(el('option',{value:'',text:'All layers / current rules'}),
        ...preferences.filters.map(filter=>el('option',{value:`f:${filter.name}`,text:`Property: ${filter.name}`})),
        ...preferences.groups.map(group=>el('option',{value:`g:${group.name}`,text:`Group: ${group.name} (${group.members.length})`})) );
        if(selection&&!Array.from(chooser.children).some(option=>option.value===selection))selection='';chooser.value=selection;};
      const editButton=button('Edit layer properties',()=>finish('edit'),true),currentButton=button('Make layer current',()=>finish('current'),true);
      const isolateButton=button('Isolate checked layers',()=>finish('isolate'),true);
      const groupAdd=button('Add checked to group',()=>{
        const group=currentGroup();if(!group)throw new Error('Choose a saved group.');
        save(saveLayerFilter(preferences,'group',group.name,[...group.members.map(member=>member.id),...selected],layers));chooseOptions();renderList();
      },true);
      const groupRemove=button('Remove checked from group',()=>{
        const group=currentGroup();if(!group)throw new Error('Choose a saved group.');
        save(saveLayerFilter(preferences,'group',group.name,group.members.map(member=>member.id).filter(id=>!selected.has(id)),layers));chooseOptions();renderList();
      },true);
      function renderList() {
        const group=currentGroup();
        if(!group)rules=collect();
        matched=filterLayers(layers,{rules,members:group?.members.map(member=>member.id)??null,invert:inverted.checked,search:search.value},statuses);
        page=Math.min(page,Math.max(0,Math.ceil(matched.length/100)-1));
        status.textContent=`${matched.length} / ${layers.length} layers match · ${selected.size} checked · page ${page+1} / ${Math.max(1,Math.ceil(matched.length/100))}`;
        list.replaceChildren();
        for(const layer of matched.slice(page*100,page*100+100)) {
          const id=layerId(layer.id),checked=el('input',{type:'checkbox','aria-label':`Include layer ${layer.name}`,checked:selected.has(id)});
          checked.addEventListener('change',()=>{if(checked.checked)selected.add(id);else selected.delete(id);updateActions();});
          const metadata=statuses.find(status=>layerId(status.id)===id);
          list.append(el('label',{class:'layer-filter-item'},[checked,el('strong',{text:layer.name}),el('small',{text:[
            metadata?.current?'Current':null,layer.visible?'On':'Off',layer.frozen?'Frozen':'Thawed',layer.locked?'Locked':'Unlocked',
            layer.plot?'Plot':'No plot',layer.linetype,layer.lineweight,`${layer.transparency}% transparent`,metadata?.uses?`${metadata.uses} references`:'Unused'
          ].filter(Boolean).join(' · ')}),el('span',{class:'layer-filter-color',text:layer.trueColor?layer.color:`ACI ${layer.colorIndex}`})]));
        }
        list.append(el('div',{class:'resource-actions'},[button('Previous layer page',()=>{page--;renderList();},page===0),
          button('Next layer page',()=>{page++;renderList();},(page+1)*100>=matched.length)]));
        updateActions();feedback.textContent='';
      }
      function updateActions(){
        editButton.disabled=currentButton.disabled=selected.size!==1;isolateButton.disabled=!selected.size||data.layerIsolation?.valid===false;
        groupAdd.disabled=groupRemove.disabled=!currentGroup()||!selected.size;
        status.textContent=`${matched.length} / ${layers.length} layers match · ${selected.size} checked · page ${page+1} / ${Math.max(1,Math.ceil(matched.length/100))}`;
      }
      function renderRules() {
        ruleHost.replaceChildren();readers=[];
        rules.forEach((rule,index)=>{
          const fields={},prefix=`Rule ${index+1}`;
          const field=(key,label,type='text')=>{const control=input(`${prefix} ${label}`,rule[key]??'',type);fields[key]=control;control.addEventListener('input',safe(()=>{selection='';chooser.value='';page=0;renderList();}));return row(label,control);};
          const booleanFields=layerFilterFields.map(key=>{
            const control=select(`${prefix} ${labels[key]}`,[['any','Any'],['yes','Yes'],['no','No']],rule[key]);fields[key]=control;
            control.addEventListener('change',safe(()=>{selection='';chooser.value='';page=0;renderList();}));return row(labels[key],control);
          });
          const weight=select(`${prefix} Lineweight`,[['','Any'],...lineweights.map(weight=>[weight,weight])],rule.lineweight);fields.lineweight=weight;
          weight.addEventListener('change',safe(()=>{selection='';chooser.value='';page=0;renderList();}));
          readers.push(()=>Object.fromEntries(Object.entries(fields).map(([key,node])=>[key,node.value])));
          const details=el('details',{},[el('summary',{text:`${prefix} — ${index?'OR':'match all chosen properties'}`}),
            el('div',{class:'layer-rule-grid'},[field('name','Name pattern'),field('description','Description pattern'),field('linetype','Linetype pattern'),
              field('color','Color (ACI / #RRGGBB)'),row('Lineweight',weight),field('transparencyMin','Minimum transparency','number'),field('transparencyMax','Maximum transparency','number'),...booleanFields]),
            button(`Remove rule ${index+1}`,()=>{rules=collect().filter((_,i)=>i!==index);renderRules();renderList();},rules.length===1)]);
          details.open=index===0;ruleHost.append(details);
        });
        ruleHost.hidden=!!currentGroup();
      }
      chooser.addEventListener('change',safe(()=>{
        selection=chooser.value;name.value=selection?selection.slice(2):'';
        const filter=preferences.filters.find(filter=>`f:${filter.name}`===selection);
        rules=filter?filter.rules.map(layerRule):[layerRule()];page=0;renderRules();renderList();
      }));
      search.addEventListener('input',safe(()=>{page=0;renderList();}));inverted.addEventListener('change',safe(()=>{page=0;renderList();}));
      const sidebar=el('aside',{},[row('Layer list',chooser),row('Find within list',search),row('Invert matches',inverted),row('Saved filter / group name',name),
        el('p',{class:'hint',text:'Saving an existing name replaces its rules or group membership. Add/Remove checked updates group members. Search and inversion affect this view only.'}),
        button('Save property filter',()=>{rules=collect();save(saveLayerFilter(preferences,'filter',name.value,rules,layers));selection=`f:${name.value.trim()}`;chooseOptions();renderRules();renderList();}),
        button('Save checked as group',()=>{if(!selected.size)throw new Error('Check layers to include in the group.');save(saveLayerFilter(preferences,'group',name.value,[...selected],layers));selection=`g:${name.value.trim()}`;chooseOptions();renderRules();renderList();}),
        groupAdd,groupRemove,
        button('Delete saved filter / group',()=>{if(!selection)throw new Error('Choose a saved filter or group.');const key=selection[0]==='g'?'groups':'filters';save({...preferences,[key]:preferences[key].filter(item=>item.name!==selection.slice(2))});selection='';chooseOptions();rules=[layerRule()];renderRules();renderList();}),
        el('p',{class:'hint',text:'Filters and groups are local to this drawing in this browser. They do not change layer visibility and are not stored in DXF. Groups follow renamed layer IDs; deleted layers are removed.'})]);
      const main=el('div',{class:'layer-manager-main'},[el('div',{class:'resource-actions'},[
        button('Check all matching layers',()=>{for(const layer of matched)selected.add(layerId(layer.id));renderList();}),
        button('Clear checked layers',()=>{selected.clear();renderList();}),editButton,currentButton,
        button('New layer',()=>finish('new'))]),ruleHost,
        button('Add OR rule',()=>{if(rules.length>=8)throw new Error('Use at most 8 OR rules.');rules=collect();rules.push(layerRule());selection='';chooser.value='';renderRules();renderList();}),
        el('p',{class:'hint',text:'Within a rule, every chosen property must match; rules are OR alternatives. Patterns support * and ?, comma-separated alternatives and ~ exclusion.'}),
        status,list,row('Isolation method',mode),el('div',{class:'resource-actions'},[isolateButton,
          button('Restore layer isolation',()=>finish('restore'),!data.layerIsolation?.active)]),
        el('p',{class:'hint',text:data.layerIsolation?.valid===false?data.layerIsolation.notice:data.layerIsolation?.active?`Isolation active (${data.layerIsolation.mode}) on ${data.layerIsolation.layerCount} changed layers. ${data.layerIsolation.notice}`:
          'Isolation changes global on/freeze/lock flags in one undoable edit. Selected layers are made visible, thawed and unlocked. Other layers are turned off or locked. No fading or viewport-specific freeze.'}),feedback]);
      root.append(sidebar,main);chooseOptions();renderRules();renderList();
      const accepted=await dialog('Layer filters and isolation',[root],'Close');alive=false;
      searchText=search.value;invert=inverted.checked;
      if(!accepted||!requested)return;
      check(key);
      if(requested==='edit'||requested==='new'){await editLayer(requested==='edit'?[...selected][0]:null,requested==='new');continue;}
      if(requested==='current')await edit('resource',{resource:{kind:'layer',action:'current',id:{value:[...selected][0]}}},{expectedKey:key});
      else await edit('layer-isolation',{isolation:requested==='restore'?{action:'restore'}:{action:'isolate',mode:mode.value,layers:[...selected].map(value=>({value}))}},{expectedKey:key});
    }
  }
  return {open,isolateSelection,restore};
}
