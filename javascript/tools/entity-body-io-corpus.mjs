// Deterministic inputs only. Expected values/bytes are observed from native C#.
const op=method=>({method});
export const entityBodyRow=(code,value)=>[code,typeof value!=='number'?value:Object.is(value,-0)?{double:'8000000000000000'}:(code>=90&&code<=99||code===1071)?{int:value}:(code>=60&&code<=79||code>=170&&code<=179||code>=270&&code<=289||code===1070)?{short:value}:value];
const row=entityBodyRow;
const xdata=[[1001,'IO_APP'],[1000,'Za\\U+017C\\U+00F3\\U+0142\\U+0107'],[1002,'{'],[1003,'Layer\\U+03A9'],[1004,{pattern:127}],[1004,{pattern:2}],[1005,'00AA'],[1010,-0],[1020,2],[1030,3],[1070,-7],[1071,1234],[1002,'}']];
const defaults={OleFrame:[[100,'AcDbOleFrame'],[90,0],[1,'OLE']],Ole2Frame:[[100,'AcDbOle2Frame'],[90,0],[1,'OLE']],Light:[[100,'AcDbLight']],LwPolyline:[[100,'AcDbPolyline'],[90,0]],AcisEntity:[[100,'AcDbModelerGeometry'],[70,1],[1,'abc']]};
export function entityBodyIOCorpus(){
  const all=[];
  const add=(name,mode,kind,tags,steps,version=18,extra={})=>all.push({name:'entity-body/'+name,category:kind,request:{op:'entity-body-io',mode,kind,version,tags:tags.map(([c,v])=>row(c,v)),steps,...extra}});
  const parse=(name,mode,kind,tags,version=18,extra={},valid=false)=>add(name,mode,kind,[...tags,[0,'ENDSEC'],[0,'EOF']],[op('next'),op('read'),...(valid?[op('write'),op('next')]:[])],version,extra);
  for(const mode of ['text','binary','legacy']){
    for(const kind of Object.keys(defaults)){
      const version=kind==='AcisEntity'?16:18;
      parse(`${mode}/${kind}/default`,mode,kind,defaults[kind],version,{entityCode:'BODY'},true);
      parse(`${mode}/${kind}/xdata`,mode,kind,[...defaults[kind],...xdata],version,{entityCode:'REGION'},true);
      parse(`${mode}/${kind}/missing`,mode,kind,[],version,{entityCode:'BODY'});
      parse(`${mode}/${kind}/wrong-subclass`,mode,kind,[[100,'Wrong']],version,{entityCode:'BODY'});
      add(`${mode}/${kind}/eof`,mode,kind,defaults[kind],[op('next'),op('read')],version,{entityCode:'BODY'});
    }
    for(const kind of ['OleFrame','Ole2Frame']){
      const marker=defaults[kind][0];
      for(const length of [0,1,2,126,127,128,129,254,255,256,4097])for(const explicit of [false,true]){
        const parts=[];for(let i=0;i<length;i+=127)parts.push([310,{pattern:Math.min(127,length-i)}]);
        parse(`${mode}/${kind}/packet/${length}/${explicit}`,mode,kind,[marker,...(explicit?[[70,3]]:[]),[90,length],...parts,[1,'OLE']],18,{},true);
      }
      const bad={negative:[[90,-1]],huge:[[90,2147483647],[1,'OLE']],missing:[[310,{bytes:[1]}],[1,'OLE']],duplicate:[[90,0],[90,0],[1,'OLE']],lengthBefore:[[90,1],[310,{bytes:[1,2]}],[1,'OLE']],lengthAfter:[[310,{bytes:[1,2]}],[90,1],[1,'OLE']],noTerm:[[90,0]],badTerm:[[90,0],[1,'OTHER']],after:[[90,0],[1,'OLE'],[70,1]],version:[[70,-1],[90,0],[1,'OLE']],versionTwice:[[70,1],[70,1],[90,0],[1,'OLE']],overChunk:[[90,128],[310,{pattern:128}],[1,'OLE']],emptyChunk:[[90,0],[310,{bytes:[]}],[1,'OLE']],xdataEarly:[[1001,'EARLY'],[90,0],[1,'OLE']],private:[[90,0],[102,'private'],[1,'OLE']]};
      for(const [name,tags]of Object.entries(bad))parse(`${mode}/${kind}/invalid/${name}`,mode,kind,[marker,...tags]);
    }
    for(let flags=0;flags<64;flags++){
      const metadata=[[70,3],[3,'A\\U+005CB\\U+03A9'],[10,-0],[20,3],[30,4],[11,5],[21,6],[31,7],[71,1],[72,1]];
      const bits=[1,2,4,4,4,8,8,8,16,32];
      parse(`${mode}/Ole2Frame/fields/${flags}`,mode,'Ole2Frame',[defaults.Ole2Frame[0],...metadata.filter((_,i)=>flags&bits[i]),[90,1],[310,{bytes:[0xFF]}],[1,'OLE']],13+(flags%6),{},true);
    }
    for(const [name,values]of Object.entries({partial:[[10,1]],partialY:[[20,1],[30,2]],partialLower:[[11,1],[31,3]],reorder:[[30,1],[10,2],[20,3]],duplicate:[[10,1],[20,2],[30,3],[10,4]],badType:[[71,0]],badType4:[[71,4]],badTile:[[72,2]],negativeVersion:[[70,-1]],decodedDelimiter:[[3,'x\\U+000Ax']]}))parse(`${mode}/Ole2Frame/meta/${name}`,mode,'Ole2Frame',[defaults.Ole2Frame[0],...values,[90,0],[1,'OLE']]);
    const lightValues=[[90,2],[1,'Light\\U+005Cname\\U+03A9'],[70,3],[290,true],[291,false],[40,1.25],[10,-0],[20,2],[30,3],[11,4],[21,5],[31,6],[72,2],[292,true],[41,4],[42,8],[50,30],[51,60],[293,false],[73,1],[91,1024],[280,2]];
    for(const version of [13,14,15,16,17,18])for(const where of ['model','paper','unused'])add(`${mode}/Light/placement/${version}/${where}`,mode,'Light',[defaults.Light[0],...lightValues,[0,'ENDSEC']],[op('next'),op('read'),{method:'place',where},op('validate'),op('write')],version);
    for(const [code,v]of lightValues){parse(`${mode}/Light/duplicate/${code}`,mode,'Light',[defaults.Light[0],[code,v],[code,v]]);}
    for(const code of [10,20,30,11,21,31])parse(`${mode}/Light/partial/${code}`,mode,'Light',[defaults.Light[0],[code,2]]);
    for(const [code,v]of [[90,-1],[70,0],[70,4],[40,-1],[72,-1],[72,3],[41,-1],[42,-1],[50,-1],[50,181],[51,-1],[51,181],[73,-1],[73,2],[91,0],[280,-1],[280,256],[1,'a\\U+000Ab'],[100,'Other'],[101,'Other'],[102,'Other'],[1000,'unprefixed'],[300,'ignored payload']])parse(`${mode}/Light/invalid/${code}/${JSON.stringify(v)}`,mode,'Light',[defaults.Light[0],[code,v]]);
    const poly=[[100,'AcDbPolyline'],[90,2],[70,129],[38,3],[39,4],[43,-0],[10,1],[20,2],[91,3],[40,0],[41,2],[42,.5],[10,3],[20,4],[42,-.25],[210,0],[220,0],[230,2]];
    for(const version of [13,14,15,16,17,18])for(const where of ['model','paper','unused'])add(`${mode}/LwPolyline/placement/${version}/${where}`,mode,'LwPolyline',[...poly,[0,'ENDSEC']],[op('next'),op('read'),{method:'place',where},op('validate'),op('write')],version);
    for(const [name,tags]of Object.entries({noCount:[],negative:[[90,-1]],huge:[[90,2147483647]],repeatCount:[[90,0],[90,0]],noY:[[90,1],[10,2]],duplicateY:[[90,1],[10,1],[20,2],[20,3]],Yfirst:[[90,1],[20,1],[10,2]],Xfirst:[[90,2],[10,1],[10,2],[20,3]],earlyId:[[90,0],[91,3]],doubleId:[[90,1],[10,1],[20,2],[91,3],[91,4]],earlyWidth:[[90,0],[40,0]],negativeWidth:[[90,1],[10,1],[20,2],[40,-1]],duplicateWidth:[[90,1],[10,1],[20,2],[40,0],[40,0]],negativeConstant:[[90,0],[43,-1]],doubleConstant:[[90,0],[43,0],[43,0]],repeatedBulge:[[90,1],[10,1],[20,2],[42,.1],[42,.2]],zeroNormal:[[90,0],[210,0],[220,0],[230,0]]}))parse(`${mode}/LwPolyline/invalid/${name}`,mode,'LwPolyline',[defaults.LwPolyline[0],...tags]);
    for(const entityCode of ['BODY','REGION','3DSOLID'])for(const version of [13,14,15,16,17,18]){
      const values=[...defaults.AcisEntity,...(entityCode==='3DSOLID'&&version>=15?[[100,'AcDb3dSolid'],[350,'0']]:[])];
      parse(`${mode}/ACIS/profile/${entityCode}/${version}`,mode,'AcisEntity',values,version,{entityCode},version<17);
      if(version<17)add(`${mode}/ACIS/unused/${entityCode}/${version}`,mode,'AcisEntity',[...values,[0,'ENDSEC']],[op('next'),op('read'),{method:'place',where:'unused'},op('validate'),op('write')],version,{entityCode});
    }
    for(const [name,tags]of Object.entries({noVersion:[[1,'abc']],versionTwice:[[70,1],[70,1],[1,'abc']],version2:[[70,2],[1,'abc']],noParts:[[70,1]],continuation:[[70,1],[3,'abc']],emptyPart:[[70,1],[1,'']],unicode:[[70,1],[1,'Ω']],longPart:[[70,1],[1,'x'.repeat(256)]],badA:[[70,1],[1,'^']],liveHistory:[[70,1],[1,'abc'],[100,'AcDb3dSolid'],[350,'A']],historyBefore:[[70,1],[350,'0'],[1,'abc']],historyTwice:[[70,1],[1,'abc'],[100,'AcDb3dSolid'],[350,'0'],[350,'0']],dataAfter:[[70,1],[1,'abc'],[1001,'APP'],[1000,'value'],[1,'abc']],unknown:[[70,1],[1,'abc'],[95,3]]}))parse(`${mode}/ACIS/invalid/${name}`,mode,'AcisEntity',[[100,'AcDbModelerGeometry'],...tags],16,{entityCode:'3DSOLID'});
    let state=0x7a66c901;const random=()=>(state=(Math.imul(state,1664525)+1013904223)>>>0);
    for(let run=0;run<48;run++){
      const count=random()%30,tags=[[100,'AcDbPolyline'],[90,count],[70,random()%256]];
      for(let i=0;i<count;i++){tags.push([10,(random()-2147483648)/8192],[20,(random()-2147483648)/8192],[42,(random()-2147483648)/4294967296]);if(random()%2)tags.push([40,random()/4294967296]);if(random()%2)tags.push([41,random()/4294967296]);if(random()%2)tags.push([91,random()|0]);}
      parse(`${mode}/LwPolyline/random/${run}`,mode,'LwPolyline',tags,18,{},true);
    }
  }
  return all;
}
