"""Real native/WASM overlap selection, temporary cycling and command acquisition."""
import ezdxf
from playwright.sync_api import expect
from browser_ui import wait_for_ui
from workspace_layout_browser_checks import settled


def verify_selection_cycling(page, output, state, command, wait_state, idle, check):
    previous = page.locator('#studio').get_attribute('data-document')
    original_viewport = page.viewport_size
    original_settings = page.evaluate('shadowStudioAgentUi.snapshot().settings')
    fixture = output / 'selection-cycling.dxf'
    drawing = ezdxf.new('R2018')
    model = drawing.modelspace()
    model.add_line((0, 0, 0), (100, 0, 0), dxfattribs={'color': 1})
    model.add_line((0, 0, 10), (100, 0, 10), dxfattribs={'color': 3})
    model.add_line((0, 60), (100, 60), dxfattribs={'color': 5})
    block = drawing.blocks.new('CycleRoot')
    block.add_line((0, 0), (100, 0))
    block.add_line((0, 0), (100, 0))
    model.add_blockref('CycleRoot', (0, 30))
    drawing.saveas(fixture)
    if page.locator('#documents .document-tab').count() >= 8:
        saved = next((tab for tab in page.locator('#documents .document-tab:not(.active)').all()
                      if not tab.locator('.name').inner_text().startswith('● ')), None)
        assert saved is not None, 'The private browser suite needs one saved document slot.'
        saved.locator('.close').click()
        expect(page.locator('#documents .document-tab')).to_have_count(7)
        idle()
        page.evaluate("id=>shadowStudioAgentUi.execute('document.activate',{id})", previous)
        expect(page.locator('#studio')).to_have_attribute('data-document', previous)
        idle()
    page.locator('#open-file').set_input_files(str(fixture))
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()

    def execute(tool, args):
        return page.evaluate('async ([tool,args]) => await shadowStudioAgentUi.execute(tool,args)', [tool, args])

    def settings(**values):
        execute('settings.set', values)

    def position(x=30, y=0):
        camera = page.evaluate('shadowStudioAgentUi.snapshot().camera')
        box = page.locator('#drawing').bounding_box()
        return (box['x'] + box['width'] / 2 + (x - camera['x']) * camera['scale'],
                box['y'] + box['height'] / 2 - (y - camera['y']) * camera['scale'])

    def click(x=30, y=0):
        page.mouse.click(*position(x, y))

    def clear():
        page.locator('#viewport').focus()
        page.keyboard.press('Escape')
        idle()

    chooser = page.get_by_role('dialog', name='Overlapping objects', exact=True)
    options = chooser.get_by_role('option')
    before = state()['state']
    try:
        settings(selectionCycling=True, osnap=False, snap=False, ortho=False, polar=False)
        execute('ui.action', {'name': 'fit'})
        idle()
        click()
        expect(chooser).to_be_visible()
        expect(options).to_have_count(2)
        check('overlap picker finds projected coincident lines at different elevations', not state()['selectedEntities'])
        page.keyboard.press('ArrowDown')
        expect(options.nth(1)).to_have_attribute('aria-selected', 'true')
        check('cycling preview is transient and leaves source selection and history unchanged',
              not state()['selectedEntities'] and state()['state']['undoCount'] == before['undoCount'])
        page.screenshot(path=str(output / 'selection-cycle-chooser.png'))
        page.keyboard.press('Escape')
        expect(chooser).not_to_be_visible()
        expect(page.locator('#viewport')).to_be_focused()
        check('Escape cancels only the chooser and restores viewport focus', not state()['selectedEntities'])

        click()
        expect(chooser).to_be_visible()
        page.keyboard.press('Shift+Tab')
        expect(chooser.get_by_role('button', name='Cancel overlapping object selection', exact=True)).to_be_focused()
        page.keyboard.press('Enter')
        expect(chooser).not_to_be_visible()
        expect(page.locator('#viewport')).to_be_focused()
        check('keyboard Enter on the Cancel button closes the chooser without selecting', not state()['selectedEntities'])

        click()
        options.nth(1).click()
        wait_state(lambda value: len(value['selectedEntities']) == 1)
        first = state()['selectedEntities'][0]
        idle()
        click()
        options.nth(0).click()
        wait_state(lambda value: len(value['selectedEntities']) == 2)
        check('choosing an overlap adds the exact alternate root to the existing selection')
        idle()
        page.keyboard.down('Shift')
        click()
        page.keyboard.up('Shift')
        options.nth(1).click()
        wait_state(lambda value: len(value['selectedEntities']) == 1)
        check('Shift overlap choice removes only the requested source', first not in state()['selectedEntities'])
        clear()

        click(30, 30)
        wait_state(lambda value: len(value['selectedEntities']) == 1)
        expect(chooser).not_to_be_visible()
        check('multiple hit leaves of one INSERT select one INSERT root without a redundant chooser',
              state()['properties']['type'] == 'Insert')
        clear()
        click(30, 60)
        wait_state(lambda value: len(value['selectedEntities']) == 1)
        expect(chooser).not_to_be_visible()
        check('single candidates select immediately')
        # Empty clicks with the default additive setting retain an existing selection.
        selected = state()['selectedEntities']
        click(110, 40)
        idle()
        expect(chooser).not_to_be_visible()
        check('empty overlap query does not invent a candidate', state()['selectedEntities'] == selected)
        clear()

        settings(selectionCycling=False)
        page.locator('#viewport').focus()
        page.keyboard.down('Shift')
        page.keyboard.press('Space')
        click()
        expect(chooser).to_be_visible()
        expect(chooser.get_by_role('listbox')).not_to_be_visible()
        page.keyboard.up('Shift')
        click()
        page.keyboard.press('Enter')
        wait_state(lambda value: len(value['selectedEntities']) == 1)
        check('Shift+Space temporarily cycles while Shift is held without turning the pick into removal',
              page.evaluate('shadowStudioAgentUi.snapshot().settings.selectionCycling') is False)
        clear()

        settings(selectionCycling=True)
        command('BREAK', False)
        expect(page.locator('#prompt')).to_contain_text('Select')
        click(30, 0)
        expect(chooser).to_be_visible()
        options.nth(1).click()
        expect(page.locator('#prompt')).to_contain_text('second break point')
        check('BREAK consumes the overlap selection point as its first point')
        page.locator('#command-input').fill('F')
        page.locator('#command-input').press('Enter')
        expect(page.locator('#prompt')).to_contain_text('first break point')
        clear()

        # Hold a real response, change the drawing scope, then release that old response.
        page.evaluate("""() => {
          window.cycleFetch = window.fetch; window.cycleHeld = false;
          window.cycleGate = new Promise(resolve => window.releaseCycleRead = resolve);
          window.fetch = async function(...args) {
            const response = await window.cycleFetch.apply(this,args);
            if (String(args[0]?.url || args[0]).includes('/selection-candidates')) {
              window.cycleHeld = true; await window.cycleGate;
            }
            return response;
          };
        }""")
        click()
        wait_for_ui(page, '() => window.cycleHeld')
        execute('layout.set', {'name': 'Layout1'})
        page.evaluate('window.releaseCycleRead()')
        idle()
        expect(chooser).not_to_be_visible()
        check('a late candidate response cannot select after the active layout changes', not state()['selectedEntities'])
        page.evaluate('window.fetch=window.cycleFetch;delete window.cycleFetch;delete window.cycleGate;delete window.releaseCycleRead;delete window.cycleHeld')
        execute('layout.set', {'name': 'Model'})
        idle()
        page.set_viewport_size({'width': 390, 'height': 844})
        settled(page)
        execute('ui.action', {'name': 'fit'})
        idle()
        click()
        expect(chooser).to_be_visible()
        box, viewport = chooser.bounding_box(), page.locator('#viewport').bounding_box()
        check('compact overlap chooser stays inside the drawing viewport',
              box['x'] >= viewport['x'] and box['y'] >= viewport['y'] and
              box['x'] + box['width'] <= viewport['x'] + viewport['width'] + 1 and
              box['y'] + box['height'] <= viewport['y'] + viewport['height'] + 1)
        page.screenshot(path=str(output / 'selection-cycle-compact.png'))
        clear()
        check('selection cycling and cancelled BREAK preserve source revision and geometry undo history',
              state()['state']['revision'] == before['revision'] and state()['state']['undoCount'] == before['undoCount'])
    except BaseException:
        page.screenshot(path=str(output / 'selection-cycle-failure.png'))
        raise
    finally:
        page.evaluate("""() => { if(window.cycleFetch)window.fetch=window.cycleFetch;window.releaseCycleRead?.(); }""")
        page.set_viewport_size(original_viewport)
        clear()
        settings(**{key: original_settings[key] for key in ('selectionCycling', 'osnap', 'snap', 'ortho', 'polar') if key in original_settings})
        # Closing the disposable fixture avoids disturbing the caller's document or undo history.
        tab = page.locator('#documents .document-tab').filter(has=page.get_by_role('tab', name='selection-cycling.dxf', exact=True))
        if tab.count():
            tab.locator('.close').click()
            expect(tab).to_have_count(0)
            wait_for_ui(page, '() => !shadowStudioAgentUi.snapshot().busy')
            idle()
        execute('document.activate', {'id': previous})
        idle()
