import {el} from './ui.mjs';
import {parsePoint,CommandSession} from './core.mjs';
import {boundedNumber} from './resource-contracts.mjs';
import {attributeTools} from './attribute-ui.mjs';

/** Definition authoring uses the same transactions and fields in native and WASM hosts. */
export function definitionPlacement(definition,expectedKey,layer='0') {
  const session=new CommandSession('POINT',layer);session.name='ATTDEF';
  session.spec={...session.spec,steps:[{kind:'point',prompt:'Specify attribute insertion point'}]};
  session.definitionRequest=structuredClone(definition);session.expectedKey=expectedKey;return session;
}

export function definitionTools({client,edit,dialog,editable,beginPlacement}) {
  async function choose() {
    editable();if(client.busy)throw new Error('Finish the current edit before editing definitions.');
    const key=client.key,blocks=(client.data?.blocks||[]).filter(b=>!b.isExternal);
    if(!blocks.length)throw new Error('Create or import a named block first.');
    const selected=client.data?.selectedEntities||[];
    const initial=client.data?.blockEditor?.id||(selected.length===1?(client.data?.items||[]).find(i=>i.id===selected[0])?.block:null);
    if(blocks.some(b=>b.id===initial))return initial;
    const list=el('select',{'aria-label':'Attribute block definition'},blocks.map(b=>el('option',{value:b.id,text:b.name})));
    if(!await dialog('Choose attribute block',[list],'Choose'))return null;
    if(key!==client.key||client.busy)throw new Error('The drawing changed. Choose the definition again.');
    return list.value;
  }
  return {
    async manage(){const block=await choose();if(block)await attributeTools({client,edit,dialog,editable,definitionBlock:block})();},
    async create(){
      editable();if(client.busy)throw new Error('Finish the current edit before defining an attribute.');
      const key=client.key;
      const [data,resources]=await Promise.all([client.read(`attribute-definitions?revision=${client.state.revision}&currentSpace=true`),client.read('resources')]);
      if(!data||!resources||key!==client.key)return;
      const block=data.block.value,owners=[{id:block,name:data.layout?`${data.layout} — current drawing space`:data.name}];
      if(!client.data?.blockEditor)owners.push(...(client.data?.blocks||[]).filter(item=>!item.isExternal&&item.id!==block));
      const owner=el('select',{'aria-label':'Definition owner'},owners.map(item=>el('option',{value:item.id,text:item.name})));
      owner.value=block;
      const controls={},rows=[];
      function field(key,label,value,type='text',options=null){
        const node=options?el('select',{'aria-label':label},options.map(name=>el('option',{value:name,text:name}))):el(key==='value'?'textarea':'input',{'aria-label':label,type:key==='value'?undefined:type});
        if(type==='checkbox')node.checked=value;else node.value=String(value);if(type==='number')node.step='any';
        controls[key]=node;rows.push(el('label',{class:'form-row'},[el('span',{text:label}),node]));return node;
      }
      field('tag','Definition tag','');field('prompt','Definition prompt','');field('value','Definition default','');
      field('position','Definition insertion point','0,0,0');field('height','Definition text height','2.5','number');
      const onScreen=el('input',{type:'checkbox','aria-label':'Specify attribute point on screen'});onScreen.checked=!!data.layout&&!!beginPlacement;onScreen.disabled=!beginPlacement;
      controls.position.disabled=onScreen.checked;onScreen.addEventListener('change',()=>controls.position.disabled=onScreen.checked);
      owner.addEventListener('change',()=>{onScreen.disabled=!beginPlacement||owner.value!==block;if(onScreen.disabled)onScreen.checked=false;controls.position.disabled=onScreen.checked;});
      const names=table=>(resources[table]||[]).map(r=>r.name??r.status?.name).filter(Boolean);
      field('textStyle','Definition text style','Standard','text',names('textStyles'));
      field('layer','Definition layer','0','text',names('layers'));
      field('invisible','Invisible definition',false,'checkbox');field('constant','Constant definition',false,'checkbox');
      field('verify','Verify definition',false,'checkbox');field('preset','Preset definition',false,'checkbox');
      const modern=['AutoCad2010','AutoCad2013','AutoCad2018'].includes(data.drawingVersion);
      field('positionLocked','Lock definition location',modern,'checkbox').disabled=!modern;
      field('multiline','Multiple lines definition',false,'checkbox').disabled=data.drawingVersion!=='AutoCad2018';
      if(!await dialog('Attribute definition (ATTDEF)',[el('label',{class:'form-row'},[el('span',{text:'Create in'}),owner]),
        el('p',{text:'Place the definition in the current drawing, or choose a named block. Coordinates belong to the chosen space.'}),...rows,
        el('label',{class:'form-row'},[el('span',{text:'Specify on-screen'}),onScreen]),
        el('p',{class:'hint',text:`Drawing profile: ${data.drawingVersion}. Position locking requires DXF 2010 or later; multiline requires DXF 2018. Multiline paragraphs use spaces in the initial single-line fallback. Existing references retain their attributes until synchronized.`})],'Create definition'))return;
      editable();if(key!==client.key||client.busy)throw new Error('The drawing changed. Reopen ATTDEF.');
      const input={};for(const [name,node]of Object.entries(controls))input[name]=node.getAttribute('type')==='checkbox'?node.checked:node.value;
      input.position=onScreen.checked?{x:0,y:0,z:0}:parsePoint(input.position);input.height=boundedNumber(input.height,1e-9,1e9,'Height');
      const definition={block:{value:owner.value},changes:{add:[input]}};
      if(onScreen.checked){if(owner.value!==block)throw new Error('On-screen placement requires the current drawing or open block editor.');beginPlacement(definitionPlacement(definition,key,input.layer));return;}
      await edit('attribute-definitions',{definition},{expectedKey:key});
    }
  };
}
