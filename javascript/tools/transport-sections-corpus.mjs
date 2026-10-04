// Deterministic INPUTS only. Exact expected bytes/state come from the native oracle.
const method=method=>({method});
const row=(code,value)=>[code,typeof value!=='number'?value:(code===90||code===91||code===92||code===421||code===440||code===441)?{int:value}:(code===63||code===284)?{short:value}:code===160?{long:String(value)}:value];
const readSteps=(tags,operation)=>tags.flatMap(()=>[method('next'),method(operation)]);
const patterns=[0,1,2,126,127,128,129,254,255,256,257,4097];
export function transportSectionsCorpus() {
  const all=[];
  const add=(name,kind,mode,version,tags,steps,extra={})=>all.push({name:'section-io/'+name,category:kind,request:{op:'transport-sections',mode,version,tags,steps,...extra}});
  for(const mode of ['text','binary','legacy']) {
    for(const length of patterns) {
      add(`${mode}/thumbnail/write/${length}`,'thumbnail',mode,18,[],[{method:'thumbnailWrite',data:{pattern:length}}]);
      const packet=[row(2,'THUMBNAILIMAGE'),row(90,length)];
      for(let at=0;at<length;at+=127)packet.push(row(310,{bytes:Array.from({length:Math.min(127,length-at)},(_,i)=>((at+i)*73+19)&255)}));
      packet.push(row(0,'ENDSEC'),row(0,'EOF'));
      add(`${mode}/thumbnail/read/${length}`,'thumbnail',mode,18,packet,[method('next'),method('thumbnailRead'),method('next')]);
    }
    const invalids={negative:[[90,-1],[0,'ENDSEC']],duplicate:[[90,0],[90,0],[0,'ENDSEC']],missing:[[310,{bytes:[1]}],[0,'ENDSEC']],
      short:[[90,1],[310,{bytes:[1,2]}],[0,'ENDSEC']],long:[[90,3],[310,{bytes:[1,2]}],[0,'ENDSEC']],large:[[90,2147483647],[0,'ENDSEC']],
      noend:[[90,0]],eof:[[90,0],[0,'EOF']],record:[[90,0],[0,'SECTION']],unexpected:[[90,0],[1,'bad'],[0,'ENDSEC']],
      after:[[310,{bytes:[1,2]}],[90,2],[0,'ENDSEC']],empty:[[90,0],[310,{bytes:[]}],[0,'ENDSEC']],chunk128:[[90,128],[310,{pattern:128}],[0,'ENDSEC']]};
    for(const [name,values] of Object.entries(invalids))add(`${mode}/thumbnail/${name}`,'thumbnail',mode,18,[row(2,'THUMBNAILIMAGE'),...values.map(([c,v])=>row(c,v))],[method('next'),method('thumbnailRead')]);
    if(mode==='text')add('text/thumbnail/comment','thumbnail',mode,18,[row(2,'THUMBNAILIMAGE'),row(999,'EOF'),row(90,0),row(0,'ENDSEC')],[method('next'),method('thumbnailRead')]);
    for(const version of [13,14,15,16,17,18]) {
      const packets=[[[430,'Book$Red']],[[430,'\\U+03A9'],[430,'Again']],[[284,0]],[[284,3]],[[284,4]],[[284,-1]],[[284,0],[284,1]],
        [[92,0]],[[160,0]],[[92,-1]],[[160,{long:'9223372036854775807'}]],[[92,16777217]],[[92,2147483647]],[[92,0],[160,0]],[[160,0],[92,0]],
        [[92,3],[310,{bytes:[1,2,3]}]],[[310,{bytes:[1,2,3]}],[92,3]],[[310,{bytes:[1,2,3]}],[92,2]],[[310,{bytes:[1]}]],[[92,0],[310,{bytes:[1]}]],[[92,2],[310,{bytes:[1]}]],
        [[92,128],[310,{pattern:128}]],[[92,129],[310,{pattern:129}]],[[1,'ignored'],[90,123]],[[310,{bytes:[]}]],[[92,0],[310,{bytes:[]}]]];
      for(let index=0;index<packets.length;index++){
        const tags=packets[index].map(([c,v])=>row(c,v));
        add(`${mode}/common/read/${version}/${index}`,'common',mode,version,tags,[...readSteps(tags,'commonRead'),method('complete'),method('complete')]);
      }
      for(const length of [null,0,1,127,128,257]) {
        add(`${mode}/common/write/${version}/${length}`,'common',mode,version,[],[method('validateCommon'),method('commonWrite')],
          {common:{ColorName:'Book$\\U+0041 Ω\0\r\n',ShadowMode:0,ProxyGraphics:length===null?null:{pattern:length}}});
      }
      for(const flags of [0,1,2,3,16,17,18,19]) for(const present of [false,true]) {
        const background={Flags:flags,ScaleFactor:present?2.5:null,ColorIndex:present?{short:0}:null,TrueColor:{int:0x123456},ColorName:'Book$Ω\\U+0041',Transparency:{int:-2147483648}};
        add(`${mode}/background/write/${version}/${flags}/${present}`,'background',mode,version,[],[method('backgroundWrite')],{background});
      }
    }
    const backgrounds=[[[90,1]],[[90,16]],[[90,4]],[[90,-1]],[[45,0]],[[45,-1]],[[45,1e100]],[[45,{double:'8000000000000000'}]],[[63,-1]],[[63,257]],[[63,256]],
      [[431,'\\U+0000']],[[431,'\\U+000D']],[[431,'\\U+000A']],[[431,'Ω']],[[421,-1],[441,-2147483648]],[[430,'foreground'],[440,0]],
      [[90,1],[90,2],[45,1.5],[45,2.5],[63,7],[63,5]],[[45,2],[45,0]],[[431,'good'],[431,'\\U+0000']]];
    for(let index=0;index<backgrounds.length;index++){
      const tags=backgrounds[index].map(([c,v])=>row(c,v));
      add(`${mode}/background/read/${index}`,'background',mode,18,tags,[...readSteps(tags,'backgroundRead'),method('backgroundWrite')]);
    }
    for(const text of ['\\U+0041','\\u+0042','\\U+  41','\\U+41  ','\\U+\t041','\\U+ZZZZ','\\U+005CU+0041','\\U+D83D\\U+DE00','', '\\U+00A0']) {
      const tags=[row(430,text)];add(`${mode}/decode/${JSON.stringify(text)}`,'strings',mode,18,tags,readSteps(tags,'commonRead'));
    }
  }
  for(const placement of ['model','paper','unused','attribute','definition']) for(const version of [13,14,15,18]) for(const field of ['ColorName','ShadowMode'])
    add(`common/traversal/${placement}/${version}/${field}`,'traversal','text',version,[],[method('validateCommonDocument')],{placement,common:{[field]:field==='ColorName'?'':0}});
  for(const placement of ['model','paper','unused']) for(const version of [13,15,18]) for(const flags of [0,1,16])
    add(`background/traversal/${placement}/${version}/${flags}`,'traversal','text',version,[],[method('validateBackgroundDocument')],{placement,target:'background',background:{Flags:flags}});
  const base={vertices:[[0,0,0],[1,0,0],[0,1,0]],faces:[[0,1,2]],edges:[]};
  const meshes=[base,{...base,faces:[null]},{...base,faces:[[]]},{...base,faces:[[0,1]]},{...base,faces:[[-1,1,2]]},{...base,faces:[[0,1,3]]},
    {...base,edges:[null]},{...base,edges:[[0,3,0]]},{...base,edges:[[0,1,{double:'7FF0000000000000'}]]},
    {...base,edges:[[0,1,{double:'7FF8000000000042'}]]},{...base,faces:[],repeat:{length:1048576,count:2048}}];
  for(const bits of ['7FF0000000000000','FFF0000000000000','7FF8000000000042'])for(let axis=0;axis<3;axis++){
    const m=structuredClone(base);m.vertices[0][axis]={double:bits};meshes.push(m);
  }
  for(let index=0;index<meshes.length;index++)add(`mesh/${index}`,'mesh','text',18,[],[method('meshValidate')],{mesh:meshes[index]});
  for(const placement of ['model','paper','unused']) for(const version of [13,15,16,18])add(`mesh/traversal/${placement}/${version}`,'mesh','text',version,[],[method('meshVersions'),method('meshDocument')],{placement,mesh:base});
  return all;
}
