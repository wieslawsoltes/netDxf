"""Additional real-origin authoring checks; imported by the existing CAD browser suite."""
from __future__ import annotations
import io
import json
import urllib.request
from collections import Counter
from pathlib import Path
import ezdxf
from playwright.sync_api import expect


def verify_authoring(page, url: str, output: Path, state, command, wait_state, idle, check):
    previous = page.locator('#studio').get_attribute('data-document')
    page.get_by_role('tab', name='Home', exact=True).click()
    page.locator('[data-action="new"]').first.click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    command('REC 10,20 50,60')
    command('H LINE 3 15 Normal')
    check('HATCH command creates a native pattern from selected analytic boundary',
          Counter(i['type'] for i in state()['items']) == {'Polyline2D': 1, 'Hatch': 1})
    command('HE CROSS 4 30 Outer')

    def request(path, body=None, headers=None):
        data = state()
        s = data['state']
        identifier = page.locator('#studio').get_attribute('data-document')
        identity = {'X-Shadow-Studio': '1', 'X-Shadow-Document': s['documentId'],
                    'X-Shadow-Workspace': s['workspaceId'], 'X-Shadow-View': str(s['viewVersion'])}
        identity.update(headers or {})
        if body is not None:
            body = json.dumps({**body, 'revision': s['revision']}).encode()
            identity['Content-Type'] = 'application/json'
        with urllib.request.urlopen(urllib.request.Request(f'{url}/api/cad/{identifier}/{path}', data=body, headers=identity), timeout=10) as response:
            return json.load(response)

    def pattern():
        return request(f'hatch/pattern?revision={state()["state"]["revision"]}')['pattern']

    check('HATCHEDIT stores scale angle and native island policy',
          pattern()['name'] == 'CROSS' and pattern()['scale'] == 4 and pattern()['angle'] == 30 and pattern()['islands'] == 1)
    page.locator('[data-action="hatch-edit"]').first.click()
    expect(page.locator('#dialog-title')).to_have_text('Hatch Edit')
    expect(page.get_by_role('textbox', name='Hatch scale', exact=True)).to_have_value('4')
    expect(page.get_by_role('textbox', name='Hatch angle', exact=True)).to_have_value('30')
    page.get_by_role('combobox', name='Hatch pattern', exact=True).select_option('LINE')
    page.get_by_role('textbox', name='Hatch scale', exact=True).fill('5')
    page.get_by_role('combobox', name='Hatch type', exact=True).select_option('0')
    page.get_by_role('checkbox', name='Double hatch', exact=True).check()
    revision = state()['state']['revision']
    page.locator('#dialog-submit').click()
    wait_state(lambda data: data['state']['revision'] > revision)
    idle()
    check('Hatch Edit dialog preserves user-defined double pattern semantics',
          pattern()['scale'] == 5 and pattern()['name'] == 'LINE' and pattern()['type'] == 0 and pattern()['isDouble'])
    command('U')
    check('hatch pattern undo retains previous source pattern', pattern()['name'] == 'CROSS' and pattern()['scale'] == 4)
    command('REDO')
    page.locator('[data-action="hatch-edit"]').first.click()
    expect(page.get_by_role('combobox', name='Hatch type', exact=True)).to_have_value('0')
    expect(page.get_by_role('checkbox', name='Double hatch', exact=True)).to_be_checked()
    page.get_by_role('button', name='Cancel', exact=True).click()
    idle()

    # Import original synthetic line-family data; never rely on copyrighted font/pattern payloads.
    page.locator('[data-action="hatch-edit"]').first.click()
    page.get_by_label('Import PAT file', exact=True).set_input_files({
        'name': 'fixture.pat', 'mimeType': 'text/plain',
        'buffer': b'*SYNTHETIC, Browser pattern\n30,0,0,.5,2,1,-1\n'})
    expect(page.locator('#dialog-body')).to_contain_text('1 patterns imported')
    revision = state()['state']['revision']
    page.locator('#dialog-submit').click()
    wait_state(lambda data: data['state']['revision'] > revision)
    idle()
    actual = pattern()
    check('custom PAT import reaches native DXF pattern definitions',
          actual['name'] == 'SYNTHETIC' and actual['description'] == 'Browser pattern' and actual['lines'][0]['dashes'] == [1, -1] and
          actual['type'] == 2 and not actual['isDouble'])
    page.locator('#command-input').press('Escape')
    idle()
    page.locator('#viewport').focus()
    page.keyboard.press('Control+a')
    wait_state(lambda data: len(data['selectedEntities']) == 2)
    idle()
    command('B Symbol 10,20')
    data = state()
    check('BLOCK command converts selected boundary and hatch into one native reference',
          data['itemCount'] == 1 and data['items'][0]['type'] == 'Insert' and any(b['name'] == 'Symbol' and b['entities'] == 2 for b in data['blocks']))
    command('U')
    check('one block undo restores both original layout entities', state()['itemCount'] == 2)
    command('REDO')
    command('BRENAME Symbol Assembly')
    check('BLOCKRENAME updates native table identity', any(b['name'] == 'Assembly' for b in state()['blocks']) and not any(b['name'] == 'Symbol' for b in state()['blocks']))
    command('I Assembly 100,20 1 0')
    check('INSERT reuses the new shared definition', len([i for i in state()['items'] if i['type'] == 'Insert']) == 2)
    command('C 200,100 8')
    command('BREDEFINE Assembly 200,100')
    check('redefinition updates the shared source definition without moving references',
          any(b['name'] == 'Assembly' and b['entities'] == 1 for b in state()['blocks']))
    command('U')
    check('redefinition undo restores analytic boundary and hatch contents', any(b['name'] == 'Assembly' and b['entities'] == 2 for b in state()['blocks']))
    page.locator('#command-input').press('Escape'); idle()
    command('HP 0,100 30,100 30,130 0,130 C')
    check('HATCHPOLY creates a solid hatch without manufacturing editable outline entities',
          Counter(i['type'] for i in state()['items']) == {'Insert': 2, 'Circle': 1, 'Hatch': 1})
    with page.expect_download() as download:
        page.locator('[data-action="save"]').first.click()
    file = output/'authored.dxf'; download.value.save_as(str(file)); idle()
    document = ezdxf.readfile(file)
    audit = document.audit()
    model = document.modelspace()
    references = list(model.query('INSERT'))
    boundary_hatch = list(document.blocks['Assembly'].query('HATCH'))[0]
    check('independent DXF audit accepts authored block references and custom hatch without repairs',
          not audit.errors and not audit.fixes and len(references) == 2 and
          all(i.dxf.name == 'Assembly' for i in references) and len(boundary_hatch.paths) == 1 and
          boundary_hatch.dxf.pattern_name == 'SYNTHETIC')
    check('block base point and remote insertion survive DXF save',
          sorted((i.dxf.insert.x, i.dxf.insert.y) for i in references) == [(10, 20), (100, 20)])
    before = state(); old = page.locator('#studio').get_attribute('data-document')
    page.locator('#open-file').set_input_files(str(file))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', old)
    wait_state(lambda data: data['itemCount'] == before['itemCount']); idle()
    check('authored definitions and hatches reopen as native editable data',
          Counter(i['type'] for i in state()['items']) == Counter(i['type'] for i in before['items']) and
          any(b['name'] == 'Assembly' and b['entities'] == 2 for b in state()['blocks']))
    command('Z E', False)
    page.screenshot(path=str(output/'authoring.png'))
