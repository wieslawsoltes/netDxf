"""Exercise shared command discovery and Quick Access against actual Studio hosts."""
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_command_access(page, output, state, command, wait_state, idle, check):
    def receipt():
        value = state()['state']
        return {key: value.get(key) for key in (
            'documentId', 'workspaceId', 'revision', 'sourceRevision', 'undoCount', 'redoCount', 'entities')}

    def settle(target=page):
        target.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')

    def open_search(query=''):
        page.get_by_role('button', name='Search commands (Ctrl+K)', exact=True).click()
        expect(dialog).to_be_visible()
        field.fill(query)
        settle()

    def close_search():
        page.keyboard.press('Escape')
        expect(dialog).not_to_be_visible()

    def preferences(target=page):
        return target.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-quick-access-v1'))")

    dialog = page.locator('.command-access-dialog')
    field = dialog.get_by_role('searchbox', name='Search commands', exact=True)
    baseline = receipt()
    selection = state()['selectedEntities']
    page.get_by_role('button', name='Search commands (Ctrl+K)', exact=True).focus()
    page.keyboard.press('Control+k')
    expect(field).to_be_focused()
    field.fill('')
    field.press('ArrowUp')
    settle()
    check('keyboard navigation scrolls the last eligible command into the visible result list',
          dialog.locator('.command-access-results').evaluate("""node => {
            const active=document.activeElement,box=active.getBoundingClientRect(),view=node.getBoundingClientRect();
            return node.contains(active)&&node.scrollTop>0&&box.top>=view.top-1&&box.bottom<=view.bottom+1;
          }"""))
    page.keyboard.press('ArrowDown')
    settle()
    check('keyboard wrapping restores the first eligible command to the visible result list',
          dialog.locator('.command-access-results').evaluate("""node => {
            const active=document.activeElement,box=active.getBoundingClientRect(),view=node.getBoundingClientRect();
            return node.contains(active)&&box.top>=view.top-1&&box.bottom<=view.bottom+1;
          }"""))
    field.focus()
    field.fill('not-a-real-command-xyzz')
    expect(dialog).to_contain_text('No matching commands.')
    field.press('Enter')
    expect(dialog).to_be_visible()
    check('empty command search never executes a hidden Quick Access command', receipt() == baseline)
    close_search()
    expect(page.get_by_role('button', name='Search commands (Ctrl+K)', exact=True)).to_be_focused()
    check('Escape from command search preserves drawing selection and restores focus',
          state()['selectedEntities'] == selection and receipt() == baseline)

    open_search('BSAVE')
    expect(dialog.get_by_role('button', name='Save block definition', exact=True)).to_be_disabled()
    field.press('Enter')
    expect(dialog).to_be_visible()
    expect(dialog).to_contain_text('Open a block definition first.')
    check('unavailable block command explains its context and Enter remains read-only', receipt() == baseline)
    field.fill('layer manager')
    expect(dialog.get_by_role('button', name='Layer manager', exact=True)).to_be_enabled()
    field.fill('minimum spanning forest')
    expect(dialog.get_by_role('button', name='Minimum spanning forest', exact=True)).to_be_enabled()
    check('multiword search discovers resource managers and topology analyses')

    dialog.get_by_role('button', name='Pin Minimum spanning forest', exact=True).click()
    expect(dialog.get_by_role('button', name='Unpin Minimum spanning forest', exact=True)).to_be_visible()
    dialog.get_by_role('button', name='Customize Quick Access', exact=True).click()
    move = dialog.get_by_role('button', name='Move Minimum spanning forest earlier', exact=True)
    for _ in range(5):
        move.click()
    expect(move).to_be_disabled()
    check('Quick Access pinning and explicit reordering persist only bounded stable IDs',
          preferences() == {'version': 1, 'pinned': ['action:graph-query:spanning_forest',
              'action:new', 'action:open', 'action:save', 'action:undo', 'action:redo']})
    close_search()
    expect(page.get_by_role('button', name='Search commands (Ctrl+K)', exact=True)).to_be_focused()
    check('Escape restores the replacement toolbar origin after pinning and reordering')
    favorite = page.locator('#quick-access [data-command-id="action:graph-query:spanning_forest"]')
    expect(favorite).to_be_visible()
    favorite.click()
    expect(page.locator('[data-palette="Graph"]')).to_be_visible()
    expect(page.get_by_label('Graph analysis operation', exact=True)).to_have_value('spanning_forest')
    check('pinned graph action opens its actual analysis workflow without executing a drawing edit', receipt() == baseline)

    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ui-workspaces'})")
    workspace = page.get_by_role('combobox', name='UI workspace', exact=True)
    expect(workspace).to_be_visible()
    page.get_by_role('textbox', name='Workspace name', exact=True).fill('Command access layout')
    page.get_by_role('button', name='Save current as', exact=True).click()
    workspace.select_option(label='Drafting (preset)')
    page.get_by_role('button', name='Apply layout', exact=True).click()
    check('Drafting workspace restores its default Quick Access commands',
          preferences()['pinned'] == ['action:new','action:open','action:save','action:undo','action:redo'])
    workspace.select_option(label='Command access layout')
    page.get_by_role('button', name='Apply layout', exact=True).click()
    check('saved UI workspace restores command choices and ordering with no drawing edit',
          preferences()['pinned'][0] == 'action:graph-query:spanning_forest' and receipt() == baseline)
    page.get_by_role('button', name='Delete workspace', exact=True).click()
    page.locator('#dialog-submit').click()
    expect(favorite).to_be_visible()

    restored = page.context.new_page()
    errors = []
    restored.on('pageerror', lambda error: errors.append(str(error)))
    try:
        response = restored.goto(page.url, wait_until='domcontentloaded')
        check('Quick Access persistence check loads a real independent application instance', bool(response and response.ok))
        expect(restored.locator('#connection')).to_have_text('Local', timeout=180000)
        wait_for_ui(restored, "() => typeof shadowStudioAgentUi !== 'undefined'", timeout=180000)
        expect(restored.locator('#quick-access [data-command-id="action:graph-query:spanning_forest"]')).to_be_visible()
        check('pinned command ordering survives application reload without a server account',
              preferences(restored)['pinned'][0] == 'action:graph-query:spanning_forest' and not errors)
    finally:
        restored.close()

    # Change the actual native module state while its search result is present.
    # The displayed entry must be revalidated, without losing the user's query.
    modules = page.evaluate("shadowStudioAgentUi.execute('cad.read', {path:'modules'})")['modules']
    module = next(value for value in modules if value['enabled'] and value['descriptor']['commands'])
    descriptor = module['descriptor']
    title = descriptor['name'] + ': ' + descriptor['commands'][0]['name']
    open_search(title)
    target = dialog.get_by_role('button', name=title, exact=True)
    expect(target).to_be_visible()
    page.evaluate("""module => shadowStudioAgentUi.execute('cad.edit', {
      path:'modules/toggle', body:{module,enabled:false}
    })""", descriptor['id'])
    wait_state(lambda value: not next(item for item in value['modules'] if item['id'] == descriptor['id'])['enabled'])
    idle()
    expect(field).to_have_value(title)
    expect(target).to_be_disabled()
    field.press('Enter')
    expect(dialog).to_be_visible()
    expect(dialog).to_contain_text('Enable this discipline module first.')
    check('a module disabled after discovery cannot run through its stale search result', receipt() == baseline)
    page.evaluate("""module => shadowStudioAgentUi.execute('cad.edit', {
      path:'modules/toggle', body:{module,enabled:true}
    })""", descriptor['id'])
    wait_state(lambda value: next(item for item in value['modules'] if item['id'] == descriptor['id'])['enabled'])
    idle()
    close_search()

    original_viewport = page.viewport_size
    page.set_viewport_size({'width': 390, 'height': 844})
    settle()
    expect(page.get_by_role('button', name='Search commands (Ctrl+K)', exact=True)).to_be_visible()
    expect(page.get_by_role('button', name='More Quick Access commands', exact=True)).to_be_visible()
    page.get_by_role('button', name='More Quick Access commands', exact=True).click()
    overflow = page.get_by_role('menu', name='More Quick Access commands', exact=True)
    expect(overflow).to_be_visible()
    overflow.get_by_role('menuitem', name='More commands…', exact=True).click()
    expect(dialog.get_by_role('button', name='Remove Minimum spanning forest from Quick Access', exact=True)).to_be_visible()
    box = dialog.bounding_box()
    check('compact search customization overlays the drawing within the viewport',
          box['x'] >= 0 and box['y'] >= 0 and box['x'] + box['width'] <= 390 and box['y'] + box['height'] <= 844)
    check('compact toolbar keeps search customize and overflow controls reachable',
          page.locator('#quick-access').evaluate('(node) => node.scrollWidth <= node.clientWidth'))
    page.screenshot(path=str(output / 'command-access-compact.png'))
    dialog.get_by_role('button', name='Remove Minimum spanning forest from Quick Access', exact=True).click()
    expect(dialog.get_by_role('button', name='Remove Minimum spanning forest from Quick Access', exact=True)).to_have_count(0)
    dialog.get_by_role('button', name='Reset Quick Access', exact=True).click()
    close_search()
    expect(page.get_by_role('button', name='More Quick Access commands', exact=True)).to_be_focused()
    check('compact Quick Access reset preserves the replacement overflow origin')
    page.set_viewport_size(original_viewport)
    settle()
    check('Quick Access reset restores the five default commands without drawing history',
          preferences()['pinned'] == ['action:new','action:open','action:save','action:undo','action:redo'] and receipt() == baseline)
    expect(page.locator('#quick-access [data-action="save"]')).to_be_visible()

    before = state()
    open_search('LINE')
    field.press('ArrowDown')
    expect(dialog.get_by_role('button', name='Line', exact=True)).to_be_focused()
    page.keyboard.press('Enter')
    expect(dialog).not_to_be_visible()
    expect(page.locator('#prompt')).to_contain_text('LINE')
    for value in ('100,100', '110,100', ''):
        page.locator('#command-input').fill(value)
        page.locator('#command-input').press('Enter')
    wait_state(lambda value: value['state']['revision'] > before['state']['revision'])
    idle()
    check('search launches the real native LINE workflow as one undo transaction',
          state()['state']['undoCount'] == before['state']['undoCount'] + 1 and state()['itemCount'] == before['itemCount'] + 1)
    page.screenshot(path=str(output / 'command-access-drawing.png'))
    command('U')
    check('one undo restores native geometry after executing a searched command',
          sorted(item['id'] for item in state()['items']) == sorted(item['id'] for item in before['items']) and
          state()['state']['undoCount'] == before['state']['undoCount'])
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    page.get_by_role('tab', name='Home', exact=True).click()
