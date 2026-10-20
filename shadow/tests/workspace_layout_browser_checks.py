"""Shared native/WASM workspace checks through actual controls and pointer input."""
import json
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def settled(page):
    page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')


def drawing_identity(data):
    """UI preferences may change the camera, but must not transact on the drawing."""
    state = data['state']
    return {key: state.get(key) for key in (
        'documentId', 'workspaceId', 'revision', 'sourceRevision', 'entities',
        'objects', 'occurrences', 'undoCount', 'redoCount', 'layout')}


def ribbon_mode(page, label, mode):
    if page.locator('#ribbon').get_attribute('data-mode') == mode:
        settled(page)
        return
    page.get_by_role('button', name='Ribbon display options', exact=True).click()
    page.get_by_role('menuitemradio', name=label, exact=True).click()
    expect(page.locator('#ribbon')).to_have_attribute('data-mode', mode)
    settled(page)


def verify_workspace_layout(page, output, state, command, wait_state, idle, check):
    baseline = drawing_identity(state())
    verify_edge_docking(page, output, check)
    verify_profile_persistence(page, output, check)
    verify_ribbon_launchers(page, output, check)
    check('docking, named UI layouts and ribbon controls preserve drawing revision and history',
          drawing_identity(state()) == baseline)
    verify_contextual_ribbon(page, output, state, command, wait_state, idle, check)
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    ribbon_mode(page, 'Full ribbon', 'full')
    page.get_by_role('tab', name='Home', exact=True).click()
    idle()


def verify_edge_docking(page, output, check):
    page.evaluate("shadowStudioAgentUi.execute('palette.show', {name:'Graph'})")
    graph = page.locator('[data-palette="Graph"]')
    workspace = page.locator('#workspace')
    preview = page.locator('.pd-drop-preview')
    zones = page.locator('.pd-drop-zones')

    def stored():
        return page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-palettes-v2'))")

    def place(label):
        graph.get_by_label('Graph palette layout', exact=True).click()
        graph.get_by_role('button', name=label + ': Graph', exact=True).click()
        settled(page)

    def drag_to(side):
        grip = graph.get_by_role('button', name='Move Graph palette', exact=True).bounding_box()
        bounds = workspace.bounding_box()
        page.mouse.move(grip['x'] + grip['width'] / 2, grip['y'] + grip['height'] / 2)
        page.mouse.down()
        page.mouse.move(bounds['x'] + (12 if side == 'left' else bounds['width'] - 12),
                        bounds['y'] + min(180, bounds['height'] / 2), steps=8)

    place('Float palette')
    drag_to('left')
    expect(preview).to_be_visible()
    expect(preview).to_contain_text('dock Graph on the left')
    page.screenshot(path=str(output / 'workspace-dock-preview.png'))
    page.mouse.up()
    expect(page.locator('.pd-dock-left [data-palette="Graph"]')).to_be_visible()
    expect(preview).to_be_hidden()
    check('pointer edge drop docks Graph beside the drawing and clears its preview',
          stored()['panels']['Graph']['mode'] == 'dock' and stored()['panels']['Graph']['side'] == 'left')

    before_cancel = stored()
    drag_to('right')
    expect(preview).to_be_visible()
    expect(preview).to_contain_text('dock Graph on the right')
    page.keyboard.press('Escape')
    page.mouse.up()
    expect(page.locator('.pd-dock-left [data-palette="Graph"]')).to_be_visible()
    expect(zones).to_be_hidden()
    check('Escape during an edge drag restores the complete previous palette layout',
          stored() == before_cancel)

    drag_to('right')
    expect(preview).to_be_visible()
    page.mouse.up()
    expect(page.locator('.pd-dock-right [data-palette="Graph"]')).to_be_visible()
    check('a docked title grip can move directly to the opposite dock',
          stored()['panels']['Graph']['side'] == 'right')

    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-lock'})")
    expect(graph.get_by_role('button', name='Move Graph palette', exact=True)).to_be_disabled()
    before_lock = stored()
    drag_to('left')
    page.mouse.up()
    expect(zones).to_be_hidden()
    check('layout lock prevents pointer dragging and preserves stored placement', stored() == before_lock)
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-lock'})")

    page.keyboard.down('Control')
    try:
        drag_to('left')
        expect(zones).to_be_visible()
        expect(preview).to_be_hidden()
        page.mouse.up()
    finally:
        page.keyboard.up('Control')
    expect(graph).to_have_class('pd-window pd-floating')
    check('holding Control near an edge keeps the palette floating',
          stored()['panels']['Graph']['mode'] == 'float')

    original_viewport = page.viewport_size
    page.set_viewport_size({'width': 520, 'height': 800})
    settled(page)
    drag_to('left')
    expect(preview).to_be_visible()
    expect(preview).to_contain_text('anchor Graph on the left')
    page.mouse.up()
    expect(graph).to_have_class('pd-window pd-anchored')
    check('compact edge drop anchors an expanded palette while retaining usable drawing width',
          page.locator('#viewport').bounding_box()['width'] >= 280 and
          stored()['panels']['Graph']['mode'] == 'dock')
    page.screenshot(path=str(output / 'workspace-compact-dock.png'))
    page.set_viewport_size(original_viewport)
    settled(page)
    place('Float palette')


