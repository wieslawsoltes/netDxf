"""Exercise shared graph analysis palettes against actual native/WASM drawing state."""
import json
from playwright.sync_api import expect
from reference_graph_browser_checks import verify_reference_graph
from workspace_layout_browser_checks import verify_workspace_layout
from palette_groups_browser_checks import verify_palette_groups
from command_access_browser_checks import verify_command_access
from reference_analysis_browser_checks import verify_reference_analysis


def verify_graph_analysis(page, output, state, command, wait_state, idle, check):
    previous = page.locator('#studio').get_attribute('data-document')
    # The preceding modify fixture saved its DXF. Close it before opening this
    # independent drawing so the integration run respects the eight-document bound.
    page.locator('#documents .document-tab.active .close').click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('[data-action="new"]').first.click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    command('L 0,0 10,0 10,10 0,10 C')
    for text in ('L 10,10 20,10', 'L 30,0 40,0'):
        revision = state()['state']['revision']
        command(text, False)
        page.locator('#command-input').press('Enter')
        wait_state(lambda data: data['state']['revision'] > revision)
        idle()
    command('Z E', False)
    baseline = state()['state']
    command('GRAPH', False)
    content = page.locator('[data-palette="Graph"] .palette-content')
    operation = page.get_by_label('Graph analysis operation', exact=True)
    page.get_by_role('button', name='Load topology', exact=True).click()
    expect(content).to_contain_text('7 / 7 nodes')
    expect(content).to_contain_text('6 / 6 edges')
    expect(content.locator('.graph-thumbnail')).to_be_visible()
    check('graph overview decodes under the existing CSP', content.locator('.graph-thumbnail').evaluate('async image => { await image.decode(); return image.complete && image.naturalWidth > 0; }'))
    page.get_by_label('Graph coloring', exact=True).select_option('features')
    page.get_by_label('Graph labels', exact=True).select_option('all')
    page.get_by_role('button', name='Fit graph', exact=True).click()
    page.screenshot(path=str(output / 'graph-topology.png'))
    with page.expect_download() as downloaded:
        page.get_by_role('button', name='Export graph JSON', exact=True).click()
    topology = json.loads(open(downloaded.value.path(), encoding='utf-8').read())['projection']
    check('complete visual topology retains independent components and source identities',
          topology['componentCount'] == 2 and len(topology['nodes']) == 7 and
          all(edge['rootEntity'] and edge['occurrence'] for edge in topology['edges']))
    with page.expect_download() as downloaded:
        page.get_by_role('button', name='Export graph SVG', exact=True).click()
    svg = open(downloaded.value.path(), encoding='utf-8').read()
    check('visual topology exports standalone geometry and feature colors',
          svg.count('<path ') == 6 and svg.count('<circle ') == 7 and '#d99cff' in svg)
    operation.select_option('path')
    def pick_node(label, node):
        page.get_by_role('button', name=label, exact=True).click()
        camera = page.evaluate('shadowStudioAgentUi.snapshot().camera')
        box = page.locator('#drawing').bounding_box()
        page.mouse.click(box['x'] + box['width']/2 + (node['x']-camera['x'])*camera['scale'],
                         box['y'] + box['height']/2 - (node['y']-camera['y'])*camera['scale'])
    nodes = [node for node in topology['nodes'] if node['component'] == 0]
    pick_node('Pick start node', nodes[0])
    expect(page.get_by_label('Start node', exact=True)).to_have_value(str(nodes[0]['id']))
    pick_node('Pick end node', nodes[-1])
    expect(page.get_by_label('End node', exact=True)).to_have_value(str(nodes[-1]['id']))
    page.get_by_role('button', name='Run analysis', exact=True).click()
    expect(content).to_contain_text('Path length')
    page.get_by_role('button', name='Select result sources', exact=True).click()
    wait_state(lambda data: len(data['selectedEntities']) > 0)
    idle()
    expect(content).to_contain_text('7 / 7 nodes')
    check('visual endpoint picking computes a path and selects its source objects without losing topology',
          state()['state']['revision'] == baseline['revision'] and state()['state']['undoCount'] == baseline['undoCount'])
    page.get_by_role('button', name='Inspect in drawing', exact=True).click()
    page.locator('#viewport').press('Escape')
    check('Escape cancels graph picking without clearing drawing selection', len(state()['selectedEntities']) > 0)
    operation.select_option('cycles')
    page.get_by_role('button', name='Run analysis', exact=True).click()
    expect(content.get_by_text('Cycle 0', exact=True)).to_be_visible()
    expect(content).to_contain_text('4 edges · closing edge')
    check('graph cycle basis is available in the shared analysis palette')
    page.get_by_role('button', name='Inspect cycle', exact=True).click()
    page.get_by_role('button', name='Run analysis', exact=True).click()
    expect(content).to_contain_text('Cycle 0 · length 40 · 4 edges · closed path')
    page.get_by_role('button', name='Highlight this result', exact=True).click()
    check('cycle drill-down displays actual closed geometry and sampled length')
    operation.select_option('spanning_forest')
    page.get_by_role('button', name='Run analysis', exact=True).click()
    expect(content).to_contain_text('Minimum forest length 50 · 2 components · 5 edges')
    page.get_by_role('button', name='Highlight this result', exact=True).click()
    after = state()['state']
    check('minimum spanning forest preserves disconnected components without editing source',
          after['revision'] == baseline['revision'] and after['undoCount'] == baseline['undoCount'])
    content.get_by_text('Topology tolerances and budgets', exact=True).click()
    page.get_by_label('Endpoint tolerance', exact=True).fill('.02')
    expect(content).to_contain_text('Inputs changed. Run analysis to update the results.')
    expect(content.get_by_text('Minimum forest length 50 · 2 components · 5 edges', exact=True)).to_have_count(0)
    check('editing graph tolerances invalidates the previous result')
    page.get_by_role('button', name='Run analysis', exact=True).click()
    expect(content).to_contain_text('Minimum forest length 50 · 2 components · 5 edges')
    page.screenshot(path=str(output / 'graph-analysis.png'))
    verify_palette_ribbon(page, output, check)
    verify_workspace_layout(page, output, state, command, wait_state, idle, check)
    verify_palette_groups(page, output, state, command, wait_state, idle, check)
    verify_large_graph_projection(page, output, state, command, wait_state, idle, check)
    verify_reference_graph(page, output, state, command, wait_state, idle, check)
    verify_reference_analysis(page, output, state, command, wait_state, idle, check)
    verify_command_access(page, output, state, command, wait_state, idle, check)


