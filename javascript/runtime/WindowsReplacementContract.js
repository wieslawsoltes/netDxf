// Internal host contract. This module has no Node/native dependency and no filesystem fallback.
import { FileNotFoundException, DirectoryNotFoundException, UnauthorizedAccessException,
  IOException, NotSupportedException } from './Errors.js';

/** Cache only a validated callable. Failed loads remain retryable and retain their causes. */
export function CreateWindowsFileReplacer(load) {
  let replace;
  return (source, destination) => {
    if (!replace) {
      try {
        const candidate = load();
        const callable = candidate?.replaceFile;
        if (typeof callable !== 'function')
          throw new TypeError('The Windows filesystem bridge must export replaceFile.');
        // Retain the validated function, not a mutable external export object.
        replace = callable.bind(candidate);
      } catch (cause) {
        throw new NotSupportedException(
          'Existing-file atomic replacement on Windows requires a valid OS bridge. ' +
          'Build with npm run build:windows-host. No unsafe fallback was attempted.', { cause });
      }
    }
    const code = replace(source, destination);
    if (!Number.isInteger(code) || code < 0 || code > 0xffffffff)
      throw new IOException('The Windows filesystem bridge returned an invalid Win32 status.');
    if (code === 0) return;
    const Type = code === 2 ? FileNotFoundException : code === 3 ? DirectoryNotFoundException :
      code === 5 ? UnauthorizedAccessException : IOException;
    const error = new Type(`Windows ReplaceFileW failed (${code}): ${destination}`);
    error.Win32ErrorCode = code;
    throw error;
  };
}
