import { CommandSession } from './core.mjs';

/** Resolve keyboard names and picker identities through one native INSERT contract. */
export function resolveInsertCommand(command,blocks) {
  const block=blocks.find(b=>command.reference?b.id===command.reference.value:b.name.toLowerCase()===command.block?.toLowerCase());
  if(!block)throw new Error(`Unknown block: ${command.block||command.reference?.value}`);
  const resolved=structuredClone(command);resolved.reference={value:block.id};delete resolved.block;return resolved;
}

const bounded = (value, label) => {
  if (typeof value !== 'number' || !Number.isFinite(value) || Math.abs(value) > 1e12) throw new Error(`${label} must be a finite number within ±1e12.`);
  return value;
};
/** Detached placement configuration shared by the picker, repeat placement and tests. */
export function insertOptions(block, options = {}) {
  if (!block || typeof block.id !== 'string' || !block.id || typeof block.name !== 'string' || !block.name || block.name.startsWith('*') || block.isExternal)
    throw new Error('Choose a local named block definition.');
  const raw = options.scale ?? { x: 1, y: 1, z: 1 };
  const scale = Object.fromEntries(['x', 'y', 'z'].map(axis => {
    const value = bounded(raw[axis], `Scale ${axis.toUpperCase()}`);
    if (Math.abs(value) < 1e-9) throw new Error('Scale axes must be nonzero (absolute value at least 1e-9). Negative values mirror the reference.');
    return [axis, value];
  }));
  if (block.scaleUniformly && ['y','z'].some(axis=>Math.abs(Math.abs(scale.x)-Math.abs(scale[axis]))>1e-9))
    throw new Error('This block definition requires uniform scale magnitudes on all axes.');
  const basePoint = options.basePoint == null ? null : Object.fromEntries(['x','y','z'].map(axis =>
    [axis,bounded(options.basePoint[axis] ?? (axis === 'z' ? 0 : undefined), 'Definition base point')]));
  return { id: block.id, name: block.name, scale, rotation: bounded(options.rotation ?? 0, 'Rotation'), basePoint, repeat: options.repeat === true };
}

/** All options are fixed before picking; each placement remains one native undo step. */
export function createInsertSession(options, layer = '0') {
  const configuration = insertOptions(options, options);
  const session = new CommandSession('INSERT', layer);
  session.spec = { ...session.spec, steps: [{kind:'point',prompt:`Specify insertion point for ${configuration.name}${configuration.repeat ? ' (repeat; Escape ends)' : ''}`}] };
  session.command = { name:'INSERT', points:[], layer, block:configuration.name, scale:configuration.scale, rotation:configuration.rotation };
  if (configuration.basePoint) session.command.basePoint = configuration.basePoint;
  session.insertOptions = structuredClone(configuration);
  return session;
}
