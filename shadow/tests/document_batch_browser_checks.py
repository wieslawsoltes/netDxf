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

    def retained_branches(ids):
        return page.evaluate("""async ids => Object.fromEntries(await Promise.all(ids.map(async id => {
          const response = await fetch(`/api/cad/${encodeURIComponent(id)}/state`, { cache: 'no-store' });
          if (!response.ok) throw new Error(`Could not read retained branches for ${id}.`);
          return [id, (await response.json()).state.branches];
        })))""", ids)

    def confirm_dirty_documents(docs):
        for document in docs:
            if not document['dirty']:
                continue
            wait_for_ui(page, """id => document.getElementById('dialog').open &&
              shadowStudioAgentUi.snapshot().documentId === id""", arg=document['id'])
            expect(page.locator('#dialog-title')).to_have_text('Unsaved drawing')
            expect(page.locator('#dialog-body')).to_contain_text(document['name'])
            page.locator('#dialog-submit').click()

    execute('command.cancel')
    execute('ui.action', {'name': 'command-line-show'})
    # The full geometry suite may already occupy all eight document slots.
    # Reuse its drawings and reserve one slot for the delayed import below.
    # This fixture runs only after their save and round-trip assertions.
    existing = snapshot()['documents']
    if len(existing) >= 8:
        spare = next((document for document in existing if not document['dirty']), existing[-1])
        execute('document.activate', {'id': spare['id']})
        if spare['dirty']:
            wait_batch(execute('ui.action', {'name': 'save'}))
        execute('ui.action', {'name': 'file-tabs-show'})
        # A saved drawing with retained branches still requires explicit
        # discard. Only this disposable end-of-suite document is closed.
        spare = next(document for document in snapshot()['documents'] if document['id'] == spare['id'])
        page.locator(f'[data-document-tab="{spare["id"]}"] .close').click()
        confirm_dirty_documents([spare])
        wait_for_ui(page, """id => !shadowStudioAgentUi.snapshot().documents.some(document => document.id === id)""",
                    arg=spare['id'])
        idle()
    while len(snapshot()['documents']) < 2:
        execute('document.create')
        command('POINT 41,23')
    execute('document.activate', {'id': snapshot()['documents'][-1]['id']})
    verify_command_completion_receipt(page, check)
    before = snapshot()
    original = before['documentId']
    targets = [document['id'] for document in before['documents']]
    assert len(targets) >= 2 and original != targets[0]
    branches_before = retained_branches(targets)
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
        check('Save All saves each intended drawing exactly once, preserves retained branches and restores the original drawing',
              saved_ids == targets and after['documentId'] == original and
              [document['id'] for document in after['documents']] == targets and
              retained_branches(targets) == branches_before and
              all(document['dirty'] == bool(branches_before[document['id']]) for document in after['documents']))
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
        confirm_dirty_documents([document for document in after_import['documents'] if document['id'] in targets])
        wait_batch(receipt)
        after = snapshot()
        closed_ids = page.evaluate('() => globalThis.__documentBatchProbe.closes')
        check('Close All closes its captured targets exactly once and preserves a later imported drawing',
              closed_ids == targets and [document['id'] for document in after['documents']] == [imported_id] and
              after['documentId'] == imported_id and not after['busy'])

        remaining = after['documents']
        receipt = execute('ui.action', {'name': 'close-all'})
        confirm_dirty_documents(remaining)
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


