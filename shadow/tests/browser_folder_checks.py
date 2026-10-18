"""Exercise the real File System Access implementation using origin-private storage.

This avoids mocking browser file handles or granting access to host files. Folder
pickers still require an interactive user gesture and are covered by the same
BrowserFolder implementation, permission checks, and reviewed write flow.
"""


def verify_browser_folder(page):
    return page.evaluate(r"""async () => {
        const { BrowserFolder } = await import(new URL('./agents/file-transfer.mjs', location.href));
        const root = await navigator.storage.getDirectory();
        const name = `shadow-browser-folder-${crypto.randomUUID()}`;
        const directory = await root.getDirectoryHandle(name, { create: true });
        async function write(path, content) {
            const parts = path.split('/'); let parent = directory;
            for (const part of parts.slice(0, -1)) parent = await parent.getDirectoryHandle(part, { create: true });
            const handle = await parent.getFileHandle(parts.at(-1), { create: true });
            const stream = await handle.createWritable();
            await stream.write(content); await stream.close();
        }
        try {
            await write('src/check.mjs', 'return "before";');
            await write('readme.md', '# Original');
            await write('obj/generated.txt', 'generated');
            const folder = new BrowserFolder(directory), imported = await folder.read();
            const changes = [{ path: 'src/check.mjs', content: 'return "after";', version: 1 },
                { path: 'new.md', content: '# Created in the browser', version: 0 }];
            const reviewed = await folder.planWrite(changes);
            await write('src/check.mjs', 'return "outside edit";');
            let rejected = false;
            try { await folder.write(reviewed); } catch (error) { rejected = /changed after review/.test(error.message); }
            const noPartialWrites = await folder.current('new.md') === null;
            const conflictPlan = await folder.planWrite(changes);
            const detected = conflictPlan.find(row => row.path === 'src/check.mjs')?.conflict === true;
            const receipt = await folder.write(conflictPlan);
            return {
                realFileHandles: directory instanceof FileSystemDirectoryHandle,
                importedTextFiles: imported.files.length,
                skippedGeneratedDirectories: imported.skipped.length,
                externalChangeRejected: rejected,
                noPartialWritesBeforeValidation: noPartialWrites,
                reviewDetectsExternalConflict: detected,
                savedPaths: receipt.written,
                savedContents: await folder.current('src/check.mjs') === 'return "after";'
                    && await folder.current('new.md') === '# Created in the browser'
            };
        } finally { await root.removeEntry(name, { recursive: true }); }
    }""")
