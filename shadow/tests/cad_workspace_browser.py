#!/usr/bin/env python3
"""Real-origin Chromium integration for the published local CAD workspace.

Requires playwright==1.57.0, ezdxf==1.4.4 and `python -m playwright install --with-deps chromium`.
The production frontend, CSP and native API are used without mocked routes.
"""
from __future__ import annotations
import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import traceback
import urllib.request
import ezdxf
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts/studio-browser'

@contextmanager
def host(published: Path):
    executable = published / ('netDxf.Shadow.Studio.exe' if os.name == 'nt' else 'netDxf.Shadow.Studio')
    if not executable.is_file():
        raise ValueError('Provide the self-contained Studio publish directory.')
    if os.name != 'nt':
        executable.chmod(executable.stat().st_mode | 0o100)
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    url = f'http://127.0.0.1:{port}'
    with (OUTPUT / 'host.log').open('w', encoding='utf-8') as log:
        process = subprocess.Popen([str(executable), '--urls', url], cwd=published, stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 40
            while True:
                try:
                    with urllib.request.urlopen(url + '/api/health', timeout=1) as response:
                        if response.status == 200:
                            break
                except OSError:
                    if process.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError('Studio startup failed; see host.log')
                    time.sleep(.1)
            yield url
        finally:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=8)

