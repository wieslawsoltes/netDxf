"""Shared native/WASM layer filters, local groups and native isolation workflows."""
import json
import ezdxf
from playwright.sync_api import expect


def verify_layer_filters(page, output, state, command, wait_state, idle, check):
    def resources():
        return page.evaluate("shadowStudioAgentUi.execute('cad.read', {path:'resources'})")

    def edit(path, body):
        result = page.evaluate("""args => shadowStudioAgentUi.execute('cad.edit', args)""", {'path': path, 'body': body})
        idle()
        return result

    def layer(name):
        return next(item for item in resources()['layers'] if item['name'] == name)

    def receipt():
        value = state()['state']
        return tuple(value.get(key) for key in ('documentId', 'workspaceId', 'revision', 'sourceRevision', 'undoCount', 'redoCount'))

    def flags():
        return {item['name']: tuple(item[field] for field in ('visible', 'frozen', 'locked', 'plot', 'color', 'linetype'))
                for item in resources()['layers']}

    def open_manager():
        page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'layer-filters'})")
        expect(page.get_by_role('combobox', name='Layer filter', exact=True)).to_be_visible()

    def close_manager():
        page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()

    def saved():
        return page.evaluate('key => JSON.parse(localStorage.getItem(key))', storage_key)

    baseline = state()
    baseline_flags = flags()
    storage_key = 'netdxf-layer-filters-v1:' + baseline['state']['documentId']
    old_storage = page.evaluate('key => localStorage.getItem(key)', storage_key)
    original_viewport = page.viewport_size
    a_name, b_name = 'QA_FilterPipes', 'QA_FilterNotes'
    check('layer fixture names do not collide with preexisting drawing resources',
          a_name not in baseline_flags and b_name not in baseline_flags)
    try:
        edit('layer', {'layer': {'name': a_name, 'description': 'Supply line', 'colorIndex': 1, 'lineweight': 'W25', 'transparency': 30}})
        edit('layer', {'layer': {'name': b_name, 'description': 'Notes', 'visible': False, 'frozen': True, 'locked': True, 'plot': False}})
        a_id, b_id = layer(a_name)['id'], layer(b_name)['id']
        discovery_receipt = receipt()
        open_manager()
        chooser = page.get_by_role('combobox', name='Layer filter', exact=True)
        page.get_by_role('textbox', name='Rule 1 Name pattern', exact=True).fill('QA_*')
        page.get_by_role('combobox', name='Rule 1 On', exact=True).select_option('yes')
        expect(page.get_by_role('checkbox', name='Include layer ' + a_name, exact=True)).to_be_visible()
        expect(page.get_by_role('checkbox', name='Include layer ' + b_name, exact=True)).to_have_count(0)
        page.get_by_role('textbox', name='Layer filter or group name', exact=True).fill('QA Visible')
        page.get_by_role('button', name='Save property filter', exact=True).click()
        expect(chooser).to_have_value('f:QA Visible')
        page.get_by_role('checkbox', name='Include layer ' + a_name, exact=True).check()
        page.get_by_role('textbox', name='Layer filter or group name', exact=True).fill('QA Group')
        page.get_by_role('button', name='Save checked as group', exact=True).click()
        expect(chooser).to_have_value('g:QA Group')
        chooser.select_option('')
        page.get_by_role('checkbox', name='Include layer ' + b_name, exact=True).check()
        page.get_by_role('textbox', name='Layer filter or group name', exact=True).fill('QA Group')
        page.get_by_role('button', name='Save checked as group', exact=True).click()
        check('saving a group again explicitly replaces its membership without duplicate groups',
              len([group for group in saved()['groups'] if group['name'] == 'QA Group']) == 1 and
              {item['id'] for item in next(group for group in saved()['groups'] if group['name'] == 'QA Group')['members']} == {a_id['value'], b_id['value']})
        check('property discovery and saved groups leave source revision and undo history unchanged',
              receipt() == discovery_receipt)
        page.screenshot(path=str(output / 'layer-filters-and-groups.png'))
        close_manager()

        edit('layer', {'layer': {'id': a_id, 'name': 'QA_RenamedPipes'}})
        edit('resource', {'resource': {'kind': 'layer', 'action': 'delete', 'id': b_id}})
        open_manager()
        chooser.select_option('g:QA Group')
        expect(page.get_by_role('checkbox', name='Include layer QA_RenamedPipes', exact=True)).to_be_visible()
        expect(chooser.locator('option:checked')).to_have_text('Group: QA Group (1)')
        check('saved group membership follows renamed stable IDs and prunes deleted layers')
        page.set_viewport_size({'width': 390, 'height': 844})
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
        dialog = page.locator('#dialog')
        box = dialog.bounding_box()
        check('compact layer filter manager and its controls stay within the viewport',
              box['x'] >= 0 and box['y'] >= 0 and box['x'] + box['width'] <= 391 and
              box['y'] + box['height'] <= 845 and
              page.locator('.layer-manager').evaluate('node => node.scrollWidth <= node.clientWidth + 1'))
        page.screenshot(path=str(output / 'layer-filters-compact.png'))
        compact_receipt = receipt()
        page.get_by_role('checkbox', name='Include layer QA_RenamedPipes', exact=True).check()
        page.get_by_role('combobox', name='Layer isolation mode', exact=True).select_option('lock')
        expect(page.get_by_role('button', name='Isolate checked layers', exact=True)).to_be_enabled()
        check('mobile scrolling reaches layer checkboxes and isolation controls without editing source',
              receipt() == compact_receipt)
        page.screenshot(path=str(output / 'layer-filters-compact-controls.png'))
        close_manager()
        page.set_viewport_size(original_viewport)

        before_isolation, before_flags = state()['state'], flags()
        open_manager()
        chooser.select_option('g:QA Group')
        page.get_by_role('checkbox', name='Include layer QA_RenamedPipes', exact=True).check()
        page.get_by_role('combobox', name='Layer isolation mode', exact=True).select_option('lock')
        page.get_by_role('button', name='Isolate checked layers', exact=True).click()
        wait_state(lambda value: value['state']['revision'] > before_isolation['revision'])
        idle()
        expect(page.locator('.layer-manager')).to_contain_text('Isolation active (lock)')
        isolated = resources()
        check('checked-layer isolation is one native undo operation with selected layer unlocked',
              state()['state']['undoCount'] == before_isolation['undoCount'] + 1 and
              all(not item['locked'] if item['name'] == 'QA_RenamedPipes' else item['locked']
                  for item in isolated['layers']) and isolated['layerIsolation']['active'])
        close_manager()
        command('U')
        check('one undo restores native layer flags and removes the isolation receipt',
              flags() == before_flags and not resources()['layerIsolation']['active'])
        command('REDO')
        check('redo restores the native isolation receipt', resources()['layerIsolation']['active'])
        with page.expect_download() as downloaded:
            page.locator('[data-action="save"]').first.click()
        path = output / 'layer-isolation.dxf'
        downloaded.value.save_as(str(path))
        idle()
        drawing = ezdxf.readfile(path)
        receipts = [item.get_xdata('SHADOW_LAYER_ISOLATION') for item in drawing.layers
                    if item.has_xdata('SHADOW_LAYER_ISOLATION')]
        check('downloaded DXF carries bounded versioned isolation receipts on actual layers',
              bool(receipts) and all(len(record) == 1 and record[0].code == 1000 and
                                    record[0].value.startswith('1|lock|') for record in receipts))
        command('LAYUNISO')
        check('LAYUNISO restores flags after saved native isolation', flags() == before_flags and not resources()['layerIsolation']['active'])

        item = next((item for item in state()['items'] if not item.get('locked')), None)
        if item:
            edit('select', {'mode': 'entities', 'entities': [{'value': item['id']}]})
            before = state()['state']
            command('LAYISO')
            check('LAYISO derives layers from current source selection in one undo operation',
                  state()['state']['undoCount'] == before['undoCount'] + 1 and resources()['layerIsolation']['active'])
            command('LAYUNISO')
            check('selection isolation restores native global flags without touching geometry', flags() == before_flags)
    finally:
        if page.locator('#dialog').is_visible():
            page.keyboard.press('Escape')
        page.set_viewport_size(original_viewport)
        while state()['state']['undoCount'] > baseline['state']['undoCount']:
            command('U')
        page.evaluate("""args => {
          if(args.value===null)localStorage.removeItem(args.key);else localStorage.setItem(args.key,args.value);
        }""", {'key': storage_key, 'value': old_storage})
        edit('select', {'mode': 'entities', 'entities': [{'value': value} for value in baseline['selectedEntities']]})
    check('layer fixture cleanup restores original resources and native geometry',
          flags() == baseline_flags and sorted(item['id'] for item in state()['items']) == sorted(item['id'] for item in baseline['items']))
