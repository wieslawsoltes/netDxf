import { ArgumentException } from './Errors.js';

/** C# += / -= map to Add / Remove. Invocations use a multicast-delegate snapshot. */
export class EventHook {
  #handlers = [];
  Add(handler) {
    if (handler == null) return;
    if (typeof handler !== 'function') throw new ArgumentException('An event handler must be a function.', 'handler');
    this.#handlers.push(handler);
  }
  Remove(handler) {
    const at = this.#handlers.lastIndexOf(handler);
    if (at >= 0) this.#handlers.splice(at, 1);
  }
  Invoke(sender, args) {
    for (const handler of this.#handlers.slice()) handler(sender, args);
  }
}
