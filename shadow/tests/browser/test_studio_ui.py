#!/usr/bin/env python3
"""Optional DOM integration tests. Requires Playwright and a Chromium executable.
All requests use a static in-memory fixture, not the HTTP service. Run the separate
http_contract.py suite for real source, renderer, HTTP and SSE contracts.
"""
import argparse
from pathlib import Path
import re
from playwright.sync_api import sync_playwright

ROOT=Path(__file__).resolve().parents[3]
WEB=ROOT/'shadow/src/netDxf.Shadow.Studio.Web/wwwroot'

def run(executable=None):
    with sync_playwright() as playwright:
        browser=playwright.chromium.launch(executable_path=executable,headless=True,args=['--no-sandbox'])
        errors=[]
        def page():
            p=browser.new_page(viewport={'width':1600,'height':1050})
            p.on('pageerror',lambda error:errors.append(str(error)))
            html=re.sub(r'<script\b[^>]*>.*?</script>|<link\b[^>]*>', '', (WEB/'diagnostics.html').read_text(),flags=re.S)
            p.set_content(html)
            p.add_style_tag(content=(WEB/'studio.css').read_text()+(WEB/'workspaces.css').read_text())
            p.add_script_tag(content=(Path(__file__).parent/'studio_fixture.js').read_text())
            p.add_script_tag(content=(WEB/'studio-state.mjs').read_text().replace('export ',''))
            p.add_script_tag(content=re.sub(r'^import[^\n]+\n','',(WEB/'studio.js').read_text()))
            p.wait_for_function("document.getElementById('drawing').naturalWidth>0")
            return p
        def fork(p):
            p.locator('#branch-name').fill('UI experiment <branch>');p.locator('#fork').click()
            p.wait_for_function("document.getElementById('workspace').value!=='live'")
        p=page(); p.locator('#linetype-alignment').select_option('A')
        p.wait_for_function("fixture.calls.some(c=>c.path==='linetype-view') && document.getElementById('linetype-alignment').value==='A'")
        assert p.locator('#undo').is_disabled(); p.close(); print('PASS ui/fitting-control-changes-presentation-without-history')
        p=page(); assert p.locator('#connection').inner_text()=='Live updates connected'
        assert p.evaluate("fixture.calls.filter(c=>c.path==='render').every(c=>c.headers['X-Shadow-Workspace']==='live')")
        fork(p); assert p.locator('#save').is_disabled()
        p.locator('[data-add=circle]').click();p.wait_for_function("document.getElementById('revision').textContent.includes('r1')")
        p.locator('#preview-merge').click();p.wait_for_selector('#read-only:visible')
        assert p.locator('[data-add=line]').is_disabled() and p.locator('#apply-merge').is_enabled()
        p.locator('#apply-merge').click();p.wait_for_function("document.getElementById('workspace').value==='live'")
        assert p.locator('#undo').is_enabled();p.close();print('PASS ui/branch-preview-apply-and-request-headers')
        p=page();fork(p);p.evaluate('fixture.conflict=true');p.locator('#preview-merge').click();p.wait_for_selector('#merge-result:visible')
        assert p.locator('#apply-merge').is_disabled() and p.locator('#merge-conflicts img').count()==0
        assert '<img' in p.locator('#merge-conflicts').inner_text()
        p.locator('#merge-choice').select_option('Incoming');p.locator('#preview-merge').click();p.wait_for_function("!document.getElementById('apply-merge').disabled")
        p.locator('#close-preview').click();assert p.locator('#read-only').is_hidden();p.close();print('PASS ui/conflict-policy-escaping-and-close')
        p=page();p.evaluate("fixture.holds.add('object/H%3A1')")
        p.locator('.entity').first.click();p.wait_for_function("fixture.held.has('object/H%3A1')")
        fork(p);p.evaluate("fixture.release('object/H%3A1')")
        p.wait_for_timeout(100);assert p.locator('#property-type').inner_text()=='';p.close();print('PASS ui/stale-inspector-receipt-is-discarded')
        p=page();p.locator('.entity').first.click();p.wait_for_selector('input[aria-label="EndPoint"]')
        p.locator('input[aria-label="EndPoint"]').fill('{"X":20}')
        p.evaluate("fixture.holds.add('state');fixture.advance()")
        p.wait_for_function("fixture.held.has('state')")
        p.locator('input[aria-label="EndPoint"]').press('Enter')
        assert not p.evaluate("fixture.calls.some(c=>c.path==='property')")
        p.evaluate("fixture.release('state')");p.wait_for_function("document.getElementById('revision').textContent.includes('r1')")
        p.close();print('PASS ui/notice-blocks-stale-property-command')
        p=page();p.locator('#retain').click();p.wait_for_function("document.getElementById('snapshot').options.length===2")
        p.locator('#snapshot').select_option('0');assert p.locator('[data-add=line]').is_disabled()
        p.locator('#snapshot').select_option('');assert p.locator('[data-add=line]').is_enabled();p.close();print('PASS ui/retained-view-editing-lock')
        p=page()
        with p.expect_download() as download:p.locator('#save').click()
        assert download.value.suggested_filename=='shadow-edited.dxf'
        assert p.evaluate("fixture.calls.some(c=>c.path==='save'&&c.method==='POST'&&c.headers['X-Shadow-Studio']==='1')")
        p.close();print('PASS ui/guarded-post-download')
        p=page();before=p.locator('#drawing').get_attribute('src')
        p.evaluate("fixture.holds.add('render')")
        p.locator('#viewport').hover();p.mouse.wheel(0,-180)
        p.wait_for_function("fixture.held.has('render')")
        p.locator('#viewport').click(position={'x':100,'y':100})
        assert not p.evaluate("fixture.calls.some(c=>c.path==='pick')"), 'Picking used a new camera with the old displayed frame.'
        p.evaluate("fixture.release('render')")
        p.wait_for_function("old=>document.getElementById('drawing').src!==old",arg=before)
        p.locator('#viewport').click(position={'x':100,'y':100})
        p.wait_for_function("fixture.calls.some(c=>c.path==='pick')")
        p.close();print('PASS ui/camera-changes-fence-picking-until-frame-publication')
        p=page()
        mapping=b'{"schemaVersion":1,"resource":"big.shx","characters":{"U+03A9":32832}}'
        p.locator('#bigfont-map-file').set_input_files({'name':'map.json','mimeType':'application/json','buffer':mapping})
        p.wait_for_function("fixture.calls.some(c=>c.path==='bigfont-map') && fixture.state().fontRevision===1")
        assert p.evaluate("fixture.calls.find(c=>c.path==='bigfont-map').headers['X-Shadow-Studio']==='1'")
        assert p.evaluate("JSON.parse(fixture.mapText).characters['U+03A9']===32832")
        assert p.locator('#undo').is_disabled()
        fork(p); assert p.locator('#bigfont-map-file').is_disabled()
        p.close(); print('PASS ui/explicit-bigfont-map-upload-preserves-history-and-branch-resource-lock')
        p=page()
        p.locator('#bigfont-map-file').set_input_files({'name':'large.json','mimeType':'application/json','buffer':b' '* (4*1024*1024+1)})
        p.wait_for_selector('#notification:visible')
        assert not p.evaluate("fixture.calls.some(c=>c.path==='bigfont-map')")
        p.close(); print('PASS ui/oversized-bigfont-map-rejected-before-request')

        p=page(); p.locator('#perspective-mode').select_option('TargetPlaneHeight');p.locator('#perspective-plane').fill('5');p.locator('#perspective-apply').click()
        p.wait_for_function("fixture.calls.some(c=>c.path==='perspective-view') && fixture.state().perspectiveMode==='TargetPlaneHeight'")
        assert p.locator('#undo').is_disabled() and p.locator('#perspective-plane').input_value()=='5'
        p.close();print('PASS ui/perspective-policy-control-is-source-neutral')
        p=page();p.locator('#perspective-mode').select_option('TargetPlaneHeight');p.locator('#perspective-apply').click()
        p.wait_for_function("fixture.state().perspectiveMode==='TargetPlaneHeight'")
        p.wait_for_timeout(200)
        box=p.locator('#viewport').bounding_box();x=box['x']+box['width']/2;y=box['y']+box['height']/2
        p.mouse.move(x,y);p.mouse.down();p.wait_for_function("fixture.state().perspectiveSelection===true")
        p.mouse.move(x+50,y+10,steps=5);p.mouse.up()
        p.wait_for_function("fixture.calls.some(c=>c.path==='translate-perspective')")
        assert not p.evaluate("fixture.calls.some(c=>c.path==='translate')")
        assert p.evaluate("(()=>{const b=JSON.parse(fixture.calls.find(c=>c.path==='translate-perspective').body);return Number.isFinite(b.fromX)&&Number.isFinite(b.toX)&&b.fromX!==b.toX})()")
        p.close();print('PASS ui/perspective-drag-sends-two-paper-points-not-an-affine-delta')
        assert not errors,errors
        browser.close();print('DOM verification: 12 scenarios passed; no page errors.')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--executable');args=parser.parse_args();run(args.executable)
