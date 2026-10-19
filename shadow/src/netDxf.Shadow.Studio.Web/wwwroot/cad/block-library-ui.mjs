import { el } from './ui.mjs';

/** Host-independent library workflow; the server owns parsing, conflict planning and history. */
export function blockLibraryTools({ client, edit, dialog, showPalette, download }) {
  const row = (label, input) => el('label', {class:'form-row'}, [el('span',{text:label}),input]);
  const choices = (label, values) => el('select', {'aria-label':label}, values.map(([value,text])=>el('option',{value,text})));
  const current = key => { if(client.key!==key||client.busy)throw new Error('The drawing changed. Reopen the library dialog.'); };
  return {
    async importLibrary() {
      if(client.busy)throw new Error('Finish the current edit before importing a library.');
      let key=client.key;
      const loaded=await client.read('block-libraries');if(!loaded)return;current(key);
      const file=el('input',{type:'file',accept:'.dxf,application/dxf','aria-label':'Block library DXF'});
      const cached=choices('Loaded block library',loaded.map(item=>[item.libraryId,item.name]));
      if(!await dialog('Open block library', [row('DXF drawing',file),...(loaded.length?[row('Or loaded library',cached)]:[]),
        el('p',{class:'hint',text:'Choose a DXF drawing up to 8 MiB. Browse model space or named definitions without changing the current drawing.'})], 'Browse definitions'))return;
      current(key);let library;
      if(file.files[0]) {
        const chosen=file.files[0];if(chosen.size>8*1024*1024)throw new Error('Block libraries are limited to 8 MiB.');
        const bytes=await chosen.arrayBuffer();current(key);
        library=await client.mutate('block-library/open',new Blob([bytes],{type:'application/dxf'}),{binary:true,query:{name:chosen.name},expectedKey:key});
      } else library=loaded.find(item=>item.libraryId===cached.value);
      if(!library)throw new Error('Choose a DXF file or a loaded block library.');
      key=client.key;current(key);
      const source=choices('Library definition',library.entries.map(entry=>[entry.name,entry.modelSpace?'Drawing model space':entry.name]));
      const name=el('input',{'aria-label':'Imported block name',required:true,maxLength:255});
      const details=el('p',{class:'hint'});let renamed=false;
      name.addEventListener('input',()=>renamed=true);
      function update(){const item=library.entries.find(entry=>entry.name===source.value);if(!item)return;
        if(!renamed)name.value=item.modelSpace?library.name.replace(/\.[^.]*$/,'').replace(/[<>/\\":;?*|=,\x00-\x1f]/g,'_'):item.name;
        details.textContent=`${item.entities} objects · ${item.attributes} attributes · ${item.units} · base ${item.basePoint.x}, ${item.basePoint.y}, ${item.basePoint.z}. ${item.description}`;
      }
      source.addEventListener('change',update);update();
      const conflict=choices('Existing block policy',[['0','Stop on conflict'],['1','Reuse existing definition'],['2','Import with a unique name'],['3','Redefine existing block']]);
      const resources=choices('Existing resource policy',[['0','Reuse destination resources'],['1','Rename incoming resources'],['2','Stop on resource conflict']]);
      if(!await dialog('Import block library',[row('Definition',source),details,row('Block name',name),row('Existing block',conflict),row('Layers, styles and nested blocks',resources),
        el('p',{class:'hint',text:'Redefinition updates existing references and retains assigned attribute values. Review the resource report and definition preview before applying. Import adds a definition; use Insert block to place it.'})], 'Preview import'))return;
      current(key);
      await edit('edit-preview',{description:`Import block ${name.value}`,operations:[{kind:'block-library',blockLibrary:{libraryId:library.libraryId,sourceName:source.value,name:name.value,blockConflict:Number(conflict.value),resourceConflict:Number(resources.value)}}]},{expectedKey:key});
      showPalette('Edit previews');
    },
    async exportLibrary() {
      if(client.busy)throw new Error('Finish the current edit before exporting a definition.');
      const key=client.key,blocks=(client.data?.blocks||[]).filter(block=>!block.isExternal);
      if(!blocks.length)throw new Error('Create a named block before exporting a library.');
      const selected=choices('Export block definition',blocks.map(block=>[block.id,block.name]));
      const binary=el('input',{type:'checkbox','aria-label':'Binary block library DXF'});
      if(!await dialog('Export block library',[row('Definition',selected),row('Binary DXF',binary),
        el('p',{class:'hint',text:'Download an independent DXF containing the named definition, its attributes and dependencies, and one reference for viewing. Export leaves the drawing and its save status unchanged.'})], 'Download DXF'))return;
      current(key);
      const blob=await client.read(`block-library/export?${new URLSearchParams({revision:client.state.revision,block:selected.value,binary:String(binary.checked)})}`,{blob:true});
      if(blob){current(key);download(blob,`${blocks.find(block=>block.id===selected.value).name.replace(/[<>:"/\\|?*\x00-\x1f]/g,'_')}.library.dxf`);}
    }
  };
}