def verify_profile_persistence(page, output, check):
    profile_name = 'Browser graph layout'
    graph = page.locator('[data-palette="Graph"]')
    page.get_by_role('tab', name='Graph', exact=True).click()
    ribbon_mode(page, 'Panel titles', 'titles')
    check('title mode preserves readable panel labels at desktop width',
          page.locator('#ribbon .ribbon-launcher > span').evaluate_all(
              '(labels) => labels.length > 0 && labels.every(label => label.scrollWidth <= label.clientWidth)'))
    page.get_by_role('separator', name='Resize command history', exact=True).focus()
    page.keyboard.press('ArrowUp')
    page.keyboard.press('ArrowUp')
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'command-history'})")
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'command-line'})")
    expect(page.locator('#command-line')).to_be_hidden()

    def open_profiles(target):
        target.locator('.document-bar [data-action="ui-workspaces"]').click()
        expect(target.locator('#dialog-title')).to_have_text('UI workspaces')
        return target.get_by_role('combobox', name='UI workspace', exact=True)

    def assert_saved_layout(target):
        expect(target.locator('#ribbon')).to_have_attribute('data-mode', 'titles')
        expect(target.get_by_role('tab', name='Graph', exact=True)).to_have_attribute('aria-selected', 'true')
        expect(target.locator('#command-line')).to_be_hidden()
        expect(target.locator('[data-palette="Graph"]')).to_have_class('pd-window pd-floating')
        expect(target.locator('[data-palette="Properties"]')).to_be_visible()
        preferences = target.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-ui-v1'))")
        return (preferences['commandHeight'] == saved['commandHeight'] and
                preferences['commandHistoryVisible'] == saved['chrome']['commandHistoryVisible'])

    select = open_profiles(page)
    page.get_by_role('textbox', name='Workspace name', exact=True).fill(profile_name)
    page.get_by_role('button', name='Save current as', exact=True).click()
    expect(select.locator('option:checked')).to_have_text(profile_name)
    saved = page.evaluate("""() => JSON.parse(localStorage.getItem('netdxf-cad-workspaces-v1'))
      .profiles.find(entry=>entry.name==='Browser graph layout').layout""")
    check('named workspace captures ribbon, command history and independent palette placement',
          saved['tab'] == 'Graph' and saved['chrome']['ribbonMode'] == 'titles' and
          not saved['chrome']['commandLineVisible'] and not saved['chrome']['commandHistoryVisible'] and
          saved['palettes']['panels']['Graph']['mode'] == 'float')
    original_identity = drawing_identity(page.evaluate("shadowStudioAgentUi.snapshot()"))
    page.get_by_text('Transfer UI workspace', exact=True).click()
    with page.expect_download() as exported:
        page.get_by_role('button', name='Export selected workspace', exact=True).click()
    transfer_path = output / 'workspace-export.json'
    exported.value.save_as(str(transfer_path))
    transfer = json.loads(transfer_path.read_text())
    check('workspace download contains a portable UI-only envelope',
          transfer['format'] == 'shadow-studio-ui-workspace' and transfer['version'] == 1 and
          transfer['layout'] == saved and set(transfer) == {'format', 'version', 'name', 'layout'})
    page.get_by_label('Import UI workspace', exact=True).set_input_files(str(transfer_path))
    expect(page.get_by_role('button', name='Save imported workspace', exact=True)).to_be_enabled()
    page.get_by_role('textbox', name='Imported workspace name', exact=True).fill('Transferred analysis')
    page.get_by_role('button', name='Save imported workspace', exact=True).click()
    expect(select.locator('option:checked')).to_have_text('Transferred analysis')
    check('workspace import previews and saves without changing drawing or active arrangement',
          drawing_identity(page.evaluate("shadowStudioAgentUi.snapshot()")) == original_identity and
          page.locator('#ribbon').get_attribute('data-mode') == 'titles')
    page.get_by_role('button', name='Delete workspace', exact=True).click()
    select.select_option(label=profile_name)

    select.select_option(label='Drafting (preset)')
    page.get_by_role('button', name='Apply layout', exact=True).click()
    page.locator('#dialog-submit').click()
    expect(graph).to_have_count(0)
    expect(page.locator('#command-line')).to_be_visible()
    expect(page.get_by_role('tab', name='Home', exact=True)).to_have_attribute('aria-selected', 'true')
    check('Drafting preset restores default chrome and closes the separate Graph palette')

    select = open_profiles(page)
    select.select_option(label=profile_name)
    page.get_by_role('button', name='Apply layout', exact=True).click()
    page.locator('#dialog-submit').click()
    check('saved workspace restores the chosen tab, hidden command line and floating Graph', assert_saved_layout(page))
    page.screenshot(path=str(output / 'workspace-saved-layout.png'))

    # A new page reads real origin storage. Reloading the original page would lose
    # its in-memory WASM drawing and make later geometry assertions meaningless.
    restored = page.context.new_page()
    restored.set_viewport_size(page.viewport_size)
    errors = []
    restored.on('pageerror', lambda error: errors.append(str(error)))
    try:
        response = restored.goto(page.url, wait_until='domcontentloaded')
        check('independent browser page loads the actual application', bool(response and response.ok))
        expect(restored.locator('#connection')).to_have_text('Local', timeout=180000)
        wait_for_ui(restored, "() => typeof shadowStudioAgentUi !== 'undefined'", timeout=180000)
        select = open_profiles(restored)
        select.select_option(label=profile_name)
        restored.get_by_role('button', name='Apply layout', exact=True).click()
        restored.locator('#dialog-submit').click()
        check('saved workspace survives a fresh application instance on the same origin',
              assert_saved_layout(restored) and not errors)
    finally:
        restored.close()

    select = open_profiles(page)
    select.select_option(label=profile_name)
    page.get_by_role('textbox', name='Workspace name', exact=True).fill(profile_name + ' renamed')
    page.get_by_role('button', name='Rename workspace', exact=True).click()
    expect(select.locator('option:checked')).to_have_text(profile_name + ' renamed')
    page.get_by_role('button', name='Delete workspace', exact=True).click()
    expect(select.locator('option', has_text=profile_name)).to_have_count(0)
    select.select_option(label='Drafting (preset)')
    page.get_by_role('button', name='Apply layout', exact=True).click()
    page.locator('#dialog-submit').click()
    check('renaming and deleting a saved workspace leaves built-in presets usable')


