// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Copy, Format } from '../../runtime/GeometryRuntime.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { NullReferenceException } from '../../runtime/Errors.js';
/** Original internal constructor. Distance arrays/lists are deliberately caller-owned. */
export class MLineVertex {
  #position; #direction; #miter; #distances;
  constructor(location,direction,miter,distances) { this.#position=Copy(location);this.#direction=Copy(direction);this.#miter=Copy(miter);this.#distances=distances; }
  get Position() { return Copy(this.#position); } set Position(value) { this.#position=Copy(value); }
  get Direction() { return Copy(this.#direction); } get Miter() { return Copy(this.#miter); }
  get Distances() { return this.#distances; }
  ToString() { return Format('{0}: ({1})','MLineVertex',this.#position); }
  Clone() {
    if(this.#distances==null)throw new NullReferenceException();
    return new MLineVertex(this.#position,this.#direction,this.#miter,FixedArray(Array.from(this.#distances,list=>new ReferenceList(list)),value=>value));
  }
}
