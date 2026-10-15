"""Real-origin DWG interchange checks, continuing the native authoring scenario."""
from __future__ import annotations
from collections import Counter
import json
from pathlib import Path
import urllib.error
import urllib.request
import ezdxf
from playwright.sync_api import expect


def verify_exchange(page, url: str, output: Path, state, command, wait_state, idle, check):
    original = state()
    signature = lambda data: Counter((i['type'], i['layer'], i['count']) for i in data['items'])
    expected = signature(original)
    page.get_by_role('tab', name='Output', exact=True).click()
    page.locator('[data-action="save-as"]').click()
    expect(page.locator('#dialog-title')).to_have_text('Save drawing as')
    page.get_by_role('combobox', name='Drawing format', exact=True).select_option('dwg')
    page.get_by_role('combobox', name='DWG version', exact=True).select_option('AC1032')
    page.get_by_role('textbox', name='Drawing filename', exact=True).fill('authored-exchange.dwg')
    with page.expect_download() as downloaded:
        page.locator('#dialog-submit').click()
    dwg = output/'authored-exchange.dwg'
    downloaded.value.save_as(str(dwg))
    idle()
    check('Save As produces a real DWG payload with matching filename',
          downloaded.value.suggested_filename == dwg.name and dwg.read_bytes()[:6] == b'AC1032' and dwg.stat().st_size > 512)
    before_id = page.locator('#studio').get_attribute('data-document')
    # Deliberately misleading extension: content selects the decoder on both ends.
    page.locator('#open-file').set_input_files({'name': 'signature-route.dxf', 'mimeType': 'application/octet-stream', 'buffer': dwg.read_bytes()})
    expect(page.locator('#studio')).not_to_have_attribute('data-document', before_id)
    wait_state(lambda data: data['itemCount'] == original['itemCount'])
    idle()
    check('DWG signature routes to the decoder even with a DXF filename', signature(state()) == expected)
    with page.expect_download() as downloaded:
        page.locator('[data-action="save"]').first.click()
    dxf = output/'dwg-roundtrip.dxf'
    downloaded.value.save_as(str(dxf))
    idle()
    check('native DXF save after DWG import retains a DXF download extension',
          downloaded.value.suggested_filename.endswith('.dxf') and dxf.read_bytes()[:2] != b'AC')
    restored = ezdxf.readfile(dxf)
    audit = restored.audit()
    inserts = list(restored.modelspace().query('INSERT'))
    hatch = list(restored.blocks['Assembly'].query('HATCH'))[0]
    check('DWG round trip preserves authored block references and hatch geometry in native DXF',
          not audit.errors and not audit.fixes and len(inserts) == 2 and
          all(i.dxf.name == 'Assembly' for i in inserts) and
          sorted((i.dxf.insert.x, i.dxf.insert.y) for i in inserts) == [(10, 20), (100, 20)] and
          hatch.dxf.pattern_name == 'SYNTHETIC' and len(hatch.paths) == 1 and
          len(list(restored.modelspace().query('HATCH'))) == 1)

    def failed_request(path, payload, revision=None, extra=None):
        s = state()['state']
        identifier = page.locator('#studio').get_attribute('data-document')
        headers = {'X-Shadow-Studio': '1', 'X-Shadow-Document': s['documentId'],
                   'X-Shadow-Workspace': s['workspaceId'], 'X-Shadow-View': str(s['viewVersion'])}
        if isinstance(payload, dict):
            headers['Content-Type'] = 'application/json'
            payload = json.dumps({**payload, 'revision': s['revision'] if revision is None else revision}).encode()
        headers.update(extra or {})
        request = urllib.request.Request(f'{url}/api/cad/{identifier}/{path}', data=payload, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=15):
                raise AssertionError('Invalid interchange operation unexpectedly succeeded')
        except urllib.error.HTTPError as error:
            return error.code, json.load(error)['error']

    before = state()
    code, message = failed_request(f'load?revision={before["state"]["revision"]}&name=corrupt.dwg', b'AC1032broken')
    after = state()
    check('corrupt DWG import is refused without replacing the current drawing',
          code == 400 and after['state']['documentId'] == before['state']['documentId'] and
          after['state']['revision'] == before['state']['revision'] and signature(after) == signature(before))
    command('POINT 500,500')
    before = state()
    code, _ = failed_request('save', {'format': 'dwg', 'version': 'AC1021'})
    with urllib.request.urlopen(url+'/api/cad/documents') as response:
        docs = json.load(response)
    current = page.locator('#studio').get_attribute('data-document')
    check('unsupported DWG export preserves dirty state and native history',
          code == 400 and state()['state']['revision'] == before['state']['revision'] and next(d for d in docs if d['id'] == current)['dirty'])
    code, _ = failed_request('save', {'format': 'dwg', 'version': 'AC1032'}, revision=before['state']['revision']-1)
    check('DWG save rejects stale source revisions', code == 409)
    code, _ = failed_request('save', {'format': 'dwg', 'version': 'AC1032'}, extra={'X-Shadow-Document': ''})
    check('DWG save keeps document identity guards', code == 409)
    command('U')
    check('drawing remains editable after import and export refusals', signature(state()) == expected)
    page.get_by_role('tab', name='Home', exact=True).click()
    command('Z E', False)
    page.screenshot(path=str(output/'dwg-exchange.png'))
