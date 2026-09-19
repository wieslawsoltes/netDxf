import { ArgumentException, NotSupportedException } from './Errors.js';
let adapter = null;
/** Optional text-file host. Portable imports never open files or import node:. */
export function SetPatternFileSystem(value) {
  if (value === null) { adapter = null; return; }
  if (!value || typeof value.ReadAllText !== 'function' || typeof value.AppendAllText !== 'function' || !['\n', '\r\n'].includes(value.NewLine))
    throw new ArgumentException('A pattern filesystem must provide ReadAllText, AppendAllText and NewLine.', 'value');
  adapter = { ReadAllText: value.ReadAllText.bind(value), AppendAllText: value.AppendAllText.bind(value), NewLine: value.NewLine };
}
function host() {
  if (!adapter) throw new NotSupportedException('PAT file access requires @netdxf/javascript/node or an explicit pattern filesystem host. Use LoadText/NamesFromText for portable input.');
  return adapter;
}
export const PatternFileSystem = Object.freeze({
  ReadAllText(file) { return host().ReadAllText(file); },
  AppendAllText(file, text) { return host().AppendAllText(file, text); },
  get NewLine() { return host().NewLine; }
});
