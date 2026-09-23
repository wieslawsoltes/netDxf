// Run every independent qualification even after a mismatch; never turn expected failures green.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { javascriptRoot, configuration } from './dotnet.mjs';
const commands=[
  ['table-geometry',['tools/table-geometry-differential.mjs']],
  ['table-styles',['tools/table-style-differential.mjs']],
  ['section-manager',['tools/section-manager-differential.mjs']],
  ['retained-polylines',['tools/retained-polyline-differential.mjs']],
  ['registered-annotations',['tools/registered-annotations-differential.mjs']],
  ['document-ownership',['tools/document-ownership-differential.mjs']],
  ['drawing-time',['tools/drawing-time-differential.mjs']],
  ['string-enum',['tools/string-enum-differential.mjs']],
  ['object-references',['tools/object-reference-differential.mjs']],
  ['concrete-dimensions',['tools/concrete-dimension-differential.mjs']],
  ['observable-dictionaries',['tools/observable-dictionary-differential.mjs']],
  ['leaders',['tools/leader-differential.mjs']],
  ['tolerances',['tools/tolerance-differential.mjs']],
  ['unit-formats',['tools/unit-format-differential.mjs']],
  ['mleaders',['tools/mleader-differential.mjs']],
  ['dimensions',['tools/dimension-differential.mjs']],
  ['headers',['tools/header-differential.mjs']],
  ['geodata-vba',['tools/geodata-vba-differential.mjs']],
  ['layout-viewport',['tools/layout-viewport-differential.mjs']],
  ['blocks',['tools/block-differential.mjs']],
  ['mlines',['tools/mline-differential.mjs']],
  ['output-settings',['tools/output-settings-differential.mjs']],
  ['groups',['tools/group-differential.mjs']],
  ['surfaces',['tools/surface-differential.mjs']],
  ['exp-log',['tools/exp-log-differential.mjs']],
  ['nurbs',['tools/nurbs-differential.mjs']],
  ['coordinates',['tools/coordinate-differential.mjs']],
  ['database-models',['tools/database-model-differential.mjs']],
  ['math',['tools/math-differential.mjs']],
  ['reference-math',['tools/reference-math-differential.mjs']],
  ['entities',['tools/entity-differential.mjs']],
  ['styles',['tools/style-differential.mjs']],
  ['hatch',['tools/hatch-differential.mjs']],
  ['lifecycle',['tools/lifecycle-differential.mjs']],
  ['raw',['tools/differential.mjs']], ['handles',['tools/handle-differential.mjs']],
  ['objects',['tools/object-differential.mjs']], ['casing',['tools/casing-differential.mjs']],
  ['geometry',['tools/geometry-differential.mjs']], ['collections',['tools/collection-differential.mjs']],
  ['foundations',['tools/foundations-differential.mjs']], ['exact-geometry',['tools/geometry-stress.mjs']],
  ['filesystem',['tools/filesystem-differential.mjs']], ['unit-factors',['tools/unit-factors.mjs','--check']],
];
const out=path.join(javascriptRoot,'artifacts','qualification',configuration);fs.mkdirSync(out,{recursive:true});
const results=[];
for(const [name,args] of commands){
  const fd=fs.openSync(path.join(out,name+'.log'),'w');let result;
  try{result=spawnSync(process.execPath,args,{cwd:javascriptRoot,stdio:['ignore',fd,fd],timeout:600000});}
  finally{fs.closeSync(fd);}
  results.push({name,passed:!result.error&&result.status===0,status:result.status,signal:result.signal,error:result.error?.message??null});
  console.log(name+': '+(results.at(-1).passed?'PASS':'FAIL')+'; '+path.join('artifacts/qualification',configuration,name+'.log'));
}
fs.writeFileSync(path.join(out,'commands.json'),JSON.stringify(results,null,2)+'\n');
if(results.some(r=>!r.passed))process.exitCode=1;
