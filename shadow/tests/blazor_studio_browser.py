#!/usr/bin/env python3
"""Exercise the published .NET WASM engine in Chromium against a static-only server.
No mocked CAD responses, HTTP API, development server, or host process is available.
"""
from __future__ import annotations
import argparse
from contextlib import contextmanager
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import threading
import time
import traceback
from urllib.parse import urlsplit
import ezdxf
from cad_modify_browser import verify_modify
from graph_analysis_browser import verify_graph_analysis
from cad_layer_states_browser import verify_layer_states
from cad_attributes_browser import verify_attributes
from cad_attribute_sync_browser import verify_attribute_sync
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/blazor-browser'

@contextmanager
def static_host(directory):
    requests = []
    class Handler(SimpleHTTPRequestHandler):
        extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.mjs': 'text/javascript'}
        def __init__(self, *args, **kwargs):
            super().__init__(*args, directory=str(directory), **kwargs)
        def translate_path(self, path):
            if path.startswith('/netDxf/'):
                path = path[len('/netDxf'):]
            return super().translate_path(path)
        def do_GET(self):
            requests.append(urlsplit(self.path).path)
            super().do_GET()
        def log_message(self, *_):
            pass
    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f'http://127.0.0.1:{server.server_port}', requests
    finally:
        server.shutdown(); server.server_close(); thread.join(timeout=5)

