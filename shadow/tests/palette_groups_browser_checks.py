"""Shared native/WASM palette grouping checks through real menus, keys and pointers."""
from playwright.sync_api import expect
from workspace_layout_browser_checks import drawing_identity, settled


def verify_palette_groups(page, output, state, command, wait_state, idle, check):
    """Preserve the current drawing and finish with the default palette arrangement."""
    baseline = drawing_identity(state())
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    page.evaluate("shadowStudioAgentUi.execute('palette.show', {name:'Graph'})")
    graph = page.locator('[data-palette="Graph"]')
    properties = page.locator('[data-palette="Properties"]')
    graph_tab = page.get_by_role('tab', name='Show Graph in palette group', exact=True)
    properties_tab = page.get_by_role('tab', name='Show Properties in palette group', exact=True)

    def stored():
        return page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-palettes-v2'))")

    def menu():
        graph.get_by_label('Graph palette layout', exact=True).click()
        return graph.locator('.pd-menu-body')

    def choose(label):
        menu().get_by_role('button', name=label, exact=True).click()
        settled(page)

    def group():
        body = menu()
        body.get_by_role('combobox', name='Group Graph with', exact=True).select_option('Properties')
        body.get_by_role('button', name='Group Graph with chosen palette', exact=True).click()
        settled(page)

    group()
    expect(graph).to_be_visible()
    expect(properties).to_have_count(0)
    expect(graph_tab).to_have_attribute('aria-selected', 'true')
    expect(properties_tab).to_have_attribute('aria-selected', 'false')
    graph_panel = graph.locator('xpath=..')
    check('grouping shares one dock frame and exposes linked tab and panel semantics',
          stored()['version'] == 3 and
          graph_panel.get_attribute('aria-labelledby') == graph_tab.get_attribute('id') and
          graph_tab.get_attribute('aria-controls') == graph_panel.get_attribute('id') and
          page.locator('#' + properties_tab.get_attribute('aria-controls')).count() == 1)
    graph_tab.focus()
    page.keyboard.press('Home')
    expect(properties).to_be_visible()
    expect(properties_tab).to_be_focused()
    expect(graph).to_have_count(0)
    page.keyboard.press('ArrowRight')
    expect(graph).to_be_visible()
    expect(graph_tab).to_be_focused()
    check('group tabs switch palette content with arrow and Home keyboard navigation')
    page.screenshot(path=str(output / 'palette-tab-group.png'))

    # Closing an active tab keeps the other pane available; reopening retains its group.
    graph.get_by_role('button', name='Close Graph palette', exact=True).click()
    expect(properties).to_be_visible()
    page.evaluate("shadowStudioAgentUi.execute('palette.show', {name:'Graph'})")
    expect(graph_tab).to_have_attribute('aria-selected', 'true')
    check('closing and reopening a grouped palette preserves its membership',
          any('Properties' in item['tabs'] and 'Graph' in item['tabs'] for item in stored()['groups']['right']))

    choose('Separate Graph from group')
    expect(properties).to_be_visible()
    expect(graph).to_be_visible()
    split = page.get_by_role('separator', name='Resize Properties and Graph palette groups', exact=True)
    expect(split).to_be_visible()
    first = page.locator('.pd-group').filter(has=properties)
    before_height = first.bounding_box()['height']
    split.focus()
    page.keyboard.press('ArrowDown')
    settled(page)
    check('vertical splitter reallocates dock height by keyboard',
          abs(first.bounding_box()['height'] - before_height - 20) < 1.1)
    before_cancel = stored()
    box = split.bounding_box()
    page.mouse.move(box['x'] + box['width'] / 2, box['y'] + box['height'] / 2)
    page.mouse.down()
    page.mouse.move(box['x'] + box['width'] / 2, box['y'] + box['height'] / 2 + 25, steps=4)
    page.keyboard.press('Escape')
    page.mouse.up()
    check('Escape cancels a vertical splitter gesture without changing saved group sizes', stored() == before_cancel)
    expect(split).to_be_focused()

    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-lock'})")
    locked = stored()
    expect(split).to_have_attribute('aria-disabled', 'true')
    split.focus()
    page.keyboard.press('ArrowUp')
    body = menu()
    expect(body.get_by_role('button', name='Move Graph group up', exact=True)).to_be_disabled()
    expect(body.get_by_role('menuitemcheckbox', name='Allow docking: Graph', exact=True)).to_be_disabled()
    page.keyboard.press('Escape')
    check('layout lock protects split sizes, group ordering and docking permissions', stored() == locked)
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-lock'})")

    choose('Move Graph group up')
    check('explicit group reorder changes the visible dock stack',
          page.locator('.pd-dock-right .pd-group').first.locator('[data-palette="Graph"]').count() == 1)
    group()
    choose('Move Graph tab left')
    expect(graph_tab).to_be_focused()
    check('tab order is independently persisted',
          next(item for item in stored()['groups']['right'] if 'Graph' in item['tabs'])['tabs'] == ['Graph', 'Properties'])

    # Named UI profiles use the same v3 normalizer as ordinary preference loading.
    name = 'Browser grouped palettes'
    page.locator('.document-bar [data-action="ui-workspaces"]').click()
    expect(page.locator('#dialog-title')).to_have_text('UI workspaces')
    page.get_by_role('textbox', name='Workspace name', exact=True).fill(name)
    page.get_by_role('button', name='Save current as', exact=True).click()
    expect(page.get_by_role('combobox', name='UI workspace', exact=True).locator('option:checked')).to_have_text(name)
    saved = stored()
    page.locator('#dialog-submit').click()

    menu().get_by_role('menuitemcheckbox', name='Allow docking: Graph', exact=True).click()
    expect(graph).to_have_class('pd-window pd-floating')
    expect(properties).to_be_visible()
    body = menu()
    expect(body.get_by_role('button', name='Dock left: Graph', exact=True)).to_be_disabled()
    expect(body.get_by_role('button', name='Auto-hide left: Graph', exact=True)).to_be_disabled()
    expect(body.get_by_role('button', name='Group Graph with chosen palette', exact=True)).to_be_disabled()
    page.keyboard.press('Escape')
    grip = graph.get_by_role('button', name='Move Graph palette', exact=True).bounding_box()
    workspace = page.locator('#workspace').bounding_box()
    page.mouse.move(grip['x'] + grip['width'] / 2, grip['y'] + grip['height'] / 2)
    page.mouse.down()
    page.mouse.move(workspace['x'] + 12, workspace['y'] + 120, steps=5)
    expect(page.locator('.pd-drop-zones')).to_be_hidden()
    page.mouse.up()
    check('Allow Docking off floats the tab and prevents menu, anchor and edge docking',
          stored()['panels']['Graph']['mode'] == 'float' and not stored()['panels']['Graph']['allowDocking'])
    page.screenshot(path=str(output / 'palette-allow-docking-off.png'))

    page.locator('.document-bar [data-action="ui-workspaces"]').click()
    choices = page.get_by_role('combobox', name='UI workspace', exact=True)
    choices.select_option(label=name)
    page.get_by_role('button', name='Apply layout', exact=True).click()
    page.locator('#dialog-submit').click()
    expect(graph_tab).to_have_attribute('aria-selected', 'true')
    check('saved UI layout restores group order, weights and per-palette docking permissions',
          stored()['groups'] == saved['groups'] and stored()['panels']['Graph']['allowDocking'])

    original_viewport = page.viewport_size
    page.set_viewport_size({'width': 520, 'height': 800})
    settled(page)
    page.evaluate("shadowStudioAgentUi.execute('palette.show', {name:'Graph'})")
    expect(graph).to_have_class('pd-window pd-anchored')
    expect(page.get_by_role('button', name='Show Properties palette', exact=True)).to_be_visible()
    expect(page.get_by_role('button', name='Show Graph palette', exact=True)).to_be_visible()
    check('compact layout retains independent edge access without discarding saved tab groups',
          stored()['groups'] == saved['groups'] and page.locator('#viewport').bounding_box()['width'] >= 280)
    page.screenshot(path=str(output / 'palette-groups-compact.png'))
    page.set_viewport_size(original_viewport)
    settled(page)
    expect(graph_tab).to_have_attribute('aria-selected', 'true')

    page.locator('.document-bar [data-action="ui-workspaces"]').click()
    page.get_by_role('combobox', name='UI workspace', exact=True).select_option(label=name)
    page.get_by_role('button', name='Delete workspace', exact=True).click()
    page.locator('#dialog-submit').click()
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    expect(properties).to_be_visible()
    expect(graph).to_have_count(0)
    idle()
    check('grouping, sizing, docking permissions and profiles preserve the drawing and undo history',
          drawing_identity(state()) == baseline)
