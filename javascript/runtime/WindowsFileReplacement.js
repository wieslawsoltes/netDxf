// Optional, separately built Node-API filesystem bridge. Portable/browser code never imports it.
import { createRequire } from 'node:module';
import path from 'node:path';
import { ArgumentException, ArgumentNullException, NotSupportedException } from './Errors.js';
import { CreateWindowsFileReplacer } from './WindowsReplacementContract.js';
const require = createRequire(import.meta.url);
const replace = CreateWindowsFileReplacer(() => require(`../native/bin/win32-${process.arch}/netdxf_windows.node`));
function NativePath(value, parameter) {
  if (value == null) throw new ArgumentNullException(parameter);
  if (typeof value !== 'string' || value.length === 0 || value.includes('\0'))
    throw new ArgumentException('A nonempty filesystem path without NUL is required.', parameter);
  return path.toNamespacedPath(value);
}
export function ReplaceWindowsFile(source, destination) {
  if (process.platform !== 'win32') throw new NotSupportedException('The Windows replacement bridge is Windows-only.');
  return replace(NativePath(source, 'source'), NativePath(destination, 'destination'));
}