def verify(base, mount, browser, passed):
    context = browser.new_context(viewport={'width': 1600, 'height': 1000}, accept_downloads=True)
    page = context.new_page()
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('console', lambda message: print('BROWSER:', message.text, flush=True) if message.type == 'error' else None)
    page.set_default_timeout(30000)
    tag = 'root' if mount == '/' else 'subpath'
    def check(name, value=True):
        if not value:
            raise AssertionError(name)
        passed.append(f'{tag}: {name}')
        print('PASS:', passed[-1], flush=True)
    def ready(path):
        response = page.goto(base + mount + path, wait_until='domcontentloaded')
        assert response and response.ok
        page.wait_for_function('document.documentElement.dataset.shadowReady || document.documentElement.dataset.shadowError', timeout=180000)
        error = page.locator('html').get_attribute('data-shadow-error')
        if error:
            raise AssertionError(error)
    def api(path, body=None, headers=None):
        return page.evaluate('''async ({path,body,headers}) => {
          const options = {headers: headers || {}};
          if (body !== null) { options.method='POST'; options.headers={...options.headers,'Content-Type':'application/json','X-Shadow-Studio':'1'}; options.body=JSON.stringify(body); }
          const response=await fetch(path,options);
          const text=await response.text();
          if (!response.ok) throw new Error(`${response.status}: ${text}`);
          return JSON.parse(text);
        }''', {'path': path, 'body': body, 'headers': headers})
    def state():
        identifier = page.locator('#studio').get_attribute('data-document')
        return api(f'/api/cad/{identifier}/state')
    def wait_state(predicate):
        deadline = time.monotonic() + 30
        while True:
            value = state()
            if predicate(value):
                return value
            if time.monotonic() > deadline:
                raise AssertionError('Drawing state did not reach the expected result.')
            page.wait_for_timeout(30)
    def idle():
        expect(page.locator('#connection')).to_have_text('Local')
    def command(text, change=True):
        previous = state()['state']['revision']
        field = page.locator('#command-input')
        field.fill(text); field.press('Enter')
        if change:
            wait_state(lambda value: value['state']['revision'] > previous)
        idle()
    def headers(s):
        return {'X-Shadow-Studio': '1', 'X-Shadow-Document': s['documentId'],
                'X-Shadow-Workspace': s['workspaceId'], 'X-Shadow-View': str(s['viewVersion'])}
    try:
        ready('')
        expect(page.locator('#studio')).to_have_attribute('data-document', 'demo')
        expect(page.locator('#loading')).to_be_hidden()
        check('Blazor boots the shared CAD interface', page.evaluate('shadowBrowser.runtime') == 'blazor-webassembly')
        info = api('/api/browser/environment')
        check('all operations are local .NET routes', not info['serverRequired'] and len(info['routes']) >= 65)
        page.locator('[data-action="new"]').first.click()
        expect(page.locator('#studio')).not_to_have_attribute('data-document', 'demo')
        idle()
        check('new document is empty', state()['itemCount'] == 0)
        command('L 0,0 100,0 100,80 C')
        check('line chain is one native undo transaction', state()['state']['undoCount'] == 1 and state()['itemCount'] == 3)
        command('U'); check('undo removes chain', state()['itemCount'] == 0)
        command('REDO'); check('redo restores chain', state()['itemCount'] == 3)
        for text in ['C 140,40 25', 'REC 200,0 270,80', 'ARC 300,0 330,50 360,0',
                     'EL 420,40 470,40 420,60', 'POL 6 80,150 25', 'PL 0,130 30,170 60,130 C',
                     'TEXT 0,220 5 0 Browser drawing', 'MTEXT 100,220 5 70 First paragraph\\PSecond paragraph',
                     'DIMALIGNED 0,0 100,0 50,-20', 'DIMLINEAR 200,0 270,0 230,-20', 'POINT 10,10']:
            command(text); check('construction ' + text.split()[0])
        for text in ['CO 10,10 20,20', 'M 20,20 @5,5', 'RO 0,0 90', 'SC 0,0 2', 'MI 0,0 100,0', 'AR 3 2 0,0 30,30']:
            command(text)
        check('native modifier and array workflow', len(state()['selectedEntities']) == 5)
        identifier = page.locator('#studio').get_attribute('data-document')
        s = state()['state']
        graph = api(f'/api/cad/{identifier}/graph', headers=headers(s))
        check('topology graph built in WebAssembly', len(graph['nodes']) > 0)
        for backend, magic in [('skia', [137,80,78,71]), ('svg', None)]:
            image = page.evaluate('''async ({path,headers}) => {
              const response=await fetch(path,{headers}); if(!response.ok)throw new Error(await response.text());
              const bytes=new Uint8Array(await response.arrayBuffer());
              return {length:bytes.length,magic:Array.from(bytes.slice(0,4)),text:new TextDecoder().decode(bytes.slice(0,200)),type:response.headers.get('Content-Type')};
            }''', {'path': f'/api/cad/{identifier}/render?width=800&height=600&cx=200&cy=80&scale=1&backend={backend}&revision={s["revision"]}', 'headers': headers(s)})
            check('native ' + backend + ' rendering and binary interop', image['length'] > 100 and (image['magic'] == magic if magic else '<svg' in image['text']))
        pixels = page.evaluate('''async ({path,headers}) => {
          const response=await fetch(path,{headers}); if(!response.ok)throw new Error(await response.text());
          const url=URL.createObjectURL(await response.blob()), png=new Image();
          try {png.src=url; await png.decode();} finally {URL.revokeObjectURL(url);}
          const raw=await fetch(path+'&pixels=true',{headers}); if(!raw.ok)throw new Error(await raw.text());
          const bytes=await raw.arrayBuffer(), width=800, height=600;
          if(bytes.byteLength!==width*height*4)throw new Error('Invalid RGBA frame length.');
          const bitmap=await createImageBitmap(new ImageData(new Uint8ClampedArray(bytes),width,height),{colorSpaceConversion:'none'});
          try {
            const read=image=>{
              const canvas=document.createElement('canvas'); canvas.width=width; canvas.height=height;
              const context=canvas.getContext('2d'); context.drawImage(image,0,0);
              return context.getImageData(0,0,width,height).data;
            };
            const expected=read(png), actual=read(bitmap);
            return {exact:expected.every((value,index)=>value===actual[index]),
                    painted:expected.some((value,index)=>index%4===3 && value>0),
                    type:raw.headers.get('Content-Type')};
          } finally {bitmap.close();}
        }''', {'path': f'/api/cad/{identifier}/render?width=800&height=600&cx=200&cy=80&scale=1&backend=skia&revision={s["revision"]}', 'headers': headers(s)})
        check('local RGBA presentation exactly matches PNG pixels', pixels['exact'] and pixels['painted'] and pixels['type'] == 'application/x-shadow-rgba')
        unchanged = page.evaluate('''async ({path,headers}) => {
          const response=await fetch(path,{headers}); if(!response.ok)throw new Error(await response.text());
          return await response.text();
        }''', {'path': f'/api/cad/{identifier}/state?knownServer={s["serverId"]}&knownChange={s["changeVersion"]}', 'headers': headers(s)})
        check('unchanged local state poll is a four-byte response', unchanged == 'null')
        json_transport = page.evaluate('''async path => {
          const reply=await DotNet.invokeMethodAsync('netDxf.Shadow.Studio.WebAssembly','Dispatch',
            {method:'GET',path,headers:{},body:null},null);
          if(!(reply.bytes instanceof Uint8Array))return false;
          const text=new TextDecoder('utf-8',{fatal:true}).decode(reply.bytes);
          const response=await fetch(path); if(!response.ok)throw new Error(await response.text());
          return reply.status===200 && reply.contentType==='application/json' && reply.json===null &&
            text===await response.text();
        }''', f'/api/cad/{identifier}/state')
        check('JSON state crosses interop as exact UTF-8 bytes', json_transport)
        bad = page.evaluate('''async ({path,headers,revision}) => {
          const response=await fetch(path,{method:'POST',headers:{...headers,'Content-Type':'application/json','X-Shadow-View':'999999'},body:JSON.stringify({revision})});
          return response.status;
        }''', {'path': f'/api/cad/{identifier}/retain', 'headers': headers(s), 'revision': s['revision']})
        check('stale view receipt is rejected atomically', bad == 409)
        command('SNAPSHOT', False)
        expect(page.get_by_role('button', name='Current drawing', exact=True)).to_be_visible()
        revision = state()['state']['revision']
        page.get_by_role('button', name=f'Revision {revision} — read only', exact=True).click()
        expect(page.locator('[data-command="LINE"]')).to_be_disabled()
        page.get_by_role('button', name='Current drawing', exact=True).click()
        check('retained snapshot read-only workflow')
        page.get_by_role('button', name='Fork branch', exact=True).click()
        page.get_by_role('textbox', name='Branch name', exact=True).fill('Browser alternative')
        page.locator('#dialog-submit').click()
        expect(page.get_by_role('button', name='Review merge', exact=True)).to_be_visible()
        command('C 420,170 12')
        check('isolated branch editing', state()['state']['workspaceId'] != 'live')
        page.get_by_role('button', name='Review merge', exact=True).click()
        expect(page.locator('#dialog-title')).to_have_text('Apply reviewed merge')
        page.locator('#dialog-submit').click()
        wait_state(lambda value: value['state']['workspaceId'] == 'live'); idle()
        check('reviewed merge returns to live source')
        with page.expect_download() as download:
            page.locator('[data-action="save"]').first.click()
        file = OUTPUT / f'{tag}-edited.dxf'
        download.value.save_as(str(file))
        drawing = ezdxf.readfile(file)
        audit = drawing.audit()
        check('browser DXF download independently audits without repairs', not audit.errors and not audit.fixes and len(drawing.modelspace().query('DIMENSION')) == 2)
        old_id = page.locator('#studio').get_attribute('data-document')
        page.locator('#open-file').set_input_files(str(file))
        expect(page.locator('#studio')).not_to_have_attribute('data-document', old_id)
        idle(); check('DXF upload reopens a separate document', state()['itemCount'] > 10)
        # Drive the actual Save As workflow. Calling the save endpoint behind the UI's
        # back can canonicalize the source and intentionally invalidate its old receipt.
        # The real client adopts the post-save receipt before allowing the next command.
        command('SAVEAS', False)
        expect(page.locator('#dialog-title')).to_have_text('Save drawing as')
        page.get_by_role('combobox', name='Drawing format', exact=True).select_option('dwg')
        page.get_by_role('combobox', name='DWG version', exact=True).select_option('AC1032')
        page.get_by_role('textbox', name='Drawing filename', exact=True).fill('Roundtrip.dwg')
        with page.expect_download() as download:
            page.locator('#dialog-submit').click()
        dwg = OUTPUT / f'{tag}-edited.dwg'
        download.value.save_as(str(dwg)); idle()
        check('Save As downloads a real DWG from WebAssembly', dwg.read_bytes()[:6] == b'AC1032' and dwg.stat().st_size > 1000)
        old_id = page.locator('#studio').get_attribute('data-document')
        page.locator('#open-file').set_input_files(str(dwg))
        expect(page.locator('#studio')).not_to_have_attribute('data-document', old_id)
        idle(); check('DWG file picker reopens the edited drawing', state()['itemCount'] > 10)
        page.locator('[data-action="new"]').first.click(); wait_state(lambda value: value['itemCount'] == 0); idle()
        command('REC 10,20 30,40'); command('C 40,30 5')
        page.locator('#viewport').focus(); page.keyboard.press('Control+a')
        wait_state(lambda value: len(value['selectedEntities']) == 2); idle()
        command('BLOCK 10,20 BrowserPart')
        check('native block definition and insertion', state()['itemCount'] == 1)
        command('BEDIT', False)
        expect(page.locator('#dialog-title')).to_have_text('Edit block definition')
        page.locator('#dialog-submit').click(); wait_state(lambda value: value.get('blockEditor') is not None)
        command('C 70,0 4'); command('BSAVE', False)
        wait_state(lambda value: value.get('blockEditor') is None); idle()
        check('isolated block editor commits shared definition', next(b for b in state()['blocks'] if b['name'] == 'BrowserPart')['entities'] == 3)
        command('U'); command('REDO')
        command('C 150,30 10'); command('H LINE 3 15 Normal'); command('HE CROSS 4 30 Outer')
        check('hatch authoring and editing', any(item['type'] == 'Hatch' for item in state()['items']))
        verify_modify(page, OUTPUT, state, command, wait_state, idle, check)
        verify_graph_analysis(page, OUTPUT, state, command, wait_state, idle, check)
        verify_layer_states(page, OUTPUT, state, command, wait_state, idle, check)
        verify_attributes(page, OUTPUT, state, command, wait_state, idle, check)
        verify_attribute_sync(page, OUTPUT, state, command, wait_state, idle, check)
        command('Z E', False)
        # Let the debounced render and image decode complete before recording evidence.
        # Correctness is asserted independently by the native state and render checks.
        page.wait_for_timeout(1000)
        expect(page.locator('#history .error')).to_have_count(0)
        check('CAD user workflow has no reported operation errors')
        page.screenshot(path=str(OUTPUT / f'{tag}-cad.png'))
        page.set_viewport_size({'width': 900, 'height': 680})
        check('compact layout does not overflow horizontally', page.evaluate('document.documentElement.scrollWidth <= innerWidth'))
        page.wait_for_timeout(500)
        page.screenshot(path=str(OUTPUT / f'{tag}-compact.png'))
        check('CAD has no uncaught JavaScript errors', not errors)
        page.close(); page = context.new_page(); page.set_default_timeout(30000)
        page.on('pageerror', lambda error: errors.append(str(error)))
        ready('diagnostics.html')
        page.wait_for_function('document.querySelector("#revision")?.textContent?.includes("0") || document.querySelector("#workspace")?.options?.length > 0', timeout=30000)
        notice = page.evaluate('''async () => {
          const response=await fetch('api/events'); const reader=response.body.getReader();
          const first=await reader.read(); await reader.cancel();
          return new TextDecoder().decode(first.value);
        }''')
        check('diagnostics notifications use local bounded subscriptions', 'event: state' in notice and 'documentId' in notice)
        legacy = api('api/state')
        updated = api('api/add', {'revision': legacy['revision'], 'kind': 'circle', 'x': 20, 'y': 30}, headers(legacy))
        check('diagnostics edits use the original engine', updated['entities'] > legacy['entities'])
        for backend in ('skia', 'svg'):
            status = page.evaluate('''async ({path,headers}) => {const r=await fetch(path,{headers}); return {status:r.status,size:(await r.blob()).size};}''',
                                   {'path': f'api/render?width=640&height=480&backend={backend}', 'headers': headers(updated)})
            check('diagnostics ' + backend + ' rendering', status['status'] == 200 and status['size'] > 100)
        page.screenshot(path=str(OUTPUT / f'{tag}-diagnostics.png'))
        check('diagnostics has no uncaught JavaScript errors', not errors)
    except Exception:
        try:
            page.screenshot(path=str(OUTPUT / f'{tag}-failure.png'), timeout=5000)
            (OUTPUT / f'{tag}-failure.html').write_text(page.content(), encoding='utf-8')
        except Exception:
            pass
        raise
    finally:
        context.close()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published', required=True, type=Path)
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    passed, failure = [], None
    try:
        with static_host(args.published.resolve()) as (base, requests), sync_playwright() as playwright:
            browser = playwright.chromium.launch(headless=True)
            try:
                verify(base, '/', browser, passed)
                verify(base, '/netDxf/', browser, passed)
                leaked = [path for path in requests if path.startswith('/api/') or '/api/' in path]
                assert not leaked, f'The browser tried to contact an API server: {leaked}'
                passed.append('No CAD or diagnostics API requests reached the static server.')
            finally:
                browser.close()
    except Exception:
        failure = traceback.format_exc()
        raise
    finally:
        (OUTPUT / 'results.json').write_text(json.dumps({'passed': passed, 'failure': failure}, indent=2), encoding='utf-8')

if __name__ == '__main__':
    main()
