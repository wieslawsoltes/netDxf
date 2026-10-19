"""Instance attribute editing against the real native and standalone WASM engines."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_action


def verify_attributes(page, output, state, command, wait_state, idle, check):
    seed = output / 'attribute-seed.dxf'
    drawing = ezdxf.new('R2018')
    drawing.layers.new('Annotations', dxfattribs={'color': 3})
    drawing.styles.new('Review', dxfattribs={'font': 'DejaVuSans.ttf'})
    block = drawing.blocks.new('TankPump')
    block.add_lwpolyline([(0, 0), (80, 0), (80, 40), (0, 40)], close=True)
    block.add_attdef('TAG', (5, 30), text='P-101', dxfattribs={'height': 3, 'prompt': 'Equipment tag'})
    block.add_attdef('SERVICE', (5, 20), text='Water', dxfattribs={'height': 3, 'prompt': 'Service'})
    insert = drawing.modelspace().add_blockref('TankPump', (0, 0))
    insert.add_auto_attribs({'TAG': 'P-101', 'SERVICE': 'Water'})
    service = insert.get_attrib('SERVICE')
    text = drawing.modelspace().add_mtext('Water\\PStorage tank', dxfattribs={'insert': (5, 20), 'char_height': 3, 'width': 65})
    service.embed_mtext(text)
    service.dxf.text = 'FALLBACK KEPT'
    drawing.saveas(seed)

    def open_file(path):
        before = page.locator('#studio').get_attribute('data-document')
        page.locator('#open-file').set_input_files(str(path))
        expect(page.locator('#studio')).not_to_have_attribute('data-document', before)
        idle()
        page.locator('#viewport').focus()
        page.keyboard.press('Control+a')
        wait_state(lambda data: len(data['selectedEntities']) == 1)
        idle()

    def read():
        return page.evaluate('''async () => await shadowStudioAgentUi.execute('cad.read', {path:'attributes'},
          document.getElementById('studio').dataset.document)''')

    def attributes():
        return {item['tag']: item for item in read()['attributes']}

    def tab(name):
        page.locator('#dialog').get_by_role('tab', name=name, exact=True).click()

    def accept():
        before = state()['state']['revision']
        page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()
        wait_state(lambda data: data['state']['revision'] > before)
        idle()

    # Release the initial clean sample tab; the native suite already has eight drawings.
    if page.locator('#documents .document-tab').count() >= 8:
        page.locator('#documents .document-tab .close').first.click()
        expect(page.locator('#documents .document-tab')).to_have_count(7)
        idle()
    open_file(seed)
    initial = read()
    check('attribute discovery exposes visible multiline content independently of fallback',
          attributes()['SERVICE']['value'] == 'Water\\PStorage tank' and attributes()['SERVICE']['multiline'])
    command('Z E', False)
    ribbon_action(page, 'attributes', 'Insert', 'Blocks')
    expect(page.locator('#dialog-title')).to_have_text('Enhanced attribute editor')
    page.get_by_label('Block attributes', exact=True).select_option(attributes()['TAG']['id']['value'])
    page.get_by_label('Attribute value', exact=True).fill('P-202')
    tab('Text options')
    page.get_by_label('Attribute height', exact=True).fill('4')
    page.get_by_label('Attribute text style', exact=True).select_option('Review')
    tab('Properties')
    page.get_by_label('Attribute layer', exact=True).select_option('Annotations')
    page.get_by_label('Attribute color', exact=True).fill('#3366CC')
    page.get_by_label('Attribute position locked', exact=True).check()
    before = state()['state']
    page.get_by_role('button', name='Apply attributes', exact=True).click()
    expect(page.get_by_text('Applied 1 attribute changes as one undo step.', exact=True)).to_be_visible()
    idle()
    current = attributes()['TAG']
    check('Apply edits text and properties together while keeping the editor open',
          current['value'] == 'P-202' and current['height'] == 4 and current['textStyle'] == 'Review' and
          current['layer'] == 'Annotations' and current['color'] == '#3366CC' and current['positionLocked'] and
          state()['state']['undoCount'] == before['undoCount'] + 1)
    applied = state()['state']['revision']
    page.locator('#dialog-submit').click()
    expect(page.locator('#dialog')).not_to_be_visible(); idle()
    check('OK after Apply creates no duplicate transaction', state()['state']['revision'] == applied)
    command('U')
    check('attribute undo restores original value properties and identities',
          attributes()['TAG'] == {item['tag']: item for item in initial['attributes']}['TAG'])
    command('REDO')
    check('attribute redo restores the authored instance without replacing its identity',
          attributes()['TAG'] == current)

    command('EATTEDIT', False)
    expect(page.locator('#dialog')).to_be_visible()
    page.get_by_label('Block attributes', exact=True).select_option(attributes()['SERVICE']['id']['value'])
    page.get_by_label('Multiline attribute value', exact=True).fill('Cooling water\nStorage tank')
    tab('Text options')
    expect(page.get_by_label('Attribute width factor', exact=True)).to_have_count(0)
    page.get_by_label('Attribute height', exact=True).fill('5')
    page.get_by_label('Attribute boundary width', exact=True).fill('70')
    page.get_by_label('Attribute rotation', exact=True).fill('15')
    page.get_by_label('Attribute line spacing', exact=True).fill('1.5')
    page.get_by_label('Exact attribute line spacing', exact=True).check()
    page.screenshot(path=str(output / 'attribute-editor.png'))
    accept()
    multiline = attributes()['SERVICE']
    check('EATTEDIT changes embedded multiline layout with independent instance values',
          multiline['value'] == 'Cooling water\\PStorage tank' and multiline['height'] == 5 and
          multiline['width'] == 70 and abs(multiline['rotation'] - 15) < 1e-8 and multiline['exactSpacing'] and multiline['lineSpacing'] == 1.5)
    command('U')
    check('multiline undo restores the captured embedded packet', attributes()['SERVICE']['value'] == 'Water\\PStorage tank')
    command('REDO')

    info = read()
    page.evaluate('''async data => await shadowStudioAgentUi.execute('cad.edit',
      {path:'attributes',body:{insert:data.insert,attributes:[{attribute:data.attributes.find(a=>a.tag==='TAG').id,value:'AGENT-303'}]}},
      document.getElementById('studio').dataset.document)''', info)
    idle()
    check('agents can discover and edit instance attributes through the same transaction', attributes()['TAG']['value'] == 'AGENT-303')
    command('U')
    # Read the actual renderer output, not an imitation in the browser fixture.
    svg = page.evaluate('''async () => {
      const id=document.getElementById('studio').dataset.document;
      const {state:s}=await (await fetch(`api/cad/${id}/state`)).json();
      const response=await fetch(`api/cad/${id}/render?width=800&height=500&cx=40&cy=20&scale=6&backend=svg&revision=${s.revision}`,
        {headers:{'X-Shadow-Document':s.documentId,'X-Shadow-Workspace':s.workspaceId,'X-Shadow-View':String(s.viewVersion)}});
      if(!response.ok)throw new Error(await response.text());return await response.text();
    }''')
    check('SVG renderer uses edited embedded content rather than stored fallback',
          'Cooling' in svg and 'P-202' in svg and 'FALLBACK KEPT' not in svg)
    with page.expect_download() as download:
        page.locator('[data-action="save"]').first.click()
    saved = output / 'attributes-edited.dxf'
    download.value.save_as(str(saved)); idle()
    document = ezdxf.readfile(saved); audit = document.audit()
    saved_insert = list(document.modelspace().query('INSERT'))[0]
    tag, service = saved_insert.get_attrib('TAG'), saved_insert.get_attrib('SERVICE')
    embedded = service.virtual_mtext_entity()
    check('independent DXF reader accepts edited attributes without errors or repairs',
          not audit.errors and not audit.fixes and tag.dxf.text == 'P-202' and tag.dxf.layer == 'Annotations' and
          tag.dxf.true_color == 0x3366CC and tag.dxf.style == 'Review' and service.dxf.text == 'FALLBACK KEPT' and
          embedded.text == 'Cooling water\\PStorage tank' and embedded.dxf.char_height == 5 and
          document.blocks['TankPump'].get_attdef('TAG').dxf.text == 'P-101')
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#documents .document-tab.active .close').click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous); idle()
    open_file(saved)
    command('ATE', False)
    expect(page.locator('#dialog-title')).to_have_text('Enhanced attribute editor')
    page.get_by_label('Block attributes', exact=True).select_option(attributes()['SERVICE']['id']['value'])
    expect(page.get_by_label('Multiline attribute value', exact=True)).to_have_value('Cooling water\\PStorage tank')
    page.get_by_role('button', name='Cancel', exact=True).click(); idle()
    check('saved attribute instances reopen with multiline values and editable resource references',
          attributes()['TAG']['color'] == '#3366CC' and attributes()['SERVICE']['height'] == 5)
