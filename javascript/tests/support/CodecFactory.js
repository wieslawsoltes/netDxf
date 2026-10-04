// Native JavaScript equivalents of the original reflection-only test factories.
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { ProbeTextReader } from '../../runtime/ProbeStreamReaders.js';
export const NewCodeWriter=(stream,binary)=>binary?new BinaryCodeValueWriter(stream):new TextCodeValueWriter(stream);
export const NewCodeReader=(stream,binary)=>binary?new BinaryCodeValueReader(stream):new TextCodeValueReader(new ProbeTextReader(stream));
