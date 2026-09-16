// Node-specific entry point. The default/index.js entry remains browser-safe.
import { SetFileSystemAdapter } from './runtime/FileSystem.js';
import { NodeFileSystem } from './runtime/NodeFileSystem.js';
SetFileSystemAdapter(NodeFileSystem);
export * from './index.js';
export { FileStream } from './runtime/NodeFileStream.js';
