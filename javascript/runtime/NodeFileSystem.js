import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { FileStream, FullPath, FileError } from './NodeFileStream.js';
import { FileNotFoundException, IOException, NotSupportedException, UnauthorizedAccessException } from './Errors.js';
/** No copy fallback: successful publication exposes the complete staged inode. */
export const NodeFileSystem = Object.freeze({
  GetFullPath: FullPath,
  ValidateDestination(filename) {
    let attributes;
    try { attributes = fs.lstatSync(filename); }
    catch (error) { const mapped = FileError(error, filename); if (mapped instanceof FileNotFoundException) return false; throw mapped; }
    if (attributes.isSymbolicLink()) throw new NotSupportedException('Atomic replacement does not follow destination symbolic links.');
    if (!attributes.isFile()) throw new IOException('An atomic DXF destination must be a regular file.');
    if (!(attributes.mode & 0o222)) throw new UnauthorizedAccessException('The atomic DXF destination is read-only.');
    return true;
  },
  CreateTemporary(destination) {
    const filename = path.join(path.dirname(destination), '.netdxf-' + randomUUID().replaceAll('-', '') + '.tmp');
    return new FileStream(filename, 'CreateNew', 'Write');
  },
  Publish(temporary, destination, existed) {
    try {
      if (existed) fs.renameSync(temporary, destination);
      else {
        // Node has no portable rename-noreplace primitive. An exclusive hard-link publication
        // rejects a concurrently created destination, then removes the private staging name.
        fs.linkSync(temporary, destination);
        try { fs.unlinkSync(temporary); }
        catch { /* Publication succeeded. Retry cleanup without reporting destination failure. */ }
      }
    } catch (error) { throw FileError(error, destination); }
  },
  Delete(filename) { try { fs.unlinkSync(filename); } catch (error) { if (error.code !== 'ENOENT') throw FileError(error, filename); } },
});
