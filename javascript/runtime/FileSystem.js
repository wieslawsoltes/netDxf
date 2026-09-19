import { ArgumentException, NotSupportedException } from './Errors.js';
let adapter = null;
/** Host boundary. The portable package does not import Node or probe browser globals. */
export function SetFileSystemAdapter(value) {
  const methods = ['GetFullPath', 'ValidateDestination', 'CreateTemporary', 'Publish', 'Delete'];
  if (!value || methods.some(name => typeof value[name] !== 'function'))
    throw new ArgumentException('A complete synchronous filesystem adapter is required.', 'value');
  adapter = Object.freeze(Object.fromEntries(methods.map(name => [name, value[name].bind(value)])));
}
export function GetFileSystemAdapter() {
  if (!adapter) throw new NotSupportedException('SaveAtomic requires a filesystem host. Import @netdxf/javascript/node in Node.js.');
  return adapter;
}
