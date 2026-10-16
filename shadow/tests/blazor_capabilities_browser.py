#!/usr/bin/env python3
"""Render every original sample layout and verify private TTF/OTF font registration."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import traceback
from playwright.sync_api import sync_playwright
from blazor_studio_browser import static_host, OUTPUT

VERIFY = r'''async () => {
  const results=[];
  const check=(name, value=true)=>{if(!value)throw new Error(name);results.push(name);};
  const identity=s=>({'X-Shadow-Studio':'1','X-Shadow-Document':s.documentId,'X-Shadow-Workspace':s.workspaceId,'X-Shadow-View':String(s.viewVersion)});
  async function request(path,body,headers={}) {
    const options={headers};
    if(body!==undefined){options.method='POST';options.headers={...headers,'X-Shadow-Studio':'1','Content-Type':'application/json'};options.body=JSON.stringify(body);}
    const response=await fetch(path,options);
    if(!response.ok)throw new Error(`${path}: ${response.status}: ${await response.text()}`);
    return response.json();
  }
  async function image(path,headers,kind='png') {
    const response=await fetch(path,{headers});
    if(!response.ok)throw new Error(`${path}: ${response.status}: ${await response.text()}`);
    const bytes=new Uint8Array(await response.arrayBuffer());
    check(`${kind} response has native image data`,bytes.length>100);
    if(kind==='png')check('PNG signature',bytes[0]===137&&bytes[1]===80&&bytes[2]===78&&bytes[3]===71);
    else check('SVG document',new TextDecoder().decode(bytes.slice(0,300)).includes('<svg'));
    return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))).map(b=>b.toString(16).padStart(2,'0')).join('');
  }
  let state=await request('api/state');
  const layouts=[...state.layouts];
  check('sample includes multiple capability layouts',layouts.length>5);
  for(const layout of layouts){
    state=await request('api/layout',{revision:state.revision,name:layout},identity(state));
    for(const backend of ['skia','svg']){
      await image(`api/render?width=800&height=600&backend=${backend}`,identity(state),backend==='skia'?'png':'svg');
      check(`original layout ${layout}: ${backend}`);
    }
  }
  state=await request('api/layout',{revision:state.revision,name:'Model'},identity(state));
  const originalDemo=await image('api/render?width=800&height=600&backend=skia',identity(state));
  const created=await request('api/cad/documents',{name:'Browser fonts.dxf'});
  const prefix=`api/cad/${created.id}`;
  let cad=await request(`${prefix}/state`);
  cad=await request(`${prefix}/command`,{revision:cad.state.revision,command:{name:'MTEXT',points:[{x:0,y:0,z:0}],layer:'0',value:5,secondary:100,text:'{\\fBrowserTest|b0|i0;Wiesław — font metrics 012345}'}},identity(cad.state));
  const render=()=>image(`${prefix}/render?width=800&height=600&cx=35&cy=-5&scale=8&backend=skia&revision=${cad.state.revision}`,identity(cad.state));
  const before=await render();
  const font=await(await fetch('fonts/NotoSans-Italic.ttf')).blob();
  let response=await fetch(`${prefix}/font?revision=${cad.state.revision}&name=BrowserTest.ttf`,{method:'POST',headers:identity(cad.state),body:font});
  if(!response.ok)throw new Error(await response.text());
  cad=await response.json();
  const after=await render();
  check('imported TTF file alias changes native glyph rendering',before!==after);
  state=await request('api/state');
  check('explicit TTF is private to its drawing',await image('api/render?width=800&height=600&backend=skia',identity(state))===originalDemo);
  const version=cad.state.changeVersion;
  response=await fetch(`${prefix}/font?revision=${cad.state.revision}&name=Broken.ttf`,{method:'POST',headers:identity(cad.state),body:new Uint8Array(32)});
  check('malformed font rejected',response.status===400);
  cad=await request(`${prefix}/state`);
  check('malformed font leaves document state untouched',cad.state.changeVersion===version);
  check('malformed font leaves rendering untouched',await render()===after);
  response=await fetch(`${prefix}/font?revision=${cad.state.revision+1}&name=Stale.ttf`,{method:'POST',headers:identity(cad.state),body:font});
  check('font registration preserves revision guards',response.status===409);
  check('browser font picker exposes TTF and OTF',document.getElementById('font-file').accept.includes('.ttf')&&document.getElementById('font-file').accept.includes('.otf'));
  const shx=new Blob(['*0,4,Browser strokes\n10,2,0,0\n*65,0,A\n8,10,0,0\n']);
  state=await request('api/state');
  response=await fetch(`api/font?revision=${state.revision}&name=browser.shp`,{method:'POST',headers:identity(state),body:shx});
  if(!response.ok)throw new Error(await response.text());
  state=await response.json();
  check('SHP parser and font registration remain available',state.fonts.includes('browser.shx'));
  return {layouts,checks:results};
}'''

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published',required=True,type=Path)
    args=parser.parse_args()
    OUTPUT.mkdir(parents=True,exist_ok=True)
    evidence={'runs':[],'failure':None}
    try:
        with static_host(args.published.resolve()) as (base,requests), sync_playwright() as playwright:
            browser=playwright.chromium.launch(headless=True)
            try:
                for mount in ['/', '/netDxf/']:
                    context=browser.new_context(viewport={'width':1600,'height':1000})
                    page=context.new_page()
                    try:
                        page.goto(base+mount+'diagnostics.html',wait_until='domcontentloaded')
                        page.wait_for_function('document.documentElement.dataset.shadowReady || document.documentElement.dataset.shadowError',timeout=180000)
                        error=page.locator('html').get_attribute('data-shadow-error')
                        if error: raise AssertionError(error)
                        result=page.evaluate(VERIFY)
                        evidence['runs'].append({'mount':mount,**result})
                        print(f'PASS {mount}: {len(result["checks"])} checks, all {len(result["layouts"])} sample layouts and private font registration.',flush=True)
                    except Exception:
                        page.screenshot(path=str(OUTPUT/('capabilities-root-failure.png' if mount=='/' else 'capabilities-subpath-failure.png')))
                        raise
                    finally:
                        context.close()
                assert not [path for path in requests if '/api/' in path], 'An API request reached the static server.'
            finally:
                browser.close()
    except Exception:
        evidence['failure']=traceback.format_exc()
        raise
    finally:
        (OUTPUT/'capabilities.json').write_text(json.dumps(evidence,indent=2),encoding='utf-8')

if __name__=='__main__':
    main()
