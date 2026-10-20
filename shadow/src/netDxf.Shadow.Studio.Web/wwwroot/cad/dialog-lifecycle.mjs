/**
 * Own one native dialog's open/close lifetime. close() clears `open` immediately,
 * but its close event is queued: do not reuse its content or returnValue until
 * that event has delivered the previous result.
 *
 * prepare must be synchronous. Visible dialogs are rejected, rather than queued.
 * Returns the native returnValue captured when this opening closes.
 */
export function createDialogLifecycle(dialog, {
  hasOpenDialog = () => !!dialog.ownerDocument.querySelector('dialog[open]')
} = {}) {
  let pending = null;
  return async function show(prepare = () => {}) {
    if (hasOpenDialog()) throw new Error('Finish the open dialog first.');
    while (pending) {
      await pending;
      if (hasOpenDialog()) throw new Error('Finish the open dialog first.');
    }
    let settle;
    const closed = new Promise(resolve => { settle = resolve; });
    pending = closed;
    const finish = () => {
      const result = dialog.returnValue;
      if (pending === closed) pending = null;
      settle(result);
    };
    dialog.addEventListener('close', finish, { once: true });
    try {
      prepare();
      dialog.returnValue = 'cancel';
      dialog.showModal();
    } catch (error) {
      dialog.removeEventListener('close', finish);
      if (pending === closed) pending = null;
      settle('cancel');
      throw error;
    }
    return closed;
  };
}
