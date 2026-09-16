// JavaScript-only harness adapters. Original test methods remain in their mirrored files.
import assert from 'node:assert/strict';
import { DxfVersion, DxfVersionStringValues } from '../../netDxf/Header/DxfVersion.js';
export const SupportedVersions = [13, 14, 15, 16, 17, 18];
export const VersionName = version => Object.keys(DxfVersion).find(key => DxfVersion[key] === version);
export const HeaderVersion = version => DxfVersionStringValues[version];
export const BooleanName = value => value ? 'True' : 'False';
export const Check = (value, message) => assert.ok(value, message);
export const Equal = (expected, actual, message) => assert.deepStrictEqual(actual, expected, message);
export const SameDoubleBits = (expected, actual, message) => assert.ok(Object.is(expected, actual), message ?? `${expected} != ${actual}`);
export function Throws(type, action) { assert.throws(action, error => error instanceof type, type.name); }
export const cases = [];
let source = null;
export function WithSource(filename, action) { source = filename; try { action(); } finally { source = null; } }
export function Run(name, action) {
  if (!source) throw new Error('Register tests under their original source filename.');
  if (cases.some(test => test.name === name)) throw new Error('Duplicate test identity: ' + name);
  cases.push({ name, action, source });
}
