"""Verify IndexedDB recovery migrations and atomic retention in a real browser.

Uses a uniquely named disposable database on the page's origin. Read probes and
injected quota failures are scoped to that database; all storage and transactions
are the browser's native IndexedDB implementation.
"""


def verify_browser_recovery_storage(page):
    return page.evaluate(r"""async () => {
        const { IndexedDbAgentStore, checkpointRecord } = await import(new URL('./agents/persistence.mjs', location.href));
        const { parseSession } = await import(new URL('./agents/contracts.mjs', location.href));
        const name = `shadow-browser-storage-${crypto.randomUUID()}`;
        const check = (condition, message) => { if (!condition) throw new Error(message); };
        const stamp = index => new Date(Date.UTC(2025, 0, 1, 0, 0, index)).toISOString();
        const makeRecord = (id, index, content = 'return "saved ☃";') => checkpointRecord({
            id, label: `Workspace ${index} private-token`, savedAt: stamp(index),
            checkpoint: JSON.stringify({ formatVersion: 1, sessionId: 'fixture', apiKey: 'private-token',
                messages: [{ role: 'user', contents: [{ kind: 'text', text: 'Continue private-token' }] }],
                files: [{ path: 'src/check.mjs', content, version: 4 }], pins: [], plan: [] }),
            draft: { path: 'src/check.mjs', content: 'return "draft private-token";', baseVersion: 4,
                basePath: 'src/check.mjs', baseContent: content }
        }, ['private-token']);
        const legacy = makeRecord('legacy', 0);
        const initial = await new Promise((resolve, reject) => {
            const request = indexedDB.open(name, 1);
            request.onupgradeneeded = () => request.result.createObjectStore('sessions', { keyPath: 'id' });
            request.onerror = () => reject(request.error); request.onsuccess = () => resolve(request.result);
        });
        await new Promise((resolve, reject) => {
            const transaction = initial.transaction('sessions', 'readwrite');
            transaction.objectStore('sessions').put(legacy);
            transaction.oncomplete = resolve; transaction.onabort = transaction.onerror = () => reject(transaction.error);
        });
        initial.close();
        const store = new IndexedDbAgentStore(indexedDB, name);
        const originalGetAll = IDBObjectStore.prototype.getAll;
        const originalGet = IDBObjectStore.prototype.get;
        const originalCursor = IDBObjectStore.prototype.openCursor;
        const originalPut = IDBObjectStore.prototype.put;
        const originalDelete = IDBObjectStore.prototype.delete;
        try {
            const database = await store.open();
            check(database.version === 2, 'Version 1 recovery database did not migrate to version 2.');
            check(database.objectStoreNames.contains('sessions') && database.objectStoreNames.contains('sessionMetadata'),
                'Migration did not create both recovery stores.');
            const migrated = await store.get(legacy.id), metadata = await store.list();
            check(JSON.stringify(migrated) === JSON.stringify(legacy), 'Migration changed the saved checkpoint or editor draft.');
            const expectedKeys = ['id', 'label', 'savedAt', 'files', 'messages', 'bytes', 'hasDraft'].sort().join(',');
            check(metadata.length === 1 && Object.keys(metadata[0]).sort().join(',') === expectedKeys,
                'Migration copied full checkpoint fields into its metadata store.');
            check(metadata[0].hasDraft && metadata[0].bytes === legacy.bytes && metadata[0].files === 1 && metadata[0].messages === 1,
                'Migrated recovery metadata does not describe its saved checkpoint.');
            check(!JSON.stringify({ migrated, metadata }).includes('private-token'), 'Recovery retained a provider credential.');

            let checkpointReads = 0;
            const observeRead = original => function(...args) {
                if (this.transaction.db.name === name && this.name === 'sessions') checkpointReads++;
                return original.apply(this, args);
            };
            IDBObjectStore.prototype.getAll = observeRead(originalGetAll);
            IDBObjectStore.prototype.get = observeRead(originalGet);
            IDBObjectStore.prototype.openCursor = observeRead(originalCursor);
            await store.list();
            await store.save(makeRecord('saved', 1));
            await store.list();
            check(checkpointReads === 0, 'Listing or pruning cloned full saved workspaces.');
            IDBObjectStore.prototype.getAll = originalGetAll;
            IDBObjectStore.prototype.get = originalGet;
            IDBObjectStore.prototype.openCursor = originalCursor;

            IDBObjectStore.prototype.put = function(...args) {
                if (this.transaction.db.name === name && this.name === 'sessionMetadata')
                    throw new DOMException('Injected metadata quota failure', 'QuotaExceededError');
                return originalPut.apply(this, args);
            };
            let rejectedSaves = 0;
            for (const record of [makeRecord('rejected', 2), makeRecord('legacy', 3, 'replacement')]) {
                try { await store.save(record); } catch (error) { if (error.name === 'QuotaExceededError') rejectedSaves++; else throw error; }
            }
            IDBObjectStore.prototype.put = originalPut;
            check(rejectedSaves === 2 && await store.get('rejected') === null,
                'A failed metadata save left a partial checkpoint record.');
            check(JSON.stringify(await store.get('legacy')) === JSON.stringify(legacy),
                'A failed metadata save replaced the previous complete checkpoint.');
            check(!(await store.list()).some(record => record.id === 'rejected'), 'Failed save left orphaned recovery metadata.');

            IDBObjectStore.prototype.delete = function(...args) {
                if (this.transaction.db.name === name && this.name === 'sessionMetadata')
                    throw new DOMException('Injected metadata delete failure', 'InvalidStateError');
                return originalDelete.apply(this, args);
            };
            let rejectedDelete = false;
            try { await store.delete('legacy'); } catch (error) { rejectedDelete = error.name === 'InvalidStateError'; }
            IDBObjectStore.prototype.delete = originalDelete;
            check(rejectedDelete && await store.get('legacy') !== null && (await store.list()).some(record => record.id === 'legacy'),
                'A failed metadata deletion removed only half of a saved checkpoint.');

            for (let index = 0; index < 12; index++) await store.save(makeRecord(`retained-${index}`, index + 10));
            const retained = await store.list();
            const keys = await store.transaction('readonly', (records, done) => {
                const request = records.getAllKeys(); request.onsuccess = () => done(request.result);
            });
            check(retained.length === 10 && keys.length === 10 && !keys.includes('retained-0') && !keys.includes('retained-1'),
                'Recovery did not retain only the 10 newest checkpoints.');
            check(keys.sort().join(',') === retained.map(record => record.id).sort().join(','),
                'Record retention left mismatched checkpoint and metadata stores.');
            const removed = retained[0].id;
            await store.delete(removed);
            check(await store.get(removed) === null && !(await store.list()).some(record => record.id === removed),
                'Deleting a recovery checkpoint did not delete its metadata.');

            const fullAscii = checkpointRecord({ id: 'full-ascii', label: 'Full workspace', savedAt: stamp(90),
                checkpoint: JSON.stringify({ formatVersion: 1,
                    messages: [{ role: 'user', contents: [{ kind: 'text', text: 'Keep the entire workspace' }] }],
                    files: [{ path: 'full.txt', content: 'a'.repeat(16 * 1024 * 1024), version: 1 }], pins: [], plan: [] }) });
            await store.save(fullAscii);
            check(fullAscii.bytes > 16 * 1024 * 1024 && (await store.get(fullAscii.id)).checkpoint === fullAscii.checkpoint,
                'A full 16 MiB ASCII workspace plus checkpoint overhead could not be saved and recovered.');
            let oversizeRejected = false;
            try { parseSession(JSON.stringify({ text: 'x'.repeat(24 * 1024 * 1024) })); }
            catch (error) { oversizeRejected = /24 MiB in UTF-8/.test(error.message); }
            check(oversizeRejected, 'The shared session parser accepted JSON over its 24 MiB byte limit.');

            // Exercise the byte budget using actual 12 MiB payloads, rather than
            // falsifying metadata sizes. Reuse the serialized fixture in memory.
            const large = checkpointRecord({ id: 'large', label: 'Large workspace', savedAt: stamp(100),
                checkpoint: JSON.stringify({ formatVersion: 1, messages: [], pins: [], plan: [],
                    files: [{ path: 'large.txt', content: 'x'.repeat(12 * 1024 * 1024), version: 1 }] }) });
            for (let index = 0; index < 6; index++)
                await store.save({ ...large, id: `large-${index}`, savedAt: stamp(100 + index) });
            const bounded = await store.list(), retainedBytes = bounded.reduce((total, record) => total + record.bytes, 0);
            check(retainedBytes <= 64 * 1024 * 1024 && !bounded.some(record => record.id === 'large-0')
                && bounded.filter(record => record.id.startsWith('large-')).length === 5,
                'Recovery did not enforce its aggregate 64 MiB budget.');
            return {
                schemaVersion: database.version, migratedCheckpointUnchanged: true, metadataAllowlisted: true,
                credentialsAbsent: true, fullCheckpointReadsForListAndSave: checkpointReads,
                failedSaveRolledBack: true, failedReplacementPreservedPrevious: true, failedDeleteRolledBack: true,
                fullAsciiWorkspaceSaved: true, fullAsciiWorkspaceBytes: 16 * 1024 * 1024, oversizeSessionRejected: true,
                countRetention: retained.length, byteRetention: retainedBytes, largeRecordsRetained: 5,
                metadataAndCheckpointsAgree: true, deleteRemovesBoth: true
            };
        } finally {
            IDBObjectStore.prototype.getAll = originalGetAll;
            IDBObjectStore.prototype.get = originalGet;
            IDBObjectStore.prototype.openCursor = originalCursor;
            IDBObjectStore.prototype.put = originalPut;
            IDBObjectStore.prototype.delete = originalDelete;
            await store.close();
            await new Promise((resolve, reject) => {
                const request = indexedDB.deleteDatabase(name);
                request.onsuccess = resolve; request.onerror = () => reject(request.error);
                request.onblocked = () => reject(new Error('Disposable recovery verification database remained open.'));
            });
        }
    }""")
