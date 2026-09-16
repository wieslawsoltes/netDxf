// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
export class StringReader {
  #text; #position = 0;
  constructor(text) { this.#text = text; }
  get Position() { return this.#position; }
  Read() { return this.#position < this.#text.length ? this.#text.charCodeAt(this.#position++) : -1; }
  ReadLine() {
    if (this.#position >= this.#text.length) return null;
    const start = this.#position;
    while (this.#position < this.#text.length) {
      const c = this.#text.charCodeAt(this.#position++);
      if (c === 10 || c === 13) {
        const end = this.#position - 1;
        if (c === 13 && this.#text.charCodeAt(this.#position) === 10) this.#position++;
        return this.#text.slice(start,end);
      }
    }
    return this.#text.slice(start);
  }
}
