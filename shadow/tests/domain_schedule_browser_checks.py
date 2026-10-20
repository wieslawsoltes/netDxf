"""Discipline schedules against shared production UI, native data and independent DXF."""
import json
import re
import ezdxf
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_domain_schedule(page, output, state, command, wait_state, idle, check):
    original = page.locator('#studio').get_attribute('data-document')
    original_name = page.locator('#documents .document-tab.active .name').inner_text()
    source = output / 'discipline-schedule.dxf'
    doc = ezdxf.new('R2010'); doc.header['$INSUNITS'] = 4
    doc.appids.new('SHADOW_CAD'); doc.layers.new('Hidden', dxfattribs={'color': -7})
    for i in range(121):
        line = doc.modelspace().add_line((i, 0, 0), (i, 3, 4), dxfattribs={'layer': 'Hidden' if i == 120 else '0'})
        record = {'module': 'pid', 'class': 'pipe', 'key': f'pipe.{i}', 'tag': f'P-{i:03}', 'schemaVersion': 1,
                  'properties': {'service': 'Water' if i < 100 else 'Steam', 'route': f'R-{i // 10}', 'nominalSize': '50', 'spec': 'A'},
                  'ports': [], 'references': [], 'passages': []}
        payload = json.dumps(record, separators=(',', ':'))
        line.set_xdata('SHADOW_CAD', [(1000, 'netdxf.domain.v1')] + [(1000, payload[n:n+200]) for n in range(0, len(payload), 200)])
    doc.saveas(source)
    page.locator('#open-file').set_input_files(str(source))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', original)
    idle()
    page.evaluate("shadowStudioAgentUi.execute('cad.edit',{path:'modules/toggle',body:{module:'pid',enabled:true}})")
    idle()
    page.evaluate("shadowStudioAgentUi.execute('ui.action',{name:'module-open:pid:data'})")
    view = page.locator('[data-palette="Modules"] .palette-content')
    baseline = state()['state']

    def refresh():
        view.get_by_role('button', name='Refresh data', exact=True).click()
        expect(view.get_by_role('button', name='Refresh data', exact=True)).to_be_enabled()
        expect(view).to_contain_text('matching objects')

    def exported(kind, filename):
        with page.expect_download() as pending:
            view.get_by_role('button', name=f'Export schedule {kind}', exact=True).click()
        target = output / filename; pending.value.save_as(str(target)); return target.read_text(encoding='utf-8')

    view.get_by_label('Schedule group field', exact=True).fill('service')
    refresh()
    expect(view).to_contain_text('/121 matching objects')
    view.get_by_role('button', name='Load remaining schedule', exact=True).click()
    expect(view).to_contain_text('121/121 matching objects')
    all_rows = json.loads(exported('JSON', 'discipline-schedule.json'))
    groups = {g['value']: g for g in all_rows['groups']}
    check('schedule includes hidden layout roots, groups by metadata and measures 3D source lengths in drawing units',
          all_rows['complete'] and not all_rows['partial'] and all_rows['drawingUnits'] == 'Millimeters' and
          groups['Water']['objects'] == 100 and groups['Water']['segmentLength'] == 500 and
          groups['Steam']['objects'] == 21 and groups['Steam']['routes'] == 3 and groups['Steam']['segmentLength'] == 105)
    check('schedule reading does not edit native geometry or undo history', state()['state']['revision'] == baseline['revision'] and state()['state']['undoCount'] == baseline['undoCount'])
    groups_detail = view.locator('details').filter(has=page.locator('summary', has_text='Groups by service'))
    groups_detail.locator('summary').click()
    view.get_by_role('button', name='Steam · 21 objects', exact=True).click()
    expect(view).to_contain_text('21/21 matching objects')
    subset = json.loads(exported('JSON', 'discipline-schedule-steam.json'))
    check('group drill-down sends a new scoped query and retains hidden metadata rows', len(subset['rows']) == 21 and subset['options']['groupValue'] == 'Steam')
    view.get_by_role('button', name='Inspect schedule object P-120', exact=True).click()
    expect(view.get_by_role('button', name='Select schedule object', exact=True)).to_be_disabled()
    check('hidden source rows remain inspectable without implicit layer changes')
    view.get_by_role('button', name='Inspect schedule object P-100', exact=True).click()
    view.get_by_role('button', name='Select schedule object', exact=True).click()
    entity = next(row['entity'] for row in subset['rows'] if row['tag'] == 'P-100')
    if isinstance(entity, dict): entity = entity['value']
    wait_state(lambda data: data['selectedEntities'] == [entity]); idle()
    # Selection may rebuild the palette; inspect its retained schedule again.
    view.get_by_role('button', name='Inspect schedule object P-100', exact=True).click()
    before = state()['state']
    view.get_by_role('button', name='Edit schedule object data', exact=True).click()
    expect(page.locator('#dialog')).to_be_visible()
    expect(page.locator('#dialog').get_by_label('Service', exact=True)).to_have_value('Steam')
    check('optional discipline update fields remain optional in native HTML validation',
          not page.locator('#dialog').get_by_label('PCE function', exact=True).evaluate('input=>input.required') and
          not page.locator('#dialog').get_by_label('PCE loop', exact=True).evaluate('input=>input.required'))
    page.locator('#dialog').get_by_label('Service', exact=True).fill('Condensate')
    page.locator('#dialog-submit').click()
    wait_state(lambda data: data['state']['revision'] > before['revision']); idle()
    check('editing one schedule row is prefilled and applies one undoable native transaction', state()['state']['undoCount'] == before['undoCount'] + 1)
    page.evaluate("shadowStudioAgentUi.execute('ui.action',{name:'module-open:pid:data'})")
    # A changed revision requires a fresh receipt, while the query stays explicit.
    view.get_by_label('Search discipline schedule', exact=True).fill('P-100')
    refresh()
    if view.get_by_role('button', name='Clear group filter: Steam', exact=True).count():
        view.get_by_role('button', name='Clear group filter: Steam', exact=True).click()
    expect(view).to_contain_text('1/1 matching objects')
    updated = json.loads(exported('JSON', 'discipline-schedule-updated.json'))
    check('fresh schedule reflects row data change using the same native source identity', updated['rows'][0]['properties']['service'] == 'Condensate' and updated['rows'][0]['entity'] == next(row['entity'] for row in subset['rows'] if row['tag'] == 'P-100'))
    csv = exported('rows CSV', 'discipline-schedule.csv')
    check('schedule CSV exports query, coverage, units and complete retained metadata', '"pagesComplete"' in csv and 'Condensate' in csv and 'Millimeters' in csv)
    # Capture a readable schedule rather than the bottom of the narrow shared dock.
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
    page.mouse.move(bounds['x'] + 920, min(workspace['y'] + workspace['height'] - 8, bounds['y'] + 650), steps=8)
    page.mouse.up()
    view.locator('.schedule-scroll').evaluate("node=>node.scrollIntoView({block:'center'})")
    page.screenshot(path=str(output / 'discipline-schedule-ui.png'))
    with page.expect_download() as pending:
        page.evaluate("shadowStudioAgentUi.execute('ui.action',{name:'save'})")
    saved = output / 'discipline-schedule-updated.dxf'; pending.value.save_as(str(saved)); idle()
    imported = ezdxf.readfile(saved)
    rows = [json.loads(''.join(str(tag.value) for tag in e.get_xdata('SHADOW_CAD')[1:])) for e in imported.modelspace()]
    check('independent DXF reader sees exactly one updated discipline row', sum(r['properties']['service'] == 'Condensate' for r in rows) == 1 and len(rows) == 121)
    current = page.locator('#studio').get_attribute('data-document')
    page.locator('#documents .document-tab.active .close').click()
    wait_for_ui(page, "id=>document.querySelector('#studio').dataset.document!==id||document.querySelector('#dialog')?.open", arg=current)
    if page.locator('#dialog').evaluate('node=>node.open'): page.locator('#dialog-submit').click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', current)
    page.locator('#documents .name').filter(has_text=re.compile('^' + re.escape(original_name) + '$')).click()
    expect(page.locator('#studio')).to_have_attribute('data-document', original); idle()
    page.evaluate("shadowStudioAgentUi.execute('ui.action',{name:'palette-reset'})")
    page.get_by_role('tab', name='Home', exact=True).click()
