"""Shared native/WASM checks for manual workspace saving and WSAUTOSAVE switching."""
from playwright.sync_api import expect
from workspace_layout_browser_checks import drawing_identity, ribbon_mode, settled
from browser_ui import wait_for_ui

EXPECTED_WORKSPACE_STORAGE_ERROR = (
    'Could not automatically save workspace “Autosave browser review”. '
    'UI workspace could not be saved. Browser storage is unavailable or full. '
    'Free browser storage or turn off automatic workspace saving before switching.'
)


def verify_workspace_autosave(page, output, state, command, wait_state, idle, check):
    baseline = drawing_identity(state())
    name = 'Autosave browser review'
    popup = page.locator('#shell-popup')

    def prefs():
        return page.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-shell-v1'))")

    def saved():
        return page.evaluate("""name => JSON.parse(localStorage.getItem('netdxf-cad-workspaces-v1'))
          .profiles.find(entry => entry.name === name).layout""", name)

    def workspace(label):
        page.locator('#workspace-switch').click()
        popup.get_by_role('menuitemradio', name=label, exact=True).click()
        settled(page)

    def auto_save(value):
        command(f'WSAUTOSAVE {value}', False)
        wait_for_ui(page, """value => JSON.parse(localStorage.getItem('netdxf-cad-shell-v1'))
          .workspaceAutoSave === !!value""", arg=value)

    def manager():
        page.locator('.document-bar [data-action="ui-workspaces"]').click()
        expect(page.locator('#dialog-title')).to_have_text('UI workspaces')
        return page.get_by_role('combobox', name='UI workspace', exact=True)

    def close_manager():
        page.locator('#dialog-submit').click()
        expect(page.locator('#dialog')).not_to_be_visible()

    try:
        auto_save(0)
        workspace('Drafting & Annotation')
        manager()
        page.get_by_role('textbox', name='Workspace name', exact=True).fill(name)
        page.get_by_role('button', name='Save current as', exact=True).click()
        close_manager()
        expect(page.locator('#workspace-name')).to_have_text(name)
        original = saved()

        page.get_by_role('tab', name='View', exact=True).click()
        workspace('Graph analysis')
        check('manual workspace switching retains the explicitly saved snapshot', saved() == original)
        workspace(name)
        expect(page.get_by_role('tab', name='Home', exact=True)).to_have_attribute('aria-selected', 'true')

        auto_save(1)
        page.get_by_role('tab', name='View', exact=True).click()
        ribbon_mode(page, 'Panel titles', 'titles')
        workspace(name)
        expect(page.get_by_role('tab', name='View', exact=True)).to_have_attribute('aria-selected', 'true')
        expect(page.locator('#ribbon')).to_have_attribute('data-mode', 'titles')
        check('reselecting the active workspace with autosave on preserves its live layout', saved() == original)

        workspace('Drafting & Annotation')
        updated = saved()
        check('switching from a saved workspace automatically saves its tab and ribbon state',
              updated['tab'] == 'View' and updated['chrome']['ribbonMode'] == 'titles')
        check('automatic workspace saving remains enabled after applying a built-in preset',
              prefs()['workspaceAutoSave'] is True)
        workspace(name)
        expect(page.get_by_role('tab', name='View', exact=True)).to_have_attribute('aria-selected', 'true')
        expect(page.locator('#ribbon')).to_have_attribute('data-mode', 'titles')

        page.get_by_role('tab', name='Manage', exact=True).click()
        page.locator('#workspace-switch').click()
        popup.get_by_role('menuitem', name='Reset current workspace', exact=True).click()
        expect(page.get_by_role('tab', name='View', exact=True)).to_have_attribute('aria-selected', 'true')
        check('explicit workspace reset restores its snapshot without overwriting it through autosave',
              saved() == updated and prefs()['workspaceAutoSave'] is True)

        page.get_by_role('tab', name='Manage', exact=True).click()
        page.evaluate("""() => {
          const original = Storage.prototype.setItem;
          window.__restoreWorkspaceStorage = () => { Storage.prototype.setItem = original; };
          Storage.prototype.setItem = function(key, value) {
            if (key === 'netdxf-cad-workspaces-v1') throw new DOMException('Storage quota reached', 'QuotaExceededError');
            return original.call(this, key, value);
          };
        }""")
        workspace('Drafting & Annotation')
        expect(page.locator('#toast')).to_have_text(EXPECTED_WORKSPACE_STORAGE_ERROR)
        quota_error = page.locator('#history .error').filter(has_text=EXPECTED_WORKSPACE_STORAGE_ERROR)
        expect(quota_error).to_have_count(1)
        expect(quota_error).to_have_text(EXPECTED_WORKSPACE_STORAGE_ERROR)
        quota_error.evaluate("node => { node.dataset.testExpectedError = 'workspace-autosave-quota'; }")
        expect(page.locator('#workspace-name')).to_have_text(name)
        expect(page.get_by_role('tab', name='Manage', exact=True)).to_have_attribute('aria-selected', 'true')
        check('storage failure aborts workspace switching and preserves the live layout', saved() == updated)
        page.evaluate('() => { window.__restoreWorkspaceStorage(); delete window.__restoreWorkspaceStorage; }')
        check('workspace autosave and reset never transact on the drawing', drawing_identity(state()) == baseline)
    finally:
        page.evaluate('() => { window.__restoreWorkspaceStorage?.(); delete window.__restoreWorkspaceStorage; }')
        if page.locator('#dialog').is_visible():
            close_manager()
        page.keyboard.press('Escape')
        auto_save(0)
        select = manager()
        options = select.locator('option').all_text_contents()
        if name in options:
            select.select_option(label=name)
            page.get_by_role('button', name='Delete workspace', exact=True).click()
        close_manager()
        workspace('Drafting & Annotation')
        idle()
    verify_ribbon_tab_reload(page, check)


def verify_ribbon_tab_reload(page, check):
    """Reload an independent context so the main WASM drawing is never reinitialized."""
    context = page.context.browser.new_context(viewport=page.viewport_size)
    restored = context.new_page()
    errors = []
    restored.on('pageerror', lambda error: errors.append(str(error)))
    restored.on('dialog', lambda dialog: dialog.accept() if dialog.type == 'beforeunload' else dialog.dismiss())
    try:
        response = restored.goto(page.url, wait_until='domcontentloaded')
        assert response and response.ok
        expect(restored.locator('#connection')).to_have_text('Local', timeout=180000)
        wait_for_ui(restored, """() => !!document.querySelector(
          '#ribbon-tabs [data-tab^="module:"]:not([disabled])')""", timeout=180000)
        tab = restored.locator('#ribbon-tabs [data-tab^="module:"]:not([disabled])').first
        key = tab.get_attribute('data-tab')
        tab.click()
        expect(tab).to_have_attribute('aria-selected', 'true')
        assert restored.evaluate("JSON.parse(localStorage.getItem('netdxf-cad-ui-v1')).ribbonTab") == key
        restored.reload(wait_until='domcontentloaded')
        expect(restored.locator('#connection')).to_have_text('Local', timeout=180000)
        wait_for_ui(restored, """key => document.querySelector(
          '#ribbon-tabs [aria-selected="true"]')?.dataset.tab === key""", arg=key, timeout=180000)
        check('an enabled discipline ribbon tab survives reload after its document modules become available', not errors)
    finally:
        context.close()
