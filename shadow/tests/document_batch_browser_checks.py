"""End-of-suite native/WASM batch checks; use a disposable host or isolated WASM context.

Close All intentionally closes the suite's open drawings. Run after all geometry,
download and round-trip assertions that need those documents.
"""
from pathlib import Path
from playwright.sync_api import expect
from browser_ui import wait_for_ui


def verify_document_batches(page, output, state, command, wait_state, idle, check):
    def snapshot():
        return page.evaluate('() => shadowStudioAgentUi.snapshot()')

    def execute(name, arguments=None):
        return page.evaluate('([name, args]) => shadowStudioAgentUi.execute(name, args)', [name, arguments or {}])

    def wait_batch(receipt):
        if receipt.get('pending'):
            operation = wait_for_ui(page, """id => {
              const operation = shadowStudioAgentUi.snapshot().ui.pending.find(value => value.id === id);
              return operation && operation.status !== 'running' ? operation : null;
            }""", arg=receipt['operationId'], timeout=30000)
            assert operation['status'] == 'completed', operation.get('error', operation)
        expect(page.locator('#documents [role="tab"]').first).to_be_enabled()
        idle()

    def rejected(name, arguments):
        return page.evaluate("""async ([name, args]) => {
          try { await shadowStudioAgentUi.execute(name, args); return null; }
          catch (error) { return error.message; }
        }""", [name, arguments])

    execute('command.cancel')
    execute('ui.action', {'name': 'command-line-show'})
    for index in range(2):
        execute('document.create')
        command(f'POINT {41 + index},23')
    before = snapshot()
    original = before['documentId']
    targets = [document['id'] for document in before['documents']]
    assert len(targets) >= 2 and original != targets[0]
    downloads = []

    def downloaded(download):
        downloads.append(download)

    page.on('download', downloaded)
    # CadClient resolves fetch at call time. Wrapping the existing implementation
    # preserves both the native HTTP transport and the in-process WASM transport.
    page.evaluate("""first => {
      const original = globalThis.fetch;
      const probe = { held: false, saves: [], closes: [], release: null,
        holdImport: false, importId: null, releaseImport: null };
      globalThis.__documentBatchProbe = probe;
      probe.restore = () => { probe.release?.(); probe.releaseImport?.(); globalThis.fetch = original; };
      globalThis.fetch = async (...args) => {
        const input = args[0], options = args[1] || {};
        const url = new URL(typeof input === 'string' ? input : input.url, location.href);
        const method = (options.method || input.method || 'GET').toUpperCase();
        const match = url.pathname.match(/^\\/api\\/cad\\/([^/]+)\\/(save|close)$/);
        if (method === 'POST' && match) probe[match[2] === 'save' ? 'saves' : 'closes'].push(decodeURIComponent(match[1]));
        if (!probe.held && method === 'GET' && url.pathname === `/api/cad/${encodeURIComponent(first)}/state` && !url.search) {
          probe.held = true;
          await new Promise(resolve => { probe.release = resolve; });
        }
        const load = url.pathname.match(/^\\/api\\/cad\\/([^/]+)\\/load$/);
        if (probe.holdImport && method === 'POST' && load) {
          probe.holdImport = false;
          probe.importId = decodeURIComponent(load[1]);
          await new Promise(resolve => { probe.releaseImport = resolve; });
        }
        return original(...args);
      };
    }""", targets[0])
    try:
        receipt = execute('ui.action', {'name': 'save-all'})
        wait_for_ui(page, '() => globalThis.__documentBatchProbe.held')
        check('Save All disables document and layout controls while waiting for activation',
              page.locator('#documents button,#layouts button,#drawing-list,#layout-list').evaluate_all(
                  'nodes => nodes.length > 0 && nodes.every(node => node.disabled)'))
        errors = [
            rejected('document.activate', {'id': original}),
            rejected('document.create', {}),
            rejected('cad.edit', {'path': 'select', 'body': {'mode': 'clear'}}),
        ]
        check('document batch ownership rejects concurrent activation, creation and edits',
              all(error == 'Wait for the current drawing batch to finish.' for error in errors))
        page.evaluate('() => globalThis.__documentBatchProbe.release()')
        wait_batch(receipt)
        saved_ids = page.evaluate('() => globalThis.__documentBatchProbe.saves')
        after = snapshot()
        check('Save All saves each intended document exactly once and restores the original active drawing',
              saved_ids == targets and after['documentId'] == original and
              [document['id'] for document in after['documents']] == targets and
              all(not document['dirty'] for document in after['documents']))
        check('Save All creates a downloadable file for every open drawing', len(downloads) == len(targets))

        # Make the first target dirty so cancellation cannot be confused with
        # earlier successful closes in the same batch.
        execute('document.activate', {'id': targets[0]})
        command('POINT 61,29')
        execute('document.activate', {'id': original})
        receipt = execute('ui.action', {'name': 'close-all'})
        expect(page.locator('#dialog-title')).to_have_text('Unsaved drawing')
        page.locator('#dialog footer button[value="cancel"]').click()
        wait_batch(receipt)
        after = snapshot()
        check('Cancel on the first dirty Close All target stops the batch and restores its original drawing',
              after['documentId'] == original and
              [document['id'] for document in after['documents']] == targets and
              page.evaluate('() => globalThis.__documentBatchProbe.closes.length') == 0)

        # The first target must already be active: reactivation would change
        # CadClient's epoch and hide the foreground-ownership race. Hold the
        # detached import until Close All is waiting on that target's prompt.
        execute('document.activate', {'id': targets[0]})
        page.evaluate('() => { globalThis.__documentBatchProbe.holdImport = true; }')
        page.locator('#open-file').set_input_files({
            'name': 'Delayed batch import.dxf',
            'mimeType': 'application/dxf',
            'buffer': Path(downloads[-1].path()).read_bytes(),
        })
        wait_for_ui(page, '() => !!globalThis.__documentBatchProbe.releaseImport')
        imported_id = page.evaluate('() => globalThis.__documentBatchProbe.importId')
        assert imported_id not in targets
        receipt = execute('ui.action', {'name': 'close-all'})
        expect(page.locator('#dialog-title')).to_have_text('Unsaved drawing')
        page.evaluate('() => globalThis.__documentBatchProbe.releaseImport()')
        wait_for_ui(page, """id => shadowStudioAgentUi.snapshot().documents.some(document => document.id === id)""",
                    arg=imported_id)
        after_import = snapshot()
        check('a prior Open completing during Close All confirmation cannot steal its active target',
              after_import['documentId'] == targets[0] and page.locator('#dialog').is_visible() and
              page.evaluate('() => globalThis.__documentBatchProbe.closes.length') == 0)
        page.locator('#dialog-submit').click()
        wait_batch(receipt)
        after = snapshot()
        closed_ids = page.evaluate('() => globalThis.__documentBatchProbe.closes')
        check('Close All closes its captured targets exactly once and preserves a later imported drawing',
              closed_ids == targets and [document['id'] for document in after['documents']] == [imported_id] and
              after['documentId'] == imported_id and not after['busy'])

        receipt = execute('ui.action', {'name': 'close-all'})
        if page.locator('#dialog').is_visible():
            expect(page.locator('#dialog-title')).to_have_text('Unsaved drawing')
            page.locator('#dialog-submit').click()
        wait_batch(receipt)
        after = snapshot()
        closed_ids = page.evaluate('() => globalThis.__documentBatchProbe.closes')
        check('closing the remaining drawing leaves a fresh editable drawing',
              closed_ids == [*targets, imported_id] and len(after['documents']) == 1 and
              after['documentId'] not in [*targets, imported_id] and
              after['state']['entities'] == 0 and after['state']['revision'] == 0 and not after['busy'])
    finally:
        page.evaluate('() => { globalThis.__documentBatchProbe?.restore(); delete globalThis.__documentBatchProbe; }')
        page.remove_listener('download', downloaded)
