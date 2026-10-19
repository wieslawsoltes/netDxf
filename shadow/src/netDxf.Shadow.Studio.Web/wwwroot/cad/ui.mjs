import { commands } from './core.mjs';
export const $ = id => document.getElementById(id);
export function el(tag, attributes = {}, children = []) {
  const node = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    if (name === 'text') node.textContent = value;
    else if (name === 'class') node.className = value;
    else if (name.startsWith('on')) node.addEventListener(name.slice(2), value);
    else if (name === 'checked' || name === 'disabled' || name === 'hidden') node[name] = !!value;
    else if (value !== undefined && value !== null) node.setAttribute(name, String(value));
  }
  for (const child of Array.isArray(children) ? children : [children]) if (child !== null && child !== undefined) node.append(child);
  return node;
}
const paths = {
  BLOCK:'M3 3h12v12H3zM13 13h8v8h-8zM5 19h4M7 17v4', HATCH:'M3 3h18v18H3zM3 10l7-7M3 17L17 3M7 21L21 7M14 21l7-7', HATCHPOLY:'M3 5l18-2l-3 18H5zM4 12l8-8M6 20L20 6M13 21l7-7', HATCHEDIT:'M3 3h18v18H3zM3 10l7-7M3 17L17 3M7 21L21 7M14 21l7-7',
  LINE:'M3 20L21 4M2 18h4v4H2zM19 2h4v4h-4z', PLINE:'M3 19L9 6L15 17L21 4', RECTANG:'M3 5h18v14H3z', POLYGON:'M12 2L22 9L18 21H6L2 9z',
  CIRCLE:'M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0M10 12h4M12 10v4', ARC:'M3 20C-1 1 25-3 21 16M2 18h3v3H2zM20 14h3v3h-3z',
  ELLIPSE:'M22 12a10 6 0 1 1-20 0a10 6 0 1 1 20 0', POINT:'M12 2v20M2 12h20M15 12a3 3 0 1 1-6 0a3 3 0 1 1 6 0',
  SPLINE:'M2 18C10-14 10 38 22 4', TEXT:'M3 4h18M12 4v17M8 21h8', MTEXT:'M3 3h18M3 8h18M3 13h13M3 18h16',
  DIMALIGNED:'M4 20L20 4M1 16l6 6M16 1l6 6M4 14v6h6M14 4h6v6', DIMLINEAR:'M3 3v18M21 3v18M3 16h18M7 13l-4 3l4 3M17 13l4 3l-4 3',
  MOVE:'M12 2v20M2 12h20M8 6l4-4l4 4M8 18l4 4l4-4M6 8l-4 4l4 4M18 8l4 4l-4 4', COPY:'M3 3h12v12H3zM9 9h12v12H9z',
  STRETCH:'M3 4h7v16H3zM14 4h7v16h-7M10 12h11M17 8l4 4l-4 4', BREAK:'M2 18L9 11M15 7l7-5M8 7l4 6M12 5l4 6', BREAKATPOINT:'M2 20L10 12M14 8l8-8M8 8h8v8H8z',
  ROTATE:'M5 7a8 8 0 1 1-1 9M5 2v6h6', SCALE:'M3 14h7v7H3zM3 3h18v18h-7M13 11l8-8M15 3h6v6', MIRROR:'M12 2v20M3 5l6 7l-6 7zM21 5l-6 7l6 7z',
  ARRAY:'M2 2h5v5H2zM10 2h5v5h-5zM18 2h5v5h-5zM2 10h5v5H2zM10 10h5v5h-5zM18 10h5v5h-5zM2 18h5v5H2zM10 18h5v5h-5zM18 18h5v5h-5z',
  OFFSET:'M3 21V3h18M8 21V8h13M13 21v-8h8', TRIM:'M4 3v18M2 7h20M14 3l7 8M21 3l-7 8', EXTEND:'M4 3v18M3 15h12M15 11l5 4l-5 4M21 3v18',
  FILLET:'M3 22V10a7 7 0 0 1 7-7h12', CHAMFER:'M3 22V11L11 3h11', JOIN:'M2 16h8M14 8h8M8 12h8M10 10l-2 2l2 2M14 10l2 2l-2 2',
  EXPLODE:'M7 7h10v10H7zM5 5L1 1M19 5l4-4M19 19l4 4M5 19l-4 4', ERASE:'M3 14L14 3l8 8l-11 11H9zM8 9l8 8M10 22h12',
  INSERT:'M3 3h12v12H3zM13 13h8v8h-8zM8 12l4 4M8 16h4v-4', DIST:'M2 17L19 2l4 4L6 21zM7 12l3 3M11 8l3 3M15 4l3 3', AREA:'M3 20V4h18v16zM7 4v16M12 4v16M17 4v16',
  new:'M5 2h10l5 5v15H5zM15 2v6h5M9 15h7M12 12v7', open:'M2 7V4h8l3 3h9v14H2zM2 11h20', save:'M3 2h16l3 3v17H3zM7 2v7h10V2M7 22v-9h11v9',
  undo:'M9 4L3 9l6 5M3 9h10a7 7 0 0 1 0 14', redo:'M15 4l6 5l-6 5M21 9H11a7 7 0 0 0 0 14',
  fit:'M3 9V3h6M15 3h6v6M21 15v6h-6M9 21H3v-6M8 8h8v8H8z', pan:'M12 2v20M2 12h20M8 6l4-4l4 4M8 18l4 4l4-4',
  layers:'M12 2L23 8L12 14L1 8zM1 12l11 6l11-6M1 16l11 6l11-6', properties:'M3 3h18v18H3zM10 3v18M3 9h18M3 15h18',
  blocks:'M3 3h8v8H3zM13 13h8v8h-8zM13 3h8v8h-8zM3 13h8v8H3z', graph:'M4 4L20 8L12 20L4 4M1 1h6v6H1zM17 5h6v6h-6zM9 17h6v6H9z',
  settings:'M12 4a8 8 0 1 1 0 16a8 8 0 1 1 0-16M12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6M12 1v3M12 20v3M1 12h3M20 12h3',
  output:'M5 2h14v6M5 16H2V8h20v8h-3M5 13h14v9H5z', workspaces:'M5 2v20M5 7h10a5 5 0 0 1 5 5v10M2 2h6v5H2zM17 17h6v5h-6z',
  diagnostics:'M3 21h18M6 18V9M12 18V3M18 18v-6', font:'M3 21L10 2h3l8 19M6 14h12', snapshot:'M2 6h5l2-3h6l2 3h5v15H2zM17 13a5 5 0 1 1-10 0a5 5 0 1 1 10 0'
};
export function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg','svg'); svg.setAttribute('viewBox','0 0 24 24'); svg.setAttribute('class','icon'); svg.setAttribute('aria-hidden','true');
  const p = document.createElementNS(svg.namespaceURI,'path'); p.setAttribute('d',paths[name] || paths[{'block-create':'BLOCK','block-redefine':'BLOCK','block-rename':'BLOCK','hatch-create':'HATCH','hatch-edit':'HATCHEDIT'}[name]] || paths.settings); svg.append(p); return svg;
}
export function tool(name, label, action = false, large = false) {
  return el('button', { class:`tool${large ? ' large' : ''}`, [action ? 'data-action' : 'data-command']:name,
    title:action ? label : `${name}${commands[name]?.aliases[0] ? ` (${commands[name].aliases[0]})` : ''} — ${label}` }, [icon(name), el('span', {text:label})]);
}
export const tabs = ['Home','Insert','Annotate','Graph','View','Manage','Output'];
const panelIcons = {Draw:'LINE',Modify:'MOVE',Annotation:'MTEXT',Layers:'layers',Block:'BLOCK',Blocks:'BLOCK',Hatch:'HATCH',Properties:'properties',Navigate:'pan',Palettes:'properties',Display:'settings',History:'undo',Text:'TEXT',Dimensions:'DIMALIGNED',Measure:'DIST','Drawing state':'workspaces',Inspect:'diagnostics',Settings:'settings','Block Editor':'BLOCK'};
export const group = (name, tools) => el('section', {class:'ribbon-group','data-panel':name,'aria-label':name}, [
  el('button',{class:'ribbon-launcher',type:'button',title:`${name} tools`,'aria-label':`${name} tools`,'aria-haspopup':'true','aria-expanded':'false'},[icon(panelIcons[name]||'settings'),el('span',{text:({'Annotation':'Annotate','Properties':'Props','Reference resources':'Resources','Drawing state':'Drawing','Annotation context':'Context','Model annotation context':'Context'}[name]||name)}),el('small',{text:'▾','aria-hidden':'true'})]),
  el('div',{class:'ribbon-tools'},tools),el('div',{class:'ribbon-group-title',text:name})]);
