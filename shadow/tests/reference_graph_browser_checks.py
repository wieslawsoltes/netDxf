"""Source-reference exploration against the actual native or WASM drawing runtime."""
import json
import re
from playwright.sync_api import expect


def verify_reference_graph(page, output, state, command, wait_state, idle, check):
    before = state()
    source = next(item for item in before['items'] if item['type'].split('.')[-1] == 'Line')
    page.evaluate("""id => shadowStudioAgentUi.execute('cad.edit', {
      path:'select',body:{mode:'entities',entities:[{value:id}]}
    })""", source['id'])
    wait_state(lambda data: data['selectedEntities'] == [source['id']])
    idle()
    baseline = state()['state']
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'reference-graph'})")
    panel = page.locator('[data-palette="References"] .palette-content')
    expect(panel).to_be_visible()
    panel.get_by_role('button', name='Use drawing selection', exact=True).click()
    expect(panel.locator('.reference-stats')).to_contain_text('indexed objects')
    expect(panel.get_by_role('button', name='Explore references', exact=True)).to_be_enabled()

    def exported():
        with page.expect_download() as pending:
            panel.get_by_role('button', name='Export reference JSON', exact=True).click()
        return json.loads(open(pending.value.path(), encoding='utf-8').read())

    def inspect(node_id):
        for _ in range(40):
            item = panel.locator('.reference-list > button').filter(
                has_text=re.compile('^' + re.escape(node_id) + r' · '))
            if item.count():
                item.click()
                return
            next_page = panel.get_by_role('button', name='Next objects', exact=True)
            if next_page.is_disabled():
                break
            next_page.click()
        raise AssertionError('Reference node not found in loaded object pages: ' + node_id)

    outgoing = exported()['graph']
    layer_link = next(edge for edge in outgoing['edges']
                      if edge['source'] == source['id'] and edge['path'] == '/Layer')
    check('reference graph preserves exact directed LINE Layer property link',
          outgoing['direction'] == 'outgoing' and outgoing['roots'] == [source['id']] and
          any(node['id'] == layer_link['target'] for node in outgoing['nodes']))
    check('reference exploration preserves drawing geometry, selection and undo history',
          state()['selectedEntities'] == [source['id']] and
          state()['state']['revision'] == baseline['revision'] and
          state()['state']['undoCount'] == baseline['undoCount'])
    image = panel.locator('.reference-diagram img')
    check('reference diagram decodes under the existing CSP',
          image.evaluate('async image => { await image.decode(); return image.complete && image.naturalWidth > 0; }'))
    inspect(layer_link['target'])
    expect(panel.get_by_role('button', name='Select source object', exact=True)).to_be_disabled()
    expect(panel).to_contain_text('not in the discovered active scene roots')
    check('resource nodes cannot masquerade as selectable drawing roots')
    panel.get_by_role('button', name='Explore users', exact=True).click()
    expect(panel.get_by_label('Reference direction', exact=True)).to_have_value('incoming')
    expect(panel.get_by_role('button', name='Explore references', exact=True)).to_be_enabled()
    expect(panel.locator('.reference-stats')).to_contain_text('indexed objects')
    remaining = panel.get_by_role('button', name='Load remaining references (bounded)', exact=True)
    if remaining.is_enabled():
        remaining.click()
        expect(panel.get_by_role('button', name='Explore references', exact=True)).to_be_enabled()
    incoming = exported()['graph']
    check('incoming users retain source-to-target direction and the selected Layer root',
          incoming['roots'] == [layer_link['target']] and incoming['direction'] == 'incoming' and
          any(edge['source'] == source['id'] and edge['target'] == layer_link['target'] and edge['path'] == '/Layer'
              for edge in incoming['edges']))
    inspect(source['id'])
    expect(panel.get_by_role('button', name='Select source object', exact=True)).to_be_enabled()
    panel.get_by_role('button', name='Select source object', exact=True).click()
    wait_state(lambda data: data['selectedEntities'] == [source['id']])
    idle()
    with page.expect_download() as pending:
        panel.get_by_role('button', name='Export reference SVG', exact=True).click()
    svg = open(pending.value.path(), encoding='utf-8').read()
    check('reference SVG exports directed property paths separately from drawing coordinates',
          'marker-end="url(#arrow)"' in svg and '/Layer' in svg and 'Source object reference graph' in svg)
    page.screenshot(path=str(output / 'source-reference-graph.png'))
    after = state()['state']
    check('reference diagrams and source selection do not create drawing edits',
          after['revision'] == baseline['revision'] and after['undoCount'] == baseline['undoCount'])
    # A real edit must invalidate every retained reference receipt. Undo restores
    # geometry but advances revision, so the old source graph must stay invalid.
    page.evaluate("""() => shadowStudioAgentUi.execute('cad.edit', {
      path:'command',body:{command:{name:'LINE',points:[{x:200,y:200,z:0},{x:210,y:200,z:0}]}}
    })""")
    wait_state(lambda data: data['state']['revision'] > baseline['revision'])
    idle()
    expect(panel.get_by_role('button', name='Export reference JSON', exact=True)).to_have_count(0)
    check('drawing revision invalidates the retained reference graph and export actions')
    command('U')
    check('reference fixture restores native drawing history',
          state()['state']['undoCount'] == baseline['undoCount'])
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    expect(page.locator('[data-palette="References"]')).to_have_count(0)
    page.get_by_role('tab', name='Home', exact=True).click()
