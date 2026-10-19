"""Shared native/WASM command UX checks with independent saved-DXF geometry audit."""
import math
import time
import ezdxf
from playwright.sync_api import expect
from browser_ui import ribbon_item


def verify_modify(page, output, state, command, wait_state, idle, check):
    previous = page.locator('#studio').get_attribute('data-document')
    page.locator('[data-action="new"]').first.click()
    expect(page.locator('#studio')).not_to_have_attribute('data-document', previous)
    idle()
    def answer(text, prompt=None):
        idle()
        field = page.locator('#command-input')
        field.fill(text); field.press('Enter')
        if prompt:
            expect(page.locator('#prompt')).to_contain_text(prompt)
        idle()

    def line(y):
        revision = state()['state']['revision']
        command(f'L 0,{y} 10,{y}', False)
        answer('', 'Command:')
        wait_state(lambda data: data['state']['revision'] > revision)

    line(0)
    before = state()['state']['revision']
    ribbon_item(page, '[data-command="STRETCH"]', 'Home', 'Modify').click()
    expect(page.locator('#prompt')).to_contain_text('first crossing-window corner')

    answer('15,5', 'opposite crossing-window corner')
    answer('5,-5', 'Crossing selection')
    wait_state(lambda data: len(data['selectedEntities']) == 1)
    answer('', 'base point')
    answer('0,0', 'second point')
    answer('5,5', 'Command:')
    data = wait_state(lambda data: data['state']['revision'] > before)
    check('STRETCH ribbon crossing workflow moves only the enclosed line endpoint',
          data['itemCount'] == 1 and data['state']['bounds']['maxX'] == 15 and data['state']['bounds']['maxY'] == 5)
    command('U')
    check('STRETCH undo restores the original endpoint in one transaction', state()['state']['bounds']['maxX'] == 10)
    command('REDO')
    command('BR 3,1 6,2')
    check('BREAK preselection removes the requested interval', state()['itemCount'] == 2)
    line(20)
    ribbon_item(page, '[data-command="BREAKATPOINT"]', 'Home', 'Modify').click()
    expect(page.locator('#prompt')).to_contain_text('Specify break point')
    before = state()['state']['revision']
    answer('5,20', 'Command:'); wait_state(lambda data: data['state']['revision'] > before)
    check('BREAKATPOINT ribbon splits an open line without a gap', state()['itemCount'] == 4)
    line(40)
    command('BR 5,40 @')
    check('BREAK @ creates a gap-free split from the command line', state()['itemCount'] == 6)
    with page.expect_download() as download:
        page.locator('[data-action="save"]').first.click()
    file = output / 'modify-parity.dxf'
    download.value.save_as(str(file)); idle()
    drawing = ezdxf.readfile(file)
    audit = drawing.audit()
    lines = list(drawing.modelspace().query('LINE'))
    expected = [((0, 0), (3, 1)), ((6, 2), (15, 5)), ((0, 20), (5, 20)),
                ((5, 20), (10, 20)), ((0, 40), (5, 40)), ((5, 40), (10, 40))]
    actual = sorted(((line.dxf.start.x, line.dxf.start.y), (line.dxf.end.x, line.dxf.end.y)) for line in lines)
    check('independent DXF audit preserves exact STRETCH and BREAK endpoints without repairs',
          not audit.errors and not audit.fixes and len(actual) == len(expected) and
          all(math.isclose(a, b, abs_tol=1e-9) for pair, wanted in zip(actual, sorted(expected))
              for p, q in zip(pair, wanted) for a, b in zip(p, q)))

    # Hold the actual crossing response in both hosts (WASM uses the fetch
    # adapter, not a network route). Escape must stop queued inline coordinates.
    page.evaluate('''() => {
      const original = window.fetch;
      window.__crossingPending = false;
      window.fetch = async (...args) => {
        const response = await original(...args);
        if (String(args[0]).endsWith('/select') && JSON.parse(args[1]?.body || '{}').mode === 'crossing') {
          window.__crossingPending = true;
          await new Promise(resolve => { window.__releaseCrossing = resolve; });
        }
        return response;
      };
      window.__restoreCrossingFetch = () => { window.fetch = original; };
    }''')
    revision = state()['state']['revision']
    field = page.locator('#command-input')
    field.fill('STRETCH -1,-1 30,60 0,0 1,1'); field.press('Enter')
    deadline = time.monotonic() + 15
    while not page.evaluate('() => window.__crossingPending'):
        if time.monotonic() > deadline:
            raise AssertionError('The crossing request did not arrive.')
        page.wait_for_timeout(25)
    field.press('Escape')
    page.evaluate('window.__releaseCrossing()')
    idle()
    expect(page.locator('#prompt')).to_have_text('Command:')
    check('Escape discards delayed STRETCH continuation without executing trailing coordinates',
          state()['state']['revision'] == revision and state()['itemCount'] == 6)
    page.evaluate('window.__restoreCrossingFetch()')
