// Input-only registered MULTILEADER / SECTION scenarios. No native outputs.
const r=ref=>({ref}), a=(array,values)=>({array,values}), i=value=>({int:value}), h=value=>({short:value});
const n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}}), g=(target,member,id)=>({method:'get',target,member,id});
const c=(target,member,args=[],id,signature)=>({method:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const s=(target,member,value)=>({method:'set',target,member,value});
const at=(target,value,id)=>({method:'item',target,args:[value],id}), snap=target=>({method:'snapshot',target}), model=target=>({method:'model',target});
function document(version=18,prefix='') {
  const id=name=>prefix+name;
  return [n(id('doc'),'DxfDocument',[{enum:'Header.DxfVersion',value:version}]),
    ...['Entities','Blocks','Layers','Linetypes','TextStyles','Views','Objects'].map(name=>g(id('doc'),name,id(name))),g(id('Objects'),'Root',id('root')),
    at(id('TextStyles'),'Standard',id('textStyle')),at(id('Linetypes'),'Continuous',id('lineType')),at(id('Blocks'),'*Model_Space',id('model'))];
}
function leader(version=18) {
  return [...document(version),n('style','Objects.DxfMLeaderStyle'),g('style','Properties','sp'),s('sp','TextStyle',r('textStyle')),c('Objects','AddMLeaderStyle',['Style',r('style')]),
    n('leader','Entities.MultiLeader'),g('leader','Properties','p'),g('leader','Context','context'),s('p','Style',r('style')),s('p','TextStyle',r('textStyle')),s('p','LeaderLinetype',r('lineType')),s('p','ContentType',h(0))];
}
function section(version=18, code='SECTIONOBJECT', owned=true) {
  const steps=[...document(version),n('section','Entities.Section',[code]),s('section','Name','Cut 東京'),n('line','Entities.Line'),c('Entities','Add',[r('line')]),c('Entities','Add',[r('section')]),g('section','Owner','block'),g('block','Record','record')];
  if(owned)steps.push(n('settings','Objects.DxfSectionSettings'),n('geometry','Objects.DxfSectionGeometrySettings'),
    n('type','Objects.DxfSectionTypeSettings',[i(4),i(17),a('DxfObject',[r('section'),r('line'),r('line'),null,r('settings')]),r('record'),'not-opened.dwg',a('Objects.DxfSectionGeometrySettings',[r('geometry')]),true]),
    c('settings','SetTypeSettings',[a('Objects.DxfSectionTypeSettings',[r('type')])]),c('Objects','SetSectionSettings',[r('section'),r('settings')]));
  return steps;
}
function metadata(owner,target){return [n('app','Tables.ApplicationRegistry',['ANNOTATION_APP']),n('data','XData',[r('app')]),g('data','XDataRecord','tags'),g(target,'Handle','pointer'),
  n('tag','XDataRecord',[{enum:'XDataCode',value:1005},r('pointer')]),c('tags','Add',[r('tag')]),g(owner,'XData','metadata'),c('metadata','Add',[r('data')])];}
export function registeredAnnotationsCorpus(){
  const all=[],add=(name,steps)=>all.push({name:'registered-annotations/'+name,request:{op:'document-ownership',steps}});
  for(const version of [13,14,15,16,17,18]) {
    for(const code of ['SECTION','SECTIONOBJECT'])add('section/profile/'+version+'/'+code,[...section(version,code,false),snap('doc'),model('section'),c('Entities','Remove',[r('section')]),snap('doc')]);
    if(version<15){add('mleader/profile/'+version,[...leader(version),c('Entities','Add',[r('leader')]),snap('doc'),model('leader')]);continue;}
    for(const content of [0,1,2]) {
      const data=content===0?[]:content===1?[n('contentBlock','Blocks.Block',['Content']),c('Blocks','Add',[r('contentBlock')]),g('contentBlock','Record','contentRecord'),n('content','Entities.MLeaderBlockContent'),s('content','Block',r('contentRecord')),s('context','Block',r('content'))]:[n('content','Entities.MLeaderMTextContent'),s('content','Style',r('textStyle')),s('content','Text','Label'),s('context','MText',r('content'))];
      add('mleader/content/'+version+'/'+content,[...leader(version),...data,s('p','ContentType',h(content)),c('Entities','Add',[r('leader')]),c('leader','Validate'),model('leader'),c('TextStyles','GetReferences',[r('textStyle')]),snap('doc'),c('Entities','Remove',[r('leader')]),c('Objects','EraseOwnedTree',[r('style')]),snap('doc')]);
    }
    for(const code of ['SECTION','SECTIONOBJECT'])for(const owned of [false,true])add('section/clone-erase/'+version+'/'+code+'/'+owned,[...section(version,code,owned),c('Objects','CloneSection',[r('section'),r('block'),null],'copy'),model('copy'),snap('doc'),c('Objects','Validate'),c('Entities','Remove',[r('section')]),c('Entities','Remove',[r('line')]),c('Objects','EraseSection',[r('copy')]),model('copy'),snap('doc'),c('Objects','EraseSection',[r('section')]),snap('doc'),c('Entities','Add',[r('section')]),c('section','Clone'),c('Objects','Validate')]);
  }
  for(const property of ['TextStyle','LeaderLinetype','ArrowHead','Style'])add('mleader/foreign-field/'+property,[...leader(),c('Entities','Add',[r('leader')]),...document(18,'foreign'),
    g('foreignmodel','Record','foreignRecord'),n('foreignStyle','Objects.DxfMLeaderStyle'),g('foreignStyle','Properties','foreignStyleProperties'),s('foreignStyleProperties','TextStyle',r('foreigntextStyle')),c('foreignObjects','AddMLeaderStyle',['Other',r('foreignStyle')]),s('p',property,r(property==='TextStyle'?'foreigntextStyle':property==='LeaderLinetype'?'foreignlineType':property==='ArrowHead'?'foreignRecord':'foreignStyle')),model('leader'),snap('doc'),snap('foreigndoc')]);
  for(const property of ['TextStyle','LeaderLinetype','ArrowHead']) {
    const table=property==='TextStyle'?'TextStyles':property==='LeaderLinetype'?'Linetypes':'Blocks',type=property==='TextStyle'?'Tables.TextStyle':property==='LeaderLinetype'?'Tables.Linetype':'Blocks.Block';
    const target=property==='ArrowHead'?'record':'resource';
    add('mleader/dynamic/'+property,[...leader(),n('resource',type,property==='TextStyle'?['Dynamic','txt.shx']:['Dynamic']),c(table,'Add',[r('resource')]),...(property==='ArrowHead'?[g('resource','Record','record')]:[]),
      s('p',property,r(target)),c('Entities','Add',[r('leader')]),c(table,'GetReferences',[r('resource')]),c(table,'Remove',[r('resource')]),s('resource','Name','Renamed'),c(table,'GetReferences',['Renamed']),
      s('p',property,property==='ArrowHead'?null:r(property==='TextStyle'?'textStyle':'lineType')),c(table,'Remove',[r('resource')]),snap('doc'),c('Entities','Remove',[r('leader')]),snap('doc')]);
  }
  add('mleader/combined',[...leader(),s('leader','Linetype',r('lineType')),c('Entities','Add',[r('leader')]),c('Linetypes','GetReferences',[r('lineType')]),c('Entities','Remove',[r('leader')]),c('Linetypes','GetReferences',[r('lineType')]),snap('doc')]);
  for(const fault of ['missing-text','owned','duplicate','wrong-dictionary','foreign-text']) {
    const setup=fault==='wrong-dictionary'?[n('wrong','Objects.DxfPlaceholder'),c('root','Add',['ACAD_MLEADERSTYLE',r('wrong'),true])]:[];
    add('mleader/style-reject/'+fault,[...document(),...setup,...(fault==='foreign-text'?document(18,'foreign'):[]),n('style','Objects.DxfMLeaderStyle'),g('style','Properties','sp'),
      ...(fault==='missing-text'?[]:[s('sp','TextStyle',r(fault==='foreign-text'?'foreigntextStyle':'textStyle'))]),
      ...(fault==='owned'?[c('root','Add',['owned',r('style'),true])]:[]),c('Objects','AddMLeaderStyle',['Style',r('style')]),
      ...(fault==='duplicate'?[n('another','Objects.DxfMLeaderStyle'),g('another','Properties','asp'),s('asp','TextStyle',r('textStyle')),c('Objects','AddMLeaderStyle',['Style',r('another')]),model('another')]:[]),snap('doc'),model('style')]);
  }
  add('mleader/style-clone',[...leader(),at('root','ACAD_MLEADERSTYLE','dict'),c('Objects','Clone',[r('dict'),r('root'),'Copy',null],'copy'),at('copy','Style','clonedStyle'),model('clonedStyle'),c('Objects','Validate'),snap('doc')]);
  for(const mapping of ['complete','missing','conflict','wrong-type']) {
    add('section/cross-document/'+mapping,[...section(),...document(18,'dest'),n('destline','Entities.Line'),c('destEntities','Add',[r('destline')]),g('section','Layer','sourceLayer'),g('section','Linetype','sourceLine'),at('destLayers','0','destLayer'),at('destLinetypes','ByLayer','destLine'),
      {method:'mapping',id:'map',pairs:mapping==='missing'?[]:[[r('line'),r('destline')],[r('sourceLayer'),r(mapping==='wrong-type'?'destline':'destLayer')],[r('sourceLine'),r('destLine')],...(mapping==='conflict'?[[r('block'),r('model')]]:[])]},
      c('destObjects','CloneSection',[r('section'),r('destmodel'),r('map')],'copy'),model('copy'),snap('destdoc'),snap('doc'),c('destObjects','Validate')]);
  }
  add('section/metadata-clone',[...section(),...metadata('section','settings'),...metadata('settings','section'),n('extension','Objects.DxfDictionary'),n('note','Objects.DxfXRecord'),g('note','Data','noteData'),g('settings','Handle','settingsHandle'),n('pointerTag','IO.DxfTag',[h(330),r('settingsHandle')]),c('noteData','Add',[r('pointerTag')]),c('extension','Add',['note',r('note'),true]),c('extension','Add',['alias',r('note'),false]),c('Objects','SetExtensionDictionary',[r('section'),r('extension')]),c('Objects','CloneSection',[r('section'),r('block'),null],'copy'),model('copy'),snap('doc'),c('Objects','Validate'),c('Objects','EraseSection',[r('copy')]),model('copy'),snap('doc')]);
  for(const owned of [false,true])for(const channel of ['xrecord','xrecord390','xdata','reactor','view','header','opaque']) {
    const setup=channel==='view'?[n('view','Tables.View',['Live']),c('Views','Add',[r('view')]),s('view','LiveSection',r('section'))]:channel==='reactor'?[g('line','PersistentReactors','reactors'),c('reactors','Add',[r('section')])]:channel==='xdata'?metadata('line','section'):channel==='header'?[g('doc','DrawingVariables','variables'),g('section','Handle','handle'),n('variable','Header.HeaderVariable',['$SECTION_REF',h(330),r('handle')]),c('variables','AddCustomVariable',[r('variable')])]:channel==='opaque'?[g('section','Handle','handle'),{method:'opaque',id:'opaque',args:['PRIVATE',a('IO.DxfTag',[{new:'IO.DxfTag',args:[h(320),r('handle')]}])]},c('root','Add',['opaque',r('opaque'),true])]:[n('note','Objects.DxfXRecord'),g('note','Data','tags'),g('section','Handle','handle'),n('tag','IO.DxfTag',[h(channel==='xrecord'?330:390),r('handle')]),{method:'append-loaded',target:'note',args:[r('tag')]},c('root','Add',['incoming',r('note'),true])];
    add('section/incoming/'+owned+'/'+channel,[...section(18,'SECTIONOBJECT',owned),...setup,c('Entities','Remove',[r('section')]),c('Objects','EraseSection',[r('section')]),model('section'),snap('doc')]);
  }
  for(const fault of ['foreign','occupied','unregistered','owned','null']) {
    add('section/settings-reject/'+fault,[...section(18,'SECTIONOBJECT',fault==='occupied'),...(fault==='foreign'?document(18,'foreign'):[]),
      n('candidate','Objects.DxfSectionSettings'),...(fault==='owned'?[c('root','Add',['owned',r('candidate'),true])]:[]),...(fault==='unregistered'?[n('section','Entities.Section')]:[]),
      c(fault==='foreign'?'foreignObjects':'Objects','SetSectionSettings',[r('section'),fault==='null'?null:r('candidate')]),snap('doc'),model('candidate')]);
  }
  for(const version of [14,18])for(const mode of ['direct','nested','insert','dimension']) {
    const steps=[...document(version),n('container','Blocks.Block',['Container']),g('container','Entities','members'),n('section','Entities.Section'),c('members','Add',[r('section')])];
    if(mode==='direct')steps.push(c('Blocks','Add',[r('container')]));
    else if(mode==='nested')steps.push(n('outer','Blocks.Block',['Outer']),g('outer','Entities','outerMembers'),n('insert','Entities.Insert',[r('container')]),c('outerMembers','Add',[r('insert')]),c('Blocks','Add',[r('outer')]));
    else if(mode==='insert')steps.push(n('insert','Entities.Insert',[r('container')]),c('Entities','Add',[r('insert')]));
    else steps.push(n('dimension','Entities.AlignedDimension'),s('dimension','Block',r('container')),c('Entities','Add',[r('dimension')]));
    steps.push(snap('doc'),model('section'));add('section/nested-adoption/'+version+'/'+mode,steps);
  }
  for(let seed=1;seed<=24;seed++) {
    let state=seed;const random=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n;
    const steps=leader();for(let j=0;j<4;j++)steps.push(n('text'+j,'Tables.TextStyle',['T'+j,'txt.shx']),c('TextStyles','Add',[r('text'+j)]));
    steps.push(c('Entities','Add',[r('leader')]));
    for(let j=0;j<32;j++){const k=random(4);steps.push(s('p','TextStyle',r('text'+k)),c('TextStyles','GetReferences',[r('text'+k)]),c('TextStyles','Remove',[r('text'+k)]),snap('doc'));}
    steps.push(c('Entities','Remove',[r('leader')]),snap('doc'));add('mleader/random/'+seed,steps);
  }
  return all;
}
