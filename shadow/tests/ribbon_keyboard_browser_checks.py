"""Shared native/WASM keyboard qualification through live ribbon controls; no providers."""
import json
import re
from playwright.sync_api import expect
from workspace_layout_browser_checks import drawing_identity, ribbon_mode, settled


def verify_ribbon_keyboard(page, output, state, command, wait_state, idle, check):
    baseline = drawing_identity(state())
    original_viewport = page.viewport_size
    original_mode = page.locator('#ribbon').get_attribute('data-mode')
    original_tab = page.locator('#ribbon-tabs [aria-selected="true"]').get_attribute('data-tab')
    original_preferences = page.evaluate("localStorage.getItem('netdxf-cad-ribbon-v1')")
    modal = page.locator('#dialog')
    modes = [('Full ribbon', 'full'), ('Panel buttons', 'buttons'), ('Panel titles', 'titles'), ('Tabs only', 'tabs')]

    def apply_preferences(value):
        page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ribbon-customize'})")
        expect(page.locator('#dialog-title')).to_have_text('Customize ribbon')
        if value:
            modal.get_by_label('Import ribbon customization', exact=True).set_input_files({
                'name': 'keyboard-ribbon.json', 'mimeType': 'application/json', 'buffer': value.encode()})
            expect(modal.get_by_role('status')).to_contain_text('Imported into the draft')
        else:
            modal.get_by_role('button', name='Reset ribbon draft', exact=True).click()
        page.locator('#dialog-submit').click()
        expect(modal).not_to_be_visible()
        settled(page)

    def visible_focus():
        return page.evaluate("""() => {
          const active=document.activeElement;
          if(!active || active.disabled || !active.getClientRects().length)return false;
          const box=active.getBoundingClientRect();
          return box.right>0 && box.bottom>0 && box.left<innerWidth && box.top<innerHeight;
        }""")

    def cancel_command():
        page.evaluate("shadowStudioAgentUi.execute('command.cancel', {clearSelection:false})")
        expect(page.locator('#prompt')).to_have_text('Command:')
        idle()

    try:
        apply_preferences(None)
        page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ribbon-customize'})")
        expect(page.locator('#dialog-title')).to_have_text('Customize ribbon')
        editor_tabs = modal.get_by_role('combobox', name='Customize ribbon tab', exact=True)
        editor_tabs.focus()
        # Native select menus on headless macOS do not commit arrow movement;
        # type-ahead emits a real keyboard-driven change without a popup.
        editor_tabs.press('i')
        expect(editor_tabs).to_be_focused()
        check('customization selector redraw preserves keyboard focus on its connected replacement',
              editor_tabs.input_value() != 'Home')
        page.keyboard.press('Escape')
        expect(modal).not_to_be_visible()
        page.set_viewport_size({'width': 1600, 'height': 1000})
        ribbon_mode(page, 'Full ribbon', 'full')
        home = page.get_by_role('tab', name='Home', exact=True)
        home.click()
        for width in [1600, 1366, 1280]:
            page.set_viewport_size({'width': width, 'height': 1000})
            settled(page)
            check(f'all Home ribbon panels remain expanded at {width}px',
                  page.locator('#ribbon .ribbon-group.collapsed').count() == 0)
        curves = page.get_by_role('button', name='Curves options', exact=True)
        curves.focus()
        curves.press('ArrowDown')
        expect(page.get_by_role('menuitem', name='Ellipse', exact=True)).to_be_focused()
        page.keyboard.press('ArrowDown')
        expect(page.get_by_role('menuitem', name='Cubic spline', exact=True)).to_be_focused()
        page.keyboard.press('Escape')
        expect(curves).to_be_focused()
        expect(page.locator('.ribbon-command-menu')).to_have_count(0)
        curves.click()
        page.get_by_role('menuitem', name='Ellipse', exact=True).click()
        expect(page.locator('.ribbon-command-menu')).to_have_count(0)
        expect(page.locator('#prompt')).not_to_have_text('Command:')
        cancel_command()
        check('split ribbon alternatives use real CAD commands and restore keyboard focus on Escape')
        page.set_viewport_size({'width': 390, 'height': 844})
        settled(page)
        check('compact Home panel buttons fit within the ribbon at 390px',
              page.locator('#ribbon').evaluate('node => node.scrollWidth <= node.clientWidth'))
        page.locator('#ribbon [data-panel="Draw"] .ribbon-launcher').click()
        expect(page.locator('.ribbon-flyout [data-command="POLYGON"]')).to_be_visible()
        expect(page.locator('.ribbon-flyout [data-command="ELLIPSE"]')).to_be_visible()
        expect(page.locator('.ribbon-flyout [data-command="SPLINE"]')).to_be_visible()
        page.keyboard.press('Escape')
        expect(page.locator('.ribbon-flyout')).to_have_count(0)
        check('collapsed panels expose split alternatives directly without nested menus')
        page.set_viewport_size({'width': 1600, 'height': 1000})
        settled(page)
        page.screenshot(path=str(output / 'ribbon-drafting-density.png'))
        home.focus()
        home.press('End')
        check('ribbon End focuses and selects the last enabled visible tab', page.locator('#ribbon-tabs').evaluate(
            """node => {const choices=[...node.querySelectorAll('button')].filter(button=>!button.disabled && button.getClientRects().length);
              return document.activeElement===choices.at(-1) && choices.at(-1).getAttribute('aria-selected')==='true';}"""))
        page.keyboard.press('Home')
        expect(home).to_be_focused()
        home.press('ArrowRight')
        expect(page.get_by_role('tab', name='Insert', exact=True)).to_be_focused()
        page.keyboard.press('ArrowLeft')
        expect(home).to_be_focused()
        check('ribbon tabs support Home and reversible horizontal navigation')

        for label, mode in modes:
            ribbon_mode(page, label, mode)
            home.click()
            page.mouse.move(1550, 900)
            home.press('ArrowDown')
            settled(page)
            check(f'{mode} ribbon Down enters a visible enabled command or launcher', visible_focus() and
                  page.locator('#ribbon').evaluate('(node)=>node.contains(document.activeElement)'))
            launcher = page.locator('#ribbon [data-panel="Draw"] .ribbon-launcher')
            if launcher.is_visible():
                page.keyboard.press('Home')
                expect(launcher).to_be_focused()
                launcher.press('ArrowDown')
                expect(page.locator('.ribbon-flyout')).to_be_visible()
                expect(launcher).to_have_attribute('aria-expanded', 'true')
                page.keyboard.press('End')
                check(f'{mode} flyout End keeps focus on a visible command', visible_focus() and
                      page.locator('.ribbon-flyout').evaluate('(node)=>node.contains(document.activeElement)'))
                page.keyboard.press('Home')
                expect(page.locator('.ribbon-flyout [data-command="LINE"]')).to_be_focused()
                page.keyboard.press('Escape')
                expect(page.locator('.ribbon-flyout')).to_have_count(0)
                expect(launcher).to_be_focused()
            else:
                expect(page.locator('#ribbon [data-command="LINE"]')).to_be_focused()
                page.keyboard.press('ArrowRight')
                expect(page.locator('#ribbon [data-command="PLINE"]')).to_be_focused()
                page.keyboard.press('Home')
            page.keyboard.press('Escape')
            expect(home).to_be_focused()
            check(f'{mode} ribbon Escape returns to its selected tab without dispatching a command',
                  page.locator('#prompt').inner_text() == 'Command:')
        page.screenshot(path=str(output / 'ribbon-keyboard-tabs.png'))

        ribbon_mode(page, 'Full ribbon', 'full')
        home.press('ArrowDown')
        settled(page)
        line = page.locator('#ribbon [data-command="LINE"]')
        expect(line).to_be_focused()
        line.press('Enter')
        expect(page.locator('#prompt')).to_contain_text('LINE')
        cancel_command()
        check('keyboard command activation reaches the existing native command controller')

        # Native controls remain independent Tab stops; use a no-op key to avoid a drawing edit.
        layers = page.locator('#ribbon #current-layer')
        if not layers.is_visible():
            page.locator('#ribbon [data-panel="Layers"] .ribbon-launcher').press('ArrowDown')
            layers = page.locator('.ribbon-flyout #current-layer')
        layers.focus()
        expect(layers).to_be_focused()
        check('native current-layer selector keeps keyboard ownership and its ordinary Tab stop',
              layers.evaluate('(node)=>node.tabIndex===0'))
        # Focus elsewhere closes an open flyout without changing the destination.
        home.focus()
        expect(page.locator('.ribbon-flyout')).to_have_count(0)

        quick = page.get_by_role('toolbar', name='Quick Access', exact=True)
        quick.locator('button:enabled').first.focus()
        page.keyboard.press('End')
        expect(quick.get_by_role('button', name='Customize Quick Access', exact=True)).to_be_focused()
        page.keyboard.press('ArrowLeft')
        search_trigger = quick.get_by_role('button', name='Search commands (Ctrl+K)', exact=True)
        expect(search_trigger).to_be_focused()
        page.keyboard.press('Enter')
        search = page.get_by_role('dialog', name='Search commands', exact=True)
        query = search.get_by_role('searchbox', name='Search commands', exact=True)
        query.fill('zoom')
        query.press('ArrowDown')
        expect(search.get_by_role('button', name='Zoom extents', exact=True)).to_be_focused()
        page.keyboard.press('End')
        expect(search.get_by_role('button', name='Zoom out', exact=True)).to_be_focused()
        page.keyboard.press('Home')
        expect(search.get_by_role('button', name='Zoom extents', exact=True)).to_be_focused()
        page.keyboard.press('Escape')
        expect(search_trigger).to_be_focused()
        page.keyboard.press('Tab')
        check('Quick Access and search share keyboard navigation, Escape restores its trigger, and Tab exits',
              quick.evaluate('(node)=>!node.contains(document.activeElement)'))

        display = page.get_by_role('button', name='Ribbon display options', exact=True)
        display.focus()
        display.press('Enter')
        expect(display).to_have_attribute('aria-expanded', 'true')
        page.keyboard.press('End')
        expect(page.get_by_role('menuitemradio', name='Tabs only', exact=True)).to_be_focused()
        page.keyboard.press('Home')
        expect(page.get_by_role('menuitemradio', name='Full ribbon', exact=True)).to_be_focused()
        page.keyboard.press('Escape')
        expect(display).to_be_focused()
        expect(display).to_have_attribute('aria-expanded', 'false')
        check('ribbon display menu supports Home/End/Escape and exposes its expanded state')
        viewport = page.locator('#viewport')
        viewport.click(button='right', position={'x': 70, 'y': 80})
        expect(page.locator('#context-menu')).to_be_visible()
        page.keyboard.press('End')
        expect(page.get_by_role('menuitem', name='Select all', exact=True)).to_be_focused()
        page.keyboard.press('Escape')
        expect(viewport).to_be_focused()
        check('drawing context menu navigation restores the actual focusable viewport')
        viewport.press('Shift+F10')
        expect(page.locator('#context-menu')).to_be_visible()
        page.keyboard.press('Escape')
        expect(viewport).to_be_focused()
        check('Shift+F10 opens drawing actions from the keyboard and Escape returns to the viewport')

        page.set_viewport_size({'width': 390, 'height': 844})
        settled(page)
        for label, mode in modes:
            ribbon_mode(page, label, mode)
            home.click()
            page.mouse.move(370, 750)
            home.press('ArrowDown')
            settled(page)
            page.keyboard.press('End')
            check(f'compact {mode} ribbon scrolls keyboard focus into the viewport', visible_focus())
            page.keyboard.press('Escape')
            expect(home).to_be_focused()
        page.screenshot(path=str(output / 'ribbon-keyboard-compact.png'))
        page.set_viewport_size({'width': 1600, 'height': 1000})
        ribbon_mode(page, 'Full ribbon', 'full')

        prefs = {'version': 1, 'tabs': [{'id': 'Graph', 'visible': False}],
                 'customTabs': [{'id': 'user:keyboard', 'name': 'Keyboard tools'}],
                 'customPanels': [{'id': 'user:keyboard-tools', 'tab': 'user:keyboard', 'name': 'Keyboard commands',
                                   'commands': ['action:block-save', 'command:LINE', 'command:CIRCLE']}],
                 'panels': [], 'floating': []}
        apply_preferences(json.dumps(prefs))
        expect(page.get_by_role('tab', name='Graph', exact=True)).to_have_count(0)
        custom_tab = page.get_by_role('tab', name='Keyboard tools', exact=True)
        home.focus()
        home.press('End')
        expect(custom_tab).to_be_focused()
        custom_tab.press('ArrowDown')
        settled(page)
        custom_line = page.locator('#ribbon [data-ribbon-command-id="command:LINE"]')
        expect(custom_line).to_be_focused()
        expect(page.locator('#ribbon [data-ribbon-command-id="action:block-save"]')).to_be_disabled()
        check('custom and hidden tabs keep roving navigation scoped to registered available commands')

        ribbon_mode(page, 'Panel buttons', 'buttons')
        custom_tab.press('ArrowDown')
        settled(page)
        launcher = page.locator('#ribbon [data-panel="Keyboard commands"] .ribbon-launcher')
        expect(launcher).to_be_focused()
        launcher.press('Alt+f')
        floating = page.get_by_role('region', name='Keyboard commands floating ribbon panel', exact=True)
        expect(floating).to_be_visible()
        grip = floating.get_by_role('button', name='Move floating Keyboard commands panel', exact=True)
        expect(grip).to_be_focused()
        page.keyboard.press('Tab')
        expect(floating.get_by_role('button', name='Toggle Keyboard commands panel orientation', exact=True)).to_be_focused()
        page.keyboard.press('Tab')
        expect(floating.get_by_role('button', name='Return Keyboard commands panel to ribbon', exact=True)).to_be_focused()
        page.keyboard.press('Tab')
        expect(floating.locator('[data-ribbon-command-id="command:LINE"]')).to_be_focused()
        page.keyboard.press('End')
        expect(floating.locator('[data-ribbon-command-id="command:CIRCLE"]')).to_be_focused()
        page.keyboard.press('Escape')
        expect(grip).to_be_focused()
        page.keyboard.press('Escape')
        expect(custom_tab).to_be_focused()
        floating.get_by_role('button', name='Return Keyboard commands panel to ribbon', exact=True).focus()
        page.keyboard.press('Escape')
        expect(custom_tab).to_be_focused()
        check('scoped Alt+F and floating panel controls support keyboard entry, commands, and two-step focus return')
        page.set_viewport_size({'width': 390, 'height': 844})
        settled(page)
        floating.locator('[data-ribbon-command-id="command:LINE"]').focus()
        page.keyboard.press('End')
        check('floating command navigation stays visible at compact viewport bounds', visible_focus())
        page.screenshot(path=str(output / 'ribbon-keyboard-floating-compact.png'))
        floating.get_by_role('button', name='Return Keyboard commands panel to ribbon', exact=True).press('Enter')
        expect(floating).to_have_count(0)
        expect(launcher).to_be_focused()
        page.set_viewport_size({'width': 1600, 'height': 1000})
        settled(page)

        # Existing definitions exercise true contextual scopes without creating geometry or history.
        block = next((value['name'] for value in state().get('blocks', [])
                      if re.fullmatch(r'[A-Za-z][A-Za-z0-9_]*', value['name'])), None)
        if block:
            ribbon_mode(page, 'Full ribbon', 'full')
            command('BEDIT ' + block, False)
            wait_state(lambda value: value.get('blockEditor') is not None)
            idle()
            context = page.get_by_role('tab', name='Block Editor', exact=True)
            context.focus()
            context.press('End')
            check('contextual tab navigation skips unavailable discipline tabs', page.locator('#ribbon-tabs').evaluate(
                """node=>{const choices=[...node.querySelectorAll('button')].filter(button=>!button.disabled);
                  return choices.at(-1)===document.activeElement && [...node.querySelectorAll('[data-tab^="module:"]')].every(button=>button.disabled);}"""))
            context.click()
            context.press('ArrowDown')
            settled(page)
            check('Block Editor protected controls remain keyboard reachable', visible_focus() and
                  page.locator('#ribbon [data-panel="Definition"]').evaluate('(node)=>node.contains(document.activeElement)'))
            command('BCLOSE', False)
            wait_state(lambda value: value.get('blockEditor') is None)
            idle()
            expect(context).to_have_count(0)
        check('ribbon keyboard navigation and command cancellation preserve drawing identity and history', drawing_identity(state()) == baseline)
    finally:
        if modal.is_visible():
            page.keyboard.press('Escape')
            expect(modal).not_to_be_visible()
        cancel_command()
        if state().get('blockEditor') is not None:
            command('BCLOSE', False)
            wait_state(lambda value: value.get('blockEditor') is None)
        page.set_viewport_size(original_viewport)
        apply_preferences(original_preferences)
        label = next(label for label, mode in modes if mode == original_mode)
        ribbon_mode(page, label, original_mode)
        page.evaluate("key=>document.querySelectorAll('#ribbon-tabs [data-tab]').forEach(node=>{if(node.dataset.tab===key&&!node.disabled)node.click();})", original_tab)
        idle()
