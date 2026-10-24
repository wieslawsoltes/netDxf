"""Shared native/WASM shell qualification using the real drawing host and controls."""
from playwright.sync_api import expect
from workspace_layout_browser_checks import drawing_identity, settled
from browser_ui import wait_for_ui


def verify_desktop_shell(page, output, state, command, wait_state, idle, check):
    baseline = drawing_identity(state())
    original_viewport = page.viewport_size
    popup = page.locator('#shell-popup')
    command_line = page.locator('#command-line')
    command_input = page.locator('#command-input')

    def prefs():
        return page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-shell-v1'))")

    def workspace(name):
        page.locator('#workspace-switch').click()
        popup.get_by_role('menuitemradio', name=name, exact=True).click()
        settled(page)

    def manager():
        page.locator('.document-bar [data-action="ui-workspaces"]').click()
        expect(page.locator('#dialog-title')).to_have_text('UI workspaces')
        return page.get_by_role('combobox', name='UI workspace', exact=True)

    def close_manager():
        page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()

    def drag(grip, dx, dy, cancel=False):
        box = grip.bounding_box()
        x, y = box['x'] + box['width'] / 2, box['y'] + box['height'] / 2
        page.mouse.move(x, y)
        page.mouse.down()
        page.mouse.move(x + dx, y + dy, steps=6)
        if cancel:
            page.keyboard.press('Escape')
        page.mouse.up()
        settled(page)

    try:
        workspace('Drafting & Annotation')
        page.set_viewport_size({'width': 1600, 'height': 1000})
        settled(page)
        viewport = page.locator('#viewport').bounding_box()
        line = command_line.bounding_box()
        dock = page.locator('.pd-dock-right').bounding_box()
        area = page.locator('#drawing-area').bounding_box()
        check('command and layout rows belong to the drawing column while palettes span its full height',
              abs(line['width'] - viewport['width']) < 2 and abs(line['x'] - viewport['x']) < 2 and
              dock['height'] > viewport['height'] and abs(dock['height'] - area['height']) < 2)
        check('all live shell IDs are unique', page.evaluate("""() => {
          const ids=[...document.querySelectorAll('[id]')].map(node=>node.id);
          return new Set(ids).size===ids.length;
        }"""))
        page.locator('#file-menu').click()
        expect(popup).to_have_attribute('role', 'dialog')
        expect(popup.get_by_role('menuitem', name='New drawing', exact=True)).to_be_enabled()
        expect(popup.get_by_role('menuitem', name='Save DXF', exact=True)).to_be_enabled()
        expect(popup.locator('.shell-open-drawings')).to_contain_text(page.locator('#documents .active .name').inner_text().replace('● ', ''))
        page.keyboard.press('Escape')
        expect(page.locator('#file-menu')).to_be_focused()
        check('application menu provides registered file commands, open drawings and Escape recovery')

        command_input.fill('LINE 10,20')
        command_input.evaluate("node=>{window.__shellCommandInput=node;node.focus();node.setSelectionRange(5,7);}")
        page.locator('#command-dock-toggle').evaluate('node=>node.click()')
        expect(command_line).to_have_class('command-line command-floating')
        check('floating reparents the existing command editor and retains its buffer, focus and selection',
              command_input.evaluate("node=>node===window.__shellCommandInput&&document.activeElement===node&&node.selectionStart===5&&node.selectionEnd===7"))
        expect(command_input).to_have_value('LINE 10,20')
        before = command_line.bounding_box()
        drag(page.locator('#command-grip'), 65, -40)
        after = command_line.bounding_box()
        check('floating command line moves by actual pointer capture', after['x'] > before['x'] + 40 and after['y'] < before['y'] - 20)
        saved = prefs()['commandWindow']
        drag(page.locator('#command-grip'), 40, 20, cancel=True)
        check('Escape cancels command-window movement without persisting the preview', prefs()['commandWindow'] == saved)
        handle = page.locator('#command-resize')
        handle.focus()
        handle.press('ArrowRight')
        check('floating command resize supports keyboard adjustment', command_line.bounding_box()['width'] > after['width'])
        page.screenshot(path=str(output / 'shell-command-floating.png'))

        # A saved workspace can intentionally share a name with a built-in preset.
        select = manager()
        page.get_by_role('textbox', name='Workspace name', exact=True).fill('Drafting')
        page.get_by_role('button', name='Save current as', exact=True).click()
        expect(select.locator('option:checked')).to_have_text('Drafting')
        close_manager()
        workspace('Drafting & Annotation')
        expect(command_line).not_to_have_class('command-line command-floating')
        workspace('Drafting')
        expect(command_line).to_have_class('command-line command-floating')
        check('named workspace switching distinguishes preset and saved entries with identical names', prefs()['workspacePreset'] is False)
        select = manager()
        select.select_option(label='Drafting')
        page.get_by_role('textbox', name='Workspace name', exact=True).fill('Shell command review')
        page.get_by_role('button', name='Rename workspace', exact=True).click()
        close_manager()
        expect(page.locator('#workspace-name')).to_have_text('Shell command review')
        check('renaming an active saved workspace updates the switcher identity')

        # Ctrl suppresses bottom docking; releasing without Ctrl docks the same editor.
        center = page.locator('#drawing-area').bounding_box()
        grip = page.locator('#command-grip').bounding_box()
        x, y = grip['x'] + grip['width']/2, grip['y'] + grip['height']/2
        target_x, target_y = center['x'] + center['width']/2, center['y'] + center['height'] - 12
        page.mouse.move(x, y); page.mouse.down(); page.keyboard.down('Control')
        page.mouse.move(target_x, target_y, steps=8); page.mouse.up(); page.keyboard.up('Control')
        expect(command_line).to_have_class('command-line command-floating')
        grip = page.locator('#command-grip').bounding_box()
        x, y = grip['x'] + grip['width']/2, grip['y'] + grip['height']/2
        page.mouse.move(x, y); page.mouse.down(); page.mouse.move(x+25, y-25, steps=3)
        page.mouse.move(target_x, target_y, steps=8)
        expect(page.locator('.command-dock-preview')).to_be_visible()
        page.mouse.up()
        expect(command_line).not_to_have_class('command-line command-floating')
        expect(command_input).to_have_value('LINE 10,20')
        check('command line docks at the drawing bottom and Ctrl bypass preserves floating mode')
        command_input.fill('')

        splitter = page.get_by_role('separator', name='Resize command history', exact=True)
        before_height = command_line.bounding_box()['height']
        drag(splitter, 0, -35, cancel=True)
        check('Escape also cancels docked command-history resizing', abs(command_line.bounding_box()['height'] - before_height) < 2)
        page.locator('#status-customize').click()
        popup.get_by_role('menuitemcheckbox', name='Drawing grid', exact=True).click()
        expect(page.locator('#drafting-modes [data-mode-key="grid"]')).to_have_count(0)
        popup.get_by_role('menuitemcheckbox', name='Show text labels', exact=True).click()
        expect(page.locator('#drafting-modes')).to_have_class('status-labels')
        page.keyboard.press('Escape')
        grid_before = page.evaluate('shadowStudioAgentUi.snapshot().settings.grid')
        page.keyboard.press('F7')
        check('hidden status indicators retain their drafting keyboard shortcut', page.evaluate('shadowStudioAgentUi.snapshot().settings.grid') is not grid_before)
        page.keyboard.press('F7')
        page.locator('#drafting-modes [data-mode-key="osnap"]').click(button='right')
        endpoint = popup.get_by_role('menuitemcheckbox', name='Endpoint', exact=True)
        initial = endpoint.get_attribute('aria-checked')
        endpoint.click()
        expect(endpoint).to_have_attribute('aria-checked', 'false' if initial == 'true' else 'true')
        endpoint.click()
        page.keyboard.press('Escape')
        check('object snap status context menu edits and restores individual running snap modes')

        # Actual asynchronous tab redraws must keep sequential keyboard navigation alive.
        tabs = page.locator('#documents [role="tab"]')
        if tabs.count() > 1:
            active = page.locator('#documents [aria-selected="true"]')
            active.focus(); page.keyboard.press('ArrowRight'); idle(); settled(page)
            expect(page.locator('#documents [aria-selected="true"]')).to_be_focused()
            page.keyboard.press('ArrowLeft'); idle(); settled(page)
            expect(page.locator('#documents [aria-selected="true"]')).to_be_focused()
            check('drawing tab arrow navigation retains focus across asynchronous document activation')
        page.locator('#documents .document-tab.active .close').focus()
        page.evaluate('window.__shellClose=document.activeElement')
        wait_for_ui(page,"() => document.activeElement!==window.__shellClose && document.activeElement?.matches('#documents .close')",timeout=10000)
        check('background document refresh preserves the focused Close action')

        # Qualify the default icon-only status bar too, including offscreen labels.
        workspace('Drafting & Annotation')

        for width, height in [(1366, 900), (1024, 768), (390, 844), (320, 640)]:
            page.set_viewport_size({'width': width, 'height': height}); settled(page)
            check(f'CAD shell fits {width}px without page overflow', page.evaluate('document.documentElement.scrollWidth<=innerWidth&&document.documentElement.scrollHeight<=innerHeight'))
            page.locator('#status-workspaces').click()
            expect(popup).to_be_visible()
            box = popup.bounding_box()
            check(f'workspace switcher remains within the {width}px viewport', box['x'] >= 0 and box['x'] + box['width'] <= width + 1)
            page.keyboard.press('Escape')
            if width == 390:
                page.screenshot(path=str(output / 'shell-compact.png'))
        page.set_viewport_size({'width': 1600, 'height': 1000})
        workspace('Drafting & Annotation')
        select = manager()
        page.get_by_role('textbox', name='Workspace name', exact=True).fill('Custom layout')
        page.get_by_role('button', name='Save current as', exact=True).click()
        select.select_option(label='Shell command review')
        page.get_by_role('button', name='Apply layout', exact=True).click()
        page.get_by_role('button', name='Delete workspace', exact=True).click()
        close_manager()
        page.locator('#workspace-switch').click()
        expect(popup.locator('[role="menuitemradio"][aria-checked="true"]')).to_have_count(0)
        expect(popup.get_by_role('menuitem', name='Reset to Drafting & Annotation', exact=True)).to_be_visible()
        popup.get_by_role('menuitemradio', name='Drafting & Annotation', exact=True).click()
        check('deleting the active workspace leaves a detached layout without matching an unrelated saved name')
        select = manager()
        select.select_option(label='Custom layout')
        page.get_by_role('button', name='Delete workspace', exact=True).click()
        close_manager()
        page.screenshot(path=str(output / 'shell-desktop.png'))
        check('shell commands, workspaces and status customization preserve drawing and undo history', drawing_identity(state()) == baseline)
    finally:
        if popup.count():
            page.keyboard.press('Escape')
        command_input.fill('')
        page.set_viewport_size(original_viewport)
        page.evaluate('delete window.__shellCommandInput;delete window.__shellClose')