const c = (name, large = false) => tool(name, commands[name].label, false, large);
const a = (name,label,large = false) => {const node=tool(name,label,true,large);if(name.startsWith('graph-'))node.querySelector('svg').replaceWith(icon('graph'));return node;};
export function ribbon(tab, data, layer, changeLayer) {
  const layerSelect = el('select', {id:'current-layer','aria-label':'Current layer',onchange:e => changeLayer(e.target.value)},
    (data?.layers || []).map(l => el('option',{value:l.name,text:l.name})));
  layerSelect.value = layer;
  const layerTools = group('Layers',[
    el('label',{class:'ribbon-field'},[icon('layers'),layerSelect]),
    a('layer-manager','Layer manager'),a('layer-states','Layer states'),a('layers','Layers palette'),a('assign-layer','Match current layer'),a('layer-new','New layer')]);
  const content = {
    Home: [group('Draw',[c('LINE',true),c('PLINE',true),c('CIRCLE',true),c('ARC'),c('RECTANG'),c('ELLIPSE'),c('POLYGON'),c('SPLINE'),c('POINT')]),
      group('Modify',[c('MOVE'),c('COPY'),c('STRETCH'),c('BREAK'),c('BREAKATPOINT'),c('ERASE'),c('ROTATE'),c('MIRROR'),c('SCALE'),c('TRIM'),c('EXTEND'),c('OFFSET'),c('FILLET'),c('CHAMFER'),c('ARRAY'),c('JOIN'),c('EXPLODE')]),
      group('Annotation',[c('MTEXT',true),c('DIMALIGNED',true),c('TEXT'),c('DIMLINEAR'),c('DIST')]),layerTools,
      group('Block',[a('block-insert','Insert block',true),a('block-create','Create block'),a('attributes','Edit attributes'),a('attribute-definitions','Manage attributes'),a('attribute-create','Define attribute'),a('attribute-sync','Synchronize attributes'),a('block-redefine','Redefine')]),group('Hatch',[a('hatch-create','Hatch',true),c('HATCHPOLY'),a('hatch-edit','Edit hatch')]), group('Properties',[a('properties','Properties',true),a('linetypes','Linetypes')])],
    Insert: [group('Blocks',[a('block-insert','Insert block',true),a('blocks','Blocks palette',true),a('block-library-import','Import library'),a('block-library-export','Export library'),a('block-create','Create block'),a('attributes','Edit attributes'),a('attribute-definitions','Manage attributes'),a('attribute-create','Define attribute'),a('attribute-sync','Synchronize attributes'),a('block-redefine','Redefine'),a('block-rename','Rename'),a('block-edit','Edit definition')]),group('Reference resources',[a('open','Open DXF / DWG',true),a('font','Load SHX / SHP',true)]),group('Documents',[a('new','New drawing'),a('demo','New sample drawing'),a('save','Save DXF')])],
    Annotate: [group('Text',[c('MTEXT',true),c('TEXT',true),a('text-styles','Text styles'),a('font','SHX / SHP resources')]),group('Dimensions',[c('DIMALIGNED',true),c('DIMLINEAR',true)]),group('Measure',[c('DIST',true),c('AREA',true)]),group('Annotation context',[a('annotation','Model annotation scale',true)])],
    Graph: [group('Topology',[a('graph-load','Visualize topology',true),a('graph-analysis','Graph workspace'),a('graph-query:summary','Summary'),a('graph-query:nodes','Nodes'),a('graph-query:edges','Edges'),a('graph-query:components','Components')]),
      group('Routes',[a('graph-query:path','Shortest path',true),a('graph-query:traverse','Traversal'),a('graph-query:nearest','Nearest node')]),
      group('Structure',[a('graph-query:cycles','Cycle basis',true),a('graph-query:cycle','Inspect cycle'),a('graph-query:spanning_forest','Minimum spanning forest')]),
      group('Features',[a('graph-query:deadEnds','Dead ends'),a('graph-query:junctions','Junctions'),a('graph-query:bridges','Bridges'),a('graph-query:articulationPoints','Articulation points'),a('graph-query:isolated','Isolated nodes'),a('graph-query:diagnostics','Build diagnostics')]),
      group('Inspection',[a('graph-fit','Fit topology'),a('graph-clear','Clear overlay'),a('properties','Source properties'),a('health','Drawing health')])],
    View: [group('Navigate',[a('fit','Zoom extents',true),a('pan','Pan',true),a('zoom-in','Zoom in'),a('zoom-out','Zoom out')]),group('Palettes',[a('properties','Properties'),a('layers','Layers'),a('blocks','Blocks'),a('explorer','Drawing explorer'),a('diagnostics','Diagnostics'),a('workspaces','Workspaces')]),group('Display',[a('settings','Drafting settings',true),a('clean','Clean screen'),a('command-line','Command line'),a('command-history','Command history'),a('ribbon-display','Ribbon display'),a('graph','Topology overlay'),a('graph-analysis','Graph workspace')]),group('Palette layout',[a('palette-picker','Open palette',true),a('dock','Change docking side'),a('palette-lock','Lock layout'),a('palette-reset','Reset palettes')]),group('History',[a('snapshot','Retain snapshot',true),a('snapshots','Snapshot views')])],
    Manage: [group('Resources',[a('layer-manager','Layers',true),a('layer-states','Layer states'),a('linetypes','Linetypes',true),a('text-styles','Text styles',true),a('font','SHX / SHP resources')]),group('Drawing state',[a('workspaces','Immutable workspaces',true),a('snapshot','Retain snapshot',true),a('undo','Undo'),a('redo','Redo')]),group('Inspect',[a('health','Drawing health',true),a('edit-previews','Edit previews',true),a('workflows','Workflow regressions',true),a('explorer','Source explorer'),a('diagnostics','Render diagnostics'),a('legacy','Advanced diagnostics')]),group('Settings',[a('modules','Discipline modules',true),a('settings','Drafting settings',true),a('reset-ui','Reset interface')])],
    Output: [group('Drawing',[a('save','Save DXF',true),a('save-as','Save drawing as',true)]),group('Render export',[a('export-png','Export PNG',true),a('export-svg','Export SVG',true)]),group('Presentation',[a('annotation','Annotation scale'),a('linetype','Linetype endpoint alignment'),a('snapshots','Snapshot views')])]
  };
  const result=content[tab] || content.Home;
  if(data?.blockEditor) result.unshift(group('Block Editor',[a('block-save','Save block',true),a('block-close','Close / discard',true)]));
  return result;
}
