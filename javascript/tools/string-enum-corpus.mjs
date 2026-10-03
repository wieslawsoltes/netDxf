// Inputs derived from enum declarations, never from native helper results.
import * as enums from '../Enums.generated.js';
export function stringEnumCorpus() {
  const corpus=[];
  const add=(name, category, type, steps, culture='')=>corpus.push({name:'string-enum/'+name,category,request:{op:'string-enum',enum:type,culture,steps}});
  for(const [name,type] of Object.entries(enums)) {
    if(name.endsWith('StringValues')||typeof type!=='object'||type===null)continue;
    const steps=['EnumType','GetStringValues','GetValues'].map(method=>({method}));
    for(const value of new Set([...Object.values(type),-2147483648,-1,0,2147483647]))steps.push({method:'GetStringValue',value});
    for(const value of [null,'','unknown'])for(const comparison of [-1,0,1,2,3,4,5,6])
      for(const method of ['Parse','IsStringDefined'])steps.push({method,value,comparison});
    for(const value of [null,'','label'])steps.push({method:'Attribute',value});
    add(name,'all-types',name,steps);
  }
  for(const name of ['DxfVersion','HatchGradientPatternType']) {
    const values=Object.values(enums[name+'StringValues']);
    const strings=new Set([null,'','missing','\0','İ','ı','ſ','ß','K','ＡＣ１０３２','LINEA\u0301R']);
    for(const value of values) for(const transformed of [value,value.toLowerCase(),value.toUpperCase(),' '+value,value+' ',value+'\0',value.slice(0,2)+'\0'+value.slice(2),value+'\u00ad',value.replace(/[!-~]/g,c=>String.fromCharCode(c.charCodeAt(0)+0xfee0)),value.replace(/[A-Za-z]/g,c=>String.fromCharCode(c.charCodeAt(0)+0xfee0))])strings.add(transformed);
    for(const culture of ['', 'en-US', 'tr-TR', 'pl-PL', 'ja-JP'])for(const comparison of [undefined,-1,0,1,2,3,4,5,6]) {
      const steps=[];
      for(const value of strings)for(const method of ['Parse','IsStringDefined'])steps.push({method,value,...(comparison===undefined?{}:{comparison})});
      add(`${name}/${culture||'invariant'}/${comparison??'default'}`,'comparisons',name,steps,culture);
    }
  }
  return corpus;
}
