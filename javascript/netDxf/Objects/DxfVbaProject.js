// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';

/** Inert byte envelope: contents are never loaded, parsed, evaluated, or executed. */
export class DxfVbaProject extends DxfDatabaseObject {
  static get MaximumDataLength(){return 16*1024*1024;}
  static get MaximumChunkCount(){return 262144;}
  static get MaximumChunkLength(){return 127;}
  #chunks=new ReferenceList(); #length=0;
  constructor(){super('VBA_PROJECT');}
  get DataLength(){return this.#length;}
  get Data() {
    const result=new Uint8Array(this.#length);let offset=0;
    for(const chunk of this.#chunks){result.set(chunk,offset);offset+=chunk.length;}return result;
  }
  set Data(value) {
    if(value==null)throw new ArgumentNullException('value');
    if(!(value instanceof Uint8Array))throw new ArgumentException('Expected a Uint8Array byte buffer.','value');
    if(value.length>DxfVbaProject.MaximumDataLength)throw new ArgumentOutOfRangeException('value');
    const replacement=new ReferenceList();
    for(let offset=0;offset<value.length;offset+=DxfVbaProject.MaximumChunkLength)
      replacement.Add(new Uint8Array(value.subarray(offset,Math.min(value.length,offset+DxfVbaProject.MaximumChunkLength))));
    this.#chunks=replacement;this.#length=value.length;
  }
  get Chunks(){return ReadOnlyReferenceView(new ReferenceList(Array.from(this.#chunks,chunk=>new Uint8Array(chunk))));}
  SetChunks(value) {
    if(value==null)throw new ArgumentNullException('value');
    const replacement=new ReferenceList();let length=0;
    for(const chunk of value) {
      if(chunk==null)throw new ArgumentException('A chunk cannot be null.','value');
      if(!(chunk instanceof Uint8Array))throw new ArgumentException('Expected Uint8Array byte chunks.','value');
      if(chunk.length>DxfVbaProject.MaximumChunkLength||replacement.Count===DxfVbaProject.MaximumChunkCount||chunk.length>DxfVbaProject.MaximumDataLength-length)
        throw new ArgumentOutOfRangeException('value','The project exceeds a payload or physical chunk admission limit.');
      replacement.Add(new Uint8Array(chunk));length+=chunk.length;
    }
    this.#chunks=replacement;this.#length=length;
  }
  // Internal typed-writer adapter; unlike public snapshots, retained chunk bytes are live.
  get StoredChunks(){return ReadOnlyReferenceView(this.#chunks);}
  CloneShell(){const copy=new DxfVbaProject();copy.SetChunks(this.#chunks);return copy;}
}
