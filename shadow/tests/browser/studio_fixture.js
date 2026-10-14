// A static, in-memory UI fixture: it never forwards requests to any server.
(() => {
  const initial = {revision:0,undoCount:0,redoCount:0,selection:[],items:[{key:'line:1',id:'H:1',type:'Line',layer:'Piping',inserted:false}]};
  let live=structuredClone(initial), active='live', version=0, view=0, snapshots=[], list=[], branches=new Map();
  let server='fixture-server';
  const current=()=>active==='live'?live:branches.get(active).snapshot;
  const state=()=>({...structuredClone(current()),serverId:server,changeVersion:version,viewVersion:view,
    documentId:'00000000-0000-4000-8000-000000000001',workspaceId:active,workspaceName:active==='live'?'Live document':branches.get(active).name,
    sourceRevision:live.revision,ancestorRevision:active==='live'?null:branches.get(active).ancestor,
    branches:[...branches].map(([id,b])=>({id,name:b.name,revision:b.snapshot.revision,ancestorRevision:b.ancestor})),
    layout:'Model',layouts:['Model','Hatch islands'],fontRevision:0,fonts:[],objects:20,entities:current().items.length,
    occurrences:current().items.length,bounds:{minX:-50,minY:-50,maxX:50,maxY:50},captureMilliseconds:1,
    snapshots:[...snapshots],diagnostics:[]});
  const response=value=>new Response(JSON.stringify(value),{headers:{'Content-Type':'application/json'}});
  const fixture=window.fixture={calls:[],held:new Map(),holds:new Set(),conflict:false,state,
    release(path){const release=this.held.get(path);if(!release)throw new Error('Nothing held: '+path);this.held.delete(path);this.holds.delete(path);release();},
    emit(){this.events.dispatchEvent(new MessageEvent('state',{data:JSON.stringify(state())}));},
    advance(){current().revision++;version++;this.emit();}};
  window.EventSource=class extends EventTarget { constructor(){super();fixture.events=this;setTimeout(()=>this.onopen?.(),0);} close(){} };
  window.confirm=()=>true;
  window.fetch=async (url,options={})=>{
    const path=String(url).split('?')[0].replace(/^api\//,'');
    fixture.calls.push({path,method:options.method||'GET',headers:options.headers,body:options.body});
    let value;
    if(path==='state') value=response(state());
    else if(path==='render') value=new Response('<svg xmlns="http://www.w3.org/2000/svg" width="600" height="400"><rect width="600" height="400" fill="#151923"/><path d="M40 200H560" stroke="#68c4eb" stroke-width="3"/></svg>',{headers:{'Content-Type':'image/svg+xml'}});
    else if(path.startsWith('object/')) value=response({...state(),id:'H:1',type:'Line',properties:[{name:'EndPoint',kind:'Structure',writable:true,value:{X:'10',Y:'0',Z:'0'}}]});
    else if(path==='graph') value=response({...state(),nodes:[],edges:[],components:1,cycleRank:0,junctions:0,deadEnds:2,bridges:1});
    else {
      const body=JSON.parse(options.body||'{}');
      if(path==='branches') {const id='branch-'+(branches.size+1);branches.set(id,{name:body.name,ancestor:live.revision,snapshot:{...structuredClone(live),selection:[],undoCount:0,redoCount:0}});active=id;view++;}
      else if(path==='workspace') {active=body.id;view++;}
      else if(path==='linetype-view') {current().linetypeAlignment=body.alignment;view++;}
      else if(path==='select') current().selection=[body.key];
      else if(path==='pick') current().selection=['line:1'];
      else if(path==='add') {current().revision++;current().undoCount++;current().items.push({key:'circle:'+current().revision,id:'S:'+current().revision,type:'Circle',layer:'0',inserted:false});}
      else if(path==='snapshot') snapshots.push(current().revision);
      else if(path==='property') {current().revision++;current().undoCount++;}
      else if(path==='history/undo') {current().revision++;current().undoCount--;current().redoCount++;}
      else if(path==='merge/preview') {
        value=response({documentId:state().documentId,branchId:body.branchId,canApply:!fixture.conflict||body.choice!=='Unresolved',
          planId:fixture.conflict&&body.choice==='Unresolved'?null:'plan-1',targetRevision:live.revision,incomingRevision:branches.get(body.branchId).snapshot.revision,
          ancestorRevision:branches.get(body.branchId).ancestor,added:1,changed:0,removed:0,conflictCount:fixture.conflict?1:0,
          conflicts:fixture.conflict?[{id:'H:1',path:['EndPoint','Y'],kind:'Value',resolution:body.choice,
            ancestor:{text:'0'},target:{text:'20'},incoming:{text:'<img src=x onerror=alert(1)>'}}]:[]});
      }
      else if(path==='merge/apply') {live=structuredClone(current());live.revision++;live.undoCount=1;active='live';view++;}
      else if(path==='save') {live.revision++;version++;return new Response('0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nEOF\n');}
      else throw new Error('Fixture does not implement '+path);
      if(!value) {version++;value=response(state());}
    }
    if(fixture.holds.has(path)) await new Promise(resolve=>fixture.held.set(path,resolve));
    return value;
  };
})();
