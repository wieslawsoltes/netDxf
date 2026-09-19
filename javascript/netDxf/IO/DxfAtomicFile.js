// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ArgumentNullException, ArgumentException, IOException, UnauthorizedAccessException,
  ThrowIfCancellationRequested as Cancel } from '../../runtime/Errors.js';
import { GetFileSystemAdapter } from '../../runtime/FileSystem.js';
/** Same-directory staged publication. Protects destination bytes, not document state. */
export class DxfAtomicFile {
  static Write(path, serialize, cancellationToken = null) {
    if (path == null) throw new ArgumentNullException('path');
    if (serialize == null) throw new ArgumentNullException('serialize');
    Cancel(cancellationToken);
    if (typeof serialize !== 'function') throw new ArgumentException('A synchronous serializer is required.', 'serialize');
    const host = GetFileSystemAdapter(); // Capture once, so reentrant adapter registration cannot change this transaction.
    const destination = host.GetFullPath(path), existed = host.ValidateDestination(destination);
    let stream = null, temporary = null;
    try {
      stream = host.CreateTemporary(destination); temporary = stream.Name;
      try {
        const result = serialize(stream);
        if (result && typeof result.then === 'function') {
          // Avoid publishing bytes before an accidentally asynchronous serializer finishes.
          Promise.resolve(result).catch(() => {});
          throw new ArgumentException('Atomic serialization must complete synchronously.', 'serialize');
        }
        Cancel(cancellationToken); stream.Flush(true);
      } finally { stream.Dispose(); }
      Cancel(cancellationToken);
      if (host.ValidateDestination(destination) !== existed)
        throw new IOException('The DXF destination appeared or disappeared while saving; no replacement was attempted.');
      host.Publish(temporary, destination, existed);
    } finally {
      if (temporary !== null) {
        try { host.Delete(temporary); }
        catch (error) { if (!(error instanceof IOException || error instanceof UnauthorizedAccessException)) throw error; }
      }
    }
  }
}
