"""Shared native/WASM ribbon preferences and live tear-off controls; no provider calls."""
import json
from playwright.sync_api import expect
from workspace_layout_browser_checks import drawing_identity, ribbon_mode, settled


def verify_ribbon_customization(page, output, state, command, wait_state, idle, check):
    baseline = drawing_identity(state())
    original_viewport = page.viewport_size
    modal = page.locator('#dialog')

    # Real HTMLDialogElement queues close events after close() clears `open`.
    # Reopen in the same JS turn, without adding timing waits to the CUI workflow.
    lifecycle = page.evaluate("""async () => {
      const {createDialogLifecycle} = await import('/cad/dialog-lifecycle.mjs');
      const element = document.createElement('dialog'); document.body.append(element);
      const show = createDialogLifecycle(element), prepared = [];
      try {
        const first = show(() => prepared.push('first'));
        element.close('ok');
        let secondSettled = false;
        const second = show(() => prepared.push('second')).then(value => {secondSettled=true; return value;});
        const before = {prepared:[...prepared], value:element.returnValue};
        const firstValue = await first;
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        const during = {prepared:[...prepared], open:element.open, secondSettled};
        element.close('cancel');
        const secondValue = await second;
        return {before, during, firstValue, secondValue};
      } finally {element.close(); element.remove();}
    }""")
    check('queued native dialog close cannot settle or overwrite a rapidly reopened dialog',
          lifecycle == {'before': {'prepared': ['first'], 'value': 'ok'},
                        'during': {'prepared': ['first', 'second'], 'open': True, 'secondSettled': False},
                        'firstValue': 'ok', 'secondValue': 'cancel'})

    def preferences():
        return page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-ribbon-v1'))")

    def customize():
        page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ribbon-customize'})")
        expect(page.locator('#dialog-title')).to_have_text('Customize ribbon')

    def apply():
        page.locator('#dialog-submit').click()
        expect(modal).not_to_be_visible()
        settled(page)

    def reset():
        customize()
        modal.get_by_role('button', name='Reset ribbon draft', exact=True).click()
        apply()

    reset()
    ribbon_mode(page, 'Full ribbon', 'full')
    customize()
    tabs = modal.get_by_role('combobox', name='Customize ribbon tab', exact=True)
    tabs.select_option('Graph')
    modal.get_by_label('Show ribbon tab', exact=True).uncheck()
    modal.get_by_role('button', name='Move tab earlier', exact=True).click()
    tabs.select_option('Home')
    expect(modal.get_by_label('Show ribbon tab', exact=True)).to_be_disabled()
    modal.get_by_role('combobox', name='Customize ribbon panel', exact=True).select_option('Modify')
    modal.get_by_label('Show ribbon panel', exact=True).uncheck()
    page.keyboard.press('Escape')
    expect(modal).not_to_be_visible()
    expect(page.get_by_role('tab', name='Graph', exact=True)).to_be_visible()
    check('Cancel discards ribbon visibility and ordering drafts', preferences()['tabs'] == [])

    customize()
    tabs.select_option('Graph')
    modal.get_by_label('Show ribbon tab', exact=True).uncheck()
    modal.get_by_role('button', name='Move tab earlier', exact=True).click()
    tabs.select_option('Home')
    modal.get_by_role('combobox', name='Customize ribbon panel', exact=True).select_option('Modify')
    modal.get_by_label('Show ribbon panel', exact=True).uncheck()
    apply()
    expect(page.get_by_role('tab', name='Graph', exact=True)).to_have_count(0)
    page.get_by_role('tab', name='Home', exact=True).click()
    expect(page.locator('#ribbon [data-panel="Modify"]')).to_have_count(0)
    check('Apply persists hidden tabs and per-tab panel visibility without editing the drawing')

    customize()
    tabs.select_option('Graph')
    modal.get_by_label('Show ribbon tab', exact=True).check()
    modal.get_by_role('textbox', name='Custom tab name', exact=True).fill('Browser tools')
    modal.get_by_role('button', name='Add custom tab', exact=True).click()
    custom_tab = tabs.input_value()
    modal.get_by_role('textbox', name='Custom panel name', exact=True).fill('Review tools')
    modal.get_by_role('button', name='Add custom panel', exact=True).click()
    custom_panel = modal.get_by_role('combobox', name='Customize ribbon panel', exact=True).input_value()
    for identifier in ('action:graph-query:spanning_forest', 'action:block-save'):
        modal.get_by_role('combobox', name='Registered ribbon command', exact=True).select_option(identifier)
        modal.get_by_role('button', name='Add panel command', exact=True).click()
    modal.get_by_role('combobox', name='Custom panel commands', exact=True).select_option('1')
    modal.get_by_role('button', name='Move command earlier', exact=True).click()
    with page.expect_download() as download:
        modal.get_by_role('button', name='Export ribbon JSON', exact=True).click()
    exported_path = output / 'ribbon-customization.json'
    download.value.save_as(exported_path)
    exported = json.loads(exported_path.read_text())
    apply()
    check('custom ribbon tab and panel persist ordered registered command identities',
          preferences()['customPanels'][0]['commands'] == ['action:block-save', 'action:graph-query:spanning_forest'])
    page.get_by_role('tab', name='Browser tools', exact=True).click()
    custom = page.locator(f'#ribbon [data-ribbon-panel="{custom_panel}"]')
    expect(custom).to_be_visible()
    expect(custom.locator('[data-ribbon-command-id="action:block-save"]')).to_be_disabled()
    custom.locator('[data-ribbon-command-id="action:graph-query:spanning_forest"]').click()
    expect(page.get_by_label('Graph analysis operation', exact=True)).to_have_value('spanning_forest')
    check('custom commands keep context restrictions and invoke the real graph workflow')
    page.screenshot(path=str(output / 'ribbon-custom-panel.png'))

    customize()
    modal.get_by_label('Import ribbon customization', exact=True).set_input_files({
        'name': 'ribbon.json', 'mimeType': 'application/json', 'buffer': json.dumps(exported).encode()})
    expect(modal.get_by_role('status')).to_contain_text('Imported into the draft')
    apply()
    check('bounded portable JSON roundtrips declarative ribbon preferences', preferences() == exported)

    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ui-workspaces'})")
    modal.get_by_role('textbox', name='Workspace name', exact=True).fill('Ribbon browser layout')
    modal.get_by_role('button', name='Save current as', exact=True).click()
    page.locator('#dialog-submit').click()
    reset()
    expect(page.get_by_role('tab', name='Browser tools', exact=True)).to_have_count(0)
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'ui-workspaces'})")
    modal.get_by_role('combobox', name='UI workspace', exact=True).select_option(label='Ribbon browser layout')
    modal.get_by_role('button', name='Apply layout', exact=True).click()
    modal.get_by_role('button', name='Delete workspace', exact=True).click()
    page.locator('#dialog-submit').click()
    expect(page.get_by_role('tab', name='Browser tools', exact=True)).to_be_visible()
    check('named UI workspace restores custom tab definitions and panel command ordering',
          preferences()['customTabs'][0]['id'] == custom_tab)
    reset()

    # Real built-in controls move intact; switching tabs keeps the window present.
    page.get_by_role('tab', name='Home', exact=True).click()
    draw = page.locator('#ribbon [data-panel="Draw"]')
    draw.get_by_role('button', name='Float Draw panel', exact=True).click()
    floating = page.get_by_role('region', name='Draw floating ribbon panel', exact=True)
    expect(floating).to_be_visible()
    page.get_by_role('tab', name='Graph', exact=True).click()
    expect(floating).to_be_visible()
    floating.locator('[data-command="CIRCLE"]').click()
    expect(page.locator('#prompt')).to_contain_text('CIRCLE')
    page.evaluate("shadowStudioAgentUi.execute('command.cancel', {clearSelection:false})")
    floating.get_by_role('button', name='Return Draw panel to ribbon', exact=True).click()
    expect(floating).to_have_count(0)
    page.get_by_role('tab', name='Home', exact=True).click()
    expect(draw).to_be_visible()
    check('floating panel survives tab changes and its original command handler remains live')

    title = draw.locator('.ribbon-group-title')
    box = title.bounding_box()
    start = (box['x'] + 24, box['y'] + box['height'] / 2)
    before = preferences()
    page.mouse.move(*start)
    page.mouse.down()
    page.mouse.move(start[0] + 60, start[1] + 180, steps=5)
    expect(floating).to_be_visible()
    page.keyboard.press('Escape')
    page.mouse.up()
    expect(floating).to_have_count(0)
    check('Escape cancels ribbon tear-off and preserves the exact saved layout', preferences() == before)
    page.mouse.move(*start)
    page.mouse.down()
    page.mouse.move(start[0] + 60, start[1] + 180, steps=5)
    page.mouse.up()
    expect(floating).to_be_visible()
    grip = floating.get_by_role('button', name='Move floating Draw panel', exact=True)
    grip.focus()
    before_x = preferences()['floating'][0]['x']
    page.keyboard.press('Shift+ArrowRight')
    settled(page)
    check('floating ribbon title supports precise keyboard movement',
          abs(preferences()['floating'][0]['x'] - before_x - 1) < 0.1)
    expect(grip).to_be_focused()
    page.screenshot(path=str(output / 'ribbon-floating.png'))
    page.set_viewport_size({'width': 390, 'height': 844})
    settled(page)
    box = floating.bounding_box()
    check('compact tear-off stays inside the viewport with an accessible return control',
          box['x'] >= 0 and box['y'] >= 0 and box['x'] + box['width'] <= 390.5 and box['y'] + box['height'] <= 844.5)
    expect(floating.get_by_role('button', name='Return Draw panel to ribbon', exact=True)).to_be_visible()
    page.screenshot(path=str(output / 'ribbon-floating-compact.png'))
    floating.get_by_role('button', name='Return Draw panel to ribbon', exact=True).click()
    page.set_viewport_size(original_viewport)
    settled(page)

    # A real module toggle must suspend its floated controls, then recover them.
    modules = state()['modules']
    module = next(value for value in modules if value['enabled'])
    page.evaluate("name => shadowStudioAgentUi.execute('ribbon.show', {name})", 'module:' + module['id'])
    page.locator('#ribbon [data-panel="Discipline"]').get_by_role('button', name='Float Discipline panel', exact=True).click()
    discipline = page.get_by_role('region', name='Discipline floating ribbon panel', exact=True)
    expect(discipline).to_be_visible()
    page.evaluate("module => shadowStudioAgentUi.execute('cad.edit', {path:'modules/toggle',body:{module,enabled:false}})", module['id'])
    wait_state(lambda value: not next(item for item in value['modules'] if item['id'] == module['id'])['enabled'])
    idle()
    expect(discipline).to_have_count(0)
    check('disabled module floats disappear without deleting their saved position',
          any(item['tab'] == 'module:' + module['id'] for item in preferences()['floating']))
    page.evaluate("module => shadowStudioAgentUi.execute('cad.edit', {path:'modules/toggle',body:{module,enabled:true}})", module['id'])
    wait_state(lambda value: next(item for item in value['modules'] if item['id'] == module['id'])['enabled'])
    idle()
    expect(discipline).to_be_visible()
    discipline.get_by_role('button', name='Return Discipline panel to ribbon', exact=True).click()
    reset()
    page.get_by_role('tab', name='Home', exact=True).click()
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    idle()
    check('ribbon customization, tear-offs and resets preserve source geometry and undo history',
          drawing_identity(state()) == baseline)
