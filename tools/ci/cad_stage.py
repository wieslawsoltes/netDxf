"""Temporary branch-local source transport and reproducible build-tool capture.
Not part of the final feature tree. Never modifies the default branch or workflows.
"""
import hashlib, json, os, pathlib, subprocess, sys, tarfile, urllib.request
REPO = 'wieslawsoltes/netDxf'
BRANCH = 'workbench/cad-block-authoring'
def git(*args):
 return subprocess.check_output(['git', *args], text=True).strip()
def api(path, value=None, method=None):
 request=urllib.request.Request('https://api.github.com/repos/'+REPO+'/'+path,
  data=json.dumps(value).encode() if value is not None else None,
  headers={'Authorization':'Bearer '+os.environ['GH_TOKEN'], 'Accept':'application/vnd.github+json',
           'Content-Type':'application/json','X-GitHub-Api-Version':'2022-11-28'}, method=method)
 with urllib.request.urlopen(request, timeout=45) as result: return json.load(result)
def source():
 assert os.environ['GITHUB_REPOSITORY']==REPO and os.environ['GITHUB_REF']=='refs/heads/'+BRANCH
 original=os.environ['GITHUB_SHA']; assert git('rev-parse','HEAD')==original
 assert api('git/ref/heads/'+BRANCH)['object']['sha']==original
 payload=pathlib.Path('.github/cad-stage.json').read_bytes()
 if len(payload)>1048576: raise ValueError('Transport size limit')
 manifest=json.loads(payload)
 if not isinstance(manifest,list) or not 1<=len(manifest)<=128: raise ValueError('Invalid path count')
 entries=[]; paths=[]
 def blob(value): return hashlib.sha1(b'blob '+str(len(value)).encode()+b'\0'+value).hexdigest()
 for entry in manifest:
  path=entry['path']; local=pathlib.Path(path)
  if not path.startswith('shadow/') or '..' in local.parts or '\\' in path or local.is_absolute() or local.is_symlink():
   raise ValueError('Non-source transport path')
  if path in paths: raise ValueError('Duplicate path')
  original_bytes=local.read_bytes()
  if len(original_bytes)>2097152 or blob(original_bytes)!=entry['before']: raise ValueError('Source preimage mismatch: '+path)
  lines=original_bytes.decode('utf-8').splitlines(keepends=True)
  end=0
  for edit in entry['edits']:
   first,count=edit['line'],edit['count']
   if not isinstance(first,int) or not isinstance(count,int) or first<end or count<0 or first+count>len(lines):
    raise ValueError('Overlapping or invalid edit')
   end=first+count
  for edit in reversed(entry['edits']): lines[edit['line']:edit['line']+edit['count']]=edit['text'].splitlines(keepends=True)
  updated=''.join(lines).encode('utf-8')
  if len(updated)>2097152 or blob(updated)!=entry['after']: raise ValueError('Source result mismatch: '+path)
  local.write_bytes(updated); paths.append(path)
  entries.append({'path':path,'mode':'100644','type':'blob','content':updated.decode('utf-8')})
 subprocess.run(['git','add','--',*paths],check=True)
 if set(git('diff','--cached','--name-only').splitlines())!=set(paths): raise ValueError('Unexpected staged paths')
 tree=api('git/trees',{'base_tree':api('git/commits/'+original)['tree']['sha'],'tree':entries})['sha']
 assert tree==git('write-tree'), 'Uploaded content differs from checked source'
 commit=api('git/commits',{'message':'Stage reviewed block authoring source for validation','tree':tree,'parents':[original]})['sha']
 assert api('git/ref/heads/'+BRANCH)['object']['sha']==original
 api('git/refs/heads/'+BRANCH,{'sha':commit,'force':False},'PATCH')
 print('SOURCE_COMMIT='+commit+'\nSOURCE_TREE='+tree,flush=True)
 with open(os.environ['GITHUB_OUTPUT'],'a') as output: output.write('commit='+commit+'\n')
def tooling():
 output=pathlib.Path(os.environ['RUNNER_TEMP'])/'cad-build-tools';output.mkdir(exist_ok=True)
 version=subprocess.check_output(['dotnet','--version'],text=True).strip()
 with urllib.request.urlopen('https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json',timeout=45) as stream: metadata=json.load(stream)
 sdk=next(s for release in metadata['releases'] for s in release.get('sdks',[]) if s['version']==version)
 file=next(f for f in sdk['files'] if f['rid']=='linux-x64' and f['name'].endswith('.tar.gz'))
 assert file['url'].startswith('https://builds.dotnet.microsoft.com/dotnet/Sdk/')
 target=output/'sdk.tar.gz'; digest=hashlib.sha512(); length=0
 with urllib.request.urlopen(file['url'],timeout=90) as stream, target.open('wb') as out:
  while chunk:=stream.read(1024*1024):
   length+=len(chunk);assert length<512*1024*1024;digest.update(chunk);out.write(chunk)
 assert digest.hexdigest().lower()==file['hash'].lower(), 'Official SDK checksum mismatch'
 subprocess.run(['dotnet','restore','shadow/tests/netDxf.Shadow.AdvancedVerification/netDxf.Shadow.AdvancedVerification.csproj','-p:TargetFrameworks=net8.0'],check=True)
 with tarfile.open(output/'nuget.tar.gz','w:gz') as archive: archive.add(os.environ['NUGET_PACKAGES'],arcname='packages')
 (output/'manifest.json').write_text(json.dumps({'sdk':version,'sdkUrl':file['url'],'sdkSha512':file['hash'],
  'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir() if p.suffix=='.gz'}},indent=2))
 print('TOOLING='+str(output),flush=True)
if __name__=='__main__':
 {'source':source,'tooling':tooling}[sys.argv[1]]()