def verify_large_graph_projection(page, output, state, command, wait_state, idle, check):
    """Use the real typed authoring API, then prove adaptive pages retain every source edge."""
    before = state()['state']
    created = page.evaluate('''() => shadowStudioAgentUi.execute('cad.edit', {
      path:'command', body:{command:{name:'PLINE',points:Array.from({length:1301},(_,x)=>({x,y:100,z:0}))}}
    })''')
    wait_state(lambda data: data['state']['revision'] > before['revision'])
    idle()
    baseline = state()['state']
    check('large graph source is one native undo transaction',
          baseline['undoCount'] == before['undoCount'] + 1 and len(created['selectedEntities']) == 1)
    page.evaluate("shadowStudioAgentUi.execute('palette.show', {name:'Graph'})")
    content = page.locator('[data-palette="Graph"] .palette-content')
    content.get_by_text('Projection filters', exact=True).click()
    content.get_by_label('Projection root entity', exact=True).fill(created['selectedEntities'][0])
    content.get_by_role('button', name='Load topology', exact=True).click()
    expect(content).to_contain_text('Partial topology.')
    expect(content).to_contain_text('/ 1301 nodes')
    expect(content).to_contain_text('/ 1300 edges')
    with page.expect_download() as downloaded:
        content.get_by_role('button', name='Export graph JSON', exact=True).click()
    first = json.loads(open(downloaded.value.path(), encoding='utf-8').read())['projection']
    check('registry response budget returns honest adaptive graph pages',
          first['partial'] and 0 < len(first['nodes']) < 1301 and 0 < len(first['edges']) < 1300)
    content.get_by_role('button', name='Load remaining topology (bounded)', exact=True).click()
    expect(content).to_contain_text('1301 / 1301 nodes', timeout=60000)
    expect(content).to_contain_text('1300 / 1300 edges', timeout=60000)
    expect(content).to_contain_text('Complete filtered topology loaded.')
    expect(content.get_by_role('button', name='Load remaining topology (bounded)', exact=True)).to_be_disabled()
    with page.expect_download() as downloaded:
        content.get_by_role('button', name='Export graph JSON', exact=True).click()
    complete = json.loads(open(downloaded.value.path(), encoding='utf-8').read())['projection']
    check('adaptive graph continuation preserves all 1300 source edges without gaps or duplicates',
          not complete['partial'] and len({node['id'] for node in complete['nodes']}) == 1301 and
          len({edge['id'] for edge in complete['edges']}) == 1300 and
          all(edge['rootEntity'] == created['selectedEntities'][0] for edge in complete['edges']))
    after = state()['state']
    check('large projection paging preserves source revision and undo history',
          after['revision'] == baseline['revision'] and after['undoCount'] == baseline['undoCount'])
    content.get_by_role('button', name='Fit graph', exact=True).click()
    check('large graph overview decodes under CSP', content.locator('.graph-thumbnail').evaluate(
        'async image => { await image.decode(); return image.complete && image.naturalWidth > 0; }'))
    page.screenshot(path=str(output / 'graph-large-projection.png'))
    content.get_by_role('button', name='Clear overlay', exact=True).click()
    command('U')
    check('large graph fixture restores the original drawing in one undo', state()['state']['undoCount'] == before['undoCount'])
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    expect(page.locator('[data-palette="Graph"]')).to_have_count(0)
    page.get_by_role('tab', name='Home', exact=True).click()



