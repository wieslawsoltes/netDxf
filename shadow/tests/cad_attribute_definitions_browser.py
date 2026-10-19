"""Block definition authoring through the shared native and standalone browser UI."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_action


def verify_attribute_definitions(page, output, state, command, wait_state, idle, check):
    seed = output / 'definition-seed.dxf'
    drawing = ezdxf.new('R2018')
    block = drawing.blocks.new('DefinitionPump')
    block.add_lwpolyline([(0, 0), (60, 0), (60, 35), (0, 35)], close=True)
    for tag, text, y, flags in [('TAG', 'P-000', 25, 0), ('SERVICE', 'Water', 15, 0), ('CONST', 'Fixed', 5, 2)]:
        block.add_attdef(tag, (5, y), text=text, dxfattribs={'height': 2.5, 'prompt': tag, 'flags': flags})
    drawing.modelspace().add_blockref('DefinitionPump', (0, 0)).add_auto_attribs({'TAG': 'P-101', 'SERVICE': 'Assigned water'})
    drawing.saveas(seed)
    if page.locator('#documents .document-tab').count() >= 8:
        page.locator('#documents .document-tab .close').first.click()
        expect(page.locator('#documents .document-tab')).to_have_count(7); idle()

    def open_file(path):
        previous = page.locator('#studio').get_attribute('data-document')
        page.locator('#open-file').set_input_files(str(path))
        expect(page.locator('#studio')).not_to_have_attribute('data-document', previous); idle()
        page.locator('#viewport').focus(); page.keyboard.press('Control+a')
        wait_state(lambda s: len(s['selectedEntities']) == 1); idle()

    def read(path):
        return page.evaluate('''async path => await shadowStudioAgentUi.execute('cad.read', {path},
          document.getElementById('studio').dataset.document)''', path)

    def definitions():
        return [d['appearance'] for d in read('attribute-definitions')['definitions']]

    def attributes():
        return {a['tag']: a for a in read('attributes')['attributes']}

    def accept():
        revision = state()['state']['revision']; page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()
        wait_state(lambda s: s['state']['revision'] > revision); idle()

    open_file(seed); initial = definitions(); original = attributes()['TAG']; command('Z E', False)
    ribbon_action(page, 'attribute-definitions', 'Insert', 'Blocks')
    expect(page.locator('#dialog-title')).to_have_text('Block attribute manager (BATTMAN)')
    page.get_by_label('Block attributes', exact=True).select_option(initial[0]['id']['value'])
    page.get_by_label('Attribute tag', exact=True).fill('PUMP_TAG')
    page.get_by_label('Attribute prompt', exact=True).fill('Pump identifier')
    page.get_by_label('Attribute default', exact=True).fill('P-999')
    page.get_by_label('Verify attribute', exact=True).check()
    page.get_by_role('tab', name='Text options', exact=True).click()
    page.get_by_label('Attribute height', exact=True).fill('4')
    page.get_by_role('button', name='Move down', exact=True).click()
    page.get_by_label('Apply changes to existing references', exact=True).check()
    before = state()['state']; page.get_by_role('button', name='Apply attributes', exact=True).click()
    expect(page.get_by_text('Applied definition changes as one undo step.', exact=True)).to_be_visible(); idle()
    check('BATTMAN renames edits and reorders definitions while retaining assigned instance values',
          [d['tag'] for d in definitions()] == ['SERVICE', 'PUMP_TAG', 'CONST'] and
          attributes()['PUMP_TAG']['id'] == original['id'] and attributes()['PUMP_TAG']['value'] == 'P-101' and
          attributes()['PUMP_TAG']['height'] == 4 and state()['state']['undoCount'] == before['undoCount'] + 1)
    page.screenshot(path=str(output / 'definition-manager.png'))
    revision = state()['state']['revision']; page.locator('#dialog-submit').click()
    expect(page.locator('#dialog')).not_to_be_visible(); idle()
    check('definition manager OK after Apply does not duplicate history', state()['state']['revision'] == revision)
    command('U'); check('definition manager undo restores tags defaults order and instance identity', definitions() == initial and attributes()['TAG'] == original)
    command('REDO')
    command('ATTDEF', False); expect(page.locator('#dialog-title')).to_have_text('Attribute definition (ATTDEF)')
    page.get_by_label('Definition tag', exact=True).fill('DETAIL')
    page.get_by_label('Definition prompt', exact=True).fill('Pump detail')
    page.get_by_label('Definition default', exact=True).fill('Cooling\nwater')
    page.get_by_label('Definition insertion point', exact=True).fill('5,10,0')
    page.get_by_label('Multiple lines definition', exact=True).check()
    page.screenshot(path=str(output / 'attribute-definition-create.png')); accept()
    check('ATTDEF creates multiline defaults without changing existing instances', definitions()[-1]['multiline'] and 'DETAIL' not in attributes())
    command('BATTMAN', False); expect(page.locator('#dialog-title')).to_have_text('Block attribute manager (BATTMAN)')
    page.get_by_role('button', name='Sync references', exact=True).click()
    expect(page.get_by_text('Applied definition changes as one undo step.', exact=True)).to_be_visible(); idle()
    check('manager Sync adds new multiline defaults while retaining existing assigned values',
          attributes()['DETAIL']['value'] == 'Cooling\\Pwater' and attributes()['PUMP_TAG']['value'] == 'P-101')
    items = {d['tag']: d for d in definitions()}
    page.get_by_label('Block attributes', exact=True).select_option(items['CONST']['id']['value'])
    expect(page.get_by_role('button', name='Move up', exact=True)).to_be_disabled()
    expect(page.get_by_role('button', name='Move down', exact=True)).to_be_disabled()
    page.get_by_label('Block attributes', exact=True).select_option(items['SERVICE']['id']['value'])
    page.get_by_role('button', name='Remove definition', exact=True).click()
    page.get_by_label('Apply changes to existing references', exact=True).check(); accept()
    check('manager removal synchronizes the selected definition as one undoable batch', 'SERVICE' not in attributes() and len(definitions()) == 3)
    command('U'); check('definition removal undo restores the assigned value', attributes()['SERVICE']['value'] == 'Assigned water')
    command('REDO')
    with page.expect_download() as download:
        page.locator('[data-action="save"]').first.click()
    saved = output / 'definitions-edited.dxf'; download.value.save_as(str(saved)); idle()
    document = ezdxf.readfile(saved); audit = document.audit(); restored = document.blocks['DefinitionPump']
    root = list(document.modelspace().query('INSERT'))[0]
    check('independent DXF audit confirms definition edits order and multiline defaults without repairs',
          not audit.errors and not audit.fixes and [a.dxf.tag for a in restored.attdefs()] == ['PUMP_TAG', 'CONST', 'DETAIL'] and
          restored.get_attdef('PUMP_TAG').dxf.text == 'P-999' and root.get_attrib('PUMP_TAG').dxf.text == 'P-101' and
          restored.get_attdef('DETAIL').virtual_mtext_entity().text == 'Cooling\\Pwater')
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#documents .document-tab.active .close').click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous); idle(); open_file(saved)
    check('saved definitions reopen in the same shared manager contracts', [d['tag'] for d in definitions()] == ['PUMP_TAG', 'CONST', 'DETAIL'])
