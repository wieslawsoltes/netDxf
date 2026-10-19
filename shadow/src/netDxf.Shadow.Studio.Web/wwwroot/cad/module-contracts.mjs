import { CommandSession } from './core.mjs';

export function moduleRequest(descriptor, command, properties = {}, symbol = null, selection = []) {
  if (!descriptor?.id || !descriptor.commands?.some(c => c.id === command?.id)) throw new Error('Choose an available module command.');
  if (command.requiresSelection && !selection.length) throw new Error('Select the objects to change first.');
  if (command.requiresSymbol && (!symbol?.symbolId || !symbol?.version)) throw new Error('Choose a symbol and version.');
  const values = {};
  for (const field of [...(command.fields || []), ...(symbol?.attributes || [])]) {
    const supplied = properties[field.id] ?? Object.entries(properties).find(([key])=>key.toLowerCase()===field.id.toLowerCase())?.[1];
    const value = String(supplied ?? field.default ?? '');
    if (field.required && !value.trim()) throw new Error(`${field.name} is required.`);
    if (value.length > 8192) throw new Error(`${field.name} is too long.`);
    if (field.choices?.length && !field.choices.includes(value)) throw new Error(`Choose an advertised ${field.name}.`);
    if(!Object.keys(values).some(key=>key.toLowerCase()===field.id.toLowerCase()))values[field.id] = value;
  }
  return { module: descriptor.id, command: { command: command.id, points: [], entities: [...new Set(selection)].map(value => ({value})),
    symbolId: symbol?.symbolId ?? null, symbolVersion: symbol?.version ?? null, properties: values } };
}

/** Reuses CAD acquisition so module routes get the same snaps, Ortho and polar behavior. */
export function moduleSession(descriptor, command, request, expectedKey) {
  if (!Number.isInteger(command.minimumPoints) || command.minimumPoints < 1 || command.maximumPoints < command.minimumPoints || command.maximumPoints > 10000)
    throw new Error('This command does not declare a valid interactive point sequence.');
  const session = new CommandSession(command.maximumPoints === 1 ? 'POINT' : 'PLINE');
  session.name = `${descriptor.name}: ${command.name}`;
  session.spec = { steps: Array.from({length:command.minimumPoints}, (_, index) => ({kind:'point',prompt:`Specify point ${index+1} of ${command.minimumPoints}`})),
    chain: command.maximumPoints > command.minimumPoints ? command.minimumPoints : undefined };
  session.moduleRequest = structuredClone(request); session.expectedKey = expectedKey;
  const pick = session.pick.bind(session), input = session.input.bind(session);
  session.pick = p => { if (session.command.points.length >= command.maximumPoints) throw new Error(`At most ${command.maximumPoints} points are allowed.`); pick(p); };
  session.input = (text, cursor) => { if (['C','CLOSE'].includes(String(text).trim().toUpperCase())) throw new Error('Press Enter to finish the open route.'); input(text,cursor); };
  return session;
}