def verify_palette_ribbon(page, output, check):
    graph = page.locator('[data-palette="Graph"]')
    properties = page.locator('[data-palette="Properties"]')
    expect(properties).to_be_visible()
    expect(graph).to_be_visible()
    expect(page.locator('#palette-tabs')).to_have_count(0)
    check('Properties and Graph open independently without a crowded tab strip')
    def place(label):
        graph.get_by_label('Graph palette layout', exact=True).click()
        graph.get_by_role('button', name=label + ': Graph', exact=True).click()
    place('Dock left')
    expect(page.locator('.pd-dock-left [data-palette="Graph"]')).to_be_visible()
    expect(page.locator('.pd-dock-right [data-palette="Properties"]')).to_be_visible()
    check('opposite dock columns preserve at least 280 pixels for drawing', page.locator('#viewport').bounding_box()['width'] >= 280)
    place('Float palette')
    expect(graph).to_have_class('pd-window pd-floating')
    original = graph.bounding_box()
    graph.get_by_role('button', name='Move Graph palette', exact=True).focus()
    page.keyboard.press('ArrowLeft')
    moved = graph.bounding_box()
    check('floating palette supports keyboard movement', moved['x'] < original['x'])
    graph.get_by_role('separator', name='Resize Graph palette', exact=True).focus()
    page.keyboard.press('ArrowRight')
    check('floating palette resizes by keyboard', graph.bounding_box()['width'] > moved['width'])
    grip = graph.get_by_role('button', name='Move Graph palette', exact=True).bounding_box()
    before_drag = graph.bounding_box()
    page.mouse.move(grip['x']+grip['width']/2, grip['y']+grip['height']/2)
    page.mouse.down()
    page.mouse.move(grip['x']+grip['width']/2+30, grip['y']+grip['height']/2+20, steps=5)
    page.mouse.up()
    check('floating palette supports pointer dragging without losing content', graph.bounding_box()['x'] > before_drag['x'])
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-lock'})")
    expect(graph.get_by_role('button', name='Move Graph palette', exact=True)).to_be_disabled()
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-lock'})")
    expect(graph.get_by_role('button', name='Move Graph palette', exact=True)).to_be_enabled()
    stored = page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-palettes-v2'))")
    check('palette layout lock and bounded preferences preserve floating geometry', stored['panels']['Graph']['mode'] == 'float')
    graph.get_by_label('Palette shown in Graph', exact=True).focus()
    page.keyboard.press('Control+9')
    expect(page.locator('#command-line')).to_be_hidden()
    page.keyboard.press('Control+9')
    expect(page.locator('#command-line')).to_be_visible()
    check('global drawing shortcuts work with keyboard focus inside a palette')
    page.screenshot(path=str(output / 'graph-floating.png'))
    place('Auto-hide left')
    expect(graph).to_have_count(0)
    page.get_by_role('button', name='Show Graph palette', exact=True).click()
    expect(graph).to_be_visible()
    check('anchored graph overlays drawing without consuming its width', 'pd-anchored' in graph.get_attribute('class'))
    graph.get_by_role('button', name='Load topology', exact=True).click()
    expect(graph).to_contain_text('7 / 7 nodes')
    graph.get_by_label('Graph analysis operation', exact=True).select_option('path')
    graph.get_by_role('button', name='Fit graph', exact=True).click()
    graph.get_by_role('button', name='Pick end node', exact=True).click()
    camera = page.evaluate('shadowStudioAgentUi.snapshot().camera')
    box = page.locator('#drawing').bounding_box()
    page.mouse.click(box['x'] + box['width']/2 + (40-camera['x'])*camera['scale'],
                     box['y'] + box['height']/2 - (0-camera['y'])*camera['scale'])
    expect(graph).to_be_visible()
    expect(graph).to_contain_text('End node:')
    check('anchored graph remains open while picking an endpoint in the drawing')
    graph.get_by_label('Palette shown in Graph', exact=True).press('Escape')
    expect(graph).to_have_count(0)
    page.get_by_role('button', name='Show Graph palette', exact=True).click()
    place('Dock right')
    page.get_by_role('button', name='Close Graph palette', exact=True).click()
    expect(graph).to_have_count(0)
    page.locator('.document-bar [data-action="palette-picker"]').click()
    page.get_by_role('menuitem', name='Graph', exact=True).click()
    expect(graph).to_be_visible()
    check('closed palettes reopen from the compact workspace picker')
    for label, mode in [('Panel buttons','buttons'),('Panel titles','titles'),('Tabs only','tabs'),('Full ribbon','full')]:
        page.get_by_role('button', name='Ribbon display options', exact=True).click()
        page.get_by_role('menuitemradio', name=label, exact=True).click()
        expect(page.locator('#ribbon')).to_have_attribute('data-mode', mode)
        if mode == 'tabs':
            expect(page.locator('#ribbon')).to_be_hidden()
            page.get_by_role('tab', name='Graph', exact=True).click()
            expect(page.locator('#ribbon')).to_be_visible()
            page.get_by_role('tab', name='Graph', exact=True).press('Escape')
            expect(page.locator('#ribbon')).to_be_hidden()
    check('ribbon supports all four display modes and transient tab expansion')
    original_viewport = page.viewport_size
    page.set_viewport_size({'width': 520, 'height': 800})
    page.locator('.document-bar [data-action="palette-picker"]').click()
    page.get_by_role('menuitem', name='Graph', exact=True).click()
    expect(graph).to_have_class('pd-window pd-anchored')
    graph.get_by_role('button', name='Load topology', exact=True).click()
    expect(graph).to_contain_text('7 / 7 nodes')
    graph.get_by_role('button', name='Fit graph', exact=True).click()
    graph.get_by_role('button', name='Pick start node', exact=True).click()
    camera = page.evaluate('shadowStudioAgentUi.snapshot().camera')
    box = page.locator('#drawing').bounding_box()
    # A right-anchored panel covers the right of the drawing; choose the left endpoint.
    page.mouse.click(box['x'] + box['width']/2 + (0-camera['x'])*camera['scale'],
                     box['y'] + box['height']/2 - (0-camera['y'])*camera['scale'])
    expect(graph).to_contain_text('Start node:')
    check('compact anchored workspace supports graph endpoint picking')
    page.screenshot(path=str(output / 'graph-compact.png'))
    page.set_viewport_size(original_viewport)
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    expect(properties).to_be_visible()
    expect(graph).to_have_count(0)
    page.get_by_role('tab', name='Home', exact=True).click()
