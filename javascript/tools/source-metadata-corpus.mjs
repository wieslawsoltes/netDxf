// Input-only common-header/source-identity probes, including discarded and ambiguous records.
import {entityBodyRow as tag} from './entity-body-io-corpus.mjs';
const h=name=>({h:name});const command=method=>({method});
export function sourceMetadataCorpus(){
  const all=[];
  const add=(name,tags,mode='text',steps=null)=>{
    if(steps===null){
      let target='line';steps=[];
      for(const [code,value]of tags){
        steps.push(command('next'));
        if(code===0)target=value==='DICTIONARY'?'root':value==='DIMSTYLE'?'style':'line';
        if(code===100||code===1001)steps.push({method:'accept',target});
        if(code===0||code===100)steps.push(command('validate'),{method:'lookup',handle:h(target)},{method:'dictionary',handle:h(target)});
      }
      steps.push(command('next'));
    }
    all.push({name:'source-metadata/'+name+'/'+mode,request:{op:'source-metadata-io',mode,tags:tags.map(([c,v])=>tag(c,v)),steps}});
  };
  const frame=[[0,'SECTION'],[2,'ENTITIES'],[0,'LINE'],[5,h('line')],[102,'{ACAD_REACTORS'],[330,'AB'],[102,'}'],[102,'{ACAD_XDICTIONARY'],[360,'CD'],[102,'}'],[100,'AcDbEntity'],[8,'Layer'],[100,'AcDbLine'],[10,1],[20,2],[30,3],[0,'ENDSEC'],[0,'EOF']];
  for(const mode of ['text','binary','legacy']){
    add('complete',frame,mode);
    for(let i=0;i<frame.length;i++){
      add('prefix/'+i,frame.slice(0,i),mode);
      add('missing/'+i,frame.filter((_,j)=>j!==i),mode);
      add('duplicate/'+i,frame.flatMap((v,j)=>j===i?[v,v]:[v]),mode);
    }
    for(const section of ['HEADER','TABLES','BLOCKS','ENTITIES','OBJECTS','CLASSES','PRIVATE'])for(const type of ['LINE','SECTION','TABLE','CLASS','ENDTAB','DICTIONARY','DIMSTYLE']){
      const target=type==='DIMSTYLE'?'style':type==='DICTIONARY'?'root':'line',code=type==='DIMSTYLE'?105:5;
      add('location/'+section+'/'+type,[[0,'SECTION'],[2,section],[0,type],[code,h(target)],[102,'{ACAD_REACTORS'],[330,'D00'],[102,'}'],[100,'AcDbObject'],[0,'ENDSEC'],[0,'EOF']],mode);
    }
    for(const firstType of ['LINE','DICTIONARY','UNKNOWN'])for(const secondType of ['LINE','UNKNOWN','DIMSTYLE'])for(const key of [h('line'),{h:'line',pad:3,lower:true}]){
      add('collision/'+firstType+'/'+secondType+'/'+JSON.stringify(key),[[0,'SECTION'],[2,'ENTITIES'],[0,firstType],[5,h('line')],[100,'AcDbEntity'],[0,secondType],[secondType==='DIMSTYLE'?105:5,key],[100,'AcDbEntity'],[0,'ENDSEC'],[0,'EOF']],mode);
    }
    const groups=[[[102,'{PRIVATE'],[330,'AE'],[102,'}']],[[102,'{ACAD_REACTORS'],[330,'AE'],[102,'{NESTED'],[330,'BE'],[102,'}'],[330,'CE'],[102,'}']],[[102,'{ACAD_XDICTIONARY'],[360,'AE'],[360,'BE'],[102,'}']],[[102,'}']],[[102,'{PRIVATE'],[100,'Subclass'],[5,'DE'],[102,'}']],[[102,'{PRIVATE'],[0,'NEXT']],[[102,'{ACAD_REACTORS'],[330,'0'],[330,'0'],[102,'}']]];
    groups.forEach((g,i)=>add('groups/'+i,[[0,'SECTION'],[2,'OBJECTS'],[0,'LINE'],[5,h('line')],...g,[100,'AcDbXrecord'],[102,'{ACAD_REACTORS'],[330,'FF'],[102,'}'],[5,'A5'],[0,'ENDSEC'],[0,'EOF']],mode));
    // A record accepted before a later duplicate must become ambiguous retroactively.
    add('retroactive-ambiguity',[[0,'SECTION'],[2,'ENTITIES'],[0,'LINE'],[5,h('line')],[100,'AcDbEntity'],[0,'UNKNOWN'],[5,h('line')],[100,'Private'],[0,'ENDSEC'],[0,'EOF']],mode);
    add('retired-identity',frame,mode,[...frame.map(()=>command('next')).slice(0,11),{method:'accept',target:'line'},{method:'lookup',handle:h('line')},{method:'replace',target:'line'},{method:'lookup',handle:h('line')},command('validate')]);
  }
  const lookupSteps=frame.slice(0,11).map(()=>command('next')).concat([{method:'accept',target:'line'}]);
  for(const value of [null,'','22','00000000000000000022','22\0','22\0\0','0x22',' 22','22 ','22\t','22\n','22G','ffffffffffffffff','10000000000000000','0','00'])
    add('lexical/'+JSON.stringify(value),frame,'text',[...lookupSteps,{method:'lookup',handle:value},{method:'dictionary',handle:value}]);
  for(const skip of [true,false]){
    const tags=frame.flatMap((v,i)=>i%3===0?[[999,'comment'],v]:[v]);
    add('comments/'+skip,tags,'text',[{method:'skip',value:skip},...tags.map(()=>command('next'))]);
  }
  for(const member of ['ReadByte','ReadBytes','ReadShort','ReadInt','ReadLong','ReadBool','ReadDouble','ReadString','ReadHex'])
    add('forward-cast/'+member,[[0,'SECTION'],[2,'HEADER'],[70,2],[40,-0],[310,{bytes:[0,255]}],[0,'ENDSEC'],[0,'EOF']],'text',Array.from({length:7},()=>[command('next'),{method:'cast',member}]).flat());
  return all;
}
