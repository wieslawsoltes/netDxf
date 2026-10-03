// Shared private state for original-path database partials; never a second registry.
const registries = new WeakMap();
export function SetDatabaseRegistry(database, registry) { registries.set(database, registry); }
export function DatabaseRegistry(database) { return registries.get(database); }
