# Focused database geometry browser qualification. The complete browser gates
# remain separate and cannot be satisfied by this report.
"""Real Chromium ESM execution without page navigation or network access.

Only relative module specifiers are rewritten to an import map. Native JavaScript
modules (including their cycles) execute in Chromium, not in a Node/mock DOM.
SHA-256 alone is supplied by the host because about:blank is not a secure context.
This does not replace the separately required HTTP-origin browser qualification.
"""
from pathlib import Path
import hashlib
import json
import os
import posixpath
import re
import shutil
import subprocess
from playwright.sync_api import sync_playwright
from browser_json_transport import transfer_json_payloads

ROOT=Path(__file__).resolve().parent.parent
IMPORT=re.compile(r"""(?P<prefix>\b(?:from\s*|import\s*))(?P<quote>['"])(?P<name>\.[^'"]+)(?P=quote)""")
sources={}
def load(name):
    if name in sources:
        return
    file=(ROOT/name).resolve()
    if ROOT not in file.parents or file.suffix not in ('.js','.mjs'):
        raise ValueError('Unexpected browser module: '+name)
    sources[name]=''
    def replace(match):
        target=posixpath.normpath(posixpath.join(posixpath.dirname(name),match['name']))
        load(target)
        return match['prefix']+match['quote']+'netdxf:'+target+match['quote']
    sources[name]=IMPORT.sub(replace,file.read_text(encoding='utf-8'))

def fingerprints():
    return json.loads(subprocess.check_output([
        os.environ.get('NODE','node'),'--input-type=module','-e',
        "import {runtimeFingerprint,verificationFingerprint} from './tools/evidence.mjs'; console.log(JSON.stringify({runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()}));"
    ],cwd=ROOT,text=True))

configuration=os.environ.get('CONFIGURATION','Release')
report={'scope':'database-io-only','fullSuite':False,'completed':False,'executionMode':'inline-native-esm','digestProvider':'host-sha256','inputTransport':'chunked-json','inputChunkCharacters':262144}
try:
    proof=fingerprints()
    corpus=json.loads((ROOT/('artifacts/database-browser/'+configuration+'/corpus.json')).read_text(encoding='utf-8'))
    if corpus['configuration'] != configuration or any(corpus.get(k)!=v for k,v in proof.items()):
        raise ValueError('Stale browser corpus or wrong build configuration.')
    native=json.loads((ROOT/'native-port-manifest.json').read_text(encoding='utf-8'))
    load('tools/browser-runner.mjs')
    with sync_playwright() as playwright:
        executable=os.environ.get('CHROMIUM') or shutil.which('chromium')
        browser=playwright.chromium.launch(headless=True,**({'executable_path':executable} if executable else {}))
        try:
            page=browser.new_page()
            errors=[]
            page.on('pageerror',lambda error:errors.append(str(error)))
            page.expose_function('netDxfHashCanonical',lambda text:hashlib.sha256(text.encode('utf-8')).hexdigest())
            # Keep DevTools messages bounded; reassemble the unchanged JSON documents
            # in the renderer before executing any corpus operation.
            transfer_json_payloads(page, {'sources':sources, 'corpus':corpus, 'native':native})
            result=page.evaluate("""async ()=>{
              let payloads;
              try {
                payloads=Object.fromEntries(Object.entries(globalThis.netDxfJsonPayloads).map(([name,entry])=>{
                  const text=entry.chunks.join('');
                  if(text.length!==entry.length)throw new Error('Incomplete browser JSON payload: '+name);
                  return [name,JSON.parse(text)];
                }));
              } finally { delete globalThis.netDxfJsonPayloads; }
              const {sources,corpus,native}=payloads;
              const imports={},urls=[];
              try {
                for(const [name,source] of Object.entries(sources)){
                  const url=URL.createObjectURL(new Blob([source],{type:'text/javascript'}));
                  urls.push(url);imports['netdxf:'+name]=url;
                }
                const map=document.createElement('script');map.type='importmap';map.textContent=JSON.stringify({imports});document.head.appendChild(map);
                const {runBrowserCorpus}=await import('netdxf:tools/browser-runner.mjs');
                return await runBrowserCorpus(corpus,native,{hashCanonical:window.netDxfHashCanonical});
              } finally { for(const url of urls)URL.revokeObjectURL(url); }
            }""")
            report.update(result)
            if report.get('comparisons') != 1490:
                report['completed']=False
                report['fatal']='Incomplete database browser comparison count.'
            report.update({'browser':browser.version,'modules':len(sources),'pageErrors':errors})
            if errors or fingerprints()!=proof:
                report['completed']=False
                report['fatal']='Browser page error or executable/verifier source changed during execution.'
        finally:
            browser.close()
except Exception as error:
    report['completed']=False
    report['fatal']=repr(error)
finally:
    output=ROOT/'artifacts/database-browser'/configuration/'results.json'
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,indent=2,ensure_ascii=True)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2,ensure_ascii=True))
if not report.get('completed') or report.get('failures') or report.get('fatal'):
    raise SystemExit('Inline native-browser differential verification failed.')
