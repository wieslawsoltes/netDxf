"""Directed analysis uses the actual native/WASM source snapshot, never a mocked graph."""
import json
import re
from playwright.sync_api import expect


def verify_reference_analysis(page, output, state, command, wait_state, idle, check):
    before = state()
    source = next(item for item in before['items'] if item['type'].split('.')[-1] == 'Line')
    layer = next(item for item in before['layers'] if item['name'] == source['layer'])
    layer_id = layer['id'].get('value') if isinstance(layer['id'], dict) else layer['id']
    baseline = before['state']
    original_selection = before['selectedEntities']
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ui-workspaces'})")
    page.get_by_role('combobox', name='UI workspace', exact=True).select_option(label='Dependency analysis (preset)')
    page.get_by_role('button', name='Apply layout', exact=True).click()
    page.locator('#dialog-submit').click()
    expect(page.get_by_role('tab', name='Show References in palette group', exact=True)).to_have_attribute('aria-selected', 'true')
    check('Dependency analysis workspace places a wide reference group beside source properties',
          page.locator('.pd-dock-left').bounding_box()['width'] >= 500 and
          page.locator('.pd-dock-right [data-palette="Properties"]').count() == 1)
    export_count = 0

    def mode(name):
        page.evaluate("name => shadowStudioAgentUi.execute('ui.action', {name:'reference-query:'+name})", name)
        panel = page.locator('[data-palette="References"] .palette-content')
        expect(panel).to_be_visible()
        expect(panel.get_by_label('Reference analysis', exact=True)).to_have_value(name)
        return panel

    def run(panel, label):
        panel.get_by_role('button', name=label, exact=True).click()
        expect(panel.get_by_role('button', name=label, exact=True)).to_be_enabled()
        expect(panel.locator('.reference-stats')).to_contain_text('indexed objects')

    def exported(panel):
        nonlocal export_count
        with page.expect_download() as pending:
            panel.get_by_role('button', name='Export reference JSON', exact=True).click()
        text = open(pending.value.path(), encoding='utf-8').read()
        export_count += 1
        (output / f'reference-analysis-{export_count:02}.json').write_text(text, encoding='utf-8')
        return json.loads(text)['graph']

    def inspect(panel, object_id):
        for _ in range(40):
            button = panel.locator('.reference-list > button').filter(has_text=re.compile('^' + re.escape(object_id) + r' · '))
            if button.count():
                button.click()
                return
            next_page = panel.get_by_role('button', name='Next objects', exact=True)
            if next_page.is_disabled():
                break
            next_page.click()
        raise AssertionError('Missing loaded reference object: ' + object_id)

    panel = mode('path')
    panel.get_by_label('Reference direction', exact=True).select_option('outgoing')
    panel.get_by_label('Reference path source', exact=True).fill(source['id'])
    panel.get_by_label('Reference path target', exact=True).fill(layer_id)
    panel.get_by_label('Reference maximum depth', exact=True).fill('0')
    run(panel, 'Find dependency path')
    expect(panel).to_contain_text('does not prove the target is unreachable')
    limited = exported(panel)
    check('dependency path depth exhaustion is incomplete, not unreachable',
          limited['status'] == 'incomplete' and limited['depthLimitReached'])
    panel.get_by_label('Reference maximum depth', exact=True).fill('16')
    run(panel, 'Find dependency path')
    path = exported(panel)
    check('directed path exposes the actual LINE Layer reference in one minimum hop',
          path['status'] == 'found' and len(path['edges']) == 1 and
          path['edges'][0] == {'source': source['id'], 'path': '/Layer', 'target': layer_id})
    check('path visualization preserves source selection and history',
          state()['selectedEntities'] == original_selection and state()['state']['revision'] == baseline['revision'] and
          state()['state']['undoCount'] == baseline['undoCount'])
    inspect(panel, layer_id)
    expect(panel.get_by_role('button', name='Select source object', exact=True)).to_be_disabled()
    with page.expect_download() as pending:
        panel.get_by_role('button', name='Export reference SVG', exact=True).click()
    path_svg = open(pending.value.path(), encoding='utf-8').read()
    check('dependency SVG retains exact proof links and proved-path status',
          '/Layer' in path_svg and 'Dependency path status: found' in path_svg and '#ffd166' in path_svg)
    panel.get_by_label('Reference path source', exact=True).fill(layer_id)
    panel.get_by_label('Reference path target', exact=True).fill(source['id'])
    panel.get_by_label('Reference direction', exact=True).select_option('incoming')
    run(panel, 'Find dependency path')
    reverse = exported(panel)
    check('incoming dependency path reverses search order while preserving stored link direction',
          [node['id'] for node in reverse['nodes']] == [layer_id, source['id']] and reverse['edges'] == path['edges'])

    panel = mode('components')
    panel.get_by_label('Reference direction', exact=True).select_option('outgoing')
    panel.get_by_label('Reference root IDs', exact=True).fill(source['id'] + ' ' + layer_id)
    panel.get_by_label('Reference maximum depth', exact=True).fill('0')
    run(panel, 'Analyze cyclic groups')
    expect(panel).to_contain_text('cannot establish global acyclicity')
    groups = exported(panel)
    check('SCC analysis discloses a partial scope without false global acyclicity',
          not groups['explorationComplete'] and groups['depthLimitReached'] and
          sum(group['nodeCount'] for group in groups['results']) == groups['nodeCount'])
    memberships = {node['id']: node['component'] for node in groups['nodes']}
    expected_links = {}
    for edge in groups['edges']:
        pair = (memberships[edge['source']], memberships[edge['target']])
        if pair[0] != pair[1]:
            expected_links[pair] = expected_links.get(pair, 0) + 1
    check('condensation aggregates actual property links between source components',
          {(link['source'], link['target']): link['referenceCount'] for link in groups['links']} == expected_links)
    panel.locator('.reference-analysis-results .reference-components > button').first.click()
    expect(panel.locator('.reference-inspector')).to_contain_text('Strongly connected group')
    panel.get_by_label('Reference diagram view', exact=True).select_option('condensed')
    image = panel.locator('.reference-diagram img')
    check('condensation DAG decodes under the unchanged CSP',
          image.evaluate('async image => { await image.decode(); return image.complete && image.naturalWidth > 0; }'))
    with page.expect_download() as pending:
        panel.get_by_role('button', name='Export reference SVG', exact=True).click()
    dag_svg = open(pending.value.path(), encoding='utf-8').read()
    check('condensation SVG exports group geometry with honest scope qualification',
          'Condensation DAG' in dag_svg and 'component:' in dag_svg and 'partial scope' in dag_svg)
    page.screenshot(path=str(output / 'reference-condensation.png'))

    panel = mode('impact')
    panel.get_by_label('Reference root IDs', exact=True).fill(layer_id)
    panel.get_by_label('Reference maximum depth', exact=True).fill('4')
    panel.get_by_label('Reference node budget', exact=True).fill('1000')
    panel.get_by_label('Reference edge budget', exact=True).fill('4000')
    expect(panel.get_by_label('Reference direction', exact=True)).to_be_disabled()
    run(panel, 'Explore reference impact')
    remaining = panel.get_by_role('button', name='Load remaining references (bounded)', exact=True)
    if remaining.is_enabled():
        remaining.click()
        expect(panel.get_by_role('button', name='Explore reference impact', exact=True)).to_be_enabled()
    impact = exported(panel)
    source_visit = next(node for node in impact['nodes'] if node['id'] == source['id'])
    check('impact analysis exports a source-to-seed predecessor proof from the real snapshot',
          impact['direction'] == 'incoming' and source_visit['depth'] == 1 and
          source_visit['predecessor'] == layer_id and source_visit['via'] == path['edges'][0])
    inspect(panel, source['id'])
    expect(panel.locator('.reference-proof')).to_contain_text('1 reference hops to an impact seed')
    expect(panel.locator('.reference-proof')).to_contain_text('/Layer')
    expect(panel.get_by_role('button', name='Select source object', exact=True)).to_be_enabled()
    panel.get_by_role('button', name='Select source object', exact=True).click()
    wait_state(lambda data: data['selectedEntities'] == [source['id']])
    idle()
    check('impact selection is explicit and never edits geometry or creates undo history',
          state()['state']['revision'] == baseline['revision'] and state()['state']['undoCount'] == baseline['undoCount'])
    page.screenshot(path=str(output / 'reference-impact.png'))
    # A real source edit invalidates all analysis modes, including retained proof metadata.
    page.evaluate("""() => shadowStudioAgentUi.execute('cad.edit', {path:'command', body:{command:{
      name:'LINE',points:[{x:220,y:200,z:0},{x:230,y:200,z:0}]}}})""")
    wait_state(lambda data: data['state']['revision'] > baseline['revision'])
    idle()
    expect(panel.get_by_role('button', name='Export reference JSON', exact=True)).to_have_count(0)
    check('revision changes invalidate analysis exports and impact proofs')
    command('U')
    check('directed analysis fixture restores the drawing history', state()['state']['undoCount'] == baseline['undoCount'])
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    expect(page.locator('[data-palette="References"]')).to_have_count(0)
    page.get_by_role('tab', name='Home', exact=True).click()
