/** Request receipts must identify a document AND its workspace epoch, not just a revision. */
export function identityHeaders(state) {
  if (!state) throw new Error('Load the current workspace before making a request.');
  return {
    'X-Shadow-Studio': '1',
    'X-Shadow-Document': state.documentId,
    'X-Shadow-Workspace': state.workspaceId,
    'X-Shadow-View': String(state.viewVersion)
  };
}

export function viewKey(state) {
  return state ? JSON.stringify([state.serverId, state.documentId, state.workspaceId,
    state.viewVersion, state.revision, state.layout, state.fontRevision, state.changeVersion]) : '';
}

/** A displayed image also belongs to one camera, pixel extent, backend and preview. */
export function presentationKey(state, camera, viewport, backend, snapshot = '', merge = null) {
  return JSON.stringify([viewKey(state), camera.x, camera.y, camera.scale,
    viewport.w, viewport.h, backend, snapshot, merge]);
}

export function sameReceipt(receipt, state, graph = false) {
  return !!state && receipt.documentId === state.documentId && receipt.workspaceId === state.workspaceId &&
    receipt.viewVersion === state.viewVersion && receipt.revision === state.revision &&
    (!graph || receipt.layout === state.layout && receipt.fontRevision === state.fontRevision);
}

/**
 * SSE is an invalidation feed, not a transaction stream. A notice advances the minimum
 * acceptable state version immediately, before the corresponding state GET completes.
 * A server change fences every in-flight read from the previous process.
 */
export class StateGate {
  #server;
  #floor = -1;
  #epoch = 0;
  stamp() { return this.#epoch; }
  observe(notice) {
    this.#validate(notice);
    if (notice.serverId !== this.#server) {
      this.#server = notice.serverId;
      this.#floor = notice.changeVersion;
      this.#epoch++;
      return true;
    }
    if (notice.changeVersion <= this.#floor) return false;
    this.#floor = notice.changeVersion;
    return true;
  }
  accept(state, stamp, authoritative = false) {
    this.#validate(state);
    if (stamp !== this.#epoch) return false;
    if (this.#server && state.serverId !== this.#server) {
      if (!authoritative) return false;
      this.#epoch++;
      this.#floor = -1;
    }
    if (state.changeVersion < this.#floor) return false;
    this.#server = state.serverId;
    this.#floor = state.changeVersion;
    return true;
  }
  current(state) {
    return !!state && state.serverId === this.#server && state.changeVersion >= this.#floor;
  }
  #validate(value) {
    if (!value || typeof value.serverId !== 'string' || !value.serverId ||
        !Number.isSafeInteger(value.changeVersion) || value.changeVersion < 0)
      throw new Error('Invalid Studio state receipt.');
  }
}
