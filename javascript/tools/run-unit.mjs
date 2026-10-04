import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { javascriptRoot } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
const proof={runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()};
const files=fs.readdirSync(path.join(javascriptRoot,'tests/unit')).filter(f=>f.endsWith('.js')).sort().map(f=>'tests/unit/'+f);
const result=spawnSync(process.execPath,['--test',...files],{cwd:javascriptRoot,encoding:'utf8'});
const log=(result.stdout||'')+(result.stderr||'');process.stdout.write(log);
const count=name=>Number(new RegExp('^# '+name+' (\\d+)$','m').exec(log)?.[1] ?? NaN);
const completed=result.status===0 && count('tests')>0 && count('fail')===0 && count('skipped')===0 && count('todo')===0 &&
  proof.runtimeFingerprint===runtimeFingerprint() && proof.verificationFingerprint===verificationFingerprint();
const dir=path.join(javascriptRoot,'artifacts/unit');fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,'tests.log'),log);
fs.writeFileSync(path.join(dir,'results.json'),JSON.stringify({...proof,completed,tests:count('tests'),passed:count('pass'),failed:count('fail'),skipped:count('skipped'),todo:count('todo')},null,2)+'\n');
if(!completed) throw result.error || new Error('Native JavaScript unit verification failed.');