def verify_ribbon_launchers(page, output, check):
    page.get_by_role('tab', name='Graph', exact=True).click()
    ribbon_mode(page, 'Panel titles', 'titles')
    launcher = page.get_by_role('button', name='Topology tools', exact=True)
    flyout = page.locator('.ribbon-flyout[aria-label="Topology"]')
    launcher.hover()
    expect(flyout).to_be_visible()
    launcher.click()
    expect(flyout).to_be_visible()
    check('clicking a hover-open ribbon title keeps its tools open and gives them keyboard focus',
          flyout.evaluate('(element) => element.contains(document.activeElement)'))
    page.keyboard.press('Escape')
    expect(flyout).to_have_count(0)
    expect(launcher).to_be_focused()
    launcher.press('ArrowDown')
    expect(flyout).to_be_visible()
    check('ArrowDown opens a title panel and Escape restores the launcher focus',
          flyout.evaluate('(element) => element.contains(document.activeElement)'))
    page.keyboard.press('Escape')
    launcher.click()
    expect(flyout).to_be_visible()
    launcher.click()
    expect(flyout).to_have_count(0)
    check('a second explicit title click closes its pinned ribbon flyout')

    original_viewport = page.viewport_size
    page.set_viewport_size({'width': 520, 'height': 800})
    settled(page)
    check('compact title mode preserves each full panel label without clipping',
          page.locator('#ribbon .ribbon-launcher > span').evaluate_all(
              '(labels) => labels.length > 0 && labels.every(label => label.scrollWidth <= label.clientWidth)'))
    launcher.press('ArrowDown')
    expect(flyout).to_be_visible()
    bounds = flyout.bounding_box()
    check('compact ribbon flyout stays inside the visible viewport',
          bounds['x'] >= 0 and bounds['y'] >= 0 and bounds['x'] + bounds['width'] <= 520 and
          bounds['y'] + bounds['height'] <= 800)
    page.screenshot(path=str(output / 'workspace-ribbon-compact.png'))
    page.keyboard.press('Escape')
    page.set_viewport_size(original_viewport)
    ribbon_mode(page, 'Full ribbon', 'full')


