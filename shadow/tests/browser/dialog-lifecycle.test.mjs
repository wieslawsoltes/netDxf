import test from 'node:test';
import assert from 'node:assert/strict';
import { createDialogLifecycle } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/dialog-lifecycle.mjs';

class Dialog extends EventTarget {
  open = false;
  returnValue = '';
  queued = [];
  showModal() { this.open = true; }
  close(value) {
    this.returnValue = value;
    this.open = false;
    this.queued.push(() => this.dispatchEvent(new Event('close')));
  }
  deliverClose() { this.queued.shift()(); }
}
function fixture() {
  const dialog = new Dialog();
  return { dialog, show: createDialogLifecycle(dialog, { hasOpenDialog: () => dialog.open }) };
}
test('rapid reopen waits for the queued native close before resetting returnValue or preparing content', async () => {
  const { dialog, show } = fixture(), prepared = [];
  const first = show(() => prepared.push('first'));
  dialog.close('ok');
  const second = show(() => prepared.push('second'));
  assert.deepEqual(prepared, ['first']);
  assert.equal(dialog.returnValue, 'ok');
  dialog.deliverClose();
  assert.equal(await first, 'ok');
  assert.deepEqual(prepared, ['first', 'second']);
  assert.equal(dialog.open, true);
  assert.equal(dialog.returnValue, 'cancel');
  let settled = false; second.then(() => { settled = true; });
  await Promise.resolve();
  assert.equal(settled, false, 'old close event must not complete the new dialog');
  dialog.close('cancel'); dialog.deliverClose();
  assert.equal(await second, 'cancel');
});
test('visible dialogs reject a concurrent open without replacing content', async () => {
  const { dialog, show } = fixture();
  const first = show();
  let prepared = false;
  await assert.rejects(show(() => { prepared = true; }), /Finish the open dialog/);
  assert.equal(prepared, false);
  dialog.close('ok'); dialog.deliverClose();
  assert.equal(await first, 'ok');
});
test('only one caller can reopen after the same pending close', async () => {
  const { dialog, show } = fixture();
  const first = show(); dialog.close('ok');
  const second = show();
  const third = show();
  const rejected = assert.rejects(third, /Finish the open dialog/);
  dialog.deliverClose();
  assert.equal(await first, 'ok');
  await rejected;
  dialog.close('second'); dialog.deliverClose();
  assert.equal(await second, 'second');
});
test('preparation and native opening failures release ownership for later attempts', async () => {
  const { dialog, show } = fixture();
  await assert.rejects(show(() => { throw new Error('prepare failed'); }), /prepare failed/);
  dialog.showModal = () => { throw new Error('native failed'); };
  await assert.rejects(show(), /native failed/);
  dialog.showModal = () => { dialog.open = true; };
  const next = show();
  dialog.close('ok'); dialog.deliverClose();
  assert.equal(await next, 'ok');
});
test('a different visible dialog also prevents opening the shared modal', async () => {
  const dialog = new Dialog();
  let otherOpen = true;
  const show = createDialogLifecycle(dialog, { hasOpenDialog: () => otherOpen || dialog.open });
  await assert.rejects(show(), /Finish the open dialog/);
  otherOpen = false;
  const result = show();
  dialog.close('ok'); dialog.deliverClose();
  assert.equal(await result, 'ok');
});
