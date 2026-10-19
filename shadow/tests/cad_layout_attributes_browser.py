"""Standalone ATTDEF placement and inclusion in a block, shared by both browser hosts."""
import ezdxf
from playwright.sync_api import expect


def verify_layout_attributes(page, output, state, command, wait_state, idle, check):
    seed = output / 'layout-attributes-seed.dxf'
    drawing = ezdxf.new('R2018'); drawing.modelspace().add_line((0, 0), (40, 0)); drawing.saveas(seed)
    if page.locator('#documents .document-tab').count() >= 8:
        page.locator('#documents .document-tab .close').first.click()
        page.wait_for_function("document.querySelectorAll('#documents .document-tab').length < 8 || document.querySelector('#dialog').open")
        if page.locator('#dialog').is_visible():
            expect(page.locator('#dialog-title')).to_have_text('Unsaved drawing')
            page.locator('#dialog-submit').click()
        expect(page.locator('#documents .document-tab')).to_have_count(7); idle()
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#open-file').set_input_files(str(seed))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous); idle()

    def definitions():
        return page.evaluate('''async () => await shadowStudioAgentUi.execute('cad.read',
          {path:'attribute-definitions',currentSpace:true},document.getElementById('studio').dataset.document)''')

    def open_definition(tag, value):
        command('ATTDEF', False); expect(page.locator('#dialog-title')).to_have_text('Attribute definition (ATTDEF)')
        page.get_by_label('Definition tag', exact=True).fill(tag)
        page.get_by_label('Definition default', exact=True).fill(value)

    before = state()['state']; open_definition('CANCELLED', 'Unused')
    page.locator('#dialog-submit').click(); expect(page.locator('#dialog')).not_to_be_visible()
    expect(page.locator('#prompt')).to_contain_text('Specify attribute insertion point')
    check('ATTDEF opens in a drawing without named blocks and waits before publication',
          state()['state']['revision'] == before['revision'] and not definitions()['definitions'])
    page.keyboard.press('Escape'); idle()
    check('Escape cancels standalone attribute placement without a history entry',
          state()['state']['undoCount'] == before['undoCount'] and not definitions()['definitions'])

    open_definition('TAG', 'P-101'); page.get_by_label('Definition prompt', exact=True).fill('Pump identifier')
    page.locator('#dialog-submit').click(); expect(page.locator('#dialog')).not_to_be_visible()
    expect(page.locator('#prompt')).to_contain_text('Specify attribute insertion point')
    revision = state()['state']['revision']
    page.locator('#command-input').fill('5,10'); page.locator('#command-input').press('Enter')
    wait_state(lambda s: s['state']['revision'] > revision); idle()
    first = definitions()['definitions'][0]; first_id = first['appearance']['id']['value']
    check('on-screen ATTDEF creates a selectable layout object with independent tag and default',
          first['appearance']['tag'] == 'TAG' and first['appearance']['value'] == 'P-101' and
          tuple(first['position'][axis] for axis in ('x', 'y', 'z')) == (5, 10, 0) and state()['selectedEntities'] == [first_id])
    command('MOVE 0,0 10,0')
    check('MOVE edits a standalone attribute through the ordinary command workflow', definitions()['definitions'][0]['position']['x'] == 15)
    command('U'); check('standalone attribute move undo retains identity and coordinates',
                        definitions()['definitions'][0]['appearance']['id']['value'] == first_id and definitions()['definitions'][0]['position']['x'] == 5)
    command('REDO')
    open_definition('DETAIL', 'Cooling\nwater')
    page.get_by_label('Specify attribute point on screen', exact=True).uncheck()
    page.get_by_label('Definition insertion point', exact=True).fill('5,5,0')
    page.get_by_label('Multiple lines definition', exact=True).check()
    revision = state()['state']['revision']; page.locator('#dialog-submit').click()
    expect(page.locator('#dialog')).not_to_be_visible(); wait_state(lambda s: s['state']['revision'] > revision); idle()
    command('Z E', False); page.screenshot(path=str(output / 'layout-attribute-definitions.png'))
    with page.expect_download() as download: page.locator('[data-action="save"]').first.click()
    standalone = output / 'layout-attributes.dxf'; download.value.save_as(str(standalone)); idle()
    document = ezdxf.readfile(standalone); audit = document.audit(); values = {item.dxf.tag: item for item in document.modelspace().query('ATTDEF')}
    check('independent DXF audit preserves standalone single and multiline definitions without repairs',
          not audit.errors and not audit.fixes and len(values) == 2 and values['TAG'].dxf.text == 'P-101' and
          values['TAG'].dxf.insert.x == 15 and values['DETAIL'].virtual_mtext_entity().text == 'Cooling\\Pwater')

    page.locator('#viewport').focus(); page.keyboard.press('Control+a'); wait_state(lambda s: len(s['selectedEntities']) == 3); idle()
    command('BLOCK LayoutPump 0,0')
    check('BLOCK includes layout definitions in the selected geometry and creates assigned defaults',
          not definitions()['definitions'] and len(state()['selectedEntities']) == 1 and
          next(block for block in state()['blocks'] if block['name'] == 'LayoutPump')['attributes'] == 2)
    command('U'); check('BLOCK undo restores the original standalone attribute identity',
                        definitions()['definitions'][0]['appearance']['id']['value'] == first_id)
    command('REDO')
    with page.expect_download() as download: page.locator('[data-action="save"]').first.click()
    converted = output / 'layout-attribute-block.dxf'; download.value.save_as(str(converted)); idle()
    document = ezdxf.readfile(converted); audit = document.audit(); insert = list(document.modelspace().query('INSERT'))[0]
    check('independent DXF audit confirms converted block definitions and assigned values',
          not audit.errors and not audit.fixes and len(list(document.modelspace().query('ATTDEF'))) == 0 and
          insert.get_attrib('TAG').dxf.text == 'P-101' and insert.get_attrib('DETAIL').virtual_mtext_entity().text == 'Cooling\\Pwater' and
          {item.dxf.tag for item in document.blocks['LayoutPump'].attdefs()} == {'TAG', 'DETAIL'})
