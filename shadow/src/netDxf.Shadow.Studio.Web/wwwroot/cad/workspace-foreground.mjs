/** One foreground workspace window, below command menus and modal dialogs.
 * Keep each component's resting layer and avoid an ever-growing z-index.
 */
let foreground = null;
const activeLayer = '42';

export function raiseWorkspaceWindow(element) {
  if (!element?.style) return;
  if (foreground?.element !== element) {
    if (foreground) releaseWorkspaceWindow(foreground.element);
    foreground = { element, layer: element.style.zIndex };
  }
  element.style.zIndex = activeLayer;
}

/** Hide, dock or dispose only releases this component if it currently owns focus order. */
export function releaseWorkspaceWindow(element) {
  if (foreground?.element !== element) return;
  const { layer } = foreground; foreground = null;
  if (layer) element.style.zIndex = layer;
  else element.style.removeProperty('z-index');
}