def verify(url: str, executable: str | None = None):
    passed, errors, violations = [], [], []
    failure = None
    def check(name: str, condition=True):
        if not condition:
            raise AssertionError(name)
        passed.append(name)
        print('PASS:', name, flush=True)
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=True, executable_path=executable)
        context = browser.new_context(viewport={'width': 1600, 'height': 1000}, accept_downloads=True)
        page = context.new_page()
        page.set_default_timeout(15000)
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.on('console', lambda message: violations.append(message.text) if message.type == 'error' and 'Content Security Policy' in message.text else None)
        try:
            response = page.goto(url, wait_until='networkidle')
            assert response and response.ok
            expect(page.locator('#studio')).to_have_attribute('data-document', 'demo')
            expect(page.locator('#loading')).to_be_hidden()
            check('native CAD frontend loads under the unchanged host CSP', not errors and not violations)
            page.screenshot(path=str(OUTPUT / 'workspace.png'))
            page.locator('[data-action="new"]').first.click()
            expect(page.locator('#studio')).not_to_have_attribute('data-document', 'demo')
            def state():
                identifier = page.locator('#studio').get_attribute('data-document')
                with urllib.request.urlopen(f'{url}/api/cad/{identifier}/state', timeout=10) as r:
                    return json.load(r)
            def idle():
                expect(page.locator('#connection')).to_have_text('Local')
            def command(text: str, change=True):
                before = state()['state']['revision']
                field = page.locator('#command-input')
                field.fill(text)
                field.press('Enter')
                if change:
                    updated = wait_state(lambda data: data['state']['revision'] > before)
                    expect(page.locator('#revision')).to_have_text(f"r{updated['state']['revision']}")
                idle()
            def wait_state(predicate):
                deadline = time.monotonic() + 15
                while True:
                    data = state()
                    if predicate(data):
                        return data
                    if time.monotonic() > deadline:
                        raise AssertionError('Native drawing state did not reach the expected result.')
                    page.wait_for_timeout(50)
            check('new document has no source geometry', state()['state']['occurrences'] == 0)
            command('L 0,0 100,0 100,80 C')
            check('native closed line chain is one transaction', state()['state']['undoCount'] == 1 and state()['state']['occurrences'] == 3)
            command('U')
            check('one undo removes the entire line chain', state()['state']['occurrences'] == 0)
            command('REDO')
            check('redo restores the complete line chain', state()['state']['occurrences'] == 3)
            for text in ['C 140,40 25', 'REC 200,0 270,80', 'ARC 300,0 330,50 360,0', 'EL 420,40 470,40 420,60', 'POL 6 80,150 25', 'PL 0,130 30,170 60,130 C', 'TEXT 0,220 5 0 Pipeline drawing', 'MTEXT 100,220 5 70 First paragraph\\PSecond paragraph', 'DIMALIGNED 0,0 100,0 50,-20', 'DIMLINEAR 200,0 270,0 230,-20', 'POINT 10,10']:
                command(text)
                check('native construction: ' + text.split()[0])
            command('CO 10,10 20,20')
            command('M 20,20 @5,5')
            command('RO 0,0 90')
            command('SC 0,0 2')
            command('MI 0,0 100,0')
            command('AR 3 2 0,0 30,30')
            check('source-preserving modifiers and rectangular array', len(state()['selectedEntities']) == 5)
            page.locator('#command-input').press('Escape')
            idle()
            command('Z E', False)
            data = state()
            box = page.locator('#drawing').bounding_box()
            assert box
            bounds = data['state']['bounds']
            scale = min(box['width'] / max(20, bounds['maxX'] - bounds['minX']), box['height'] / max(20, bounds['maxY'] - bounds['minY'])) * .82
            def pixel(x, y):
                return (box['x'] + box['width']/2 + (x-(bounds['minX']+bounds['maxX'])/2)*scale,
                        box['y'] + box['height']/2 - (y-(bounds['minY']+bounds['maxY'])/2)*scale)
            page.mouse.click(*pixel(138,64))
            expect(page.get_by_role('textbox', name='Radius', exact=True)).to_be_visible()
            radius = page.get_by_role('textbox', name='Radius', exact=True)
            radius.fill('30')
            radius.press('Enter')
            wait_state(lambda data: any(p['name']=='Radius' and p['value']=='30' for p in (data.get('properties') or {}).get('properties', [])))
            check('geometric selection and source inspector editing')
            grip_revision = state()['state']['revision']
            page.mouse.move(*pixel(140,40))
            page.mouse.down()
            page.mouse.move(*pixel(160,55), steps=8)
            page.mouse.up()
            wait_state(lambda data: data['state']['revision'] > grip_revision)
            center = next(p['value'] for p in state()['properties']['properties'] if p['name']=='Center')
            # Pointer coordinates may be rounded by the platform to CSS pixels.
            # Bound input quantization to one pixel, not a drawing-size epsilon.
            input_tolerance = 1 / scale
            check('dragging the native center grip updates source coordinates',
                  abs(float(center['X'])-160) <= input_tolerance and
                  abs(float(center['Y'])-55) <= input_tolerance)
            page.locator('[data-action="layer-new"]').first.click()
            page.get_by_role('textbox', name='Layer name', exact=True).fill('Fabrication')
            page.locator('#dialog-submit').click()
            expect(page.get_by_role('checkbox', name='Fabrication Lock', exact=True)).to_be_visible()
            command('C 350,170 20')
            check('new entities use the chosen native layer', state()['items'][-1]['layer']=='Fabrication')
            page.get_by_role('checkbox', name='Fabrication Lock', exact=True).check()
            wait_state(lambda data: any(l['name']=='Fabrication' and l['locked'] for l in data['layers']))
            idle()
            before = state()['state']['revision']
            command('M 0,0 5,5', False)
            expect(page.locator('#toast')).to_contain_text('locked')
            check('locked layer fails without editing geometry', state()['state']['revision']==before)
            page.get_by_role('checkbox', name='Fabrication Lock', exact=True).uncheck()
            wait_state(lambda data: any(l['name']=='Fabrication' and not l['locked'] for l in data['layers']))
            idle()
            command('CO 0,0 55,0')
            check('a refused transaction does not poison the next command')
            command('SNAPSHOT', False)
            expect(page.get_by_role('button', name='Current drawing', exact=True)).to_be_visible()
            revision = state()['state']['revision']
            page.get_by_role('button', name=f'Revision {revision} — read only', exact=True).click()
            expect(page.locator('[data-command="LINE"]')).to_be_disabled()
            check('retained snapshots are visibly read only')
            page.get_by_role('button', name='Current drawing', exact=True).click()
            expect(page.locator('[data-command="LINE"]')).to_be_enabled()
            page.get_by_role('button', name='Fork branch', exact=True).click()
            page.get_by_role('textbox', name='Branch name', exact=True).fill('Alternative')
            page.locator('#dialog-submit').click()
            expect(page.get_by_role('button', name='Review merge', exact=True)).to_be_visible()
            command('C 420,170 12')
            check('branch edits are isolated', state()['state']['workspaceId']!='live')
            page.get_by_role('button', name='Review merge', exact=True).click()
            expect(page.locator('#dialog-title')).to_have_text('Apply reviewed merge')
            page.locator('#dialog-submit').click()
            idle()
            wait_state(lambda data: data['state']['workspaceId']=='live')
            check('reviewed branch merge returns to live source')
            with page.expect_download() as download:
                page.locator('[data-action="save"]').first.click()
            file = OUTPUT/'edited.dxf'
            download.value.save_as(str(file))
            check('real DXF file download', file.stat().st_size > 1000 and b'SECTION' in file.read_bytes())
            independent = ezdxf.readfile(file)
            dimensions_before = list(independent.modelspace().query('DIMENSION'))
            audit = independent.audit()
            check('independent DXF audit preserves both dimensions without repairs',
                  len(dimensions_before) == 2 and not audit.errors and not audit.fixes and
                  len(independent.modelspace().query('DIMENSION')) == 2 and
                  all(d.dxf.hasattr('geometry') and len(independent.blocks[d.dxf.geometry]) > 0
                      for d in dimensions_before))
            original_id = page.locator('#studio').get_attribute('data-document')
            original_entities = state()['state']['entities']
            page.locator('#open-file').set_input_files(str(file))
            expect(page.locator('#studio')).not_to_have_attribute('data-document', original_id)
            expect(page.locator('#title')).to_contain_text('edited.dxf')
            idle()
            wait_state(lambda data: data['state']['entities']==original_entities)
            check('download reopens in a separate native document')
            page.locator('#backend').select_option('svg')
            # SVG decoding is asynchronous: wait for a matching render network response.
            with page.expect_response(lambda r:'/render?' in r.url and 'backend=svg' in r.url and r.status==200):
                command('REGEN', False)
            page.get_by_role('tab', name='Output', exact=True).click()
            with page.expect_download() as download:
                page.locator('[data-action="export-svg"]').click()
            svg = OUTPUT/'edited.svg'
            download.value.save_as(str(svg))
            check('native SVG export', b'<svg' in svg.read_bytes())
            page.get_by_role('tab', name='Home', exact=True).click()
            page.set_viewport_size({'width': 900, 'height': 680})
            page.screenshot(path=str(OUTPUT/'workspace-compact.png'))
            # Read Chromium layout metrics without injecting/evaluating a script.
            protocol = context.new_cdp_session(page)
            metrics = protocol.send('Page.getLayoutMetrics')
            protocol.detach()
            check('compact workspace has no document-level horizontal overflow',
                  metrics['cssContentSize']['width'] <= metrics['cssLayoutViewport']['clientWidth'])
            check('no uncaught JavaScript errors or CSP violations', not errors and not violations)
        except Exception:
            failure = traceback.format_exc()
            raise
        finally:
            try:
                page.screenshot(path=str(OUTPUT/'final.png'))
            finally:
                (OUTPUT/'results.json').write_text(json.dumps({
                    'passed': passed, 'errors': errors,
                    'cspViolations': violations, 'failure': failure
                }, indent=2), encoding='utf-8')
                context.close()
                browser.close()

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published', type=Path, required=True)
    parser.add_argument('--browser', help='Optional Chromium executable; default is Playwright-managed Chromium')
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with host(args.published.resolve()) as url:
        verify(url, args.browser)
