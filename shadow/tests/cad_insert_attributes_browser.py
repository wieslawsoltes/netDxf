"""Insertion values, verification, cancellation and explicit agent previews in both hosts."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_action


def verify_insert_attributes(page, output, state, command, wait_state, idle, check):
    seed = output / 'insertion-seed.dxf'
    drawing = ezdxf.new('R2018'); block = drawing.blocks.new('AttributePump')
    block.add_lwpolyline([(0, 0), (60, 0), (60, 40), (0, 40)], close=True)
    for tag, value, y, flags in [('TAG', 'P-000', 30, 4), ('PRESET', 'Water', 20, 8), ('CONST', 'Fixed', 10, 2), ('SECRET', 'Hidden', 5, 1)]:
        block.add_attdef(tag, (5, y), text=value, dxfattribs={'height': 2.5, 'prompt': tag, 'flags': flags})
    drawing.modelspace().add_blockref('AttributePump', (0, 0)).add_auto_attribs({}); drawing.saveas(seed)
    if page.locator('#documents .document-tab').count() >= 8:
        page.locator('#documents .document-tab .close').first.click()
        expect(page.locator('#documents .document-tab')).to_have_count(7); idle()
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#open-file').set_input_files(str(seed))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous); idle()

    def execute(tool, args):
        return page.evaluate('''async ([tool,args]) => await shadowStudioAgentUi.execute(tool,args,
          document.getElementById('studio').dataset.document)''', [tool, args])

    def attributes():
        return {a['tag']: a for a in execute('cad.read', {'path': 'attributes'})['attributes']}

    def input_prompt(text, change=False):
        revision = state()['state']['revision']
        field = page.locator('#command-input'); field.fill(text); field.press('Enter')
        if change: wait_state(lambda s: s['state']['revision'] > revision)
        idle()

    def accept_insert(value):
        page.get_by_label('Insert attribute TAG', exact=True).fill(value)
        page.locator('#dialog-submit').click()
        expect(page.locator('#dialog-title')).to_have_text('Verify block attributes')
        revision = state()['state']['revision']; page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()
        wait_state(lambda s: s['state']['revision'] > revision); idle()

    command('ATTREQ 1', False); command('ATTDIA 1', False)
    before = state()['state']; command('INSERT AttributePump 80,0 1 0', False)
    expect(page.locator('#dialog-title')).to_have_text('Insert block attributes')
    check('INSERT waits for values before publishing geometry', state()['state']['revision'] == before['revision'])
    expect(page.get_by_label('Insert attribute PRESET', exact=True)).to_have_value('Water')
    expect(page.get_by_label('Insert attribute CONST', exact=True)).to_have_count(0)
    page.get_by_label('Insert attribute TAG', exact=True).fill('P-101')
    page.get_by_label('Insert attribute PRESET', exact=True).fill('Cooling')
    page.get_by_label('Insert attribute SECRET', exact=True).fill('Hidden assignment')
    page.locator('#dialog-submit').click()
    expect(page.locator('#dialog-title')).to_have_text('Verify block attributes')
    expect(page.get_by_label('Verify attribute TAG', exact=True)).to_have_value('P-101')
    page.get_by_label('Verify attribute TAG', exact=True).fill('P-102')
    page.screenshot(path=str(output / 'insertion-verification.png'))
    page.locator('#dialog-submit').click(); expect(page.locator('#dialog')).not_to_be_visible()
    wait_state(lambda s: s['state']['revision'] > before['revision']); idle()
    values = attributes()
    check('insertion dialog preserves constant defaults and accepts verified preset and invisible values',
          values['TAG']['value'] == 'P-102' and values['PRESET']['value'] == 'Cooling' and
          values['CONST']['value'] == 'Fixed' and values['SECRET']['value'] == 'Hidden assignment' and
          state()['state']['undoCount'] == before['undoCount'] + 1)
    command('U'); command('REDO')
    page.locator('#viewport').focus(); page.keyboard.press('Escape'); idle()
    check('insertion undo redo remains one native operation', state()['state']['undoCount'] == before['undoCount'] + 1)

    command('ATTDIA 0', False)
    input_prompt('INSERT AttributePump 160,0 1 0')
    expect(page.locator('#prompt')).to_contain_text('TAG')
    input_prompt('P-201'); expect(page.locator('#prompt')).to_contain_text('SECRET')
    input_prompt('END'); expect(page.locator('#prompt')).to_contain_text('Verify TAG')
    input_prompt('', True); values = attributes()
    check('command insertion skips preset constants and treats snap-like attribute input as literal text',
          values['TAG']['value'] == 'P-201' and values['PRESET']['value'] == 'Water' and values['SECRET']['value'] == 'END')
    command('ATTREQ 0', False); command('INSERT AttributePump 240,0 1 0')
    check('ATTREQ zero uses defaults without verification prompts', attributes()['TAG']['value'] == 'P-000')

    command('ATTREQ 1', False); command('ATTDIA 1', False); before = state()['state']
    command('INSERT AttributePump 320,0 1 0', False)
    expect(page.locator('#dialog-title')).to_have_text('Insert block attributes')
    page.get_by_role('button', name='Cancel', exact=True).click(); expect(page.locator('#dialog')).not_to_be_visible(); idle()
    check('cancelling insertion attributes creates no source entities or undo entry', state()['state']['revision'] == before['revision'])
    input_prompt('-INSERT AttributePump 320,0 1 0'); expect(page.locator('#prompt')).to_contain_text('TAG')
    expect(page.locator('#dialog')).not_to_be_visible(); page.locator('#viewport').focus(); page.keyboard.press('Escape'); idle()
    check('-INSERT uses command prompts even when ATTDIA is enabled and Escape cancels it', state()['state']['revision'] == before['revision'])

    ribbon_action(page, 'block-insert', 'Insert', 'Blocks')
    expect(page.locator('#dialog-title')).to_have_text('Insert block')
    page.get_by_label('Repeat block placement', exact=True).check(); page.locator('#dialog-submit').click()
    expect(page.locator('#dialog')).not_to_be_visible(); input_prompt('320,0')
    expect(page.locator('#dialog-title')).to_have_text('Insert block attributes'); accept_insert('P-301')
    expect(page.locator('#prompt')).to_contain_text('repeat; Escape ends')
    input_prompt('400,0'); expect(page.locator('#dialog-title')).to_have_text('Insert block attributes')
    expect(page.get_by_label('Insert attribute TAG', exact=True)).to_have_value('P-000'); accept_insert('P-302')
    page.locator('#viewport').focus(); page.keyboard.press('Escape'); idle()
    check('repeated placement collects independent values for each insertion', 'Command:' in page.locator('#prompt').inner_text())

    definition = next(b for b in state()['blocks'] if b['name'] == 'AttributePump')
    definitions = execute('cad.read', {'path': 'attribute-definitions', 'block': definition['id']})['definitions']
    tag = next(d['appearance']['id'] for d in definitions if d['appearance']['tag'] == 'TAG')
    receipt = execute('cad.edit', {'path': 'edit-preview', 'body': {'description': 'Insert pump with explicit attributes', 'operations': [
        {'kind': 'insert', 'insert': {'command': {'name': 'INSERT', 'points': [{'x': 480, 'y': 0}], 'reference': {'value': definition['id']}},
                                     'attributes': [{'definition': tag, 'value': 'AGENT-401'}]}}]}})
    execute('ui.action', {'name': 'edit-previews'}); preview = page.locator('.analysis-preview')
    expect(preview).to_have_count(1); expect(preview.locator('img')).to_have_count(2)
    page.screenshot(path=str(output / 'insertion-preview.png'))
    execute('cad.edit', {'path': 'edit-preview/apply', 'body': {'previewId': receipt['previewId']}}); idle()
    check('typed agent insertion preview applies explicit values and owned attributes', attributes()['TAG']['value'] == 'AGENT-401')
    copy_before = state()['state']; command('COPY 0,0 0,80')
    check('COPY retains assigned block attributes in one undoable command', attributes()['TAG']['value'] == 'AGENT-401' and
          state()['state']['undoCount'] == copy_before['undoCount'] + 1)
    command('U'); command('REDO')
    with page.expect_download() as download: page.locator('[data-action="save"]').first.click()
    saved = output / 'inserted-attributes.dxf'; download.value.save_as(str(saved)); idle()
    loaded = ezdxf.readfile(saved); audit = loaded.audit(); values = [i.get_attrib('TAG').dxf.text for i in loaded.modelspace().query('INSERT')]
    check('independent DXF audit confirms every inserted assignment without repairs', not audit.errors and not audit.fixes and
          values == ['P-000', 'P-102', 'P-201', 'P-000', 'P-301', 'P-302', 'AGENT-401', 'AGENT-401'])
    inserted = list(loaded.modelspace().query('INSERT')); original, copied = inserted[-2:]
    check('copied attributes retain translated geometry after undo redo and save',
          copied.dxf.insert.isclose(original.dxf.insert + (0, 80, 0)) and
          all(a.dxf.insert.isclose(b.dxf.insert + (0, 80, 0)) and a.dxf.text == b.dxf.text
              for a, b in zip(copied.attribs, original.attribs)) and len(copied.attribs) == len(original.attribs) == 4)
    command('Z E', False); page.screenshot(path=str(output / 'insertion-values.png'))
