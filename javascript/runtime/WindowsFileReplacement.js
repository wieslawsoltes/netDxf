// Optional, separately built Node-API filesystem bridge. Portable/browser code never imports it.
import { createRequire } from 'node:module';
import path from 'node:path';
import { FileNotFoundException, DirectoryNotFoundException, UnauthorizedAccessException, IOException, NotSupportedException } from './Errors.js';
const require = createRequire(import.meta.url);
let bridge;
export function ReplaceWindowsFile(source, destination) {
  if (process.platform !== 'win32') throw new NotSupportedException('The Windows replacement bridge is Windows-only.');
  if (!bridge) {
    try { bridge = require(`../native/bin/win32-${process.arch}/netdxf_windows.node`); }
    catch (error) {
      throw new NotSupportedException('Existing-file atomic replacement on Windows requires the optional OS bridge. Build with npm run build:windows-host. No unsafe fallback was attempted.', { cause: error });
    }
    if (typeof bridge.replaceFile !== 'function') throw new NotSupportedException('Invalid Windows filesystem bridge.');
  }
  const code = bridge.replaceFile(path.toNamespacedPath(source), path.toNamespacedPath(destination));
  if (code === 0) return;
  const Type = code === 2 ? FileNotFoundException : code === 3 ? DirectoryNotFoundException : code === 5 ? UnauthorizedAccessException : IOException;
  const error = new Type(`Windows ReplaceFileW failed (${code}): ${destination}`);
  error.Win32ErrorCode = code;
  throw error;
}
