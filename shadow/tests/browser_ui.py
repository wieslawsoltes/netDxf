"""Interactions with the responsive ribbon shared by real-browser CAD suites."""
from playwright.sync_api import expect


def ribbon_item(page, selector: str, tab: str, panel: str):
    """Open the actual visible group when a responsive ribbon collapses its tools."""
    page.get_by_role('tab', name=tab, exact=True).click()
    # Switching tabs schedules responsive measurement on the next frame. A tool
    # can be momentarily visible before its panel collapses into the launcher.
    page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
    target = page.locator(selector + ':visible').first
    if not target.is_visible():
        launcher = page.locator(f'#ribbon [data-panel="{panel}"] .ribbon-launcher')
        expect(launcher).to_be_visible()
        launcher.click()
    expect(target).to_be_visible()
    return target


def ribbon_action(page, action: str, tab: str, panel: str):
    ribbon_item(page, f'[data-action="{action}"]', tab, panel).click()
