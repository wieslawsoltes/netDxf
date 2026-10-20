"""Declared discipline graph workflows against the real native/WASM runtime."""
import json
import re
import ezdxf
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_domain_connectivity(page, output, state, command, wait_state, idle, check):
    original_id = page.locator('#studio').get_attribute('data-document')
    original_name = page.locator('#documents .document-tab.active .name').inner_text()
    export_number = 0

    def action(name):
        return page.evaluate("name=>shadowStudioAgentUi.execute('ui.action',{name})", name)

    def edit(path, body):
        result = page.evaluate("p=>shadowStudioAgentUi.execute('cad.edit',p)", {'path': path, 'body': body})
        idle()
        return result

    def read(body):
        return page.evaluate("body=>shadowStudioAgentUi.execute('cad.read',{path:'modules/connectivity',body})", body)

    def failure(body):
        return page.evaluate("async body=>{try{await shadowStudioAgentUi.execute('cad.read',{path:'modules/connectivity',body});return '';}catch(error){return String(error.message||error);}}", body)

    def panel(module):
        action('module-open:' + module + ':connectivity')
        result = page.locator('[data-palette="Modules"] .palette-content')
        expect(result.get_by_label('Connectivity operation', exact=True)).to_be_visible()
        return result

    def run(view):
        view.get_by_role('button', name='Run connectivity analysis', exact=True).click()
        expect(view.get_by_role('button', name='Run connectivity analysis', exact=True)).to_be_enabled()
        expect(view).to_contain_text('weak components')

    def exported(view):
        nonlocal export_number
        with page.expect_download() as download:
            view.get_by_role('button', name='Export connectivity JSON', exact=True).click()
        data = json.loads(open(download.value.path(), encoding='utf-8').read())
        export_number += 1
        (output / f'discipline-connectivity-{export_number:02}.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
        return data

    def inspect(view, node):
        view.get_by_label('Search connectivity ports', exact=True).fill(node['key'])
        view.get_by_role('button', name=(node['tag'] or node['class']) + ' · ' + node['port'], exact=True).click()
        expect(view.locator('.domain-inspector')).to_contain_text(node['id'])

    def close_current():
        current_id = page.locator('#studio').get_attribute('data-document')
        page.locator('#documents .document-tab.active .close').click()
        wait_for_ui(page, "id=>document.querySelector('#studio').dataset.document!==id||document.querySelector('#dialog')?.open", arg=current_id)
        if page.locator('#dialog').evaluate('dialog=>dialog.open'):
            page.locator('#dialog-submit').click()
        expect(page.locator('#studio')).not_to_have_attribute('data-document', current_id)
        idle()

    # Earlier native-host fixtures can fill the eight-document limit. Retire a
    # saved, inactive fixture before adding this independent drawing; never
    # discard unsaved state or change the graph fixture used by later checks.
    if page.locator('#documents .document-tab').count() >= 8:
        saved = next((tab for tab in page.locator('#documents .document-tab:not(.active)').all()
                      if not tab.locator('.name').inner_text().startswith('● ')), None)
        assert saved is not None, 'The private browser suite needs one saved document slot.'
        saved.locator('.close').click()
        expect(page.locator('#documents .document-tab')).to_have_count(7)
        idle()
        page.locator('#documents .name').filter(has_text=re.compile('^' + re.escape(original_name) + '$')).click()
        expect(page.locator('#studio')).to_have_attribute('data-document', original_id)
        idle()
    action('new')
    expect(page.locator('#studio')).not_to_have_attribute('data-document', original_id)
    idle()
    saved_network = None
    for index, module in enumerate(('pid', 'piping', 'electrical')):
        edit('modules/toggle', {'module': module, 'enabled': True})
        y = index * 30
        edit('modules/command', {'module': module, 'command': {'command': 'route', 'points': [{'x': x, 'y': y, 'z': 0} for x in (0, 10, 20)],
             'properties': {'tag': 'D-' + module.upper(), 'service': 'Water', 'spec': 'CS', 'nominalSize': '50', 'flow': 'forward', 'circuit': 'C1', 'crossSection': '1.5'}}})
        baseline = state()
        view = panel(module)
        view.get_by_label('Connectivity operation', exact=True).select_option('network')
        run(view)
        network = exported(view)
        check(module + ' connectivity follows explicit route passages and reciprocal endpoint reference',
              network['complete'] and network['declarationsComplete'] and not network['partial'] and
              len(network['nodes']) == 4 and len(network['edges']) == 3 and len(network['components']) == 1 and
              sum(edge['kind'] == 'passage' for edge in network['edges']) == 2)
        check(module + ' graph analysis preserves source geometry, selection and undo history',
              state()['state']['revision'] == baseline['state']['revision'] and
              state()['state']['undoCount'] == baseline['state']['undoCount'] and state()['selectedEntities'] == baseline['selectedEntities'])
        image = view.locator('.domain-graph-image')
        check(module + ' schematic decodes under existing CSP', image.evaluate('async image=>{await image.decode();return image.complete&&image.naturalWidth>0;}'))
        start = next(node for node in network['nodes'] if node['position']['x'] == 0)
        end = next(node for node in network['nodes'] if node['position']['x'] == 20)
        inspect(view, start)
        view.get_by_role('button', name='Use as source port', exact=True).click()
        inspect(view, end)
        view.get_by_role('button', name='Use as target port', exact=True).click()
        view.get_by_label('Connectivity operation', exact=True).select_option('path')
        view.get_by_label('Connectivity direction', exact=True).select_option('forward')
        run(view)
        path = exported(view)
        check(module + ' minimum-link path exports exact predecessor proof chain',
              path['walk']['status'] == 'found' and path['walk']['pathLength'] == 3 and len(path['visits']) == 4 and
              all(visit['depth'] == 0 or visit['connection'] in {edge['id'] for edge in path['edges']} for visit in path['visits']))
        inspect(view, end)
        view.get_by_role('button', name='Show predecessor proof', exact=True).click()
        expect(view).to_contain_text('Proof: 3 links')
        with page.expect_download() as svg_download:
            view.get_by_role('button', name='Export connectivity SVG', exact=True).click()
        svg = open(svg_download.value.path(), encoding='utf-8').read()
        (output / f'discipline-connectivity-{module}-proof.svg').write_text(svg, encoding='utf-8')
        check(module + ' SVG exports declared proof highlights and receipt coverage', '#dc2626' in svg and 'declarations complete' in svg and 'not geometry or simulation' in svg)
        check(module + ' SVG draws directional links visibly outside port cards',
              'refX="10"' in svg and ('marker-end="url(#arrow)"' in svg or 'marker-start="url(#arrow)"' in svg) and 'fill="none"' in svg)
        if module == 'pid':
            window = page.locator('[data-palette="Modules"]')
            window.get_by_label('Modules palette layout', exact=True).click()
            window.get_by_role('button', name='Float palette: Modules', exact=True).click()
            workspace = page.locator('#workspace').bounding_box()
            grip = window.get_by_role('button', name='Move Modules palette', exact=True).bounding_box()
            page.keyboard.down('Control')
            page.mouse.move(grip['x'] + grip['width'] / 2, grip['y'] + grip['height'] / 2)
            page.mouse.down()
            page.mouse.move(workspace['x'] + 60, workspace['y'] + 25, steps=8)
            page.mouse.up()
            page.keyboard.up('Control')
            box = window.bounding_box()
            resize = window.get_by_role('separator', name='Resize Modules palette', exact=True).bounding_box()
            page.mouse.move(resize['x'] + resize['width'] / 2, resize['y'] + resize['height'] / 2)
            page.mouse.down()
            page.mouse.move(box['x'] + 930, min(workspace['y'] + workspace['height'] - 8, box['y'] + 640), steps=8)
            page.mouse.up()
            view.locator('.domain-diagram').evaluate("node=>node.scrollIntoView({block:'start'})")
            expect(view.locator('.domain-graph-image')).to_be_visible()
            expect(view.locator('.domain-inspector')).to_be_visible()
            check('wide floating discipline workspace presents schematic and port inspector together', window.bounding_box()['width'] >= 800)
            page.screenshot(path=str(output / 'discipline-connectivity-pid-proof.png'))
            window.get_by_label('Modules palette layout', exact=True).click()
            window.get_by_role('button', name='Dock right: Modules', exact=True).click()
            splitter = page.get_by_role('separator', name='Resize right docked palettes', exact=True).bounding_box()
            page.mouse.move(splitter['x'] + splitter['width'] / 2, splitter['y'] + splitter['height'] / 2)
            page.mouse.down()
            page.mouse.move(workspace['x'] + workspace['width'] - 340, splitter['y'] + splitter['height'] / 2, steps=8)
            page.mouse.up()
            view.locator('.domain-diagram').evaluate("node=>node.scrollIntoView({block:'start'})")
            check('docked connectivity preserves readable intrinsic labels with contained horizontal scrolling',
                  image.evaluate("""async image=>{
                    await image.decode();
                    const frame=image.closest('.domain-diagram');
                    return image.naturalWidth===784 &&
                      Math.abs(image.getBoundingClientRect().width-image.naturalWidth)<1 &&
                      frame.scrollWidth>frame.clientWidth &&
                      frame.getBoundingClientRect().right<=innerWidth+1 &&
                      document.documentElement.scrollWidth<=innerWidth+1;
                  }"""))
            page.screenshot(path=str(output / 'discipline-connectivity-pid-docked.png'))
        with page.expect_download() as csv_download:
            view.get_by_role('button', name='Export ports CSV', exact=True).click()
        csv = open(csv_download.value.path(), encoding='utf-8').read()
        check(module + ' CSV retains scope, coverage and predecessor fields', 'graphKey' in csv and 'declarationsComplete' in csv and 'predecessor' in csv and 'D-' + module.upper() in csv)
        view.get_by_label('Connectivity operation', exact=True).select_option('network')
        run(view)
        with page.expect_download() as fresh_svg_download:
            view.get_by_role('button', name='Export connectivity SVG', exact=True).click()
        fresh_svg = open(fresh_svg_download.value.path(), encoding='utf-8').read()
        check(module + ' fresh network keeps the same ports while removing the previous proof highlight',
              '#dc2626' not in fresh_svg and '<path ' in fresh_svg and start['port'] in fresh_svg and end['port'] in fresh_svg)
        inspect(view, end)
        view.get_by_role('button', name='Select port source', exact=True).click()
        source_id = end['entity']['value'] if isinstance(end['entity'], dict) else end['entity']
        wait_state(lambda data: data['selectedEntities'] == [source_id])
        idle()
        check(module + ' explicit source selection preserves history', state()['state']['undoCount'] == baseline['state']['undoCount'])
        stale_query = {**network['query'], 'graphKey': network['graphKey']}
        check(module + ' graph keys reject another operation scope', 'stale' in failure(stale_query).lower())
        edit('modules/toggle', {'module': module, 'enabled': False})
        check(module + ' disabled module refuses connectivity while retaining geometry',
              'enable' in failure({**network['query'], 'revision': state()['state']['revision']}).lower() and
              state()['state']['undoCount'] == baseline['state']['undoCount'])
        edit('modules/toggle', {'module': module, 'enabled': True})
        if module == 'pid':
            saved_network = network

    view = panel('pid')
    command('SNAPSHOT', False)
    revision = state()['state']['revision']
    page.get_by_role('button', name=f'Revision {revision} — read only', exact=True).click()
    action('module-open:pid:connectivity')
    expect(view).to_contain_text('Return to Current drawing')
    expect(view.get_by_role('button', name='Run connectivity analysis', exact=True)).to_have_count(0)
    check('retained snapshot cannot masquerade as current discipline analysis')
    page.get_by_role('button', name='Current drawing', exact=True).click()
    with page.expect_download() as dxf_download:
        action('save')
    fixture_path = output / 'discipline-connectivity-roundtrip.dxf'
    dxf_download.value.save_as(str(fixture_path))
    close_current()
    page.locator('#open-file').set_input_files(str(fixture_path))
    wait_state(lambda data: data['state']['occurrences'] == 6)
    idle()
    edit('modules/toggle', {'module': 'pid', 'enabled': True})
    view = panel('pid')
    view.get_by_label('Connectivity operation', exact=True).select_option('network')
    run(view)
    imported = exported(view)
    check('DXF export/import preserves portable port and declared connection identities',
          {node['id'] for node in imported['nodes']} == {node['id'] for node in saved_network['nodes']} and
          {edge['id'] for edge in imported['edges']} == {edge['id'] for edge in saved_network['edges']})
    close_current()

    # Large imported native fixture uses ordinary documented XDATA, not an in-memory graph stub.
    doc = ezdxf.new('R2010')
    doc.appids.new('SHADOW_CAD')
    model = doc.modelspace()
    for i in range(1301):
        line = model.add_line((i, 0), (i, 1))
        record = {'module': 'pid', 'class': 'pipe', 'key': 'large.' + str(i), 'tag': 'L-' + str(i), 'schemaVersion': 1,
                  'properties': {'openPorts': 'start,end'}, 'ports': [{'id': port, 'kind': 'process', 'position': {'x': i, 'y': y, 'z': 0},
                    'direction': {'x': 0, 'y': 0, 'z': 0}} for port, y in [('start', 0), ('end', 1)]], 'references': []}
        if i < 1300:
            record['passages'] = [{'fromPort': 'start', 'toPort': 'end', 'direction': 'both'}]
        payload = json.dumps(record, separators=(',', ':'))
        line.set_xdata('SHADOW_CAD', [(1000, 'netdxf.domain.v1')] + [(1000, payload[j:j + 200]) for j in range(0, len(payload), 200)])
    model.add_line((-1, .5), (1301, .5))  # A crossing creates no implicit connections.
    large_path = output / 'discipline-connectivity-large.dxf'
    doc.saveas(large_path)
    page.locator('#open-file').set_input_files(str(large_path))
    wait_state(lambda data: data['state']['occurrences'] == 1302)
    idle()
    edit('modules/toggle', {'module': 'pid', 'enabled': True})
    view = panel('pid')
    view.get_by_label('Connectivity operation', exact=True).select_option('network')
    run(view)
    first = exported(view)
    check('large native domain projection starts with honest bounded pages', first['partial'] and first['totals']['nodes'] == 2602 and first['totals']['edges'] == 1300)
    view.get_by_role('button', name='Load remaining connectivity', exact=True).click()
    expect(view.get_by_role('button', name='Run connectivity analysis', exact=True)).to_be_enabled(timeout=60000)
    loaded = exported(view)
    check('bounded continuation covers every imported port, edge and component with no duplicate or inferred crossing',
          not loaded['partial'] and len(loaded['nodes']) == 2602 and len(loaded['edges']) == 1300 and len(loaded['components']) == 1302 and
          len({node['id'] for node in loaded['nodes']}) == 2602)
    check('legacy imported record explicitly reports unspecified internal passages', loaded['complete'] and not loaded['declarationsComplete'] and any(issue['code'] == 'unspecified-passages' for issue in loaded['issues']))
    view.get_by_label('Connectivity operation', exact=True).select_option('path')
    view.get_by_label('Connectivity source port', exact=True).fill('large.1300/start')
    view.get_by_label('Connectivity target port', exact=True).fill('large.1300/end')
    run(view)
    legacy = exported(view)
    check('missing legacy passages never fabricate a path or prove disconnection', legacy['walk']['status'] == 'incomplete' and legacy['walk']['pathLength'] is None)
    page.screenshot(path=str(output / 'discipline-connectivity.png'))
    close_current()
    page.locator('#documents .name').filter(has_text=re.compile('^' + re.escape(original_name) + '$')).click()
    expect(page.locator('#studio')).to_have_attribute('data-document', original_id)
    idle()
    action('palette-reset')
    page.get_by_role('tab', name='Home', exact=True).click()
    check('discipline fixture restores original active drawing without changing its source')
