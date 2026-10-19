"""Shared native/WASM layer-state manager workflows and independent DXF audit."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_action


def verify_layer_states(page, output, state, command, wait_state, idle, check):
    def resources():
        current = state()['state']
        return page.evaluate('''async headers => {
          const id=document.getElementById('studio').dataset.document;
          const response=await fetch(`api/cad/${id}/resources`, {headers});
          if (!response.ok) throw new Error(`${response.status}: ${await response.text()}`);
          return await response.json();
        }''', {'X-Shadow-Document': current['documentId'], 'X-Shadow-Workspace': current['workspaceId'],
               'X-Shadow-View': str(current['viewVersion'])})

    def apply():
        revision = state()['state']['revision']
        page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()
        wait_state(lambda data: data['state']['revision'] > revision)
        idle()

    ribbon_action(page, 'layer-states', 'Manage', 'Resources')
    expect(page.get_by_text('Layer states manager', exact=True)).to_be_visible()
    page.get_by_label('Layer state name', exact=True).fill('Baseline layers')
    page.get_by_label('Layer state description', exact=True).fill('Browser state roundtrip')
    apply()
    check('layer-state manager captures the current layer settings in native DXF state',
          resources()['layerStates'][0]['name'] == 'Baseline layers')

    ribbon_action(page, 'layer-manager', 'Manage', 'Resources')
    page.get_by_label('Color mode', exact=True).select_option('Indexed')
    page.get_by_label('Indexed color', exact=True).fill('1')
    apply()
    check('layer edit changes the source independently of the saved state',
          next(layer for layer in resources()['layers'] if layer['name'] == '0')['colorIndex'] == 1)

    command('LAYERSTATE', False)
    expect(page.get_by_label('Saved layer states', exact=True)).to_be_visible()
    page.get_by_role('button', name='Clear all', exact=True).click()
    page.get_by_label('Restore Color', exact=True).check()
    page.get_by_label('Restore current layer', exact=True).uncheck()
    apply()
    check('LAYERSTATE restores only selected saved properties',
          next(layer for layer in resources()['layers'] if layer['name'] == '0')['colorIndex'] == 7)
    command('U')
    check('layer-state restore is one undoable operation',
          next(layer for layer in resources()['layers'] if layer['name'] == '0')['colorIndex'] == 1)
    command('REDO')

    command('LAS', False)
    page.get_by_label('Layer state name', exact=True).fill('Reviewed layers')
    revision = state()['state']['revision']
    page.get_by_role('button', name='Rename', exact=True).click()
    wait_state(lambda data: data['state']['revision'] > revision); idle()
    command('LAS', False)
    expect(page.get_by_label('Layer state name', exact=True)).to_have_value('Reviewed layers')
    page.screenshot(path=str(output / 'layer-state-manager.png'))
    page.get_by_role('button', name='Cancel', exact=True).click()
    with page.expect_download() as download:
        page.locator('[data-action="save"]').first.click()
    path = output / 'layer-states.dxf'
    download.value.save_as(str(path)); idle()
    drawing = ezdxf.readfile(path); audit = drawing.audit()
    records = [entity for entity in drawing.objects.query('XRECORD')
               if any(tag.code == 301 and tag.value == 'Browser state roundtrip' for tag in entity.tags)]
    check('independent DXF reader finds saved layer-state data without repairs',
          not audit.errors and not audit.fixes and len(records) == 1 and
          any(tag.code == 302 and tag.value == '0' for tag in records[0].tags))
    # Reuse the saved current document slot by closing it before opening the file.
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('#documents .document-tab.active .close').click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    with page.expect_file_chooser() as chooser:
        page.locator('[data-action="open"]').first.click()
    chooser.value.set_files(str(path))
    expect(page.locator('#documents .document-tab.active')).to_contain_text('layer-states.dxf')
    idle()
    command('LAS', False)
    expect(page.get_by_label('Layer state name', exact=True)).to_have_value('Reviewed layers')
    revision = state()['state']['revision']
    page.get_by_role('button', name='Delete', exact=True).click()
    wait_state(lambda data: data['state']['revision'] > revision); idle()
    check('saved states survive reopen and can be deleted through the manager', not resources()['layerStates'])
    command('U')
    check('deleted layer states can be restored with undo', resources()['layerStates'][0]['name'] == 'Reviewed layers')