def verify_contextual_ribbon(page, output, state, command, wait_state, idle, check):
    before = state()
    before_ids = sorted(item['id'] for item in before['items'])
    page.locator('#viewport').focus()
    page.keyboard.press('Control+a')
    wait_state(lambda value: len(value['selectedEntities']) == before['itemCount'])
    idle()
    command('BLOCK 0,0 WorkspaceRibbonFixture')
    check('contextual ribbon fixture creates one native block transaction',
          state()['state']['undoCount'] == before['state']['undoCount'] + 1)
    baseline = drawing_identity(state())
    module = page.locator('#ribbon-tabs [data-tab^="module:"]:not(:disabled)').first
    expect(module).to_be_visible()
    module_key = module.get_attribute('data-tab')

    for previous in ('Graph', module_key):
        prior = page.locator(f'#ribbon-tabs [data-tab="{previous}"]')
        prior.click()
        command('BEDIT WorkspaceRibbonFixture', False)
        wait_state(lambda value: value.get('blockEditor') is not None)
        idle()
        context = page.get_by_role('tab', name='Block Editor', exact=True)
        expect(context).to_have_attribute('aria-selected', 'true')
        expect(page.locator('#ribbon [data-action="block-save"]')).to_be_visible()
        module_tabs = page.locator('#ribbon-tabs [data-tab^="module:"]')
        check('block editor selects its contextual tab and disables discipline commands',
              module_tabs.count() > 0 and module_tabs.evaluate_all('(tabs) => tabs.every(tab=>tab.disabled)'))
        # Selecting a normal tab during editing must not overwrite the entry tab.
        page.get_by_role('tab', name='Home', exact=True).click()
        expect(context).to_be_visible()
        context.click()
        page.screenshot(path=str(output / ('workspace-block-module.png' if previous == module_key else 'workspace-block-context.png')))
        command('BCLOSE', False)
        wait_state(lambda value: value.get('blockEditor') is None)
        idle()
        expect(context).to_have_count(0)
        expect(prior).to_have_attribute('aria-selected', 'true')
        expect(page.locator(f'#ribbon-tabs [data-tab="{module_key}"]')).to_be_enabled()
        check(f'leaving block editor restores prior {previous} tab without a live drawing transaction',
              drawing_identity(state()) == baseline)

    command('U')
    restored = state()
    check('one undo removes the temporary definition and restores original graph source identities',
          restored['state']['undoCount'] == before['state']['undoCount'] and
          sorted(item['id'] for item in restored['items']) == before_ids and
          not any(block['name'] == 'WorkspaceRibbonFixture' for block in restored['blocks']))
