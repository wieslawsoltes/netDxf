// Type-name and overload adapters for the independent model test interpreter.
import * as api from '../index.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
export const resolve = name => name.startsWith('List<') ? ReferenceList : name.replace(/^netDxf\./, '').replace(/^(Units|Collections|Entities|Tables|Objects|Blocks|Header|IO|GTE)\./, '').split('+').reduce((scope, part) => scope?.[part], api);
export function typeName(name) {
  if (name.endsWith('&')) return 'out ' + typeName(name.slice(0,-1));
  if (name.endsWith('[]')) return typeName(name.slice(0,-2)) + '[]';
  if (name.startsWith('List<')) return 'System.Collections.Generic.List<' + typeName(name.slice(5,-1)) + '>';
  if (name.startsWith('IEnumerable<')) return 'System.Collections.Generic.IEnumerable<' + typeName(name.slice(12,-1)) + '>';
  const simple={Double:'double',Int32:'int',Int16:'short',Byte:'byte',Boolean:'bool',String:'string',Object:'object',IFormatProvider:'System.IFormatProvider',Color:'System.Drawing.Color'};
  if (simple[name]) return simple[name];
  if(name.startsWith('netDxf.')) return name.replaceAll('+', '.');
  return 'netDxf.'+name.replaceAll('+', '.');
}
