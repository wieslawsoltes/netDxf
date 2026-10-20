"""Shared real-browser checks for palette transparency and modal isolation."""
from playwright.sync_api import expect
from workspace_layout_browser_checks import drawing_identity, settled


def verify_palette_appearance(page, output, state, command, wait_state, idle, check):
    baseline = drawing_identity(state())
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    page.evaluate("shadowStudioAgentUi.execute('palette.show', {name:'Graph'})")
    graph = page.locator('[data-palette="Graph"]')
    modal = page.get_by_role('dialog', name='Graph transparency', exact=True)

    def preferences():
        return page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-palettes-v2'))")

    def menu():
        graph.get_by_label('Graph palette layout', exact=True).click()
        return graph.locator('.pd-menu-body')

    def appearance():
        menu().get_by_role('button', name='Transparency: Graph', exact=True).click()
        expect(modal).to_be_visible()

    def resting():
        page.locator('#viewport').focus()
        page.mouse.move(5, 5)
        settled(page)

    menu().get_by_role('button', name='Float palette: Graph', exact=True).click()
    saved = preferences()
    appearance()
    opacity = modal.get_by_role('slider', name='Palette opacity', exact=True)
    opacity.press('Home')
    for _ in range(4):
        opacity.press('PageUp')
    expect(opacity).to_have_value('40')
    rollover = modal.get_by_role('slider', name='Rollover opacity', exact=True)
    rollover.press('End')
    for _ in range(20):
        rollover.press('ArrowLeft')
    expect(rollover).to_have_value('80')
    expect(modal.locator('.pd-opacity-sample')).to_have_css('opacity', '0.4')
    preview = modal.get_by_role('button', name='Hold to preview rollover', exact=True)
    preview.focus()
    page.keyboard.down('Space')
    expect(modal.locator('.pd-opacity-sample')).to_have_css('opacity', '0.8')
    page.keyboard.up('Space')
    expect(modal.locator('.pd-opacity-sample')).to_have_css('opacity', '0.4')
    page.keyboard.press('Control+9')
    expect(page.locator('#command-line')).to_be_visible()
    page.keyboard.press('Escape')
    expect(modal).to_have_count(0)
    check('transparency draft and rollover preview cancel without changing preferences or drawing shortcuts',
          preferences() == saved)

    appearance()
    opacity.press('Home')
    for _ in range(4):
        opacity.press('PageUp')
    rollover.press('End')
    for _ in range(20):
        rollover.press('ArrowLeft')
    modal.get_by_role('checkbox', name='Apply transparency to all palettes', exact=True).check()
    modal.get_by_role('button', name='Apply transparency', exact=True).click()
    resting()
    expect(graph).to_have_css('opacity', '0.4')
    graph.locator('.palette-content').hover()
    expect(graph).to_have_css('opacity', '0.8')
    graph.get_by_role('combobox', name='Palette shown in Graph', exact=True).focus()
    expect(graph).to_have_css('opacity', '1')
    check('floating opacity responds to hover and keyboard focus; all-palettes settings persist',
          all(panel['opacity'] == 40 and panel['rolloverOpacity'] == 80
              for panel in preferences()['panels'].values()))
    page.screenshot(path=str(output / 'palette-transparency.png'))

    menu().get_by_role('button', name='Dock right: Graph', exact=True).click()
    resting()
    expect(graph).to_have_css('opacity', '1')
    check('docked palettes remain opaque without discarding floating opacity settings',
          preferences()['panels']['Graph']['opacity'] == 40)
    menu().get_by_role('button', name='Auto-hide right: Graph', exact=True).click()
    page.get_by_role('button', name='Show Graph palette', exact=True).click()
    appearance()
    # Let the real anchor's delayed auto-hide run while focus belongs to the modal.
    page.wait_for_timeout(400)
    expect(graph).to_be_visible()
    modal.get_by_role('slider', name='Palette opacity', exact=True).press('ArrowLeft')
    page.keyboard.press('Escape')
    expect(graph.get_by_label('Graph palette layout', exact=True)).to_be_focused()
    check('anchored palette stays available during appearance editing and Escape restores its layout-menu focus')
    menu().get_by_role('button', name='Float palette: Graph', exact=True).click()
    page.get_by_label('Drawing palettes menu', exact=True).click()
    page.locator('.pd-launcher').get_by_role('checkbox', name='Disable all palette transparency', exact=True).check()
    page.get_by_label('Drawing palettes menu', exact=True).click()
    resting()
    expect(graph).to_have_css('opacity', '1')
    check('global transparency override restores visibility independently of floating placement',
          preferences()['transparencyDisabled'])
    page.evaluate("shadowStudioAgentUi.execute('ui.action', {name:'palette-reset'})")
    idle()
    check('palette appearance changes preserve drawing revision, source identity and undo history',
          drawing_identity(state()) == baseline)
