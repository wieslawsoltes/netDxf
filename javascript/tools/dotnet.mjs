// Offline, package-free .NET oracle build using the Roslyn compiler supplied with .NET 8.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
export const javascriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const sourceRoot = path.resolve(process.env.NETDXF_SOURCE_ROOT || path.join(javascriptRoot, '..'));
export const configuration = process.env.CONFIGURATION || 'Release';
export const baseline = JSON.parse(fs.readFileSync(path.join(javascriptRoot,'baseline.json'),'utf8'));
export const oracleRoot = path.join(javascriptRoot, 'artifacts', 'oracle', configuration);
const command = process.env.DOTNET || (process.env.DOTNET_ROOT ? path.join(process.env.DOTNET_ROOT, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet') : 'dotnet');
export function run(executable, args, options = {}) {
  const result = spawnSync(executable, args, { cwd: sourceRoot, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024, ...options });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${executable} failed (${result.status})\n${result.stdout || ''}\n${result.stderr || ''}`);
  return result.stdout;
}
function toolchain() {
  const rows = run(command, ['--list-sdks']).trim().split(/\r?\n/)
    .map(line => /^(8\.\d+\.\d+) \[(.*)\]$/.exec(line)).filter(Boolean)
    .sort((a,b) => a[1].localeCompare(b[1], 'en', { numeric: true }));
  if (!rows.length) throw new Error('The differential tools require the .NET 8 SDK. Production JavaScript does not.');
  const selected = rows.find(row=>row[1] === baseline.toolchain.sdk);
  if (!selected) throw new Error(`Install pinned SDK ${baseline.toolchain.sdk}; comparison evidence cannot silently roll forward.`);
  const root = path.dirname(selected[2]);
  const packRoot = path.join(root, 'packs', 'Microsoft.NETCore.App.Ref');
  const refVersion = baseline.toolchain.referencePack;
  if (!fs.existsSync(path.join(packRoot,refVersion))) throw new Error(`Missing pinned reference pack ${refVersion}.`);
  if (!fs.existsSync(path.join(root,'shared','Microsoft.NETCore.App',baseline.toolchain.runtime))) throw new Error(`Missing pinned runtime ${baseline.toolchain.runtime}.`);
  if (!refVersion) throw new Error('Missing .NET 8 reference pack. No NuGet/network fallback is attempted.');
  return { root, sdkVersion: selected[1], refVersion, compiler: path.join(selected[2],selected[1],'Roslyn','bincore','csc.dll'), refs: path.join(packRoot,refVersion,'ref','net8.0') };
}
export function walk(root) {
  return fs.readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    if (['bin','obj','.git','artifacts','node_modules'].includes(entry.name)) return [];
    const filename = path.join(root,entry.name);
    return entry.isDirectory() ? walk(filename) : entry.isFile() ? [filename] : [];
  }).sort();
}
function compile(tool, name, files, executable = false, extra = []) {
  fs.mkdirSync(oracleRoot, { recursive: true });
  const flags = ['-nologo', `-target:${executable ? 'exe' : 'library'}`, '-langversion:latest', '-deterministic+',
    configuration === 'Debug' ? '-optimize-' : '-optimize+', `-define:${configuration === 'Debug' ? 'DEBUG;TRACE' : 'TRACE'}`,
    `-out:${path.join(oracleRoot,name+'.dll')}`, '-nowarn:1591',
    ...fs.readdirSync(tool.refs).filter(f=>f.endsWith('.dll')).map(f=>'-r:'+path.join(tool.refs,f)), ...extra, ...files];
  const response = path.join(oracleRoot,name+'.rsp');
  fs.writeFileSync(response, flags.map(arg=>'"'+arg+'"').join('\n'));
  const result = spawnSync(command, [tool.compiler,'@'+response], { cwd: sourceRoot, encoding:'utf8', maxBuffer:32*1024*1024 });
  fs.writeFileSync(path.join(oracleRoot,name+'.build.log'), (result.stdout || '')+(result.stderr || ''));
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Compilation failed for ${name}; see ${name}.build.log.\n${result.stdout}`);
  fs.writeFileSync(path.join(oracleRoot,name+'.runtimeconfig.json'), JSON.stringify({runtimeOptions:{tfm:'net8.0',rollForward:'Disable',framework:{name:'Microsoft.NETCore.App',version:baseline.toolchain.runtime}}}));
  console.log(`${configuration}: compiled ${name} (${files.length} source files).`);
}
export function build(mode = 'oracle') {
  if (!['Debug','Release'].includes(configuration)) throw new Error('CONFIGURATION must be Debug or Release.');
  const tool = toolchain();
  compile(tool,'netDxf.netstandard',walk(path.join(sourceRoot,'netDxf')).filter(f=>f.endsWith('.cs')),false,['-keyfile:'+path.join(sourceRoot,'netDxf','netDxf.snk')]);
  const library = '-r:'+path.join(oracleRoot,'netDxf.netstandard.dll');
  if (mode === 'inventory') {
    const roslyn = path.dirname(tool.compiler);
    const names = ['Microsoft.CodeAnalysis','Microsoft.CodeAnalysis.CSharp'];
    compile(tool,'SourceInventory',[path.join(javascriptRoot,'tools','SourceInventory','Program.cs')],true,[library,'-nullable:enable',...names.map(n=>'-r:'+path.join(roslyn,n+'.dll'))]);
    for (const name of names) fs.copyFileSync(path.join(roslyn,name+'.dll'),path.join(oracleRoot,name+'.dll'));
    console.log(run(command,[path.join(oracleRoot,'SourceInventory.dll'),sourceRoot,path.join(oracleRoot,'netDxf.netstandard.dll'),javascriptRoot,...(process.argv.includes('--generate')?['--generate']:[])]).trim());
  } else if (mode === 'conformance') {
    const globalUsings = path.join(oracleRoot,'GlobalUsings.cs');
    fs.writeFileSync(globalUsings,['System','System.Collections.Generic','System.IO','System.Linq','System.Net.Http','System.Threading','System.Threading.Tasks'].map(n=>`global using ${n};`).join('\n'));
    compile(tool,'netDxf.Conformance',[...walk(path.join(sourceRoot,'tests','netDxf.Conformance')).filter(f=>f.endsWith('.cs')),globalUsings],true,[library,'-nullable:enable']);
    const artifactPath = process.env.DXF_TEST_ARTIFACTS || path.join(javascriptRoot,'artifacts','dotnet-'+configuration.toLowerCase());
    const log = path.join(oracleRoot,'conformance.log'), fd = fs.openSync(log,'w');
    try { run(command,[path.join(oracleRoot,'netDxf.Conformance.dll')],{stdio:['ignore',fd,fd],env:{...process.env,DXF_TEST_ARTIFACTS:artifactPath}}); }
    finally { fs.closeSync(fd); }
    console.log(fs.readFileSync(log,'utf8').trim().split(/\r?\n/).at(-1));
  } else if (mode === 'oracle') {
    compile(tool,'Oracle',[path.join(javascriptRoot,'tools','Oracle','Program.cs')],true,[library,'-nullable:enable']);
  } else throw new Error('Usage: node tools/dotnet.mjs oracle|inventory [--generate]|conformance');
  fs.writeFileSync(path.join(oracleRoot,'toolchain.json'),JSON.stringify({configuration,sdk:tool.sdkVersion,referencePack:tool.refVersion,runtime:baseline.toolchain.runtime,sourceRoot,framework:'.NET 8',build:'SDK Roslyn csc; no NuGet restore'},null,2)+'\n');
}
export const dotnetCommand = command;
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) build(process.argv[2]);
