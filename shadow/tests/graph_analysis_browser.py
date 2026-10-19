"""Exercise shared graph analysis palettes against actual native/WASM drawing state."""
from playwright.sync_api import expect


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
    content = page.locator('#palette-content')
    operation = page.get_by_label('Graph analysis operation', exact=True)
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