def verify_command_completion_receipt(page, check):
    """Finish a real POINT while a second drawing's activation has no state yet."""
    def snapshot():
        return page.evaluate('() => shadowStudioAgentUi.snapshot()')

    def execute(name, arguments):
        return page.evaluate('([name, args]) => shadowStudioAgentUi.execute(name, args)', [name, arguments])

    before = snapshot()
    original = before['documentId']
    other = next(document['id'] for document in before['documents'] if document['id'] != original)
    execute('document.activate', {'id': other})
    other_before = snapshot()['state']
    execute('document.activate', {'id': original})
    page.evaluate("""([source, destination]) => {
      const fetch = globalThis.fetch, history = document.getElementById('history');
      const existing = new Set(history.children);
      const probe = { receiptRevision: null, releaseDocuments: null, releaseState: null,
        activation: null, documentsHeld: false, stateHeld: false };
      globalThis.__commandCompletionProbe = probe;
      probe.restore = () => { probe.releaseDocuments?.(); probe.releaseState?.(); globalThis.fetch = fetch; };
      probe.messages = () => [...history.children].filter(node => !existing.has(node))
        .map(node => ({ text: node.textContent, error: node.classList.contains('error'), success: node.classList.contains('success') }));
      globalThis.fetch = async (...args) => {
        const input = args[0], options = args[1] || {};
        const url = new URL(typeof input === 'string' ? input : input.url, location.href);
        const method = (options.method || input.method || 'GET').toUpperCase();
        if (method === 'POST' && url.pathname === `/api/cad/${encodeURIComponent(source)}/command` && !url.search) {
          const response = await fetch(...args);
          if (response.ok) probe.receiptRevision = (await response.clone().json()).state.revision;
          return response;
        }
        if (method === 'GET' && url.pathname === '/api/cad/documents' && !url.search &&
            probe.receiptRevision !== null && !probe.documentsHeld) {
          probe.documentsHeld = true;
          await new Promise(resolve => { probe.releaseDocuments = resolve; });
        }
        if (method === 'GET' && url.pathname === `/api/cad/${encodeURIComponent(destination)}/state` && !url.search &&
            probe.documentsHeld && !probe.stateHeld) {
          probe.stateHeld = true;
          await new Promise(resolve => { probe.releaseState = resolve; });
        }
        return fetch(...args);
      };
    }""", [original, other])
    try:
        receipt = execute('command', {'text': 'POINT 83,37'})
        assert receipt.get('pending'), 'The completed command must wait for its document refresh.'
        wait_for_ui(page, '() => !!globalThis.__commandCompletionProbe.releaseDocuments')
        page.evaluate("""id => {
          const probe = globalThis.__commandCompletionProbe;
          probe.activation = { status: 'running' };
          shadowStudioAgentUi.execute('document.activate', { id }).then(
            () => { probe.activation.status = 'completed'; },
            error => { probe.activation = { status: 'error', error: error.message }; });
        }""", other)
        wait_for_ui(page, """id => !!globalThis.__commandCompletionProbe.releaseState &&
          shadowStudioAgentUi.snapshot().documentId === id && shadowStudioAgentUi.snapshot().state === null""", arg=other)
        page.evaluate('() => globalThis.__commandCompletionProbe.releaseDocuments()')
        operation = wait_for_ui(page, """id => {
          const operation = shadowStudioAgentUi.snapshot().ui.pending.find(value => value.id === id);
          return operation && operation.status !== 'running' ? operation : null;
        }""", arg=receipt['operationId'])
        assert operation['status'] == 'completed', operation.get('error', operation)
        revision = page.evaluate('() => globalThis.__commandCompletionProbe.receiptRevision')
        messages = page.evaluate('() => globalThis.__commandCompletionProbe.messages()')
        logged_without_current_state = snapshot()['state'] is None and any(
            message['success'] and message['text'] == f'POINT completed — revision {revision}.' for message in messages)
        page.evaluate('() => globalThis.__commandCompletionProbe.releaseState()')
        activation = wait_for_ui(page, """() => {
          const value = globalThis.__commandCompletionProbe.activation;
          return value.status !== 'running' ? value : null;
        }""")
        assert activation['status'] == 'completed', activation.get('error', activation)
        other_after = snapshot()['state']
        execute('document.activate', {'id': original})
        check('command completion logs its own mutation receipt while another drawing activates without changing that drawing',
              logged_without_current_state and revision == before['state']['revision'] + 1 and
              snapshot()['state']['revision'] == revision and other_after == other_before and
              not any(message['error'] for message in messages))
    finally:
        page.evaluate('() => globalThis.__commandCompletionProbe?.restore()')
        wait_for_ui(page, """() => !globalThis.__commandCompletionProbe.activation ||
          globalThis.__commandCompletionProbe.activation.status !== 'running'""")
        execute('document.activate', {'id': original})
        page.evaluate('() => { delete globalThis.__commandCompletionProbe; }')
