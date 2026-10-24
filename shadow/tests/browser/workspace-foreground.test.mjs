import { test } from 'node:test';
import assert from 'node:assert/strict';
import { raiseWorkspaceWindow, releaseWorkspaceWindow } from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/workspace-foreground.mjs';

const windowNode = layer => ({ style: { zIndex: layer, removeProperty(name) { if (name === 'z-index') delete this.zIndex; } } });

test('palette, ribbon and command windows can alternate foreground without increasing layers or covering menus', () => {
  const palette = windowNode(), ribbon = windowNode(), command = windowNode('32');
  raiseWorkspaceWindow(ribbon); assert.equal(ribbon.style.zIndex, '42');
  raiseWorkspaceWindow(palette); assert.equal(palette.style.zIndex, '42'); assert.equal(ribbon.style.zIndex, undefined);
  raiseWorkspaceWindow(command); assert.equal(command.style.zIndex, '42'); assert.equal(palette.style.zIndex, undefined);
  raiseWorkspaceWindow(ribbon); assert.equal(command.style.zIndex, '32'); assert.equal(ribbon.style.zIndex, '42');
  raiseWorkspaceWindow(ribbon); releaseWorkspaceWindow(ribbon); assert.equal(ribbon.style.zIndex, undefined);
});

test('hiding or disposing a background window does not release a different active component', () => {
  const palette = windowNode('12'), ribbon = windowNode('38');
  raiseWorkspaceWindow(palette); raiseWorkspaceWindow(ribbon); releaseWorkspaceWindow(palette);
  assert.equal(palette.style.zIndex, '12'); assert.equal(ribbon.style.zIndex, '42');
  releaseWorkspaceWindow(ribbon); assert.equal(ribbon.style.zIndex, '38');
  releaseWorkspaceWindow(ribbon); raiseWorkspaceWindow(null); assert.equal(ribbon.style.zIndex, '38');
});
