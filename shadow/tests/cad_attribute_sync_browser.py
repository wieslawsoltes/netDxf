"""ATTSYNC review/apply/history/save against both published Studio engines."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_action


def verify_attribute_sync(page, output, state, command, wait_state, idle, check):
    drawing = ezdxf.new('R2018')
    drawing.appids.new('SYNC_REVIEW')
    block = drawing.blocks.new('SyncPump')
    block.add_lwpolyline([(0, 0), (70, 0), (70, 30), (0, 30)], close=True)
    block.add_attdef('TAG', (4, 20), text='DEFAULT', dxfattribs={'height': 3})
    old = block.add_attdef('OLD', (4, 10), text='obsolete', dxfattribs={'height': 3})
    for index in range(2):
        root = drawing.modelspace().add_blockref('SyncPump', (index * 100, 0))
        root.add_auto_attribs({'TAG': f'P-{index + 1}', 'OLD': f'removed-value-{index + 1}'})
        root.get_attrib('TAG').dxf.height = 8
        root.get_attrib('TAG').dxf.color = 1
        root.set_xdata('SYNC_REVIEW', [(1000, 'instance data')])
    block.delete_entity(old)
    block.add_attdef('NEW', (4, 10), text='new default', dxfattribs={'height': 3})
    seed = output / 'attribute-sync-seed.dxf'
    drawing.saveas(seed)
    # Release the previous attribute-editor drawing before importing the new fixture.
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#documents .document-tab.active .close').click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#open-file').set_input_files(str(seed))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    initial = state()
    target = next(item['id'] for item in initial['items'] if item['type'] == 'Insert')
    page.evaluate('''async id => await shadowStudioAgentUi.execute('selection.set',{entities:[id]},
      document.getElementById('studio').dataset.document)''', target)
    idle()
    command('Z E', False)
    ribbon_action(page, 'attribute-sync', 'Insert', 'Blocks')
    expect(page.locator('#dialog-title')).to_have_text('Synchronize block attributes (ATTSYNC)')
    expect(page.get_by_label('Block to synchronize', exact=True)).to_have_value(initial['blocks'][0]['id'])
    page.get_by_role('button', name='Cancel', exact=True).click()
    idle()
    check('ATTSYNC Cancel leaves drawing and history untouched', state()['state']['revision'] == initial['state']['revision'])
    command('ATTSYNC', False)
    expect(page.locator('#dialog')).to_be_visible()
    page.locator('#dialog-submit').click()
    expect(page.locator('#dialog')).not_to_be_visible()
    preview = page.locator('.analysis-preview')
    expect(preview).to_have_count(1)
    expect(preview).to_contain_text('SyncPump: 2 block references.')
    expect(preview).to_contain_text('2 added, 2 removed. 2 INSERT XData records deleted.')
    expect(preview.locator('img')).to_have_count(2)
    preview.get_by_text('Attribute reset details', exact=True).click()
    expect(preview).to_contain_text('removed-value-1')
    expect(preview).to_contain_text('removed-value-2')
    page.screenshot(path=str(output / 'attribute-sync-preview.png'))
    check('ATTSYNC preview covers all references and shows dropped values before any write',
          state()['state']['revision'] == initial['state']['revision'])
    before = state()['state']
    preview.get_by_role('button', name='Apply as one edit', exact=True).click()
    wait_state(lambda data: data['state']['revision'] > before['revision'])
    idle()

    def attributes():
        result = page.evaluate('''async () => await shadowStudioAgentUi.execute('cad.read',{path:'attributes'},
          document.getElementById('studio').dataset.document)''')
        return {a['tag']: a for a in result['attributes']}

    synced = attributes()
    check('ATTSYNC apply preserves assigned values resets formatting and adds definition defaults in one undo',
          set(synced) == {'TAG', 'NEW'} and synced['TAG']['value'].startswith('P-') and synced['TAG']['height'] == 3 and
          synced['NEW']['value'] == 'new default' and state()['state']['undoCount'] == before['undoCount'] + 1)
    command('U')
    check('ATTSYNC undo restores removed attributes and instance formatting',
          set(attributes()) == {'TAG', 'OLD'} and attributes()['TAG']['height'] == 8)
    command('REDO')
    check('ATTSYNC redo retains stable attribute identities', attributes() == synced)
    with page.expect_download() as download:
        page.locator('[data-action="save"]').first.click()
    saved = output / 'attributes-synchronized.dxf'
    download.value.save_as(str(saved))
    idle()
    loaded = ezdxf.readfile(saved)
    audit = loaded.audit()
    inserts = list(loaded.modelspace().query('INSERT'))
    check('independent DXF audit confirms all references and XData reset without repairs',
          not audit.errors and not audit.fixes and len(inserts) == 2 and
          {root.get_attrib('TAG').dxf.text for root in inserts} == {'P-1', 'P-2'} and
          all([a.dxf.tag for a in root.attribs] == ['TAG', 'NEW'] and root.get_attrib('TAG').dxf.height == 3 and
              not root.has_xdata('SYNC_REVIEW') for root in inserts))
