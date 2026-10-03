import fs from 'node:fs';
import { FullPath, FileError } from './NodeFileStream.js';
export const NodeSupportFileSystem = Object.freeze({
  DirectorySeparators: process.platform === 'win32' ? '/\\' : '/',
  InvalidPathChars: process.platform === 'win32' ? '\"<>|' + String.fromCharCode(...Array.from({length:32}, (_, i) => i)) : '\0',
  ReadAllBytes(file) {
    const filename = FullPath(file);
    try { return new Uint8Array(fs.readFileSync(filename)); } catch (error) { throw FileError(error, filename); }
  },
  Exists(file) {
    if (typeof file !== 'string' || !file.length || file.includes('\0')) return false;
    try { return fs.statSync(file).isFile(); } catch { return false; }
  }
});
