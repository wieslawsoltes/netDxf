"""Declared removal scenarios and exact rerouting proofs in the shared native/WASM workspace."""
import json
import re
import ezdxf
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_domain_isolation(page, output, state, command, wait_state, idle, check):
    original_id = page.locator('#studio').get_attribute('data-document')
    original_name = page.locator('#documents .document-tab.active .name').inner_text()

    def action(name):
        result = page.evaluate("name=>shadowStudioAgentUi.execute('ui.action',{name})", name)
        idle()
        return result

    def export(view, label, name):
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
        for index in range(2 if legacy else 1):
            key = 'legacy' if index else 'reroute'
            count = 2 if index else 4
            line = doc.modelspace().add_line((index * 20, 0), (index * 20 + 10, 0))
            record = {'module': 'pid', 'class': 'isolation-fixture', 'key': key, 'tag': key.upper(), 'schemaVersion': 1,
                      'properties': {'openPorts': ','.join('p'+str(i) for i in range(count))}, 'references': [],
                      'ports': [{'id': 'p'+str(i), 'kind': 'process', 'position': {'x': index * 20 + i, 'y': 0, 'z': 0},
                                 'direction': {'x': 0, 'y': 0, 'z': 0}} for i in range(count)]}
            if not index:
                record['passages'] = [{'fromPort': 'p'+str(a), 'toPort': 'p'+str(b), 'direction': 'forward'}
                                      for a, b in [(0, 1), (1, 3), (0, 2), (2, 3)]]
            payload = json.dumps(record, separators=(',', ':'))
            line.set_xdata('SHADOW_CAD', [(1000, 'netdxf.domain.v1')] + [(1000, payload[i:i+200]) for i in range(0, len(payload), 200)])
        doc.saveas(path)

    def open_fixture(legacy=False):
        path = output / ('discipline-isolation-legacy.dxf' if legacy else 'discipline-isolation.dxf')
        fixture(path, legacy)
        page.locator('#open-file').set_input_files(str(path))
        wait_state(lambda data: data['state']['occurrences'] == (2 if legacy else 1))
        idle()
        page.evaluate("()=>shadowStudioAgentUi.execute('cad.edit',{path:'modules/toggle',body:{module:'pid',enabled:true}})")
        idle()
        action('module-open:pid:isolation')
        view = page.locator('[data-palette="Modules"] .palette-content')
        expect(view.get_by_label('Connectivity operation', exact=True)).to_have_value('isolation')
        view.get_by_label('Connectivity direction', exact=True).select_option('forward')
        view.get_by_label('Connectivity source port', exact=True).fill('reroute/p0')
        view.get_by_label('Connectivity target port', exact=True).fill('reroute/p3')
        return view

    def run(view, name):
        view.get_by_role('button', name='Run connectivity analysis', exact=True).click()
        expect(view.get_by_role('button', name='Run connectivity analysis', exact=True)).to_be_enabled()
        expect(view).to_contain_text('Isolation and rerouting comparison')
        return json.loads(export(view, 'Export connectivity JSON', name))

    if page.locator('#documents .document-tab').count() >= 8:
        saved = next((tab for tab in page.locator('#documents .document-tab:not(.active)').all()
                      if not tab.locator('.name').inner_text().startswith('● ')), None)
        assert saved is not None, 'Private isolation verification needs a saved document slot.'
        saved.locator('.close').click()
        expect(page.locator('#documents .document-tab')).to_have_count(7)
        idle()
        page.locator('#documents .name').filter(has_text=re.compile('^'+re.escape(original_name)+'$')).click()
        expect(page.locator('#studio')).to_have_attribute('data-document', original_id)

    view = open_fixture()
    before = state()
    baseline = run(view, 'discipline-isolation-baseline.json')
    removed = next(e['id'] for e in baseline['edges'] if e['source'] == 'reroute/p0' and e['target'] == 'reroute/p1')
    # Port search is a workspace preference retained across drawings and earlier graph fixtures.
    view.get_by_label('Search connectivity ports', exact=True).fill('')
    view.get_by_role('button', name='REROUTE · p0', exact=True).click()
    view.get_by_role('button', name='Exclude connection '+removed, exact=True).click()
    expect(view.get_by_label('Excluded connection IDs', exact=True)).to_have_value(removed)
    result = run(view, 'discipline-isolation.json')
    assessment = {item['id']: item for item in result['assessments']}
    check('declared exclusion proves one lost branch while retaining an alternative target path',
          assessment['reroute/p1']['status'] == 'lost' and assessment['reroute/p3']['status'] == 'reachable' and
          result['isolation']['before']['complete'] and result['isolation']['after']['complete'] and
          result['isolation']['after']['targetPathLength'] == 2)
    check('isolation is read-only with unchanged native history and source selection',
          state()['state']['revision'] == before['state']['revision'] and state()['state']['undoCount'] == before['state']['undoCount'] and
          state()['selectedEntities'] == before['selectedEntities'])
    view.get_by_role('button', name='Inspect scenario target path', exact=True).click()
    expect(view.locator('.domain-inspector')).to_contain_text('reroute/p0 → reroute/p2')
    check('scenario diagram decodes under CSP', view.locator('.domain-graph-image').evaluate('async image=>{await image.decode();return image.complete&&image.naturalWidth>0;}'))
    svg = export(view, 'Export connectivity SVG', 'discipline-isolation-proof.svg')
    view.locator('details').filter(has=page.locator('summary', has_text='Isolation exports')).locator('summary').click()
    csv = export(view, 'Export reachability CSV', 'discipline-isolation.csv')
    proofs = export(view, 'Export scenario predecessors CSV', 'discipline-isolation-predecessors.csv')
    assumptions = export(view, 'Export exclusion assumptions CSV', 'discipline-isolation-assumptions.csv')
    check('isolation exports contain scope, budgets, exact exclusion IDs and retained path proof',
          'not valve states' in svg and '#dc2626' in svg and '#ffe3e3' in svg and 'maximumExaminedEdges' in csv and
          '"lost"' in csv and removed in assumptions and all(row['connection'] in proofs for row in result['afterVisits'] if row['connection']))
    # Preserve readable intrinsic graph labels and show the comparison in a useful visual artifact.
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
    page.screenshot(path=str(output / 'discipline-isolation-comparison.png'))
    view.get_by_role('button', name='Select isolation proof sources', exact=True).click()
    source = result['nodes'][0]['entity']
    source = source['value'] if isinstance(source, dict) else source
    wait_state(lambda data: data['selectedEntities'] == [source])
    idle()
    check('only explicit proof selection selects the original native source root', state()['state']['undoCount'] == before['state']['undoCount'])
    view.get_by_label('Isolation edge examination limit', exact=True).fill('1')
    limited = run(view, 'discipline-isolation-limited.json')
    check('exhausted traversal budget reports unknown rather than false loss',
          limited['isolation']['after']['edgeLimitReached'] and limited['isolation']['after']['targetStatus'] == 'unknown' and
          not any(item['status'] == 'lost' for item in limited['assessments']))
    view.get_by_label('Isolation edge examination limit', exact=True).fill('200000')
    close_current()
    view = open_fixture(legacy=True)
    view.get_by_label('Excluded connection IDs', exact=True).fill(removed)
    legacy = run(view, 'discipline-isolation-legacy.json')
    check('missing imported passages preserve positive proofs but cannot prove isolated loss',
          not legacy['isolation']['scopeComplete'] and
          next(a['status'] for a in legacy['assessments'] if a['id'] == 'reroute/p1') == 'unknown' and
          legacy['isolation']['after']['targetStatus'] == 'reachable')
    close_current()
    page.locator('#documents .name').filter(has_text=re.compile('^'+re.escape(original_name)+'$')).click()
    expect(page.locator('#studio')).to_have_attribute('data-document', original_id)
    idle()
    action('palette-reset')
    page.get_by_role('tab', name='Home', exact=True).click()
    check('isolation helper restores the original drawing and palette layout')
