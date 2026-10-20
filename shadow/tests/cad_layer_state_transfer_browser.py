"""Shared native/WASM saved-layer editing and guarded LAS transfer through the real UI."""
from pathlib import Path
from playwright.sync_api import expect


def verify_layer_state_transfer(page, output, state, command, wait_state, idle, check):
    def read(route):
        current = state()['state']
        return page.evaluate('''async ({route,headers}) => {
          const id=document.getElementById('studio').dataset.document;
          const response=await fetch(`api/cad/${id}/${route}`,{headers});
          if(!response.ok) throw new Error(`${response.status}: ${await response.text()}`);
          return response.json();
        }''', {'route': route, 'headers': {'X-Shadow-Document': current['documentId'],
              'X-Shadow-Workspace': current['workspaceId'], 'X-Shadow-View': str(current['viewVersion'])}})

    def resources():
        return read('resources')

    def open_manager():
        command('LAYERSTATE', False)
        expect(page.get_by_label('Saved layer states', exact=True)).to_be_visible()
        values = resources()['layerStates']
        own = next((item for item in values if item['name'] == 'Transfer browser'), None)
        if own:
            page.get_by_label('Saved layer states', exact=True).select_option(own['id']['value'])

    def submit():
        before = state()['state']['revision']
        page.locator('#dialog-submit').click()
        wait_state(lambda data: data['state']['revision'] > before)
        idle()

    def details():
        own = next(item for item in resources()['layerStates'] if item['name'] == 'Transfer browser')
        return read('layer-state/details?id=' + own['id']['value'])

    initial = resources()
    initial_layers = initial['layers']
    open_manager()
    page.get_by_role('button', name='New', exact=True).click()
    page.get_by_label('Layer state name', exact=True).fill('Transfer browser')
    submit()
    open_manager()
    page.get_by_role('button', name='Edit saved properties', exact=True).click()
    expect(page.get_by_label('Saved layer entries', exact=True)).to_be_visible()
    page.get_by_label('Saved layer entries', exact=True).select_option('0')
    page.get_by_label('Saved color mode', exact=True).select_option('Indexed')
    page.get_by_label('Saved indexed color', exact=True).fill('3')
    page.get_by_label('Saved Locked', exact=True).check()
    page.get_by_label('Saved transparency', exact=True).fill('25')
    submit()
    saved_zero = next(entry for entry in details()['entries'] if entry['name'] == '0')
    check('saved property editing changes the state while preserving all live layers',
          saved_zero['colorIndex'] == 3 and saved_zero['flags'] & 4 and saved_zero['transparency'] == 25 and resources()['layers'] == initial_layers)

    open_manager()
    page.get_by_role('button', name='Edit saved properties', exact=True).click()
    page.get_by_label('Saved layer entries', exact=True).select_option('0')
    page.get_by_role('button', name='Remove saved layer', exact=True).click()
    submit()
    check('saved membership can remove a drawing layer without deleting its resource',
          not any(entry['name'] == '0' for entry in details()['entries']) and resources()['layers'] == initial_layers)
    open_manager()
    page.get_by_role('button', name='Edit saved properties', exact=True).click()
    page.get_by_label('Drawing layer to add', exact=True).select_option('0')
    page.get_by_role('button', name='Add drawing layer', exact=True).click()
    page.get_by_role('button', name='Remove pending addition', exact=True).click()
    expect(page.get_by_role('button', name='Remove pending addition', exact=True)).not_to_be_visible()
    page.get_by_label('Drawing layer to add', exact=True).select_option('0')
    page.get_by_role('button', name='Add drawing layer', exact=True).click()
    submit()
    check('saved membership adds a drawing-layer snapshot and can undo pending additions',
          any(entry['name'] == '0' for entry in details()['entries']) and resources()['layers'] == initial_layers)

    open_manager()
    page.get_by_role('button', name='Clear all', exact=True).click()
    page.get_by_label('Restore Color', exact=True).check()
    before = state()['state']['revision']
    page.get_by_role('button', name='Save restore mask', exact=True).click()
    wait_state(lambda data: data['state']['revision'] > before); idle()
    open_manager()
    expect(page.get_by_label('Restore Color', exact=True)).to_be_checked()
    expect(page.get_by_label('Restore On / Off', exact=True)).not_to_be_checked()
    with page.expect_download() as transfer:
        page.get_by_role('button', name='Export LAS', exact=True).click()
    las = Path(output) / 'layer-state-transfer.las'
    transfer.value.save_as(str(las)); idle()
    lines = las.read_text(encoding='utf-8-sig').splitlines()
    pairs = [(int(lines[index]), lines[index + 1]) for index in range(0, len(lines), 2)]
    check('LAS export carries persisted native restore-mask bits', next(value for code, value in pairs if code == 91) == '32')

    # Change only the saved entry name in the local fixture to exercise explicit resource repair.
    incoming = [(code, 'Mapped import' if code == 1 else 'Missing source layer' if code == 8 and value == '0' else value)
                for code, value in pairs]
    imported = Path(output) / 'layer-state-missing-resource.las'
    imported.write_text(''.join(f'{code}\n{value}\n' for code, value in incoming), encoding='utf-8')
    open_manager()
    page.get_by_role('button', name='Import LAS', exact=True).click()
    page.get_by_label('LAS file', exact=True).set_input_files(str(imported))
    before = state()['state']
    page.locator('#dialog-submit').click()
    expect(page.get_by_label('Map layer Missing source layer', exact=True)).to_be_visible()
    check('LAS conflict preview does not mutate source or history', state()['state']['revision'] == before['revision'])
    page.get_by_label('Map layer Missing source layer', exact=True).select_option('0')
    page.locator('#dialog-submit').click()
    expect(page.locator('#dialog-submit')).to_have_text('Import saved state')
    page.screenshot(path=str(Path(output) / 'layer-state-las-preview.png'))
    check('mapped preview stays read-only until explicit import', state()['state']['revision'] == before['revision'])
    submit()
    check('guarded LAS import creates a saved state without restoring live layer properties',
          any(item['name'] == 'Mapped import' for item in resources()['layerStates']) and resources()['layers'] == initial_layers)
    command('U')
    check('LAS import is one undo operation', not any(item['name'] == 'Mapped import' for item in resources()['layerStates']))

    original_viewport = page.viewport_size
    page.set_viewport_size({'width': 390, 'height': 844})
    open_manager()
    page.get_by_role('button', name='Edit saved properties', exact=True).click()
    expect(page.get_by_label('Saved layer entries', exact=True)).to_be_visible()
    add_box = page.get_by_label('Drawing layer to add', exact=True).bounding_box()
    check('saved-layer resource dropdown stays compact on mobile', add_box is not None and add_box['height'] <= 45)
    page.get_by_label('Saved transparency', exact=True).scroll_into_view_if_needed()
    expect(page.get_by_label('Saved transparency', exact=True)).to_be_visible()
    expect(page.locator('#dialog-submit')).to_be_visible()
    box = page.locator('#dialog').bounding_box()
    check('saved layer editor remains within the mobile viewport with reachable save controls',
          box is not None and box['x'] >= -1 and box['y'] >= -1 and box['x'] + box['width'] <= 391 and box['y'] + box['height'] <= 845)
    page.screenshot(path=str(Path(output) / 'layer-state-editor-mobile.png'))
    page.get_by_role('button', name='Cancel', exact=True).click()
    if original_viewport:
        page.set_viewport_size(original_viewport)
    for _ in range(5):
        command('U')
    check('saved-state edit and transfer workflow returns to its original source layers and states',
          resources()['layers'] == initial_layers and resources()['layerStates'] == initial['layerStates'])
