"""Directed discipline structure against the real shared native/WASM application."""
import json
import re
import ezdxf
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_domain_structure(page, output, state, command, wait_state, idle, check):
    original_id = page.locator('#studio').get_attribute('data-document')
    original_name = page.locator('#documents .document-tab.active .name').inner_text()

    def action(name):
        return page.evaluate("name=>shadowStudioAgentUi.execute('ui.action',{name})", name)

    def edit(path, body):
        result = page.evaluate("p=>shadowStudioAgentUi.execute('cad.edit',p)", {'path': path, 'body': body})
        idle()
        return result

    def run(view):
        view.get_by_role('button', name='Run connectivity analysis', exact=True).click()
        expect(view.get_by_role('button', name='Run connectivity analysis', exact=True)).to_be_enabled()
        expect(view).to_contain_text('Directed structure')

    def exported(view, label, name):
        with page.expect_download() as pending:
            view.get_by_role('button', name=label, exact=True).click()
        target = output / name
        pending.value.save_as(str(target))
        return target.read_text(encoding='utf-8')

    def close_current():
        current = page.locator('#studio').get_attribute('data-document')
        page.locator('#documents .document-tab.active .close').click()
        wait_for_ui(page, "id=>document.querySelector('#studio').dataset.document!==id||document.querySelector('#dialog')?.open", arg=current)
        if page.locator('#dialog').evaluate('dialog=>dialog.open'):
            page.locator('#dialog-submit').click()
        expect(page.locator('#studio')).not_to_have_attribute('data-document', current)
        idle()

    def fixture(path, legacy=False):
        doc = ezdxf.new('R2010')
        doc.appids.new('SHADOW_CAD')
        records = [('triangle', 4, [('a', 'b', 'forward'), ('b', 'c', 'forward'), ('c', 'a', 'forward'), ('c', 'd', 'forward')]),
                   ('parallel', 2, [('a', 'b', 'both'), ('a', 'b', 'forward')]),
                   ('bidirectional', 2, [('a', 'b', 'both')])]
        if legacy:
            records.append(('legacy', 2, None))
        for index, (key, count, passages) in enumerate(records):
            line = doc.modelspace().add_line((index * 20, 0), (index * 20 + 10, 0))
            ports = [chr(ord('a') + i) for i in range(count)]
            record = {'module': 'pid', 'class': 'structure-fixture', 'key': key, 'tag': key.upper(), 'schemaVersion': 1,
                      'properties': {'openPorts': ','.join(ports)}, 'references': [],
                      'ports': [{'id': port, 'kind': 'process', 'position': {'x': index * 20 + i, 'y': 0, 'z': 0},
                                 'direction': {'x': 0, 'y': 0, 'z': 0}} for i, port in enumerate(ports)]}
            if passages is not None:
                record['passages'] = [{'fromPort': a, 'toPort': b, 'direction': direction} for a, b, direction in passages]
            payload = json.dumps(record, separators=(',', ':'))
            line.set_xdata('SHADOW_CAD', [(1000, 'netdxf.domain.v1')] + [(1000, payload[i:i + 200]) for i in range(0, len(payload), 200)])
        doc.saveas(path)

    if page.locator('#documents .document-tab').count() >= 8:
        saved = next((tab for tab in page.locator('#documents .document-tab:not(.active)').all()
                      if not tab.locator('.name').inner_text().startswith('● ')), None)
        assert saved is not None, 'Private structure verification needs one saved document slot.'
        saved.locator('.close').click()
        expect(page.locator('#documents .document-tab')).to_have_count(7)
        idle()
        page.locator('#documents .name').filter(has_text=re.compile('^' + re.escape(original_name) + '$')).click()
        expect(page.locator('#studio')).to_have_attribute('data-document', original_id)

    path = output / 'discipline-structure.dxf'
    fixture(path)
    page.locator('#open-file').set_input_files(str(path))
    wait_state(lambda data: data['state']['occurrences'] == 3)
    idle()
    edit('modules/toggle', {'module': 'pid', 'enabled': True})
    before = state()
    action('module-open:pid:structure')
    view = page.locator('[data-palette="Modules"] .palette-content')
    expect(view.get_by_label('Connectivity operation', exact=True)).to_have_value('structure')
    run(view)
    result = json.loads(exported(view, 'Export connectivity JSON', 'discipline-structure.json'))
    check('directed structure identifies imported SCCs, condensation and weak cut links',
          result['structure']['scopeComplete'] and result['structure']['loopSearchComplete'] and not result['partial'] and
          len(result['strongComponents']) == 4 and len(result['condensation']) == 1 and len(result['criticalConnections']) == 2 and
          sum(port['articulationPoint'] for port in result['portStructures']) == 1)
    check('parallel passages form a real two-connection loop while one bidirectional link does not',
          sorted(loop['stepCount'] for loop in result['loops']) == [2, 3] and
          len({step['connection'] for step in result['loopSteps']}) == 5)
    check('structure analysis does not edit source, selection or native undo history',
          state()['state']['revision'] == before['state']['revision'] and
          state()['state']['undoCount'] == before['state']['undoCount'] and state()['selectedEntities'] == before['selectedEntities'])
    view.get_by_role('button', name='Show condensation', exact=True).click()
    expect(view.get_by_role('button', name='Show condensation', exact=True)).to_have_attribute('aria-pressed', 'true')
    check('condensation diagram decodes under existing CSP',
          view.locator('.domain-graph-image').evaluate('async image=>{await image.decode();return image.complete&&image.naturalWidth>0;}'))
    view.locator('details').filter(has=page.locator('summary', has_text='Structure exports')).locator('summary').click()
    svg = exported(view, 'Export condensation SVG', 'discipline-structure-condensation.svg')
    csv = exported(view, 'Export structural ports CSV', 'discipline-structure-ports.csv')
    proof_csv = exported(view, 'Export loop proof CSV', 'discipline-structure-proof.csv')
    check('structure exports retain coverage, membership, articulation and exact loop connection identities',
          'Scope complete' in svg and 'eight-column' in svg and 'articulationPoint' in csv and 'strongComponent' in csv and
          all(step['connection'] in proof_csv for step in result['loopSteps']))
    triangle = next(loop for loop in result['loops'] if loop['stepCount'] == 3)
    view.get_by_role('button', name=f"Inspect loop {triangle['id']}", exact=True).click()
    expect(view.locator('.domain-inspector')).to_contain_text('Closed directed proof: 3 distinct connections')
    highlighted = exported(view, 'Export connectivity SVG', 'discipline-structure-loop.svg')
    check('loop inspection highlights exact declared proof and never substitutes geometry', '#dc2626' in highlighted and 'not geometry or simulation' in highlighted)
    # Useful visual artifact: the bounded proof and source inspector in a wide floating palette.
    palette = page.locator('[data-palette="Modules"]')
    palette.get_by_label('Modules palette layout', exact=True).click()
    palette.get_by_role('button', name='Float palette: Modules', exact=True).click()
    workspace = page.locator('#workspace').bounding_box()
    grip = palette.get_by_role('button', name='Move Modules palette', exact=True).bounding_box()
    page.keyboard.down('Control')
    page.mouse.move(grip['x'] + grip['width'] / 2, grip['y'] + grip['height'] / 2)
    page.mouse.down()
    page.mouse.move(workspace['x'] + 60, workspace['y'] + 25, steps=8)
    page.mouse.up()
    page.keyboard.up('Control')
    bounds = palette.bounding_box()
    resize = palette.get_by_role('separator', name='Resize Modules palette', exact=True).bounding_box()
    page.mouse.move(resize['x'] + resize['width'] / 2, resize['y'] + resize['height'] / 2)
    page.mouse.down()
    page.mouse.move(bounds['x'] + 940, min(workspace['y'] + workspace['height'] - 8, bounds['y'] + 680), steps=8)
    page.mouse.up()
    view.locator('.domain-diagram').evaluate("node=>node.scrollIntoView({block:'start'})")
    page.screenshot(path=str(output / 'discipline-structure-proof.png'))
    view.get_by_role('button', name=f"Select loop {triangle['id']} sources", exact=True).click()
    triangle_source = next(node['entity'] for node in result['nodes'] if node['key'] == 'triangle')
    triangle_source = triangle_source['value'] if isinstance(triangle_source, dict) else triangle_source
    wait_state(lambda data: data['selectedEntities'] == [triangle_source])
    check('loop source selection explicitly selects one native root and preserves history',
          state()['state']['undoCount'] == before['state']['undoCount'])
    idle()
    view.get_by_label('Connectivity operation', exact=True).select_option('structure')
    bounds_detail = view.locator('details').filter(has=page.locator('summary', has_text='Scan and search bounds'))
    if not bounds_detail.evaluate('node=>node.open'):
        bounds_detail.locator('summary').click()
    view.get_by_label('Loop search work limit', exact=True).fill('1')
    run(view)
    limited = json.loads(exported(view, 'Export connectivity JSON', 'discipline-structure-limited.json'))
    check('search work exhaustion remains explicit instead of claiming absent loops',
          limited['structure']['searchLimitReached'] and not limited['structure']['loopSearchComplete'] and
          any(group['loopStatus'] == 'incomplete' for group in limited['strongComponents']))
    view.get_by_label('Loop search work limit', exact=True).fill('100000')
    close_current()
    legacy_path = output / 'discipline-structure-legacy.dxf'
    fixture(legacy_path, legacy=True)
    page.locator('#open-file').set_input_files(str(legacy_path))
    wait_state(lambda data: data['state']['occurrences'] == 4)
    idle()
    edit('modules/toggle', {'module': 'pid', 'enabled': True})
    action('module-open:pid:structure')
    run(view)
    legacy = json.loads(exported(view, 'Export connectivity JSON', 'discipline-structure-legacy.json'))
    check('legacy missing passages preserve proven loops but prevent complete-drawing structure claims',
          not legacy['structure']['scopeComplete'] and len(legacy['loops']) == 2 and not legacy['declarationsComplete'])
    close_current()
    page.locator('#documents .name').filter(has_text=re.compile('^' + re.escape(original_name) + '$')).click()
    expect(page.locator('#studio')).to_have_attribute('data-document', original_id)
    idle()
    action('palette-reset')
    page.get_by_role('tab', name='Home', exact=True).click()
    check('structure helper restores the original active drawing and palette layout')
