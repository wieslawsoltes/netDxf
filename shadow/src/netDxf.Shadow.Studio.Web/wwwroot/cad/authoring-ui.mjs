import { el } from './ui.mjs';
import { number, parsePoint } from './core.mjs';
import { blockName, hatchPatterns, islandStyles, parsePat } from './authoring-contracts.mjs';

const actions = new Set(['block-create', 'block-redefine', 'block-rename', 'hatch-create', 'hatch-edit']);
export const isAuthoringAction = name => actions.has(name);
const field = (label, node) => el('label', { class: 'form-row' }, [el('span', { text: label }), node]);
const input = (label, value, extra = {}) => el('input', { value, 'aria-label': label, ...extra });
const select = (label, values) => el('select', { 'aria-label': label }, values.map(([value, text]) => el('option', { value, text })));
const hint = text => el('p', { class: 'hint', text });
/** Typed dialogs are adapters only. Their complete receipt is rechecked when the queued write starts. */
export async function authoringDialog(name, { client, editable, edit, dialog, showPalette, currentLayer }) {
  if (!actions.has(name)) throw new Error(`Unknown authoring action: ${name}`);
  editable();
  if (client.busy) throw new Error('Finish the current edit before opening an authoring dialog.');
  const key = client.key, data = client.data, entities = data.selectedEntities.map(value => ({ value }));
  const commit = (path, body) => edit(path, body, { expectedKey: key });
  if (name.startsWith('block-')) {
    const redefine = name === 'block-redefine', rename = name === 'block-rename';
    if (!rename && !entities.length) throw new Error('Select source objects first, or use the BLOCK command to select them interactively.');
    if ((redefine || rename) && !data.blocks.length) throw new Error('The drawing has no named block definitions.');
    const target = select('Block definition', data.blocks.map(b => [b.id, b.name]));
    const newName = input('Block name', `Block${data.blocks.length + 1}`, { maxlength: 255, required: true });
    const base = input('Block base point', '0,0,0', { required: true });
    const description = input('Block description', '', { maxlength: 4096 });
    const disposition = select('Source disposition', [['0','Retain original objects'],['1','Convert to block reference'],['2','Delete original objects']]);
    disposition.value = redefine ? '0' : '1';
    const content = [];
    if (rename || redefine) content.push(field('Definition', target));
    if (!redefine) content.push(field(rename ? 'New name' : 'Name', newName));
    if (!rename) content.push(field('Base point (WCS)', base), field('Description', description), field('Source objects', disposition));
    content.push(hint(rename ? 'All existing references keep the same definition identity. Renaming is undoable.' :
      `${entities.length} selected source objects. ${redefine ? 'Every existing reference to this definition will display the replacement geometry. ' : ''}` +
      'The entire operation is one undoable transaction. Nested references stay shared; geometry is not flattened.'));
    if (!await dialog(rename ? 'Rename Block' : redefine ? 'Redefine Block' : 'Block Definition', content, rename ? 'Rename' : redefine ? 'Redefine' : 'Create')) return;
    if (rename) await commit('block/rename', { block: { value: target.value }, name: blockName(newName.value) });
    else {
      const definition = { name: redefine ? data.blocks.find(b => b.id === target.value).name : blockName(newName.value),
        basePoint: parsePoint(base.value), entities, disposition: Number(disposition.value),
        description: redefine && !description.value ? undefined : description.value, insertLayer: currentLayer, redefine: redefine ? { value: target.value } : null };
      await commit('block/define', { definition });
    }
    showPalette('Blocks'); return;
  }
  if (!entities.length) throw new Error('Select closed boundary objects first, or draw a solid boundary with HATCHPOLY.');
  const editing = name === 'hatch-edit';
  if (editing && data.items.some(i => data.selectedEntities.includes(i.id) && i.type !== 'Hatch')) throw new Error('Hatch Edit requires only hatch objects selected.');
  const patterns = new Map(hatchPatterns.map(name => [name, { name }]));
  const pattern = select('Hatch pattern', hatchPatterns.map(name => [name, name]));
  const scale = input('Hatch scale', '1', { required: true });
  const angle = input('Hatch angle', '0', { required: true });
  const origin = input('Hatch origin', '0,0', { required: true });
  const islands = select('Hatch islands', islandStyles.map((text, value) => [value, text]));
  const type = select('Hatch type', [['0', 'User-defined'], ['1', 'Predefined'], ['2', 'Custom']]);
  const double = input('Double hatch', '', { type: 'checkbox' });
  const setFamilyFlags = () => {
    const chosen = patterns.get(pattern.value);
    type.value = String(chosen?.type ?? 2); double.checked = !!chosen?.isDouble;
    double.disabled = type.value !== '0';
  };
  pattern.addEventListener('change', setFamilyFlags);
  type.addEventListener('change', () => { double.disabled = type.value !== '0'; });
  if (editing && entities.length === 1) {
    const current = await client.read(`hatch/pattern?revision=${data.state.revision}`);
    if (!current || current.entity !== entities[0].value || client.key !== key) throw new Error('The drawing changed while opening Hatch Edit. Reopen the dialog.');
    if (current.pattern) {
      const p = current.pattern; patterns.set('$CURRENT', p);
      pattern.prepend(el('option', { value: '$CURRENT', text: `${p.name} — current source definition` }));
      pattern.value = '$CURRENT'; scale.value = String(p.scale); angle.value = String(p.angle);
      origin.value = `${p.origin.x},${p.origin.y}`; islands.value = String(p.islands);
    }
  }
  setFamilyFlags();
  const file = input('Import PAT file', '', { type: 'file', accept: '.pat,text/plain' });
  const status = hint('Built-in patterns are original generic line families. Import your own PAT definitions for custom fills.');
  let pending = false, importError = null, disposed = false, importGeneration = 0;
  file.addEventListener('change', async () => {
    const chosen = file.files?.[0]; if (!chosen) return;
    const generation = ++importGeneration;
    pending = true; importError = null; status.textContent = 'Reading pattern definitions…';
    try {
      if (chosen.size > 1_048_576) throw new Error('PAT import is limited to 1 MiB.');
      const definitions = parsePat(await chosen.text());
      if (disposed || generation !== importGeneration) return;
      if (definitions.some(p => patterns.has(p.name))) throw new Error('Imported names must not duplicate built-in or already imported patterns.');
      // Update only after the complete file passes: partial imports are never retained.
      for (const definition of definitions) {
        patterns.set(definition.name, definition);
        pattern.append(el('option', { value: definition.name, text: `${definition.name} — ${definition.description || 'Custom'}` }));
      }
      pattern.value = definitions[0].name; setFamilyFlags(); status.textContent = `${definitions.length} patterns imported from ${chosen.name}.`;
    } catch (error) { if (!disposed && generation === importGeneration) { importError = error; status.textContent = error.message; } }
    finally { if (generation === importGeneration) pending = false; }
  });
  try {
    const accepted = await dialog(editing ? 'Hatch Edit' : 'Hatch Creation', [
      field('Pattern', pattern), field('Scale', scale), field('Angle (degrees)', angle), field('Origin (WCS)', origin),
      field('Island detection', islands), field('Pattern type', type), field('Perpendicular double fill', double), field('Custom pattern file', file), status,
      hint('Normal alternates nested islands. Outer fills only the outer band. Ignore fills through internal islands. ' +
        (editing ? 'Pattern edits preserve existing analytic boundaries.' : 'New hatches copy analytic XY boundaries and are non-associative; original objects are not modified.'))
    ], editing ? 'Apply' : 'Create');
    if (!accepted) return;
    if (pending) throw new Error('The PAT import is still being read. Reopen Hatch settings when it has finished.');
    if (importError) throw importError;
    const chosen = patterns.get(pattern.value), value = number(scale.value), offset = parsePoint(origin.value);
    if (value < 1e-9 || value > 1e9 || Math.abs(offset.z) > 1e-9) throw new Error('Use a positive bounded scale and an XY pattern origin.');
    const definition = { name: chosen.name, description: chosen.description || '', lines: chosen.lines, scale: value, angle: number(angle.value), origin: offset, islands: Number(islands.value), type: Number(type.value), isDouble: double.checked };
    await commit(editing ? 'hatch/pattern' : 'hatch/create', editing ? { pattern: definition, entities } :
      { hatch: { boundaries: entities, pattern: definition, layer: currentLayer } });
    showPalette('Properties');
  } finally { disposed = true; }
}
